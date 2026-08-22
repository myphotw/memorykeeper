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

        var recentCount = photos.Count(photo =>
            photo.RecentRank.HasValue || ResolveImportedAt(photo.Photo).HasValue);

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
        var filtered = await QueryPhotosAsync(query, cancellationToken).ConfigureAwait(false);
        if (query.RecentOnly)
        {
            return filtered
                .OrderBy(photo => photo.RecentRank ?? int.MaxValue)
                .ThenByDescending(photo => ResolveImportedAt(photo.Photo))
                .Select(photo => photo.Photo)
                .ToList();
        }

        return filtered
            .OrderByDescending(photo => ResolveSortDate(photo.Photo))
            .Select(photo => photo.Photo)
            .ToList();
    }

    /// <summary>
    /// Projects the exact same hierarchy selection into Visit Map places. Place identity,
    /// display name, counts, coordinates and previews therefore share one NAS-only source.
    /// </summary>
    public async Task<VisitRecordQueryResult> QueryVisitRecordsAsync(
        GalleryHierarchyQuery query,
        CancellationToken cancellationToken = default)
    {
        var photos = await QueryPhotosAsync(query, cancellationToken).ConfigureAwait(false);
        var places = photos
            .Where(HasPlace)
            .GroupBy(PlaceStableId)
            .Select(group => ToVisitPlace(group.Key, group.ToList()))
            .Concat(photos
                .Where(photo => !HasPlace(photo))
                .GroupBy(_ => LibraryConstants.UnclassifiedPlaceId)
                .Select(group => ToVisitPlace(group.Key, group.ToList(), isUnclassified: true)))
            .OrderByDescending(place => place.LastCapturedDate)
            .ThenBy(place => place.PlaceName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var chips = string.IsNullOrWhiteSpace(query.SearchText)
            ? Array.Empty<MemorySearchChipDto>()
            : new[]
            {
                new MemorySearchChipDto
                {
                    Label = query.SearchText.Trim(),
                    Kind = MemorySearchChipKind.Place,
                },
            };

        return new VisitRecordQueryResult
        {
            AllMapPlaces = places,
            TimelinePlaces = places,
            Chips = chips,
        };
    }

    private async Task<List<HierarchyPhoto>> QueryPhotosAsync(
        GalleryHierarchyQuery query,
        CancellationToken cancellationToken)
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
                .Where(photo => photo.RecentRank.HasValue || ResolveImportedAt(photo.Photo).HasValue)
                .OrderBy(photo => photo.RecentRank ?? int.MaxValue)
                .ThenByDescending(photo => ResolveImportedAt(photo.Photo))
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

        if (query.Season is TravelSeason season)
        {
            var months = season switch
            {
                TravelSeason.Spring => new[] { 3, 4, 5 },
                TravelSeason.Summer => new[] { 6, 7, 8 },
                TravelSeason.Autumn => new[] { 9, 10, 11 },
                TravelSeason.Winter => new[] { 12, 1, 2 },
                _ => Array.Empty<int>(),
            };
            filtered = filtered.Where(photo =>
            {
                var date = ResolveSortDate(photo.Photo);
                return date != DateTimeOffset.MinValue && months.Contains(date.Month);
            });
        }

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var term = query.SearchText.Trim();
            filtered = filtered.Where(photo => MatchesSearch(photo, term));
        }

        return filtered.ToList();
    }

    private async Task<List<HierarchyPhoto>> LoadPhotosAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _catalog.QueryAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var markersByFileId = snapshot.MapMarkers
            .Where(marker => !string.IsNullOrWhiteSpace(marker.FileId))
            .GroupBy(marker => marker.FileId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var recentRankByFileId = snapshot.RecentPhotoFileIds
            .Select((fileId, rank) => (FileId: fileId?.Trim(), Rank: rank))
            .Where(item => !string.IsNullOrWhiteSpace(item.FileId))
            .GroupBy(item => item.FileId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Min(item => item.Rank), StringComparer.OrdinalIgnoreCase);
        var result = new List<HierarchyPhoto>();

        foreach (var photo in snapshot.Photos)
        {
            if (IsDeleted(photo))
            {
                continue;
            }

            var fileId = photo.FileId?.Trim();
            if (!string.IsNullOrWhiteSpace(fileId) && !seen.Add(fileId))
            {
                continue;
            }

            snapshot.LocationMetadataByFileId.TryGetValue(
                fileId ?? string.Empty,
                out var metadata);
            markersByFileId.TryGetValue(fileId ?? string.Empty, out var marker);
            recentRankByFileId.TryGetValue(fileId ?? string.Empty, out var recentRank);
            var registeredPlaceId = photo.MemorykeeperPlaceId
                                    ?? metadata?.MemorykeeperPlaceId
                                    ?? marker?.MemorykeeperPlaceId;
            snapshot.RegisteredPlacesById.TryGetValue(
                registeredPlaceId ?? Guid.Empty,
                out var registeredPlace);
            var latitude = marker is not null
                           && PlaceIdentity.HasValidCoordinates(marker.Latitude, marker.Longitude)
                ? marker.Latitude
                : photo.GpsLatitude ?? metadata?.Latitude;
            var longitude = marker is not null
                            && PlaceIdentity.HasValidCoordinates(marker.Latitude, marker.Longitude)
                ? marker.Longitude
                : photo.GpsLongitude ?? metadata?.Longitude;
            result.Add(new HierarchyPhoto(
                photo,
                metadata,
                marker,
                registeredPlace,
                latitude,
                longitude,
                snapshot.ApiBaseUrl,
                recentRankByFileId.ContainsKey(fileId ?? string.Empty) ? recentRank : null));
        }

        _logger.LogDebug("Gallery hierarchy loaded {PhotoCount} NAS photos", result.Count);
        return result;
    }

    private static bool HasPlace(HierarchyPhoto photo) =>
        RegisteredPlaceId(photo).HasValue || !string.IsNullOrWhiteSpace(RawPlaceName(photo));

    private static bool IsPending(HierarchyPhoto photo) =>
        !RegisteredPlaceId(photo).HasValue;

    private static bool IsDeleted(PhotoDto photo) =>
        string.Equals(photo.Status?.Trim(), "deleted", StringComparison.OrdinalIgnoreCase)
        || string.Equals(photo.Status?.Trim(), "tombstone", StringComparison.OrdinalIgnoreCase);

    private static string CountryLabel(HierarchyPhoto photo) =>
        ToCountryLabel(FirstNotEmpty(
            photo.Photo.Country,
            photo.Metadata?.Country,
            photo.Marker?.Country,
            photo.RegisteredPlace?.Country));

    private static string CityLabel(HierarchyPhoto photo)
    {
        var region = FirstNotEmpty(
            photo.Photo.City,
            photo.Metadata?.City,
            photo.Marker?.City,
            photo.Photo.Province,
            photo.Metadata?.Province,
            photo.Marker?.Province,
            photo.RegisteredPlace?.City,
            photo.RegisteredPlace?.Province);
        if (ContainsHangul(region))
        {
            // Preserve Backend administrative labels such as "구례군" and "서울특별시" verbatim.
            // PlaceNormalizer remains the fallback for aliases/non-Korean values.
            return region;
        }

        if (!string.IsNullOrWhiteSpace(region))
        {
            return ToCityLabel(region);
        }

        var rawPlaceName = RawPlaceName(photo);
        var transientPlace = new Place
        {
            Country = CountryLabel(photo),
            Province = photo.RegisteredPlace?.Province ?? string.Empty,
            City = photo.RegisteredPlace?.City ?? string.Empty,
            DisplayName = rawPlaceName,
            CanonicalName = rawPlaceName,
        };
        return PlaceNormalizer.ResolveCityLabel(transientPlace, OtherTitle);
    }

    private static string PlaceDisplayName(HierarchyPhoto photo)
    {
        var raw = FirstNotEmpty(
            photo.Photo.PlaceDisplayName,
            photo.Metadata?.PlaceDisplayName,
            photo.Marker?.PlaceDisplayName,
            RawPlaceName(photo));
        if (string.IsNullOrWhiteSpace(raw))
        {
            return UnclassifiedTitle;
        }

        return PlaceNormalizer.GetDisplayLabel(new Place
        {
            DisplayName = raw,
            CanonicalName = raw,
            Country = CountryLabel(photo),
            Province = FirstNotEmpty(photo.Photo.Province, photo.Metadata?.Province, photo.Marker?.Province),
            City = FirstNotEmpty(photo.Photo.City, photo.Metadata?.City, photo.Marker?.City),
        });
    }

    private static Guid PlaceStableId(HierarchyPhoto photo) =>
        RegisteredPlaceId(photo) ?? PlaceIdentity.StableId(
            CountryLabel(photo),
            CityLabel(photo),
            PlaceDisplayName(photo));

    private static Guid? RegisteredPlaceId(HierarchyPhoto photo) =>
        photo.Photo.MemorykeeperPlaceId
        ?? photo.Metadata?.MemorykeeperPlaceId
        ?? photo.Marker?.MemorykeeperPlaceId;

    private static string RawPlaceName(HierarchyPhoto photo) =>
        FirstNotEmpty(
            photo.Photo.PlaceName,
            photo.Photo.GeocodedPlaceName,
            photo.Metadata?.PlaceName,
            photo.Metadata?.GeocodedPlaceName,
            photo.Marker?.PlaceName,
            photo.Marker?.GeocodedPlaceName);

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
            photo.RegisteredPlace?.Country,
            photo.RegisteredPlace?.Province,
            photo.RegisteredPlace?.City,
            photo.RegisteredPlace?.District,
            photo.Photo.PlaceCanonicalName,
            photo.Metadata?.PlaceCanonicalName,
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

    private static VisitRecordPlaceDto ToVisitPlace(
        Guid placeId,
        IReadOnlyList<HierarchyPhoto> photos,
        bool isUnclassified = false)
    {
        var first = photos
            .OrderByDescending(photo => string.IsNullOrWhiteSpace(photo.Photo.Country) ? 0 : 1)
            .ThenByDescending(photo => string.IsNullOrWhiteSpace(photo.Photo.City) ? 0 : 1)
            .ThenByDescending(photo => string.IsNullOrWhiteSpace(photo.Photo.PlaceDisplayName) ? 0 : 1)
            .First();
        var representative = photos.FirstOrDefault(photo => photo.Photo.Favorite) ?? first;
        var dates = photos
            .Select(photo => ResolveSortDate(photo.Photo))
            .Where(date => date != DateTimeOffset.MinValue)
            .OrderBy(date => date)
            .ToList();
        var coordinates = photos
            .Where(photo => photo.Latitude is double latitude
                            && photo.Longitude is double longitude
                            && PlaceIdentity.HasValidCoordinates(latitude, longitude))
            .Select(photo => (photo.Latitude!.Value, photo.Longitude!.Value))
            .ToList();
        var representativeCoordinates = representative.Latitude is double repLatitude
                                        && representative.Longitude is double repLongitude
            ? (repLatitude, repLongitude)
            : ((double Latitude, double Longitude)?)null;
        var resolvedCoordinates = PlaceIdentity.ResolveCoordinates(representativeCoordinates, coordinates);
        var previews = photos
            .OrderByDescending(photo => ResolveSortDate(photo.Photo))
            .Select(ToVisitPreview)
            .ToList();
        var visitCount = dates.Select(date => date.ToLocalTime().Date).Distinct().Count();

        return new VisitRecordPlaceDto
        {
            PlaceId = placeId,
            PlaceName = isUnclassified ? UnclassifiedTitle : PlaceDisplayName(first),
            Country = CountryLabel(first),
            City = CityLabel(first),
            Latitude = resolvedCoordinates?.Latitude ?? 0d,
            Longitude = resolvedCoordinates?.Longitude ?? 0d,
            PhotoCount = photos.Count,
            VisitRecordCount = visitCount,
            FavoriteCount = photos.Count(photo => photo.Photo.Favorite),
            RepresentativeMediaId = ToMediaId(representative.Photo.FileId),
            RepresentativeAbsolutePath = ResolveThumbnailUrl(representative),
            FirstCapturedDate = dates.Count > 0 ? dates[0] : null,
            LastCapturedDate = dates.Count > 0 ? dates[^1] : null,
            CaptureYears = photos
                .Select(photo => ResolveYear(photo.Photo))
                .Where(year => year.HasValue)
                .Select(year => year!.Value)
                .Distinct()
                .OrderByDescending(year => year)
                .ToList(),
            AllPhotos = previews,
            PreviewPhotos = previews.Take(8).ToList(),
            MarkerScale = VisitRecordPlaceScoping.CalculateMarkerScale(visitCount, photos.Count),
            IsUnclassified = isUnclassified,
        };
    }

    private static VisitRecordPreviewPhotoDto ToVisitPreview(HierarchyPhoto photo)
    {
        var thumbnail = ResolveThumbnailUrl(photo) ?? string.Empty;
        return new VisitRecordPreviewPhotoDto
        {
            MediaId = BackendFileIdCodec.ToGuid(photo.Photo.FileId),
            BackendFileId = photo.Photo.FileId,
            FileName = photo.Photo.Filename,
            ThumbnailUrl = thumbnail,
            AbsoluteLibraryPath = thumbnail,
            IsFavorite = photo.Photo.Favorite,
            CapturedAt = ResolveSortDate(photo.Photo) is var captured && captured != DateTimeOffset.MinValue
                ? captured
                : null,
            CaptureYear = ResolveYear(photo.Photo) ?? 0,
        };
    }

    private static Guid? ToMediaId(string? fileId)
    {
        var value = BackendFileIdCodec.ToGuid(fileId);
        return value == Guid.Empty ? null : value;
    }

    private static string? ResolveThumbnailUrl(HierarchyPhoto photo)
    {
        var raw = FirstNotEmpty(photo.Photo.ThumbnailUrl, photo.Photo.PreviewUrl);
        if (Uri.TryCreate(raw, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        var relative = !string.IsNullOrWhiteSpace(raw)
            ? raw
            : $"/api/common/gallery/{Uri.EscapeDataString(photo.Photo.FileId)}/thumbnail";
        if (!Uri.TryCreate(EnsureTrailingSlash(photo.ApiBaseUrl), UriKind.Absolute, out var baseUri))
        {
            return relative;
        }

        return new Uri(baseUri, relative.TrimStart('/')).ToString();
    }

    private static string EnsureTrailingSlash(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.TrimEnd('/') + "/";

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
        GalleryPhotoLocationMetadataDto? Metadata,
        MapMarkerDto? Marker,
        GalleryRegisteredPlaceGeographyDto? RegisteredPlace,
        double? Latitude,
        double? Longitude,
        string ApiBaseUrl,
        int? RecentRank);
}
