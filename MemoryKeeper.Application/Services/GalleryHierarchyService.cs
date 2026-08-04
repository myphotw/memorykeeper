using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Application.Time;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

/// <summary>
/// Gallery explorer hierarchy: Year → Country → City → Place.
/// Additive query layer; does not change import/place assignment rules.
/// </summary>
public sealed class GalleryHierarchyService
{
    public const string UnclassifiedTitle = "미분류";
    public const string OtherTitle = "기타";

    private readonly IMediaRepository _mediaRepository;
    private readonly IPlaceRepository _placeRepository;
    private readonly IStorageRepository _storageRepository;
    private readonly IFileAccessService _fileAccessService;
    private readonly IPlaceDisplayNameRefreshService _placeDisplayNameRefreshService;
    private readonly ILogger<GalleryHierarchyService> _logger;

    public GalleryHierarchyService(
        IMediaRepository mediaRepository,
        IPlaceRepository placeRepository,
        IStorageRepository storageRepository,
        IFileAccessService fileAccessService,
        IPlaceDisplayNameRefreshService placeDisplayNameRefreshService,
        ILogger<GalleryHierarchyService> logger)
    {
        _mediaRepository = mediaRepository;
        _placeRepository = placeRepository;
        _storageRepository = storageRepository;
        _fileAccessService = fileAccessService;
        _placeDisplayNameRefreshService = placeDisplayNameRefreshService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<GalleryYearCountDto>> GetYearsAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Gallery hierarchy GetYears");
        var photos = await GetPhotosAsync(cancellationToken);
        return photos
            .GroupBy(media => ResolveYear(media))
            .Select(group => new GalleryYearCountDto
            {
                Year = group.Key,
                Count = group.Count()
            })
            .OrderByDescending(item => item.Year)
            .ToList();
    }

