using CommunityToolkit.Mvvm.ComponentModel;
using MemoryKeeper.Application.DTOs;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MemoryKeeper.App.Models;

public partial class PendingMemoryMediaItem : ObservableObject
{
    public PendingMemoryMediaItem(PendingMemoryItemDto media, PlaceDto? registeredPlace = null)
    {
        Media = media;
        EffectiveDisplayMedia = media.WithEffectiveGeography(registeredPlace);
        IsIncluded = false;
    }

    public PendingMemoryItemDto Media { get; }

    public PendingMemoryItemDto EffectiveDisplayMedia { get; }

    public Guid MediaId => Media.MediaId;

    public string FileName => Media.FileName;

    public string CapturedAtText => Media.CapturedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "-";

    public string AbsoluteLibraryPath => Media.AbsoluteLibraryPath;

    public bool HasGps => Media.HasGps;

    public string GpsStatusText => Media.GpsStatusText;

    public string PlaceStatusText => Media.PlaceStatusText;

    public string StatusSummaryText => $"{GpsStatusText} · {PlaceStatusText}";

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
