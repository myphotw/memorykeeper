using MemoryKeeper.Application;

namespace MemoryKeeper.Mobile.Models;

public enum MobileGalleryViewMode
{
    Gallery,
    Viewer,
    YearSelection,
    PlaceSelection,
    PlaceYearSelection,
    Search,
}

public enum MobileGalleryContextKind
{
    Recent,
    Year,
    Place,
    Unclassified,
    DateUnclassified,
}

public sealed record MobileGalleryContext(
    GalleryBrowseScope Scope,
    MobileGalleryContextKind Kind,
    string Title,
    string? Detail,
    int? PhotoCount,
    int? Year)
{
    public static MobileGalleryContext Recent { get; } = new(
        GalleryBrowseScope.DefaultFeed,
        MobileGalleryContextKind.Recent,
        "MemoryKeeper",
        "최근 사진",
        null,
        null);

    public string Summary => PhotoCount is int count
        ? string.IsNullOrWhiteSpace(Detail)
            ? $"{count:N0}장"
            : $"{Detail} · {count:N0}장"
        : Detail ?? string.Empty;

    public string BreadcrumbText => Detail ?? string.Empty;

    public string HierarchyBreadcrumbText
    {
        get
        {
            var detail = Detail ?? string.Empty;
            if (Year is int year)
            {
                var yearPrefix = $"{year}년 · ";
                if (detail.StartsWith(yearPrefix, StringComparison.Ordinal))
                {
                    detail = detail[yearPrefix.Length..];
                }
                else if (string.Equals(detail, $"{year}년", StringComparison.Ordinal))
                {
                    detail = string.Empty;
                }
            }

            return string.IsNullOrWhiteSpace(detail) && HasHierarchyYear ? Title : detail;
        }
    }

    public string YearText => Year is int year ? $"{year}년" : string.Empty;

    public string YearSelectorText => Year is int year ? $"{year}년 ⌄" : string.Empty;

    public string PhotoCountText => PhotoCount is int count ? $"{count:N0}장" : string.Empty;

    public bool IsPlaceLeaf => GalleryPlaceIdentity.FromScope(Scope) is not null;

    public bool HasHierarchyYear => Scope switch
    {
        GalleryBrowseScope.YearScope => true,
        GalleryBrowseScope.HierarchyScope { Year: not null } => true,
        GalleryBrowseScope.CanonicalRegionScope => true,
        _ => false,
    };

    public static MobileGalleryContext FromOption(MobileGalleryBrowseOption option)
    {
        ArgumentNullException.ThrowIfNull(option);

        return option.Scope switch
        {
            GalleryBrowseScope.DefaultFeedScope => Recent,
            GalleryBrowseScope.YearScope year => new MobileGalleryContext(
                option.Scope,
                MobileGalleryContextKind.Year,
                $"{year.Year}년",
                "전체 사진",
                option.PhotoCount,
                year.Year),
            GalleryBrowseScope.HierarchyScope { DateUnclassified: true } hierarchy =>
                new MobileGalleryContext(
                    option.Scope,
                    MobileGalleryContextKind.DateUnclassified,
                    option.DisplayName,
                    hierarchy.Year is int year ? $"{year}년" : option.Context,
                    option.PhotoCount,
                    hierarchy.Year),
            GalleryBrowseScope.HierarchyScope { Unclassified: true } hierarchy =>
                new MobileGalleryContext(
                    option.Scope,
                    MobileGalleryContextKind.Unclassified,
                    option.DisplayName,
                    hierarchy.Year is int year ? $"{year}년" : null,
                    option.PhotoCount,
                    hierarchy.Year),
            GalleryBrowseScope.HierarchyScope hierarchy => new MobileGalleryContext(
                option.Scope,
                MobileGalleryContextKind.Place,
                option.GalleryTitle ?? option.DisplayName,
                option.Context,
                option.PhotoCount,
                hierarchy.Year),
            GalleryBrowseScope.CanonicalRegionScope region => new MobileGalleryContext(
                option.Scope,
                MobileGalleryContextKind.Place,
                option.GalleryTitle ?? option.DisplayName,
                option.Context,
                option.PhotoCount,
                region.Year),
            _ => throw new ArgumentOutOfRangeException(nameof(option)),
        };
    }
}
