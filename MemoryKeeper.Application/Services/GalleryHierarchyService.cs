using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.DTOs.Gallery;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

/// <summary>
/// NAS-backed Gallery explorer hierarchy. Restores the original
/// Year -&gt; Country -&gt; City -&gt; Place and Place -&gt; Year browse models
/// without reading local SQLite.
/// </summary>
public sealed class GalleryHierarchyService
{
    public const string UnclassifiedTitle = "미분류";
    public const string OtherTitle = "기타";

    private readonly IGalleryPhotoCatalog _catalog;
    private readonly ILogger<GalleryHierarchyService> _logger;

    public GalleryHierarchyService(
        IGalleryPhotoCatalog catalog,
        ILogger<GalleryHierarchyService> logger)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<GallerySidebarSummaryDto> GetSidebarSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        var photos = await LoadPhotosAsync(cancellationToken).ConfigureAwait(false);
        var years = photos
            .Select(photo => ResolveYear(photo.Photo))
            .Where(year => year.HasValue)
            .GroupBy(year => year!.Value)
            .Select(group => new GalleryYearCountDto
            {
                Year = group.Key,
                Count = group.Count(),
            })
            .OrderByDescending(item => item.Year)
            .ToList();

        var recentCount = photos
            .Count(photo => ResolveImportedAt(photo.Photo).HasValue);