    public async Task<IReadOnlyList<GalleryTreeChildDto>> GetCountriesAsync(
        int year,
        CancellationToken cancellationToken = default)
    {
        var (photos, placesById) = await GetYearPhotosWithPlacesAsync(year, cancellationToken);
        var result = new List<GalleryTreeChildDto>();

        var unclassified = photos.Count(media => media.PlaceId is null);
        if (unclassified > 0)
        {
            result.Add(new GalleryTreeChildDto
            {
                Title = UnclassifiedTitle,
                Count = unclassified,
                IsUnclassified = true
            });
        }

        var countries = photos
            .Where(media => media.PlaceId is Guid placeId && placesById.ContainsKey(placeId))
            .GroupBy(media => ToCountryLabel(placesById[media.PlaceId!.Value].Country))
            .Select(group => new GalleryTreeChildDto
            {
                Title = group.Key,
                Count = group.Count()
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase);

        result.AddRange(countries);
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

        var (photos, placesById) = await GetYearPhotosWithPlacesAsync(year, cancellationToken);
        var countryKey = ToCountryLabel(country);

        return photos
            .Where(media =>
                media.PlaceId is Guid placeId
                && placesById.TryGetValue(placeId, out var place)
                && string.Equals(ToCountryLabel(place.Country), countryKey, StringComparison.OrdinalIgnoreCase))
            .GroupBy(media => ToCityLabel(placesById[media.PlaceId!.Value]))
            .Select(group => new GalleryTreeChildDto
            {
                Title = group.Key,
                Count = group.Count()
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
        var (photos, placesById) = await GetYearPhotosWithPlacesAsync(year, cancellationToken);
        var countryKey = ToCountryLabel(country);
        var cityKey = ToCityLabel(city);

        return photos
            .Where(media =>
                media.PlaceId is Guid placeId
                && placesById.TryGetValue(placeId, out var place)
                && string.Equals(ToCountryLabel(place.Country), countryKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(ToCityLabel(place), cityKey, StringComparison.OrdinalIgnoreCase))
            .GroupBy(media => media.PlaceId!.Value)
            .Select(group =>
            {
                var place = placesById[group.Key];
                return new GalleryTreeChildDto
                {
                    Title = PlaceNormalizer.GetDisplayLabel(place),
                    Count = group.Count(),
                    PlaceId = place.Id,
                    PlaceType = place.Category,
                    Icon = PlaceTypeCatalog.GetIcon(place.Category)
                };
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Place browse roots: Place → Year. Sorted by place name ascending.
    /// </summary>
    public async Task<IReadOnlyList<GalleryTreeChildDto>> GetPlaceBrowseRootsAsync(
        string? searchText = null,
        CancellationToken cancellationToken = default)
    {
        var photos = await GetPhotosAsync(cancellationToken);
        var placeIds = photos
            .Where(media => media.PlaceId.HasValue)
            .Select(media => media.PlaceId!.Value)
            .Distinct()
            .ToList();
        if (placeIds.Count == 0)
        {
            return [];
        }

        var places = (await _placeRepository.GetByIdsAsync(placeIds, cancellationToken)).ToList();
        if (places.Count > 0)
        {
            await _placeDisplayNameRefreshService.RefreshKoreanNamesAsync(places, cancellationToken);
        }

        var placesById = places.ToDictionary(place => place.Id);
        var term = string.IsNullOrWhiteSpace(searchText) ? null : searchText.Trim();

        return photos
            .Where(media => media.PlaceId is Guid id && placesById.ContainsKey(id))
            .GroupBy(media => media.PlaceId!.Value)
            .Select(group =>
            {
                var place = placesById[group.Key];
                return new GalleryTreeChildDto
                {
                    Title = PlaceNormalizer.GetDisplayLabel(place),
                    Count = group.Count(),
                    PlaceId = place.Id,
                    PlaceType = place.Category,
                    Icon = PlaceTypeCatalog.GetIcon(place.Category)
                };
            })
            .Where(item =>
                term is null
                || item.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (placesById.TryGetValue(item.PlaceId!.Value, out var place)
                    && MatchesPlaceSearch(place, term)))
            .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Years under a place, descending, with photo counts.
    /// </summary>
    public async Task<IReadOnlyList<GalleryTreeChildDto>> GetYearsForPlaceAsync(
        Guid placeId,
        CancellationToken cancellationToken = default)
    {
        var photos = await GetPhotosAsync(cancellationToken);
        return photos
            .Where(media => media.PlaceId == placeId)
            .GroupBy(media => ResolveYear(media))
            .Select(group => new GalleryTreeChildDto
            {
                Title = group.Key.ToString(),
                Count = group.Count(),
                PlaceId = placeId,
                Year = group.Key
            })
            .OrderByDescending(item => item.Year)
            .ToList();
    }

    private static bool MatchesPlaceSearch(Place place, string term) =>
        place.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
        || place.Country.Contains(term, StringComparison.OrdinalIgnoreCase)
        || place.City.Contains(term, StringComparison.OrdinalIgnoreCase)
        || place.Province.Contains(term, StringComparison.OrdinalIgnoreCase)
        || (!string.IsNullOrWhiteSpace(place.CanonicalName)
            && place.CanonicalName.Contains(term, StringComparison.OrdinalIgnoreCase))
        || PlaceNormalizer.GetDisplayLabel(place).Contains(term, StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<GalleryMediaDto>> QueryAsync(
        GalleryHierarchyQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var photos = await GetPhotosAsync(cancellationToken);
        IEnumerable<Media> filtered = photos;

        if (query.PendingOnly)
        {
            filtered = filtered.Where(media => media.Status == MediaStatus.Pending);
        }
        else if (query.FavoritesOnly)
        {
            filtered = filtered.Where(media => media.IsFavorite);
        }
        else if (query.RecentOnly)
        {
            filtered = filtered
                .OrderByDescending(media => media.ImportedAt)
                .Take(MediaService.RecentGalleryTake);
        }
        else
        {
            if (query.Year is int year)
            {
                filtered = filtered.Where(media => ResolveYear(media) == year);
            }

            if (query.UnclassifiedOnly)
            {
                filtered = filtered.Where(media => media.PlaceId is null);
            }
            else if (query.PlaceId is Guid placeId)
            {
                filtered = filtered.Where(media => media.PlaceId == placeId);
            }
            else if (!string.IsNullOrWhiteSpace(query.Country) || !string.IsNullOrWhiteSpace(query.City))
            {
                var placeIds = await ResolvePlaceIdsAsync(query.Country, query.City, cancellationToken);
                filtered = filtered.Where(media =>
                    media.PlaceId is Guid id && placeIds.Contains(id));
            }
        }

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var term = query.SearchText.Trim();
            var places = await _placeRepository.GetAllAsync(cancellationToken);
            var placesById = places.ToDictionary(place => place.Id);
            filtered = filtered.Where(media => MatchesSearch(media, placesById, term));
        }

        var list = filtered
            .OrderByDescending(media => media.CapturedAt ?? media.ImportedAt)
            .ThenByDescending(media => media.ImportedAt)
            .ToList();

        var storages = (await _storageRepository.GetAllAsync(cancellationToken))
            .ToDictionary(storage => storage.Id);

        return list
            .Select(media => MapGallery(media, storages))
            .Where(item => item is not null)
            .Cast<GalleryMediaDto>()
            .ToList();
    }

    private async Task<(List<Media> Photos, Dictionary<Guid, Place> PlacesById)> GetYearPhotosWithPlacesAsync(
        int year,
        CancellationToken cancellationToken)
    {
        var photos = (await GetPhotosAsync(cancellationToken))
            .Where(media => ResolveYear(media) == year)
            .ToList();
        var placeIds = photos
            .Where(media => media.PlaceId.HasValue)
            .Select(media => media.PlaceId!.Value)
            .Distinct()
            .ToList();
        var places = placeIds.Count == 0
            ? []
            : await _placeRepository.GetByIdsAsync(placeIds, cancellationToken);
        var placeList = places.ToList();
        if (placeList.Count > 0)
        {
            await _placeDisplayNameRefreshService.RefreshKoreanNamesAsync(placeList, cancellationToken);
        }

        return (photos, placeList.ToDictionary(place => place.Id));
    }

    private async Task<HashSet<Guid>> ResolvePlaceIdsAsync(
        string? country,
        string? city,
        CancellationToken cancellationToken)
    {
        var places = await _placeRepository.GetAllAsync(cancellationToken);
        IEnumerable<Place> filtered = places;

        if (!string.IsNullOrWhiteSpace(country))
        {
            var countryKey = ToCountryLabel(country);
            filtered = filtered.Where(place =>
                string.Equals(ToCountryLabel(place.Country), countryKey, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(city))
        {
            var cityKey = ToCityLabel(city);
            filtered = filtered.Where(place =>
                string.Equals(ToCityLabel(place), cityKey, StringComparison.OrdinalIgnoreCase));
        }

        return filtered.Select(place => place.Id).ToHashSet();
    }

    private async Task<List<Media>> GetPhotosAsync(CancellationToken cancellationToken)
    {
        var mediaItems = await _mediaRepository.GetAllAsync(cancellationToken);
        return mediaItems.Where(media => media.MediaType == MediaType.Photo).ToList();
    }

    private static bool MatchesSearch(
        Media media,
        IReadOnlyDictionary<Guid, Place> placesById,
        string term)
    {
        if (media.FileName.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (media.PlaceId is Guid placeId && placesById.TryGetValue(placeId, out var place))
        {
            if (place.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || place.Country.Contains(term, StringComparison.OrdinalIgnoreCase)
                || place.Province.Contains(term, StringComparison.OrdinalIgnoreCase)
                || place.City.Contains(term, StringComparison.OrdinalIgnoreCase)
                || place.Address.Contains(term, StringComparison.OrdinalIgnoreCase)
                || ToCountryLabel(place.Country).Contains(term, StringComparison.OrdinalIgnoreCase)
                || ToCityLabel(place).Contains(term, StringComparison.OrdinalIgnoreCase)
                || PlaceNormalizer.GetDisplayLabel(place).Contains(term, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(place.CanonicalName)
                    && place.CanonicalName.Contains(term, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private GalleryMediaDto? MapGallery(
        Media media,
        IReadOnlyDictionary<Guid, Storage> storages)
    {
        if (!storages.TryGetValue(media.StorageId, out var storage))
        {
            return null;
        }

        string absolute;
        try
        {
            absolute = _fileAccessService.ResolveAbsolutePath(storage.PhotoRoot, media.RelativePath);
        }
        catch
        {
            absolute = string.Empty;
        }

        return new GalleryMediaDto
        {
            Id = media.Id,
            FileName = media.FileName,
            AbsoluteLibraryPath = absolute,
            CapturedAt = DateTimeHelper.ToUtcOffset(media.CapturedAt),
            PlaceId = media.PlaceId,
            MediaType = media.MediaType,
            IsFavorite = media.IsFavorite
        };
    }

    private static int ResolveYear(Media media) => MediaDate.ResolveYear(media.CapturedAt, media.ImportedAt);

    /// <summary>
    /// Gallery tree labels: always prefer Korean via PlaceNormalizer (Japan→일본, Osaka→오사카).
    /// </summary>
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

        if (string.Equals(value.Trim(), OtherTitle, StringComparison.Ordinal))
        {
            return OtherTitle;
        }

        var normalized = PlaceNormalizer.NormalizePlace(value);
        return string.IsNullOrWhiteSpace(normalized) ? OtherTitle : normalized;
    }

    private static string ToCityLabel(Place place) =>
        PlaceNormalizer.ResolveCityLabel(place, OtherTitle);
}
