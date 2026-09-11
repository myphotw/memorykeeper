using CommunityToolkit.Mvvm.ComponentModel;
using MemoryKeeper.Application.DTOs;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MemoryKeeper.App.Models;

public partial class PendingMemoryMediaItem : ObservableObject
{
    public PendingMemoryMediaItem(
        PendingMemoryItemDto media,
        PlaceDto? registeredPlace = null,
        CleanupMode cleanupMode = CleanupMode.Place)
    {
        Media = media;
        EffectiveDisplayMedia = media.WithEffectiveGeography(registeredPlace);
        CleanupMode = cleanupMode;
        IsIncluded = false;
    }

    public PendingMemoryItemDto Media { get; }

    public PendingMemoryItemDto EffectiveDisplayMedia { get; }

    public CleanupMode CleanupMode { get; }

    public Guid MediaId => Media.MediaId;

    public string FileName => Media.FileName;

    public string CapturedAtText =>
        string.Equals(Media.UserCapturePrecision, "DATE", StringComparison.OrdinalIgnoreCase)
            ? FormatDateOnly(Media.EffectiveCaptureDate, Media.CapturedAt)
            : Media.CapturedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "촬영일 정보 없음";

    public string DateBasisText => CleanupDisplayText.DateBasis(Media.DateBasis);

    public string DateCleanupReasonText => CleanupDisplayText.Reason(Media.DateCleanupReason);

    public bool HasUserCaptureOverride =>
        string.Equals(Media.DateBasis, "USER", StringComparison.OrdinalIgnoreCase);

    public string AbsoluteLibraryPath => Media.AbsoluteLibraryPath;

    public bool HasGps => Media.HasGps;

    public string GpsStatusText => Media.GpsStatusText;

    public string PlaceStatusText => Media.PlaceStatusText;

    public string StatusSummaryText => CleanupMode == CleanupMode.CaptureDate
        ? $"{DateCleanupReasonText} · {DateBasisText}"
        : $"{GpsStatusText} · {PlaceStatusText}";

    public string GeographyText => EffectiveDisplayMedia.GeographyText;

    public string SuggestedPlaceText => string.IsNullOrWhiteSpace(Media.SuggestedPlaceName)
        ? string.Empty
        : $"추천 장소: {Media.SuggestedPlaceName}";

    [ObservableProperty]
    private bool isIncluded;

    [ObservableProperty]
    private BitmapImage? thumbnailImage;

    [ObservableProperty]
    private bool isThumbnailLoading;

    private static string FormatDateOnly(string effectiveCaptureDate, DateTimeOffset? fallback) =>
        DateOnly.TryParseExact(
            effectiveCaptureDate,
            "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var date)
            ? date.ToString("yyyy.MM.dd", System.Globalization.CultureInfo.InvariantCulture)
            : fallback?.ToLocalTime().ToString("yyyy.MM.dd") ?? "촬영일 정보 없음";
}

public enum CleanupMode
{
    Place,
    CaptureDate,
}

public sealed class PlaceCleanupGroupItem(PlaceCleanupGroupDto group)
{
    public PlaceCleanupGroupDto Group { get; } = group;

    public string GroupId => Group.GroupId;
    public string Title => string.IsNullOrWhiteSpace(Group.Title) ? "장소 정리 필요" : Group.Title;
    public string CountText => $"사진 {Group.MediaCount:N0}장";
    public string PeriodText => CleanupDisplayText.Period(
        Group.FirstEffectiveCaptureDatetime,
        Group.LastEffectiveCaptureDatetime);
    public string LocationText => string.IsNullOrWhiteSpace(Group.EstimatedLocation)
        ? "예상 위치 없음"
        : Group.EstimatedLocation!;
    public string IssueText => Group.IssueType switch
    {
        "PLACE_UNASSIGNED" => "장소 미지정",
        "HIERARCHY_INCOMPLETE" => "장소 정보 확인 필요",
        _ => "장소 정리 필요",
    };
    public string StatusText => Group.ProcessingStatus?.ToUpperInvariant() switch
    {
        "PROCESSING" => "처리 중",
        "COMPLETED" => "완료",
        _ => "미처리",
    };
    public string IssueStatusText => $"{IssueText} · {StatusText}";
}

public sealed class CaptureDateCleanupGroupItem(CaptureDateCleanupGroupDto group)
{
    public CaptureDateCleanupGroupDto Group { get; } = group;

