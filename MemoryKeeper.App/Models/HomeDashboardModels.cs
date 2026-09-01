using CommunityToolkit.Mvvm.ComponentModel;
using MemoryKeeper.Application.DTOs;
using Microsoft.UI.Xaml.Media;
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

    public string Country => string.IsNullOrWhiteSpace(Dto.Country) ? string.Empty : Dto.Country;

    public string FlagEmoji => CountryFlag(Country);

    public string TitleLine =>
        string.IsNullOrWhiteSpace(Country)
            ? PlaceName
            : $"{PlaceName}, {Country} {FlagEmoji}".Trim();

    public string RegionLine =>
        string.IsNullOrWhiteSpace(Country)
            ? VisitCountText
            : $"{Country} · {VisitCountText}";

    public string VisitCountText => $"방문 {Dto.VisitRecordCount}회";

    public string PhotoCountText => Dto.PhotoCount > 0 ? $"사진 {Dto.PhotoCount}장" : string.Empty;

    public string LastVisitText => Dto.LastVisitDate?.ToLocalTime().ToString("yyyy.MM.dd") ?? string.Empty;

    public string MetaLine
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(LastVisitText))
            {
                parts.Add(LastVisitText);
            }

            if (!string.IsNullOrWhiteSpace(PhotoCountText))
            {
                parts.Add(PhotoCountText);
            }

            return string.Join(" · ", parts);
        }
    }

    public string TagsText => Dto.TopTags.Count == 0 ? string.Empty : string.Join(" · ", Dto.TopTags);

    private static string CountryFlag(string country) =>
        country.Trim() switch
        {
            "대한민국" or "한국" or "Korea" or "South Korea" => "🇰🇷",
            "일본" or "Japan" => "🇯🇵",
            "중국" or "China" => "🇨🇳",
            "미국" or "United States" or "USA" => "🇺🇸",
            _ => string.Empty
        };

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

    public string? FallbackAbsoluteLibraryPath => Dto.FallbackAbsoluteLibraryPath;

    public string CaptionLine
    {
        get
        {
            var date = Dto.CapturedAt?.ToLocalTime().ToString("yyyy.MM.dd");
            var place = string.Join(
                ", ",
                new[] { Dto.PlaceName, Dto.Country }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (!string.IsNullOrWhiteSpace(date) && !string.IsNullOrWhiteSpace(place))
            {
                return $"{date}  {place}";
            }

            return date ?? place ?? FileName;
        }
    }

    [ObservableProperty]
    private BitmapImage? thumbnailImage;

    [ObservableProperty]
    private bool isThumbnailLoading;
}

public sealed class HomeYearBarItem
{
    public HomeYearBarItem(string year, int count, double heightRatio, double barHeight)
    {
        Year = year;
        Count = count;
        HeightRatio = heightRatio;
        BarHeight = barHeight;
    }

    public string Year { get; }

    public int Count { get; }

    public double HeightRatio { get; }

    public double BarHeight { get; }

    public string CountText => Count.ToString();
}

public sealed class HomeCountrySliceItem
{
    public HomeCountrySliceItem(string name, int count, double startAngle, double sweepAngle, Windows.UI.Color color)
    {
        Name = name;
        Count = count;
        StartAngle = startAngle;
        SweepAngle = sweepAngle;
        Color = color;
        Brush = new SolidColorBrush(color);
    }

    public string Name { get; }

    public int Count { get; }

    public double StartAngle { get; }

    public double SweepAngle { get; }

    public Windows.UI.Color Color { get; }

    public SolidColorBrush Brush { get; }

    public string LegendText => $"{Name}  {Count}";
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
