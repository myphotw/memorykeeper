using CommunityToolkit.Mvvm.ComponentModel;
using MemoryKeeper.Application.DTOs;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MemoryKeeper.App.Models;

public partial class PendingMemoryMediaItem : ObservableObject
{
    public PendingMemoryMediaItem(PendingMemoryItemDto media)
    {
        Media = media;
        IsIncluded = true;
    }

    public PendingMemoryItemDto Media { get; }

    public Guid MediaId => Media.MediaId;

    public string FileName => Media.FileName;

    public string CapturedAtText => Media.CapturedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "-";

    public string AbsoluteLibraryPath => Media.AbsoluteLibraryPath;

    public bool HasGps => Media.HasGps;

    public string GpsStatusText => Media.GpsStatusText;

    public string PlaceStatusText => Media.PlaceStatusText;

    public string StatusSummaryText => $"{GpsStatusText} · {PlaceStatusText}";

    [ObservableProperty]
    private bool isIncluded;

    [ObservableProperty]
    private BitmapImage? thumbnailImage;

    [ObservableProperty]
    private bool isThumbnailLoading;
}

public sealed class PendingMemoryGroupItem
{
    public PendingMemoryGroupItem(PendingMemoryGroupDto group)
    {
        Group = group;
        MediaItems = group.MediaItems
            .Select(item => new PendingMemoryMediaItem(item))
            .ToList();
    }

    public PendingMemoryGroupDto Group { get; }

    public Guid GroupId => Group.GroupId;

    public string GroupName => Group.HasUnknownDate ? "날짜 미상" : Group.GroupName;

    public int MediaCount => Group.MediaCount;

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

    public string EstimatedLocationText =>
        string.IsNullOrWhiteSpace(Group.EstimatedLocationSummary)
            ? "예상 위치 없음"
            : Group.EstimatedLocationSummary;

    public string ProcessingStatus => Group.ProcessingStatus;

    public IReadOnlyList<PendingMemoryMediaItem> MediaItems { get; }

    public string SummaryText =>
        $"사진 {MediaCount}장 · {EstimatedLocationText} · {ProcessingStatus}";
}