    public string GroupId => Group.GroupId;
    public string Title => string.IsNullOrWhiteSpace(Group.Title) ? "촬영일 정리 필요" : Group.Title;
    public string CountText => $"사진 {Group.MediaCount:N0}장";
    public string PeriodText => CleanupDisplayText.Period(
        Group.FirstEffectiveCaptureDatetime,
        Group.LastEffectiveCaptureDatetime);
    public string ReasonText => CleanupDisplayText.Reason(Group.CleanupReason);
    public string DateBasisText => CleanupDisplayText.DateBasis(Group.DateBasis);
    public string DateSummaryText => string.IsNullOrWhiteSpace(Group.EffectiveCaptureDate)
        ? DateBasisText
        : $"{Group.EffectiveCaptureDate} · {DateBasisText}";
}

public static class CleanupDisplayText
{
    public static string DateBasis(string? value) => value?.ToUpperInvariant() switch
    {
        "USER" => "사용자 지정",
        "EXIF" => "사진 촬영정보",
        "IMPORTED" => "가져온 날짜 기준",
        "CREATED" => "파일 생성일 기준",
        _ => "날짜 정보 없음",
    };

    public static string Reason(string? value) => value?.ToUpperInvariant() switch
    {
        "MISSING_CAPTURE_DATE" => "촬영일 정보 없음",
        "FALLBACK_DATE_REQUIRES_REVIEW" => "촬영일 확인 필요",
        "INVALID_CAPTURE_DATE" => "촬영일 정보 확인 필요",
        _ => "촬영일 정리 필요",
    };

    public static string Period(DateTimeOffset? first, DateTimeOffset? last)
    {
        if (first is null && last is null)
        {
            return "촬영일 정보 없음";
        }

        var firstText = first?.ToLocalTime().ToString("yyyy.MM.dd") ?? "-";
        var lastText = last?.ToLocalTime().ToString("yyyy.MM.dd") ?? "-";
        return firstText == lastText ? firstText : $"{firstText} ~ {lastText}";
    }
}

public sealed class PendingMemoryGroupItem
{
    public PendingMemoryGroupItem(
        PendingMemoryGroupDto group,
        IReadOnlyDictionary<Guid, PendingMemoryMediaItem>? loadedItemsById = null)
    {
        Group = group;
        MediaItems = group.MediaItems
            .Select(item => loadedItemsById is not null
                            && loadedItemsById.TryGetValue(item.MediaId, out var loadedItem)
                ? loadedItem
                : new PendingMemoryMediaItem(item))
            .ToList();
    }

    public PendingMemoryGroupDto Group { get; }

    public Guid GroupId => Group.GroupId;

    public string GroupName => Group.HasUnknownDate ? "날짜 미상" : Group.GroupName;

    public int MediaCount => MediaItems.Count;

    public bool HasUnknownDate => Group.HasUnknownDate;

    public string PeriodText
    {
        get
        {
            if (Group.HasUnknownDate)
            {
                return "촬영일 정보 없음";
            }

            var first = Group.FirstCapturedDate?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "-";
            var last = Group.LastCapturedDate?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "-";
            return $"{first} ~ {last}";
        }
    }

    public string EstimatedLocationText
    {
        get
        {
            var summary = PendingMemoryGroupDto.GetEffectiveLocationSummary(
                MediaItems.Select(item => item.EffectiveDisplayMedia));
            return string.IsNullOrWhiteSpace(summary)
                ? "예상 위치 없음"
                : summary;
        }
    }

    public string ProcessingStatus => Group.ProcessingStatus;

    public IReadOnlyList<PendingMemoryMediaItem> MediaItems { get; }

    public string SummaryText =>
        $"사진 {MediaCount}장 · {EstimatedLocationText} · {ProcessingStatus}";
}
