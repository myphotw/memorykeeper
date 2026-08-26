using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs.Gallery;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Infrastructure.Services.Api;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace MemoryKeeper.Infrastructure.Repositories.Api;

/// <summary>
/// Loads the canonical Gallery search rows and coordinates from TC-Backend.
/// </summary>
public sealed class GalleryPhotoCatalog : IGalleryPhotoCatalog
{
    private const int PageSize = 200;
    private const int MaxPages = 50;
    private const int RecentTake = 48;

    private readonly IGalleryApiRepository _galleryApi;
    private readonly IMemoryKeeperPlaceApiRepository _placeApi;
    private readonly BaseApiClient _apiClient;
    private readonly ILogger<GalleryPhotoCatalog> _logger;

    public GalleryPhotoCatalog(
        IGalleryApiRepository galleryApi,
        IMemoryKeeperPlaceApiRepository placeApi,
        BaseApiClient apiClient,
        ILogger<GalleryPhotoCatalog> logger)
    {
        _galleryApi = galleryApi ?? throw new ArgumentNullException(nameof(galleryApi));
        _placeApi = placeApi ?? throw new ArgumentNullException(nameof(placeApi));
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<GalleryPhotoCatalogSnapshot> QueryAsync(
        int? year = null,
        string? country = null,
        string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        var mapTask = _galleryApi.GetMapAsync(year: year, cancellationToken: cancellationToken);
        var registeredPlacesTask = FetchRegisteredPlaceGeographyAsync(cancellationToken);
        var photosTask = FetchAllPhotosAsync(year, country, keyword, cancellationToken);
        var recentTask = year is null
                         && string.IsNullOrWhiteSpace(country)
                         && string.IsNullOrWhiteSpace(keyword)
            ? FetchRecentPhotoFileIdsAsync(cancellationToken)
            : Task.FromResult<IReadOnlyList<string>>([]);

        var photos = await photosTask.ConfigureAwait(false);
        var recentPhotoFileIds = await recentTask.ConfigureAwait(false);
        var registeredPlaces = await registeredPlacesTask.ConfigureAwait(false);
        MapResultDto map;
        try
        {
            map = await mapTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gallery /map failed; GPS detail metadata fallback will be used.");
            map = new MapResultDto();
        }
        var detailMetadata = await FetchMissingLocationMetadataAsync(
                photos,
                map.Items,
                cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Gallery catalog loaded from Backend. Photos={Photos} Markers={Markers} Year={Year} Country={Country} HasKeyword={HasKeyword}",
            photos.Count,
            map.Items.Count,
            year,
            country,
            !string.IsNullOrWhiteSpace(keyword));

        return new GalleryPhotoCatalogSnapshot
        {
            Photos = photos,
            MapMarkers = map.Items,
            RecentPhotoFileIds = recentPhotoFileIds,
            LocationMetadataByFileId = detailMetadata,
            RegisteredPlacesById = registeredPlaces,
            ApiBaseUrl = _apiClient.ApiBaseUrl,
        };
    }

    private async Task<IReadOnlyDictionary<Guid, GalleryRegisteredPlaceGeographyDto>> FetchRegisteredPlaceGeographyAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _placeApi.GetPlacesAsync(cancellationToken).ConfigureAwait(false);
            return response.Items
                .Where(place => place.Id != Guid.Empty)
                .GroupBy(place => place.Id)
                .ToDictionary(
                    group => group.Key,
                    group =>
                    {
                        var place = group.First();
                        return new GalleryRegisteredPlaceGeographyDto
                        {
                            Country = place.Country,
                            Province = place.Province,
                            City = place.City,
                            District = place.District,
                        };
                    });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "MemoryKeeper Place geography lookup failed; raw Gallery geography remains available.");
            return new Dictionary<Guid, GalleryRegisteredPlaceGeographyDto>();
        }
    }

    private async Task<IReadOnlyList<string>> FetchRecentPhotoFileIdsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await _galleryApi.GetPhotosAsync(
                    page: 1,
                    pageSize: RecentTake,
                    sort: "created_at_desc",
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return page.Items
                .Select(photo => photo.FileId?.Trim())
                .Where(fileId => !string.IsNullOrWhiteSpace(fileId))
                .Select(fileId => fileId!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(RecentTake)
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Gallery recent-upload ordering failed; the main catalog remains available.");
            return [];
        }
    }

    private async Task<IReadOnlyDictionary<string, GalleryPhotoLocationMetadataDto>> FetchMissingLocationMetadataAsync(
        IReadOnlyList<PhotoDto> photos,
        IReadOnlyList<MapMarkerDto> markers,
        CancellationToken cancellationToken)
    {
        var markerIds = markers
            .Select(marker => marker.FileId?.Trim())
            .Where(fileId => !string.IsNullOrWhiteSpace(fileId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targets = photos
            .Where(photo => !string.IsNullOrWhiteSpace(photo.FileId))
            .Where(photo =>
                (photo.HasGps && !markerIds.Contains(photo.FileId.Trim()))
                || string.IsNullOrWhiteSpace(photo.Country)
                || (string.IsNullOrWhiteSpace(photo.City) && string.IsNullOrWhiteSpace(photo.Province)))
            .ToList();
        if (targets.Count == 0)
        {
            return new Dictionary<string, GalleryPhotoLocationMetadataDto>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, GalleryPhotoLocationMetadataDto>(StringComparer.OrdinalIgnoreCase);
        using var gate = new SemaphoreSlim(4, 4);
        var tasks = targets.Select(async photo =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var mediaId = BackendFileIdCodec.ToGuid(photo.FileId);
                if (mediaId == Guid.Empty)
                {
                    return;
                }

                var detail = await _galleryApi.GetPhotoAsync(mediaId, cancellationToken).ConfigureAwait(false);
                var metadata = ToLocationMetadata(detail);
                if (metadata is not null)
                {
                    lock (result)
                    {
                        result[photo.FileId.Trim()] = metadata;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Gallery detail location fallback failed. FileIdHash={FileIdHash}",
                    BackendFileIdCodec.ToGuid(photo.FileId));
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        _logger.LogInformation(
            "Gallery detail location fallback completed. MissingMap={MissingMap} Recovered={Recovered}",
            targets.Count,
            result.Count);
        return result;
    }

    private static GalleryPhotoLocationMetadataDto? ToLocationMetadata(PhotoDetailDto detail)
    {
        var metadata = detail.Metadata;
        var latitude = GetDouble(metadata, "gps_lat");
        var longitude = GetDouble(metadata, "gps_lon");
        var country = GetString(metadata, "country");
        var province = GetString(metadata, "province");
        var city = GetString(metadata, "city");
        var district = GetString(metadata, "district");
        var placeName = GetString(metadata, "place_name");
        var memorykeeperPlaceId = detail.MemorykeeperPlaceId ?? GetGuid(metadata, "memorykeeper_place_id");
        var placeDisplayName = detail.PlaceDisplayName ?? GetString(metadata, "place_display_name");
        var geocodedPlaceName = detail.GeocodedPlaceName ?? GetString(metadata, "geocoded_place_name");
        if (latitude is null
            && longitude is null
            && string.IsNullOrWhiteSpace(country)
            && string.IsNullOrWhiteSpace(province)
            && string.IsNullOrWhiteSpace(city)
            && string.IsNullOrWhiteSpace(district)
            && string.IsNullOrWhiteSpace(placeName)
            && memorykeeperPlaceId is null
            && string.IsNullOrWhiteSpace(placeDisplayName)
            && string.IsNullOrWhiteSpace(geocodedPlaceName))
        {
            return null;
        }

        return new GalleryPhotoLocationMetadataDto
        {
            Latitude = latitude,
            Longitude = longitude,
            Country = country,
            Province = province,
            City = city,
            District = district,
            PlaceName = placeName,
            MemorykeeperPlaceId = memorykeeperPlaceId,
            PlaceDisplayName = placeDisplayName,
            PlaceCanonicalName = detail.PlaceCanonicalName ?? GetString(metadata, "place_canonical_name"),
            GeocodedPlaceName = geocodedPlaceName,
            PlaceMatchSource = detail.PlaceMatchSource ?? GetString(metadata, "place_match_source"),
            PlaceMatchDistanceM = detail.PlaceMatchDistanceM ?? GetDouble(metadata, "place_match_distance_m"),
            PlaceRevision = detail.PlaceRevision is > 0 ? detail.PlaceRevision.Value : GetInt(metadata, "place_revision") ?? 0,
        };
    }

    private static double? GetDouble(IReadOnlyDictionary<string, JsonElement> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(
                value.GetString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var number) => number,
            _ => null,
        };
    }

    private static string? GetString(IReadOnlyDictionary<string, JsonElement> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && value.ValueKind is not JsonValueKind.Null
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;

    private static Guid? GetGuid(IReadOnlyDictionary<string, JsonElement> metadata, string key) =>
        Guid.TryParse(GetString(metadata, key), out var value) ? value : null;

    private static int? GetInt(IReadOnlyDictionary<string, JsonElement> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var number) => number,
            _ => null,
        };
    }

    private async Task<IReadOnlyList<PhotoDto>> FetchAllPhotosAsync(
        int? year,
        string? country,
        string? keyword,
        CancellationToken cancellationToken)
    {
        var all = new List<PhotoDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var total = int.MaxValue;

        for (var page = 1; page <= MaxPages && all.Count < total; page++)
        {
            var batch = await _galleryApi.SearchAsync(
                    year: year,
                    country: country,
                    keyword: keyword,
                    page: page,
                    pageSize: PageSize,
                    sort: "capture_datetime_desc",
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            total = batch.TotalCount;
            foreach (var photo in batch.Items)
            {
                var key = photo.FileId?.Trim();
                if (string.IsNullOrWhiteSpace(key) || seen.Add(key))
                {
                    all.Add(photo);
                }
            }

            if (batch.Items.Count == 0 || all.Count >= total || batch.Items.Count < PageSize)
            {
                break;
            }
        }

        return all;
    }
}
