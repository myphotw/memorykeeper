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

    private readonly IGalleryApiRepository _galleryApi;
    private readonly BaseApiClient _apiClient;
    private readonly ILogger<GalleryPhotoCatalog> _logger;

    public GalleryPhotoCatalog(
        IGalleryApiRepository galleryApi,
        BaseApiClient apiClient,
        ILogger<GalleryPhotoCatalog> logger)
    {
        _galleryApi = galleryApi ?? throw new ArgumentNullException(nameof(galleryApi));
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
        var photosTask = FetchAllPhotosAsync(year, country, keyword, cancellationToken);

        var photos = await photosTask.ConfigureAwait(false);
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
            LocationMetadataByFileId = detailMetadata,
            ApiBaseUrl = _apiClient.ApiBaseUrl,
        };
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
            .Where(photo => photo.HasGps)
            .Where(photo => !string.IsNullOrWhiteSpace(photo.FileId))
            .Where(photo => !markerIds.Contains(photo.FileId.Trim()))
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
                var metadata = ToLocationMetadata(detail.Metadata);
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

    private static GalleryPhotoLocationMetadataDto? ToLocationMetadata(
        IReadOnlyDictionary<string, JsonElement> metadata)
    {
        var latitude = GetDouble(metadata, "gps_lat");
        var longitude = GetDouble(metadata, "gps_lon");
        if (latitude is null || longitude is null)
        {
            return null;
        }

        return new GalleryPhotoLocationMetadataDto
        {
            Latitude = latitude,
            Longitude = longitude,
            Country = GetString(metadata, "country"),
            Province = GetString(metadata, "province"),
            City = GetString(metadata, "city"),
            District = GetString(metadata, "district"),
            PlaceName = GetString(metadata, "place_name"),
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
