using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.DTOs.Gallery;
using MemoryKeeper.Application.Interfaces;

namespace MemoryKeeper.Application.Services;

/// <summary>Exports immutable NAS originals and writes MemoryKeeper metadata to XMP sidecars.</summary>
public sealed class PhotoExportService
{
    private readonly IPhotoExportSource _source;

    public PhotoExportService(IPhotoExportSource source) =>
        _source = source ?? throw new ArgumentNullException(nameof(source));

    public Task<PhotoExportResultDto> ExportAsync(
        string destinationRoot,
        IProgress<PhotoExportProgressDto>? progress = null,
        CancellationToken cancellationToken = default) =>
        ExportCoreAsync(destinationRoot, null, progress, cancellationToken);

    public Task<PhotoExportResultDto> ExportSelectedAsync(
        string destinationRoot,
        IReadOnlyCollection<string> fileIds,
        IProgress<PhotoExportProgressDto>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileIds);
        var selectedFileIds = fileIds
            .Where(fileId => !string.IsNullOrWhiteSpace(fileId))
            .Select(fileId => fileId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (selectedFileIds.Length == 0)
        {
            throw new ArgumentException("내보낼 사진을 선택해 주세요.", nameof(fileIds));
        }

        return ExportCoreAsync(destinationRoot, selectedFileIds, progress, cancellationToken);
    }

    private async Task<PhotoExportResultDto> ExportCoreAsync(
        string destinationRoot,
        IReadOnlyCollection<string>? selectedFileIds,
        IProgress<PhotoExportProgressDto>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(destinationRoot))
        {
            throw new ArgumentException("내보낼 폴더를 선택해 주세요.", nameof(destinationRoot));
        }

