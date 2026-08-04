using System.Globalization;
using System.Text.RegularExpressions;
using MemoryKeeper.Application.Time;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;

namespace MemoryKeeper.Application.Services;

/// <summary>
/// Dynamically groups unfinished memories. Results are not persisted.
/// </summary>
public sealed class MemoryGroupingService
{
    public const string UnknownDateGroupName = "날짜 미상 사진";

    public static readonly TimeSpan DefaultSessionGap = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan ExtendedSessionGap = TimeSpan.FromMinutes(60);

    private static readonly Regex TrailingNumberRegex = new(
        @"^(?<prefix>.*?)(?<number>\d+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly TimeSpan _sessionGap;
    private readonly TimeSpan _extendedSessionGap;

    public MemoryGroupingService()
        : this(DefaultSessionGap, ExtendedSessionGap)
    {
    }

    public MemoryGroupingService(TimeSpan sessionGap, TimeSpan extendedSessionGap)
    {
        _sessionGap = sessionGap;
        _extendedSessionGap = extendedSessionGap;
    }

    /// <summary>
    /// Groups GPS-missing pending media.
    /// Known dates use CapturedAt; unknown dates use import time / folder / filename only.
    /// ImportedAt is never treated as a capture-date substitute.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<Media>> GroupWithoutGps(IEnumerable<Media> mediaItems)
    {
        ArgumentNullException.ThrowIfNull(mediaItems);

        var candidates = mediaItems
            .Where(IsPendingCandidate)
            .Where(HasNoGps)
            .Where(media => media.MediaType == MediaType.Photo)
            .ToList();

        if (candidates.Count == 0)
        {
            return [];
        }

        var knownDateGroups = GroupKnownDateMedia(
            candidates.Where(HasKnownDate));
        var unknownDateGroups = GroupUnknownDateMedia(
            candidates.Where(HasUnknownDate));

        return knownDateGroups
            .Concat(unknownDateGroups)
            .ToList();
    }

    public static bool IsPendingCandidate(Media media)
    {
        return media.Status == MediaStatus.Pending || media.PlaceId is null;
    }

    public static bool HasGps(Media media)
    {
        return media.Latitude is not null && media.Longitude is not null;
    }

    public static bool HasNoGps(Media media) => !HasGps(media);

    public static bool HasKnownDate(Media media) => media.CapturedAt.HasValue;

    public static bool HasUnknownDate(Media media) => !media.CapturedAt.HasValue;

    /// <summary>
    /// Capture date only. Does not fall back to ImportedAt.
    /// </summary>
    public static DateTimeOffset? GetCapturedAt(Media media) => DateTimeHelper.ToUtcOffset(media.CapturedAt);

    public static bool GroupHasUnknownDate(IReadOnlyList<Media> group)
    {
        return group.Count > 0 && group.All(HasUnknownDate);
    }

    public static string BuildGroupName(IReadOnlyList<Media> group)
    {
        if (group.Count == 0)
        {
            return "빈 사진 그룹";
        }

        if (GroupHasUnknownDate(group))
        {
            return UnknownDateGroupName;
        }

        var capturedAt = group
            .Select(GetCapturedAt)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .DefaultIfEmpty()
            .Min();

        return $"{capturedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} 사진 그룹";
    }

    public static Guid CreateTemporaryGroupId(IReadOnlyList<Media> group)
    {
        if (group.Count == 0)
        {
            return Guid.NewGuid();
        }

        var first = group[0].Id.ToByteArray();
        var last = group[^1].Id.ToByteArray();
        var bytes = new byte[16];
        for (var i = 0; i < 8; i++)
        {
            bytes[i] = first[i];
            bytes[i + 8] = last[i];
        }

        var countBytes = BitConverter.GetBytes((ushort)Math.Min(group.Count, ushort.MaxValue));
        bytes[14] = countBytes[0];
        bytes[15] = countBytes[1];
        return new Guid(bytes);
    }

    public static bool AreSequentialFileNames(string previousFileName, string nextFileName)
    {
        if (!TryParseFileName(previousFileName, out var previousPrefix, out var previousNumber)
            || !TryParseFileName(nextFileName, out var nextPrefix, out var nextNumber))
        {
            return false;
        }

        if (!string.Equals(previousPrefix, nextPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return nextNumber == previousNumber + 1;
    }

    public static string GetOriginalFolder(Media media)
    {
        if (string.IsNullOrWhiteSpace(media.OriginalPath))
        {
            return string.Empty;
        }

        return Path.GetDirectoryName(media.OriginalPath) ?? string.Empty;
    }

    private IReadOnlyList<IReadOnlyList<Media>> GroupKnownDateMedia(IEnumerable<Media> mediaItems)
    {
        var ordered = mediaItems
            .OrderBy(media => media.CapturedAt)
            .ThenBy(media => media.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return BuildGroups(ordered, BelongsToSameKnownDateGroup);
    }

    private IReadOnlyList<IReadOnlyList<Media>> GroupUnknownDateMedia(IEnumerable<Media> mediaItems)
    {
        var ordered = mediaItems
            .OrderBy(media => media.ImportedAt)
            .ThenBy(GetOriginalFolder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(media => media.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return BuildGroups(ordered, BelongsToSameUnknownDateGroup);
    }

    private static IReadOnlyList<IReadOnlyList<Media>> BuildGroups(
        IReadOnlyList<Media> ordered,
        Func<Media, Media, bool> belongsTogether)
    {
        if (ordered.Count == 0)
        {
            return [];
        }

        var groups = new List<List<Media>>();
        var current = new List<Media> { ordered[0] };

        for (var index = 1; index < ordered.Count; index++)
        {
            var previous = current[^1];
            var next = ordered[index];

            if (belongsTogether(previous, next))
            {
                current.Add(next);
                continue;
            }

            groups.Add(current);
            current = [next];
        }

        groups.Add(current);
        return groups;
    }

    private bool BelongsToSameKnownDateGroup(Media previous, Media next)
    {
        if (previous.CapturedAt is null || next.CapturedAt is null)
        {
            return false;
        }

        var previousAt = previous.CapturedAt.Value;
        var nextAt = next.CapturedAt.Value;

        if (previousAt.Date != nextAt.Date)
        {
            return false;
        }

        var gap = Abs(nextAt - previousAt);
        if (gap <= _sessionGap)
        {
            return true;
        }

        return gap <= _extendedSessionGap && AreSequentialFileNames(previous.FileName, next.FileName);
    }

    private bool BelongsToSameUnknownDateGroup(Media previous, Media next)
    {
        if (!string.Equals(GetOriginalFolder(previous), GetOriginalFolder(next), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var gap = Abs(next.ImportedAt - previous.ImportedAt);
        if (gap <= _sessionGap)
        {
            return true;
        }

        if (gap <= _extendedSessionGap && AreSequentialFileNames(previous.FileName, next.FileName))
        {
            return true;
        }

        // Filename continuity on the same import day is an auxiliary signal.
        return previous.ImportedAt.Date == next.ImportedAt.Date
            && AreSequentialFileNames(previous.FileName, next.FileName);
    }

    private static TimeSpan Abs(TimeSpan value) => value < TimeSpan.Zero ? value.Negate() : value;

    private static bool TryParseFileName(string fileName, out string prefix, out int number)
    {
        prefix = string.Empty;
        number = 0;

        var name = Path.GetFileNameWithoutExtension(fileName);
        var match = TrailingNumberRegex.Match(name);
        if (!match.Success)
        {
            return false;
        }

        prefix = match.Groups["prefix"].Value;
        return int.TryParse(
            match.Groups["number"].Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out number);
    }
}
