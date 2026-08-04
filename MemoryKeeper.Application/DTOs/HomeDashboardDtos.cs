namespace MemoryKeeper.Application.DTOs;

public sealed class HomeDashboardDto
{
    public IReadOnlyList<HeroMemoryDto> HeroMemories { get; init; } = [];

    public IReadOnlyList<TodayMemoryPhotoDto> TodayMemories { get; init; } = [];

    public IReadOnlyList<RecentVisitDto> RecentVisits { get; init; } = [];

    public IReadOnlyList<DashboardPhotoDto> Favorites { get; init; } = [];

    public IReadOnlyList<DashboardPhotoDto> RecentImports { get; init; } = [];

    public PendingSummaryDto PendingSummary { get; init; } = new();

    public IReadOnlyList<string> RecentQueries { get; init; } = [];

    public DashboardStatisticsDto Statistics { get; init; } = new();
}

public sealed class HeroMemoryDto
{
    public Guid PlaceId { get; init; }

    public string PlaceName { get; init; } = string.Empty;

    public int Year { get; init; }

    public int YearsAgo { get; init; }

    public int PhotoCount { get; init; }

    public int VisitRecordCount { get; init; }

    public Guid? RepresentativeMediaId { get; init; }

    public string? AbsoluteLibraryPath { get; init; }

    public IReadOnlyList<string> TopTags { get; init; } = [];

    /// <summary>예: 오늘의 추억, 최근 여행</summary>
    public string KindLabel { get; init; } = "추억";

    /// <summary>날짜/기간 표시용</summary>
    public string DateText { get; init; } = string.Empty;

    /// <summary>짧은 안내 문장</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>장소명을 먼저 읽게 한다.</summary>
    public string Title => string.IsNullOrWhiteSpace(PlaceName) ? KindLabel : PlaceName;

    public string Subtitle => string.IsNullOrWhiteSpace(DateText)
        ? KindLabel
        : DateText;
}

public sealed class TodayMemoryPhotoDto
{
    public Guid MediaId { get; init; }

    public Guid? PlaceId { get; init; }

    public string PlaceName { get; init; } = string.Empty;

    public string AbsoluteLibraryPath { get; init; } = string.Empty;

    public int YearsAgo { get; init; }

    public IReadOnlyList<string> TopTags { get; init; } = [];
}

public sealed class RecentVisitDto
{
    public Guid PlaceId { get; init; }

    public string PlaceName { get; init; } = string.Empty;

    public string? AbsoluteLibraryPath { get; init; }

    public Guid? RepresentativeMediaId { get; init; }

    public int VisitRecordCount { get; init; }

    public DateTimeOffset? LastVisitDate { get; init; }

    public IReadOnlyList<string> TopTags { get; init; } = [];
}

public sealed class DashboardPhotoDto
{
    public Guid MediaId { get; init; }

    public string AbsoluteLibraryPath { get; init; } = string.Empty;

    public bool IsFavorite { get; init; }

    public string FileName { get; init; } = string.Empty;
}

public sealed class PendingSummaryDto
{
    public int Total { get; init; }

    public int NoGps { get; init; }

    public int HasGps { get; init; }

    public int UnknownDate { get; init; }

    public Guid? RepresentativeMediaId { get; init; }

    public string? RepresentativeAbsoluteLibraryPath { get; init; }

    public DateTimeOffset? LatestImportedAt { get; init; }

    public bool HasItems => Total > 0;
}

public sealed class DashboardStatisticsDto
{
    public int PhotoCount { get; init; }

    public int PlaceCount { get; init; }

    public int VisitRecordCount { get; init; }

    public int FavoriteCount { get; init; }

    public int TagCount { get; init; }
}