        return new GallerySidebarSummaryDto
        {
            TotalCount = photos.Count,
            FavoriteCount = photos.Count(photo => photo.Photo.Favorite),
            RecentCount = Math.Min(MediaService.RecentGalleryTake, recentCount),
            PendingCount = photos.Count(IsPending),
            Years = years,
        };
    }

    public async Task<IReadOnlyList<GalleryYearCountDto>> GetYearsAsync(
        CancellationToken cancellationToken = default) =>
        (await GetSidebarSummaryAsync(cancellationToken).ConfigureAwait(false)).Years;

    public async Task<IReadOnlyList<GalleryTreeChildDto>> GetCountriesAsync(
        int year,
        CancellationToken cancellationToken = default)
    {
        var photos = (await LoadPhotosAsync(cancellationToken).ConfigureAwait(false))
            .Where(photo => ResolveYear(photo.Photo) == year)
            .ToList();
        var result = new List<GalleryTreeChildDto>();

        var unclassified = photos.Count(photo => !HasPlace(photo));
        if (unclassified > 0)
        {
            result.Add(new GalleryTreeChildDto
            {
                Title = UnclassifiedTitle,
                Count = unclassified,
                IsUnclassified = true,
            });
        }

        result.AddRange(photos
            .Where(HasPlace)
            .GroupBy(photo => CountryLabel(photo), StringComparer.OrdinalIgnoreCase)
            .Select(group => new GalleryTreeChildDto
            {
                Title = group.Key,
                Count = group.Count(),
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase));

        return result;
    }

    public async Task<IReadOnlyList<GalleryTreeChildDto>> GetCitiesAsync(
        int year,
        string country,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(country, UnclassifiedTitle, StringComparison.Ordinal))
        {
            return [];
        }

        var countryKey = ToCountryLabel(country);
        return (await LoadPhotosAsync(cancellationToken).ConfigureAwait(false))
            .Where(photo => ResolveYear(photo.Photo) == year)
            .Where(HasPlace)
            .Where(photo => string.Equals(
                CountryLabel(photo),
                countryKey,
                StringComparison.OrdinalIgnoreCase))
            .GroupBy(photo => CityLabel(photo), StringComparer.OrdinalIgnoreCase)
            .Select(group => new GalleryTreeChildDto
            {
                Title = group.Key,
                Count = group.Count(),
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<GalleryTreeChildDto>> GetPlacesAsync(
        int year,
        string country,
        string city,
        CancellationToken cancellationToken = default)
    {
        var countryKey = ToCountryLabel(country);
        var cityKey = ToCityLabel(city);

        return (await LoadPhotosAsync(cancellationToken).ConfigureAwait(false))
            .Where(photo => ResolveYear(photo.Photo) == year)
            .Where(HasPlace)
            .Where(photo => string.Equals(
                              CountryLabel(photo),
                              countryKey,
                              StringComparison.OrdinalIgnoreCase)
                            && string.Equals(
                              CityLabel(photo),
                              cityKey,
                              StringComparison.OrdinalIgnoreCase))
            .GroupBy(PlaceStableId)
            .Select(group =>
            {
                var first = group.First();
                return new GalleryTreeChildDto
                {
                    Title = PlaceDisplayName(first),
                    Count = group.Count(),
                    PlaceId = group.Key,
                    PlaceType = first.Photo.PlaceType,
                    Icon = PlaceTypeCatalog.GetIcon(first.Photo.PlaceType),
                };
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<GalleryTreeChildDto>> GetPlaceBrowseRootsAsync(
        string? searchText = null,
        CancellationToken cancellationToken = default)
    {
        var term = string.IsNullOrWhiteSpace(searchText) ? null : searchText.Trim();

        return (await LoadPhotosAsync(cancellationToken).ConfigureAwait(false))
            .Where(photo => ResolveYear(photo.Photo).HasValue)
            .Where(HasPlace)
            .GroupBy(PlaceStableId)
            .Where(group => term is null || group.Any(photo => MatchesSearch(photo, term)))
            .Select(group =>
            {
                var first = group.First();
                return new GalleryTreeChildDto
                {
                    Title = PlaceDisplayName(first),
                    Count = group.Count(),
                    PlaceId = group.Key,
                    PlaceType = first.Photo.PlaceType,
                    Icon = PlaceTypeCatalog.GetIcon(first.Photo.PlaceType),
                };
            })
            .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<GalleryTreeChildDto>> GetYearsForPlaceAsync(
        Guid placeId,
        CancellationToken cancellationToken = default)
    {
        return (await LoadPhotosAsync(cancellationToken).ConfigureAwait(false))
            .Where(HasPlace)
            .Where(photo => PlaceStableId(photo) == placeId)
            .Select(photo => ResolveYear(photo.Photo))
            .Where(year => year.HasValue)
            .GroupBy(year => year!.Value)
            .Select(group => new GalleryTreeChildDto
            {
                Title = group.Key.ToString(),
                Count = group.Count(),
                PlaceId = placeId,
                Year = group.Key,
            })
            .OrderByDescending(item => item.Year)
            .ToList();
    }

    public async Task<IReadOnlyList<PhotoDto>> QueryAsync(
        GalleryHierarchyQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IEnumerable<HierarchyPhoto> filtered = await LoadPhotosAsync(cancellationToken).ConfigureAwait(false);
        if (query.PendingOnly)
        {
            filtered = filtered.Where(IsPending);
        }
        else if (query.FavoritesOnly)
        {
            filtered = filtered.Where(photo => photo.Photo.Favorite);
        }
        else if (query.RecentOnly)
        {
            filtered = filtered
                .Where(photo => ResolveImportedAt(photo.Photo).HasValue)
                .OrderByDescending(photo => ResolveImportedAt(photo.Photo))
                .Take(MediaService.RecentGalleryTake);
        }
        else
        {
            if (query.Year is int year)
            {
                filtered = filtered.Where(photo => ResolveYear(photo.Photo) == year);
            }

            if (query.UnclassifiedOnly)
            {
                filtered = filtered.Where(photo => !HasPlace(photo));
            }
            else if (query.PlaceId is Guid placeId)
            {
                filtered = filtered.Where(photo =>
                    HasPlace(photo) && PlaceStableId(photo) == placeId);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(query.Country))
                {
                    var countryKey = ToCountryLabel(query.Country);
                    filtered = filtered.Where(photo =>
                        HasPlace(photo)
                        && string.Equals(
                            CountryLabel(photo),
                            countryKey,
                            StringComparison.OrdinalIgnoreCase));
                }

                if (!string.IsNullOrWhiteSpace(query.City))
                {
                    var cityKey = ToCityLabel(query.City);
                    filtered = filtered.Where(photo =>
                        HasPlace(photo)
                        && string.Equals(
                            CityLabel(photo),
                            cityKey,
                            StringComparison.OrdinalIgnoreCase));
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var term = query.SearchText.Trim();
            filtered = filtered.Where(photo => MatchesSearch(photo, term));
        }

        return filtered
            .OrderByDescending(photo => ResolveSortDate(photo.Photo))
            .Select(photo => photo.Photo)
            .ToList();
    }

    private async Task<List<HierarchyPhoto>> LoadPhotosAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _catalog.QueryAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<HierarchyPhoto>();

        foreach (var photo in snapshot.Photos)
        {
            var fileId = photo.FileId?.Trim();
            if (!string.IsNullOrWhiteSpace(fileId) && !seen.Add(fileId))
            {
                continue;
            }

            snapshot.LocationMetadataByFileId.TryGetValue(
                fileId ?? string.Empty,
                out var metadata);
            result.Add(new HierarchyPhoto(photo, metadata));
        }

        _logger.LogDebug("Gallery hierarchy loaded {PhotoCount} NAS photos", result.Count);
        return result;
    }

    private static bool HasPlace(HierarchyPhoto photo) =>
        !string.IsNullOrWhiteSpace(RawPlaceName(photo));

    private static bool IsPending(HierarchyPhoto photo) =>
        string.Equals(photo.Photo.Status?.Trim(), "pending", StringComparison.OrdinalIgnoreCase);

    private static string CountryLabel(HierarchyPhoto photo) =>
        ToCountryLabel(FirstNotEmpty(photo.Photo.Country, photo.Metadata?.Country));

    private static string CityLabel(HierarchyPhoto photo)
    {
        var rawCity = FirstNotEmpty(photo.Photo.City, photo.Metadata?.City);
        if (ContainsHangul(rawCity))
        {
            // Preserve Backend administrative labels such as "구례군" verbatim.
            // PlaceNormalizer remains the fallback for aliases/non-Korean values.
            return rawCity;
        }

        var rawPlaceName = RawPlaceName(photo);
        var transientPlace = new Place
        {
            Country = CountryLabel(photo),
            Province = FirstNotEmpty(photo.Photo.Province, photo.Metadata?.Province),
            City = rawCity,
            DisplayName = rawPlaceName,
            CanonicalName = rawPlaceName,
        };
        return PlaceNormalizer.ResolveCityLabel(transientPlace, OtherTitle);
    }

    private static string PlaceDisplayName(HierarchyPhoto photo)
    {
        var raw = RawPlaceName(photo);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return UnclassifiedTitle;
        }

        return PlaceNormalizer.GetDisplayLabel(new Place
        {
            DisplayName = raw,
            CanonicalName = raw,
            Country = CountryLabel(photo),
            Province = FirstNotEmpty(photo.Photo.Province, photo.Metadata?.Province),
            City = FirstNotEmpty(photo.Photo.City, photo.Metadata?.City),
        });
    }

    private static Guid PlaceStableId(HierarchyPhoto photo) =>
        PlaceIdentity.StableId(
            CountryLabel(photo),
            CityLabel(photo),
            PlaceDisplayName(photo));

    private static string RawPlaceName(HierarchyPhoto photo) =>
        FirstNotEmpty(photo.Photo.PlaceName, photo.Metadata?.PlaceName);

    private static bool MatchesSearch(HierarchyPhoto photo, string term)
    {
        var candidates = new[]
        {
            photo.Photo.Filename,
            CountryLabel(photo),
            photo.Photo.Country,
            photo.Metadata?.Country,
            photo.Photo.Province,
            photo.Metadata?.Province,
            photo.Photo.City,
            photo.Metadata?.City,
            photo.Photo.District,
            photo.Metadata?.District,
            RawPlaceName(photo),
            PlaceDisplayName(photo),
            CityLabel(photo),
        };
        return candidates.Any(candidate =>
            !string.IsNullOrWhiteSpace(candidate)
            && candidate.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static int? ResolveYear(PhotoDto photo)
    {
        var date = photo.CaptureDatetime ?? ResolveImportedAt(photo);
        return date?.ToLocalTime().Year;
    }

    private static DateTimeOffset? ResolveImportedAt(PhotoDto photo) =>
        photo.ImportedAt ?? photo.CreatedAt;

    private static DateTimeOffset ResolveSortDate(PhotoDto photo) =>
        photo.CaptureDatetime ?? ResolveImportedAt(photo) ?? DateTimeOffset.MinValue;

    private static string ToCountryLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return OtherTitle;
        }

        if (string.Equals(value.Trim(), UnclassifiedTitle, StringComparison.Ordinal)
            || string.Equals(value.Trim(), OtherTitle, StringComparison.Ordinal))
        {
            return value.Trim();
        }

        var normalized = PlaceNormalizer.NormalizeCountry(value);
        return string.IsNullOrWhiteSpace(normalized) ? OtherTitle : normalized;
    }

    private static string ToCityLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return OtherTitle;
        }

        if (ContainsHangul(value))
        {
            return value.Trim();
        }

        if (string.Equals(value.Trim(), OtherTitle, StringComparison.Ordinal))
        {
            return OtherTitle;
        }

        var normalized = PlaceNormalizer.NormalizePlace(value);
        return string.IsNullOrWhiteSpace(normalized) ? OtherTitle : normalized;
    }

    private static string FirstNotEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static bool ContainsHangul(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Any(character => character is >= '\uAC00' and <= '\uD7A3');

    private sealed record HierarchyPhoto(
        PhotoDto Photo,
        GalleryPhotoLocationMetadataDto? Metadata);
}
