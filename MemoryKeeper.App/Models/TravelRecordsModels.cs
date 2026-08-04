using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MemoryKeeper.Application.DTOs;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MemoryKeeper.App.Models;

public sealed class TravelYearChapterItem
{
    public TravelYearChapterItem(TravelYearChapterDto dto)
    {
        Year = dto.Year;
        YearTitle = dto.YearTitle;
        Trips = new ObservableCollection<TravelTripCardItem>(
            dto.Trips.Select(trip => new TravelTripCardItem(trip)));
    }

    public int Year { get; }

    public string YearTitle { get; }

    public ObservableCollection<TravelTripCardItem> Trips { get; }
}

public partial class TravelTripCardItem : ObservableObject
{
    public TravelTripCardItem(TravelTripCardDto dto)
    {
        Dto = dto;
    }

    public TravelTripCardDto Dto { get; }

    public Guid FocusPlaceId => Dto.FocusPlaceId;

    public string TripName => Dto.TripName;

    public string LocationText => Dto.LocationText;

    public bool HasLocationText => !string.IsNullOrWhiteSpace(Dto.LocationText);

    public string PeriodText => Dto.PeriodText;

    public string Country => Dto.Country;

    public int PlaceCount => Dto.PlaceCount;

    public int PhotoCount => Dto.PhotoCount;

    public int VisitDayCount => Dto.VisitDayCount;

    public string StatsText
    {
        get
        {
            var parts = new List<string>();
            if (Dto.PhotoCount > 0)
            {
                parts.Add($"사진 {Dto.PhotoCount}장");
            }

            if (Dto.PlaceCount > 1)
            {
                parts.Add($"방문 장소 {Dto.PlaceCount}곳");
            }

            if (Dto.VisitDayCount > 0)
            {
                parts.Add($"촬영일수 {Dto.VisitDayCount}일");
            }

            return string.Join(" · ", parts);
        }
    }

    public string AbsoluteLibraryPath => Dto.AbsoluteLibraryPath ?? string.Empty;

    public Guid? RepresentativeMediaId => Dto.RepresentativeMediaId;

    public int Year => Dto.Year;

    [ObservableProperty]
    private BitmapImage? thumbnailImage;

    [ObservableProperty]
    private bool isHighlighted;
}

public partial class TravelPlaceCardItem : ObservableObject
{
    public TravelPlaceCardItem(TravelPlaceSummaryDto dto)
    {
        Dto = dto;
    }

    public TravelPlaceSummaryDto Dto { get; }

    public Guid PlaceId => Dto.PlaceId;

    public int Rank => Dto.Rank;

    public string PlaceName => Dto.PlaceName;

    public string VisitCountText => $"{Dto.VisitRecordCount}회";

    public string LastVisitText => Dto.LastVisitDate?.ToLocalTime().ToString("yyyy-MM-dd") ?? "-";

    public string RelativeText => Dto.RelativeLastVisitText;

    public string TagsText => Dto.TopTags.Count == 0 ? string.Empty : string.Join(" · ", Dto.TopTags);

    public string AbsoluteLibraryPath => Dto.AbsoluteLibraryPath ?? string.Empty;

    public Guid? RepresentativeMediaId => Dto.RepresentativeMediaId;

    [ObservableProperty]
    private BitmapImage? thumbnailImage;
}

public sealed class TravelSeasonCardItem
{
    public TravelSeasonCardItem(TravelSeasonSummaryDto dto)
    {
        Dto = dto;
    }

    public TravelSeasonSummaryDto Dto { get; }

    public TravelSeason Season => Dto.Season;

    public string DisplayText => $"{Dto.Emoji} {Dto.PlaceName}";

    public string SubText => Dto.VisitRecordCount > 0 ? $"{Dto.VisitRecordCount}회" : "기록 없음";

    public bool HasPlace => Dto.PlaceId.HasValue;
}

public sealed class TravelCountryCardItem
{
    public TravelCountryCardItem(TravelCountrySummaryDto dto)
    {
        Dto = dto;
    }

    public TravelCountrySummaryDto Dto { get; }

    public int Rank => Dto.Rank;

    public string Country => Dto.Country;

    public string VisitCountText => $"{Dto.VisitRecordCount}회";

    public string PlaceCountText => $"장소 {Dto.PlaceCount}곳";
}

public partial class TravelFarthestCardItem : ObservableObject
{
    public TravelFarthestCardItem(TravelFarthestSummaryDto dto)
    {
        Dto = dto;
    }

    public TravelFarthestSummaryDto Dto { get; }

    public Guid PlaceId => Dto.PlaceId;

    public int Rank => Dto.Rank;

    public string PlaceName => Dto.PlaceName;

    public string DistanceText => $"{Dto.DistanceKm:0}km";

    public string YearText => Dto.Year?.ToString() ?? "-";

    public string HomeHint => string.IsNullOrWhiteSpace(Dto.HomePlaceName)
        ? string.Empty
        : $"기준: {Dto.HomePlaceName}";

    public string AbsoluteLibraryPath => Dto.AbsoluteLibraryPath ?? string.Empty;

    public Guid? RepresentativeMediaId => Dto.RepresentativeMediaId;

    [ObservableProperty]
    private BitmapImage? thumbnailImage;
}
