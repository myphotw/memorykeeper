using CommunityToolkit.Mvvm.ComponentModel;
using MemoryKeeper.Application.DTOs;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MemoryKeeper.App.Models;

public partial class HomeHeroItem : ObservableObject
{
    public HomeHeroItem(HeroMemoryDto dto)
    {
        Dto = dto;
    }

    public HeroMemoryDto Dto { get; }

    public Guid PlaceId => Dto.PlaceId;

    public string KindLabel => Dto.KindLabel;

    public string Title => Dto.Title;

    public string Subtitle => Dto.Subtitle;

    public string DateText => Dto.DateText;

    public string Description => Dto.Description;

    public string PhotoCountText =>
        Dto.PhotoCount > 0
            ? $"추억 {Dto.PhotoCount}장"
            : Dto.VisitRecordCount > 0
                ? $"방문 {Dto.VisitRecordCount}회"
                : string.Empty;

    public string AbsoluteLibraryPath => Dto.AbsoluteLibraryPath ?? string.Empty;

    public Guid? RepresentativeMediaId => Dto.RepresentativeMediaId;

    [ObservableProperty]
    private BitmapImage? thumbnailImage;

    [ObservableProperty]
    private bool isThumbnailLoading;

    [ObservableProperty]
    private bool isSelected;
}

public partial class HomeTodayItem : ObservableObject
{
    public HomeTodayItem(TodayMemoryPhotoDto dto)
    {
        Dto = dto;
    }

    public TodayMemoryPhotoDto Dto { get; }

    public Guid MediaId => Dto.MediaId;

    public string PlaceName => string.IsNullOrWhiteSpace(Dto.PlaceName) ? "장소 미상" : Dto.PlaceName;

    public string YearsAgoText => $"{Dto.YearsAgo}년 전 오늘";

    public string TagsText => Dto.TopTags.Count == 0 ? string.Empty : string.Join(" · ", Dto.TopTags);

    public string AbsoluteLibraryPath => Dto.AbsoluteLibraryPath;

    [ObservableProperty]
    private BitmapImage? thumbnailImage;

    [ObservableProperty]
    private bool isThumbnailLoading;
}

public partial class HomeRecentVisitItem : ObservableObject
{
    public HomeRecentVisitItem(RecentVisitDto dto)
    {
        Dto = dto;
    }

    public RecentVisitDto Dto { get; }

    public Guid PlaceId => Dto.PlaceId;

    public string PlaceName => Dto.PlaceName;

    public string VisitCountText => $"방문 {Dto.VisitRecordCount}회";

    public string LastVisitText => Dto.LastVisitDate?.ToLocalTime().ToString("yyyy.MM.dd") ?? string.Empty;

    public string TagsText => Dto.TopTags.Count == 0 ? string.Empty : string.Join(" · ", Dto.TopTags);

    public string AbsoluteLibraryPath => Dto.AbsoluteLibraryPath ?? string.Empty;

    public Guid? RepresentativeMediaId => Dto.RepresentativeMediaId;

    [ObservableProperty]
    private BitmapImage? thumbnailImage;

    [ObservableProperty]
    private bool isThumbnailLoading;
}

public partial class HomePhotoItem : ObservableObject
{
    public HomePhotoItem(DashboardPhotoDto dto)
    {
        Dto = dto;
    }

    public DashboardPhotoDto Dto { get; }

    public Guid MediaId => Dto.MediaId;

    public string FileName => Dto.FileName;

    public string AbsoluteLibraryPath => Dto.AbsoluteLibraryPath;

    [ObservableProperty]
    private BitmapImage? thumbnailImage;

    [ObservableProperty]
    private bool isThumbnailLoading;
}

public partial class HomeHeroIndicator : ObservableObject
{
    public HomeHeroIndicator(int index)
    {
        Index = index;
    }

    public int Index { get; }

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private double dotOpacity = 0.35;

    partial void OnIsSelectedChanged(bool value) => DotOpacity = value ? 1.0 : 0.35;
}