        Directory.CreateDirectory(destinationRoot);
        var catalog = await _source.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        var requestedFileIds = selectedFileIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = requestedFileIds is null
            ? catalog.ToList()
            : catalog
                .Where(item => requestedFileIds.Contains(item.FileId))
                .DistinctBy(item => item.FileId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        var resolvedFileIds = items
            .Select(item => item.FileId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unresolvedCount = requestedFileIds is null
            ? 0
            : requestedFileIds.Count(fileId => !resolvedFileIds.Contains(fileId));
        var totalCount = requestedFileIds?.Count ?? items.Count;
        var exported = 0;
        var duplicateSkipped = 0;
        var metadataPartial = 0;
        var copyFailed = unresolvedCount;
        var completed = unresolvedCount;

        if (unresolvedCount > 0)
        {
            progress?.Report(new PhotoExportProgressDto
            {
                Total = totalCount,
                Completed = completed,
                Failed = copyFailed,
                DuplicateSkipped = duplicateSkipped,
            });
        }

        var manifest = await PhotoExportManifestStore.OpenAsync(
            destinationRoot,
            cancellationToken).ConfigureAwait(false);
        var existingFileIds = await manifest.FindExistingAsync(
            items.Select(item => item.FileId),
            cancellationToken).ConfigureAwait(false);
        duplicateSkipped = existingFileIds.Count;
        completed += duplicateSkipped;
        if (duplicateSkipped > 0)
        {
            progress?.Report(new PhotoExportProgressDto
            {
                Total = totalCount,
                Completed = completed,
                Failed = copyFailed,
                DuplicateSkipped = duplicateSkipped,
            });
        }

        var pendingItems = items
            .Where(item => !existingFileIds.Contains(item.FileId))
            .ToList();
        var resolvedItems = new List<ResolvedExportItem>(pendingItems.Count);
        foreach (var item in pendingItems.OrderBy(item => item.FileId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var detail = await _source.GetDetailAsync(item.FileId, cancellationToken).ConfigureAwait(false);
                resolvedItems.Add(new ResolvedExportItem(item, detail));
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                copyFailed++;
                completed++;
                progress?.Report(new PhotoExportProgressDto
                {
                    Total = totalCount,
                    Completed = completed,
                    Failed = copyFailed,
                    DuplicateSkipped = duplicateSkipped,
                    CurrentFileName = item.Filename,
                });
            }
        }

        var plans = BuildExportPlans(destinationRoot, resolvedItems);
        foreach (var plan in plans)
        {
            var item = plan.Item;
            var detail = plan.SourceDetail;
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Directory.CreateDirectory(plan.Directory);
                var targetPath = plan.TargetPath
                                 ?? ResolveCollisionPath(plan.Directory, item.Filename);
                var temporaryPath = targetPath + ".partial";
                try
                {
                    await using (var output = new FileStream(
                                     temporaryPath,
                                     FileMode.CreateNew,
                                     FileAccess.Write,
                                     FileShare.None,
                                     81920,
                                     useAsync: true))
                    {
                        await _source.DownloadOriginalAsync(
                            item.FileId,
                            detail.OriginalUrl,
                            output,
                            cancellationToken).ConfigureAwait(false);
                    }

                    var exportedSha256 = await PhotoExportManifestStore.ComputeSha256Async(
                        temporaryPath,
                        cancellationToken).ConfigureAwait(false);
                    var exportedFileSize = new FileInfo(temporaryPath).Length;
                    File.Move(temporaryPath, targetPath, overwrite: false);
                    manifest.Record(item.FileId, targetPath, exportedSha256, exportedFileSize);
                    exported++;
                }
                catch
                {
                    TryDelete(temporaryPath);
                    throw;
                }

                try
                {
                    var sidecarPath = Path.ChangeExtension(targetPath, ".xmp");
                    await using var sidecar = new FileStream(
                        sidecarPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        81920,
                        useAsync: true);
                    await using var writer = new StreamWriter(
                        sidecar,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    await writer.WriteAsync(
                        BuildXmp(item, detail.Detail).AsMemory(),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    metadataPartial++;
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                copyFailed++;
            }
            finally
            {
                completed++;
                progress?.Report(new PhotoExportProgressDto
                {
                    Total = totalCount,
                    Completed = completed,
                    Failed = copyFailed,
                    DuplicateSkipped = duplicateSkipped,
                    CurrentFileName = item.Filename,
                });
            }
        }

        await manifest.SaveIfChangedAsync(cancellationToken).ConfigureAwait(false);
        return new PhotoExportResultDto
        {
            TotalCount = totalCount,
            ExportedCount = exported,
            DuplicateSkippedCount = duplicateSkipped,
            MetadataPartialCount = metadataPartial,
            CopyFailedCount = copyFailed,
            DestinationPath = Path.GetFullPath(destinationRoot),
        };
    }

    private static IReadOnlyList<PhotoExportPlan> BuildExportPlans(
        string destinationRoot,
        IReadOnlyList<ResolvedExportItem> items)
    {
        var dated = items
            .Select(item => new
            {
                Item = item,
                Date = ResolveEffectiveDate(item),
                Datetime = ResolveEffectiveDatetime(item),
                Precision = ResolveEffectivePrecision(item),
                Extension = ResolveOriginalExtension(item),
            })
            .Where(candidate => candidate.Date.HasValue && candidate.Extension is not null)
            .ToList();
        var datedFileIds = dated
            .Select(candidate => candidate.Item.Item.FileId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var plans = new List<PhotoExportPlan>(items.Count);
        var reservedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dateGroup in dated
                     .GroupBy(candidate => candidate.Date!.Value)
                     .OrderBy(group => group.Key))
        {
            var sequence = 1;
            foreach (var candidate in dateGroup
                         .OrderBy(candidate => IsDateTimePrecision(candidate.Precision) ? 0 : 1)
                         .ThenBy(candidate => candidate.Datetime.HasValue ? 0 : 1)
                         .ThenBy(candidate => candidate.Datetime?.TimeOfDay ?? TimeSpan.Zero)
                         .ThenBy(candidate => candidate.Item.Item.FileId, StringComparer.Ordinal))
            {
                var directory = ResolveExportDirectory(destinationRoot, candidate.Item.Item);
                string targetPath;
                do
                {
                    var filename = $"{dateGroup.Key:yyyyMMdd}_{sequence:000}{candidate.Extension}";
                    targetPath = Path.Combine(directory, filename);
                    sequence++;
                }
                while (HasExportPathCollision(targetPath, reservedPaths));

                ReserveExportPaths(targetPath, reservedPaths);
                plans.Add(new PhotoExportPlan(
                    candidate.Item.Item,
                    candidate.Item.Detail,
                    directory,
                    targetPath));
            }
        }

        plans.AddRange(items
            .Where(item => !datedFileIds.Contains(item.Item.FileId))
            .OrderBy(item => item.Item.FileId, StringComparer.Ordinal)
            .Select(item => new PhotoExportPlan(
                item.Item,
                item.Detail,
                ResolveExportDirectory(destinationRoot, item.Item),
                null)));
        return plans;
    }

    private static DateOnly? ResolveEffectiveDate(ResolvedExportItem item)
    {
        var precision = ResolveEffectivePrecision(item);
        var date = ParseEffectiveCaptureDate(item.Detail.Detail.EffectiveCaptureDate)
                   ?? item.Item.EffectiveCaptureDate;
        return date.HasValue
               && (IsDateTimePrecision(precision)
                   || string.Equals(precision, "DATE", StringComparison.OrdinalIgnoreCase))
            ? date
            : null;
    }

    private static string? ResolveEffectivePrecision(ResolvedExportItem item) =>
        !string.IsNullOrWhiteSpace(item.Detail.Detail.EffectiveCapturePrecision)
            ? item.Detail.Detail.EffectiveCapturePrecision
            : item.Item.EffectiveCapturePrecision;

    private static DateTimeOffset? ResolveEffectiveDatetime(ResolvedExportItem item) =>
        item.Detail.Detail.EffectiveCaptureDatetime
        ?? item.Item.EffectiveCaptureDatetime;

    private static DateOnly? ParseEffectiveCaptureDate(string? value) =>
        DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;

    private static bool IsDateTimePrecision(string? precision) =>
        string.Equals(precision, "DATETIME", StringComparison.OrdinalIgnoreCase);

    private static string ResolveExportDirectory(string destinationRoot, PhotoExportCatalogItemDto item) =>
        Path.Combine(
            destinationRoot,
            SafeSegment(item.Year, GalleryHierarchyService.UnclassifiedTitle),
            SafeSegment(item.Country, GalleryHierarchyService.OtherTitle),
            SafeSegment(item.Region, GalleryHierarchyService.OtherTitle),
            SafeSegment(item.Place, GalleryHierarchyService.UnclassifiedTitle));

    private static string? ResolveOriginalExtension(ResolvedExportItem item)
    {
        foreach (var candidate in new[]
                 {
                     item.Detail.Detail.Extension,
                     Path.GetExtension(item.Detail.Detail.Filename),
                     item.Item.Extension,
                     Path.GetExtension(item.Item.Filename),
                 })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var extension = candidate.Trim();
            if (!extension.StartsWith('.'))
            {
                extension = "." + extension;
            }

            if (extension.Length > 1 && extension.Skip(1).All(char.IsAsciiLetterOrDigit))
            {
                return extension;
            }
        }

        return null;
    }

    private static bool HasExportPathCollision(string targetPath, ISet<string> reservedPaths) =>
        File.Exists(targetPath)
        || Directory.Exists(targetPath)
        || File.Exists(targetPath + ".partial")
        || Directory.Exists(targetPath + ".partial")
        || File.Exists(Path.ChangeExtension(targetPath, ".xmp"))
        || Directory.Exists(Path.ChangeExtension(targetPath, ".xmp"))
        || reservedPaths.Contains(targetPath)
        || reservedPaths.Contains(targetPath + ".partial")
        || reservedPaths.Contains(Path.ChangeExtension(targetPath, ".xmp"))
        || HasExistingStemCollision(targetPath);

    private static bool HasExistingStemCollision(string targetPath)
    {
        var directory = Path.GetDirectoryName(targetPath);
        var stem = Path.GetFileNameWithoutExtension(targetPath);
        return !string.IsNullOrWhiteSpace(directory)
               && Directory.Exists(directory)
               && Directory.EnumerateFileSystemEntries(directory, stem + ".*").Any();
    }

    private static void ReserveExportPaths(string targetPath, ISet<string> reservedPaths)
    {
        reservedPaths.Add(targetPath);
        reservedPaths.Add(targetPath + ".partial");
        reservedPaths.Add(Path.ChangeExtension(targetPath, ".xmp"));
    }

    internal static string ResolveCollisionPath(string directory, string filename)
    {
        var safeName = SafeSegment(Path.GetFileName(filename), "photo");
        var extension = Path.GetExtension(safeName);
        var stem = Path.GetFileNameWithoutExtension(safeName);
        var candidate = Path.Combine(directory, safeName);
        var suffix = 2;
        while (File.Exists(candidate)
               || File.Exists(candidate + ".partial")
               || File.Exists(Path.ChangeExtension(candidate, ".xmp")))
        {
            candidate = Path.Combine(directory, $"{stem}_{suffix}{extension}");
            suffix++;
        }

        return candidate;
    }

    private sealed record ResolvedExportItem(
        PhotoExportCatalogItemDto Item,
        PhotoExportSourceDetailDto Detail);

    private sealed record PhotoExportPlan(
        PhotoExportCatalogItemDto Item,
        PhotoExportSourceDetailDto SourceDetail,
        string Directory,
        string? TargetPath);

    private static string BuildXmp(
        PhotoExportCatalogItemDto item,
        MemoryKeeper.Application.DTOs.Gallery.PhotoDetailDto detail)
    {
        XNamespace x = "adobe:ns:meta/";
        XNamespace rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        XNamespace xmp = "http://ns.adobe.com/xap/1.0/";
        XNamespace exif = "http://ns.adobe.com/exif/1.0/";
        XNamespace mk = "https://memorykeeper.local/ns/1.0/";
        XNamespace xml = XNamespace.Xml;

        var metadata = detail.Metadata;
        var capture = GetDate(metadata, "datetime_original") ?? item.CaptureDatetime;
        var latitude = GetDouble(metadata, "gps_lat") ?? item.Latitude;
        var longitude = GetDouble(metadata, "gps_lon") ?? item.Longitude;
        var country = GetString(metadata, "country") ?? item.Country;
        var province = GetString(metadata, "province");
        var city = GetString(metadata, "city") ?? item.Region;
        var district = GetString(metadata, "district");
        var rawAddress = GetString(metadata, "place_name") ?? detail.GeocodedPlaceName;
        var place = detail.PlaceDisplayName ?? detail.PlaceCanonicalName ?? item.Place;
        var tags = detail.Tags
            .Select(tag => string.IsNullOrWhiteSpace(tag.DisplayName) ? tag.Tag : tag.DisplayName)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var description = new XElement(rdf + "Description",
            new XAttribute(rdf + "about", string.Empty),
            new XAttribute(XNamespace.Xmlns + "dc", dc),
            new XAttribute(XNamespace.Xmlns + "xmp", xmp),
            new XAttribute(XNamespace.Xmlns + "exif", exif),
            new XAttribute(XNamespace.Xmlns + "mk", mk),
            new XAttribute(mk + "favorite", detail.Favorite.ToString().ToLowerInvariant()),
            new XAttribute(mk + "country", country ?? string.Empty),
            new XAttribute(mk + "province", province ?? string.Empty),
            new XAttribute(mk + "city", city ?? string.Empty),
            new XAttribute(mk + "district", district ?? string.Empty),
            new XAttribute(mk + "place", place ?? string.Empty),
            new XAttribute(mk + "rawAddress", rawAddress ?? string.Empty));

        if (capture.HasValue)
        {
            description.Add(new XAttribute(xmp + "CreateDate", capture.Value.ToString("O", CultureInfo.InvariantCulture)));
        }

        if (latitude.HasValue)
        {
            description.Add(new XAttribute(exif + "GPSLatitude", latitude.Value.ToString("R", CultureInfo.InvariantCulture)));
        }

        if (longitude.HasValue)
        {
            description.Add(new XAttribute(exif + "GPSLongitude", longitude.Value.ToString("R", CultureInfo.InvariantCulture)));
        }

        if (tags.Count > 0)
        {
            description.Add(new XElement(dc + "subject",
                new XElement(rdf + "Bag", tags.Select(tag => new XElement(rdf + "li", tag)))));
        }

        if (!string.IsNullOrWhiteSpace(detail.Memo))
        {
            description.Add(new XElement(dc + "description",
                new XElement(rdf + "Alt",
                    new XElement(rdf + "li", new XAttribute(xml + "lang", "x-default"), detail.Memo))));
        }

        return new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(x + "xmpmeta",
                new XAttribute(XNamespace.Xmlns + "x", x),
                new XElement(rdf + "RDF", description))).ToString();
    }

    private static string SafeSegment(string? value, string fallback)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = trimmed.Select(character =>
            invalid.Contains(character) || character is '/' or '\\' ? '_' : character).ToArray();
        var safe = new string(chars).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
    }

    private static string? GetString(IReadOnlyDictionary<string, JsonElement> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && value.ValueKind is not JsonValueKind.Null
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;

    private static double? GetDouble(IReadOnlyDictionary<string, JsonElement> metadata, string key)
    {
        var raw = GetString(metadata, key);
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static DateTimeOffset? GetDate(IReadOnlyDictionary<string, JsonElement> metadata, string key)
    {
        var raw = GetString(metadata, key);
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var value)
            ? value
            : null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup of an incomplete export copy.
        }
    }
}
