using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MemoryKeeper.App.Models;

public partial class VisitRecordPlaceItem : ObservableObject
{
    public VisitRecordPlaceItem(VisitRecordPlaceDto place, int? scopeYear = null)
    {
        Place = place;
        ScopeYear = scopeYear;
        TopTags = place.TopTags.ToList();
    }

    public VisitRecordPlaceDto Place { get; }

    public int? ScopeYear { get; }

    public Guid PlaceId => Place.PlaceId;

    public string PlaceName => Place.PlaceName;

    public string RegionSummary =>
        string.Join(" / ", new[] { Place.Country, Place.City }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public int VisitRecordCount => Place.VisitRecordCount;

    public int PhotoCount => Place.PhotoCount;

    public bool HasFavorite => Place.HasFavorite;

    public bool IsUnclassified => Place.IsUnclassified;

    public Guid? RepresentativeMediaId => Place.RepresentativeMediaId;

    public string? RepresentativeAbsolutePath => Place.RepresentativeAbsolutePath;

    public string DateRangeText
    {
        get
        {
            var first = FormatDate(Place.FirstCapturedDate);
            var last = FormatDate(Place.LastCapturedDate);
            return first == last ? first : $"{first} ~ {last}";
        }
    }

    public IReadOnlyList<string> TopTags { get; }

    public bool HasTags => TopTags.Count > 0;

    public string TagSummary => string.Join("   ", TopTags.Select(tag => $"#{tag}"));

    public double Latitude => Place.Latitude;

    public double Longitude => Place.Longitude;

    public double MarkerScale => Place.MarkerScale;

    public IReadOnlyList<VisitRecordPreviewPhotoDto> PreviewPhotos => Place.PreviewPhotos;

    public IReadOnlyList<VisitRecordPreviewPhotoDto> AllPhotos => Place.AllPhotos;

    public IReadOnlyList<int> CaptureYears => Place.CaptureYears;

    public bool HasCaptureYear(int year) =>
        CaptureYears.Contains(year)
        || AllPhotos.Any(photo => photo.CaptureYear == year)
        || ((Place.LastCapturedDate ?? Place.FirstCapturedDate)?.ToLocalTime().Year == year);

    public VisitRecordPlaceItem ForYear(int year) =>
        new(VisitRecordPlaceScoping.ScopeToYear(Place, year), year);

    [ObservableProperty]
    private BitmapImage? thumbnailImage;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private bool isHighlighted;

    [ObservableProperty]
    private bool isExpanded = true;

    [ObservableProperty]
    private Thickness selectionBorderThickness = new(1);

    partial void OnIsSelectedChanged(bool value)
    {
        UpdateChrome();
    }

    partial void OnIsHighlightedChanged(bool value)
    {
        UpdateChrome();
    }

    private void UpdateChrome()
    {
        SelectionBorderThickness = IsSelected || IsHighlighted ? new Thickness(2) : new Thickness(1);
    }

    private static string FormatDate(DateTimeOffset? value) => value?.ToLocalTime().ToString("yyyy.MM.dd") ?? "-";
}

public partial class VisitRecordYearGroup : ObservableObject
{
    public const int AllYearsSentinel = 0;

    public VisitRecordYearGroup(int year, IEnumerable<VisitRecordPlaceItem> places)
    {
        Year = year;
        YearTitle = year switch
        {
            AllYearsSentinel => "전체",
            < 0 => "년도 미상",
            _ => $"{year}년"
        };
        Places = new ObservableCollection<VisitRecordPlaceItem>(places);
        // 방문지도 진입 시 연도는 기본 접힘. 선택·포커스 시에만 펼친다.
        IsExpanded = false;
    }

    public int Year { get; }

    public bool IsAll => Year == AllYearsSentinel;

    public string YearTitle { get; }

    public ObservableCollection<VisitRecordPlaceItem> Places { get; }

    public int PlaceCount => Places.Count;

    public int PhotoCount => Places.Sum(place => place.PhotoCount);

    public string CountSummary => $"{PlaceCount}곳 · {PhotoCount}장";

    [ObservableProperty]
    private bool isExpanded;

    [ObservableProperty]
    private bool isSelected;

    public string ExpandGlyph => IsAll ? string.Empty : (IsExpanded ? "▼" : "▶");

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ExpandGlyph));
        foreach (var place in Places)
        {
            place.IsExpanded = value;
        }
    }
}

public partial class VisitPreviewItem : ObservableObject
{
    public VisitPreviewItem(VisitRecordPreviewPhotoDto photo)
    {
        Photo = photo;
    }

    public VisitRecordPreviewPhotoDto Photo { get; }

    public Guid MediaId => Photo.MediaId;

    public string FileName => Photo.FileName;

    public string AbsoluteLibraryPath => Photo.AbsoluteLibraryPath;

    public bool IsFavorite => Photo.IsFavorite;

    [ObservableProperty]
    private BitmapImage? thumbnailImage;
}
