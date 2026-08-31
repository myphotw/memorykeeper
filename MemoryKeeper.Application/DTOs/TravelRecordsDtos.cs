namespace MemoryKeeper.Application.DTOs;

public enum TravelSeason
{
    Spring = 1,
    Summer = 2,
    Autumn = 3,
    Winter = 4
}

public enum TravelRecordsDetailKind
{
    MostVisited = 1,
    LongUnvisited = 2,
    Recent = 3,
    Countries = 4,
    Farthest = 5,
    Season = 6
}

public sealed class TravelRecordsDashboardDto
{
    /// <summary>Unique photos identified by Backend file ID, then Media ID.</summary>
    public int UniquePhotoCount { get; init; }

    /// <summary>Unique real places, excluding the synthetic missing-place bucket.</summary>
    public int DistinctPlaceCount { get; init; }

    /// <summary>Foreign countries represented by <see cref="CountryVisitStatistics"/>.</summary>
    public int VisitedForeignCountryCount { get; init; }

    public TravelPlaceSummaryDto? MostVisitedPlace { get; init; }

    public TravelPlaceSummaryDto? LongUnvisitedPlace { get; init; }

    public IReadOnlyList<TravelSeasonSummaryDto> SeasonHighlights { get; init; } = [];

    public IReadOnlyList<TravelPlaceSummaryDto> RecentPlaces { get; init; } = [];

    public TravelCountrySummaryDto? TopCountry { get; init; }

    public TravelFarthestSummaryDto? FarthestPlace { get; init; }

    /// <summary>
    /// Foreign-country visits calculated only for the country graph by merging capture
    /// dates across places and counting consecutive-day ranges.
    /// </summary>
    public IReadOnlyList<TravelCountryVisitSummaryDto> CountryVisitStatistics { get; init; } = [];

    public IReadOnlyList<TravelMemoryCardDto> MemoryCards { get; init; } = [];

    /// <summary>연도 Chapter → 여행(장소) 카드. Memory Timeline 본문.</summary>
    public IReadOnlyList<TravelYearChapterDto> YearChapters { get; init; } = [];
}

public sealed class TravelCountryVisitSummaryDto
{
    public string Country { get; init; } = string.Empty;

    public int VisitCount { get; init; }

    public int CapturedDayCount { get; init; }

    public int Rank { get; init; }
}

public enum TravelMemoryCardKind
{
    YearsAgoToday = 1,
    LastYearAroundNow = 2,
    YearsAgoAroundNow = 3,
    Rediscovered = 4,
}

public sealed class TravelMemoryCardDto
{
    public TravelMemoryCardKind Kind { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Subtitle { get; init; } = string.Empty;

    public Guid FocusPlaceId { get; init; }

    public Guid? RepresentativeMediaId { get; init; }

    public IReadOnlyList<TravelMemoryPhotoDto> Photos { get; init; } = [];
}

public sealed class TravelMemoryPhotoDto
{
    public Guid? MediaId { get; init; }

    public Guid PlaceId { get; init; }

    public string PlaceName { get; init; } = string.Empty;

    public string Country { get; init; } = string.Empty;

    public DateTimeOffset CapturedAt { get; init; }

    public string ThumbnailPath { get; init; } = string.Empty;
}

/// <summary>연도 하나의 Chapter (예: 2025).</summary>
public sealed class TravelYearChapterDto
{
    public int Year { get; init; }

    public string YearTitle => Year <= 0 ? "날짜 미상" : $"{Year}";

    public IReadOnlyList<TravelTripCardDto> Trips { get; init; } = [];
}

/// <summary>여행 앨범 한 페이지. 대표사진 → 여행명 → 기간 → 장소 → 통계.</summary>
public sealed class TravelTripCardDto
{
    public Guid FocusPlaceId { get; init; }

    public string TripName { get; init; } = string.Empty;

    public string LocationText { get; init; } = string.Empty;

    public string Country { get; init; } = string.Empty;

    public int Year { get; init; }

    public DateTimeOffset? StartDate { get; init; }

    public DateTimeOffset? EndDate { get; init; }

    public string PeriodText { get; init; } = string.Empty;

    public int PlaceCount { get; init; }

    public int PhotoCount { get; init; }

    public int VisitDayCount { get; init; }

    public Guid? RepresentativeMediaId { get; init; }

    public string? AbsoluteLibraryPath { get; init; }

    public IReadOnlyList<string> PlaceNames { get; init; } = [];
}

public sealed class TravelPlaceSummaryDto
{
    public Guid PlaceId { get; init; }

    public string PlaceName { get; init; } = string.Empty;

    public string Country { get; init; } = string.Empty;

    public int VisitRecordCount { get; init; }

    public DateTimeOffset? LastVisitDate { get; init; }

    public string RelativeLastVisitText { get; init; } = string.Empty;

    public Guid? RepresentativeMediaId { get; init; }

    public string? AbsoluteLibraryPath { get; init; }

    public IReadOnlyList<string> TopTags { get; init; } = [];

    public int Rank { get; init; }
}

public sealed class TravelSeasonSummaryDto
{
    public TravelSeason Season { get; init; }

    public string SeasonLabel { get; init; } = string.Empty;

    public string Emoji { get; init; } = string.Empty;

    public Guid? PlaceId { get; init; }

    public string PlaceName { get; init; } = string.Empty;

    public int VisitRecordCount { get; init; }
}

public sealed class TravelCountrySummaryDto
{
    public string Country { get; init; } = string.Empty;

    public int VisitRecordCount { get; init; }

    public int PlaceCount { get; init; }

    public int Rank { get; init; }
}

public sealed class TravelFarthestSummaryDto
{
    public Guid PlaceId { get; init; }

    public string PlaceName { get; init; } = string.Empty;

    public string Country { get; init; } = string.Empty;

    public double DistanceKm { get; init; }

    public int? Year { get; init; }

    public Guid? HomePlaceId { get; init; }

    public string HomePlaceName { get; init; } = string.Empty;

    public Guid? RepresentativeMediaId { get; init; }

    public string? AbsoluteLibraryPath { get; init; }

    public int Rank { get; init; }
}

public sealed class TravelRecordsDetailDto
{
    public TravelRecordsDetailKind Kind { get; init; }

    public string Title { get; init; } = string.Empty;

    public TravelSeason? Season { get; init; }

    public IReadOnlyList<TravelPlaceSummaryDto> Places { get; init; } = [];

    public IReadOnlyList<TravelCountrySummaryDto> Countries { get; init; } = [];

    public IReadOnlyList<TravelFarthestSummaryDto> FarthestPlaces { get; init; } = [];
}
