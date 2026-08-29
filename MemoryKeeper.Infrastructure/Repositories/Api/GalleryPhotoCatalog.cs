using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs.Gallery;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Infrastructure.Services.Api;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
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
    private static readonly TimeSpan DefaultSnapshotTtl = TimeSpan.FromSeconds(30);

    private readonly IGalleryApiRepository _galleryApi;
    private readonly IMemoryKeeperPlaceApiRepository _placeApi;
    private readonly BaseApiClient _apiClient;
    private readonly ILogger<GalleryPhotoCatalog> _logger;
    private readonly ICatalogInvalidation _catalogInvalidation;
    private readonly object _snapshotGate = new();

    private GalleryPhotoCatalogSnapshot? _defaultSnapshot;
    private DateTimeOffset _defaultSnapshotExpiresAt;
    private long _defaultSnapshotGeneration = -1;
    private Task<SnapshotFetchResult>? _defaultSnapshotInFlight;
    private long _defaultSnapshotInFlightGeneration = -1;

    public GalleryPhotoCatalog(
        IGalleryApiRepository galleryApi,
        IMemoryKeeperPlaceApiRepository placeApi,
        BaseApiClient apiClient,
        ILogger<GalleryPhotoCatalog> logger,
        ICatalogInvalidation? catalogInvalidation = null)
    {
        _galleryApi = galleryApi ?? throw new ArgumentNullException(nameof(galleryApi));
        _placeApi = placeApi ?? throw new ArgumentNullException(nameof(placeApi));
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _catalogInvalidation = catalogInvalidation ?? new CatalogInvalidation();
        _catalogInvalidation.Invalidated += OnCatalogInvalidated;
    }

    public Task<GalleryPhotoCatalogSnapshot> QueryAsync(
        int? year = null,
        string? country = null,
        string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        return IsDefaultSnapshotQuery(year, country, keyword)
            ? GetDefaultSnapshotAsync(cancellationToken)
            : FetchUncachedSnapshotAsync(year, country, keyword, cancellationToken);
    }

    private async Task<GalleryPhotoCatalogSnapshot> GetDefaultSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Task<SnapshotFetchResult> sharedTask;
        TaskCompletionSource<SnapshotFetchResult>? fetchSource = null;
        long generation;
        var now = DateTimeOffset.UtcNow;

        lock (_snapshotGate)
        {
            generation = _catalogInvalidation.Generation;
            if (_defaultSnapshot is not null
                && _defaultSnapshotGeneration == generation
                && now < _defaultSnapshotExpiresAt)
            {
                _logger.LogInformation(
                    "Gallery default Snapshot cache hit. Generation={Generation}",
                    generation);
                return _defaultSnapshot;
            }

            if (_defaultSnapshotInFlight is not null
                && _defaultSnapshotInFlightGeneration == generation)
            {
                sharedTask = _defaultSnapshotInFlight;
                _logger.LogInformation(
                    "Gallery default Snapshot in-flight reused. Generation={Generation}",
                    generation);
            }
            else
            {
                fetchSource = new TaskCompletionSource<SnapshotFetchResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                sharedTask = fetchSource.Task;
                _defaultSnapshotInFlight = sharedTask;
                _defaultSnapshotInFlightGeneration = generation;
            }
        }

        if (fetchSource is not null)
        {
            _ = PopulateDefaultSnapshotAsync(fetchSource, generation);
            TryWriteSnapshotLog(() => _logger.LogInformation(
                "Gallery default Snapshot cache miss. Generation={Generation}",
                generation));
        }

        var result = await sharedTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        return result.Snapshot;
    }

    private async Task PopulateDefaultSnapshotAsync(
        TaskCompletionSource<SnapshotFetchResult> fetchSource,
        long generation)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await FetchSnapshotAsync(
                    year: null,
                    country: null,
                    keyword: null,
                    CancellationToken.None)
                .ConfigureAwait(false);
            var completedAt = DateTimeOffset.UtcNow;
            stopwatch.Stop();

            var staleGeneration = false;
            lock (_snapshotGate)
            {
                var currentGeneration = _catalogInvalidation.Generation;
                staleGeneration = currentGeneration != generation;
                if (!staleGeneration
                    && ReferenceEquals(_defaultSnapshotInFlight, fetchSource.Task)
                    && !result.IsDegraded)
                {
                    _defaultSnapshot = result.Snapshot;
                    _defaultSnapshotGeneration = generation;
                    _defaultSnapshotExpiresAt = completedAt + DefaultSnapshotTtl;
                }
            }

            fetchSource.TrySetResult(result);

            TryWriteSnapshotLog(() => _logger.LogInformation(
                "Gallery default Snapshot cold fetch completed. ElapsedMs={ElapsedMs} Generation={Generation} Degraded={Degraded}",
                stopwatch.ElapsedMilliseconds,
                generation,
                result.IsDegraded));

            if (staleGeneration)
            {
                TryWriteSnapshotLog(() => _logger.LogInformation(
                    "Gallery default Snapshot stale generation result discarded from cache. FetchGeneration={FetchGeneration} CurrentGeneration={CurrentGeneration}",
                    generation,
                    _catalogInvalidation.Generation));
            }
            else if (result.IsDegraded)
            {
                TryWriteSnapshotLog(() => _logger.LogInformation(
                    "Gallery default Snapshot degraded result was not cached. Generation={Generation}",
                    generation));
            }
        }
        catch (OperationCanceledException ex)
        {
            stopwatch.Stop();
            fetchSource.TrySetCanceled(ex.CancellationToken);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            fetchSource.TrySetException(ex);
            TryWriteSnapshotLog(() => _logger.LogWarning(
                ex,
                "Gallery default Snapshot cold fetch failed. ElapsedMs={ElapsedMs} Generation={Generation}",
                stopwatch.ElapsedMilliseconds,
                generation));
        }
        finally
        {
            lock (_snapshotGate)
            {
                if (ReferenceEquals(_defaultSnapshotInFlight, fetchSource.Task))
                {
                    _defaultSnapshotInFlight = null;
                    _defaultSnapshotInFlightGeneration = -1;
                }
            }
        }
    }

    private async Task<GalleryPhotoCatalogSnapshot> FetchUncachedSnapshotAsync(
        int? year,
        string? country,
        string? keyword,
        CancellationToken cancellationToken)
    {
        var result = await FetchSnapshotAsync(year, country, keyword, cancellationToken).ConfigureAwait(false);
        return result.Snapshot;
    }

    private async Task<SnapshotFetchResult> FetchSnapshotAsync(
        int? year,
        string? country,
        string? keyword,
        CancellationToken cancellationToken)
    {
        var mapTask = _galleryApi.GetMapAsync(year: year, cancellationToken: cancellationToken);
        var registeredPlacesTask = FetchRegisteredPlaceGeographyAsync(cancellationToken);
        var photosTask = FetchAllPhotosAsync(year, country, keyword, cancellationToken);
        var recentTask = year is null
                         && string.IsNullOrWhiteSpace(country)
                         && string.IsNullOrWhiteSpace(keyword)
            ? FetchRecentPhotoFileIdsAsync(cancellationToken)
            : Task.FromResult(AuxiliaryFetchResult<IReadOnlyList<string>>.Success([]));

        var photos = await photosTask.ConfigureAwait(false);
        var recentPhotoFileIds = await recentTask.ConfigureAwait(false);
        var registeredPlaces = await registeredPlacesTask.ConfigureAwait(false);
        var mapSucceeded = false;
        MapResultDto map;
        try
        {
            map = await mapTask.ConfigureAwait(false);
            mapSucceeded = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gallery /map failed; an empty map result will be used.");
            map = new MapResultDto();
        }
        var detailMetadata = mapSucceeded
            ? await FetchMissingLocationMetadataAsync(photos, map.Items, cancellationToken).ConfigureAwait(false)
            : AuxiliaryFetchResult<IReadOnlyDictionary<string, GalleryPhotoLocationMetadataDto>>.Success(
                new Dictionary<string, GalleryPhotoLocationMetadataDto>(StringComparer.OrdinalIgnoreCase));

        _logger.LogInformation(
            "Gallery catalog loaded from Backend. Photos={Photos} Markers={Markers} Year={Year} Country={Country} HasKeyword={HasKeyword}",
            photos.Count,
            map.Items.Count,
            year,
            country,
            !string.IsNullOrWhiteSpace(keyword));

        var snapshot = new GalleryPhotoCatalogSnapshot
        {
            Photos = photos,
            MapMarkers = map.Items,
            RecentPhotoFileIds = recentPhotoFileIds.Value,
            LocationMetadataByFileId = detailMetadata.Value,
            RegisteredPlacesById = registeredPlaces.Value,
            ApiBaseUrl = _apiClient.ApiBaseUrl,
        };

        return new SnapshotFetchResult(
            snapshot,
            IsDegraded: !mapSucceeded
                        || !registeredPlaces.Succeeded
                        || !recentPhotoFileIds.Succeeded
                        || !detailMetadata.Succeeded);
    }

    private async Task<AuxiliaryFetchResult<IReadOnlyDictionary<Guid, GalleryRegisteredPlaceGeographyDto>>> FetchRegisteredPlaceGeographyAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _placeApi.GetPlacesAsync(cancellationToken).ConfigureAwait(false);
            var places = response.Items
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
            return AuxiliaryFetchResult<IReadOnlyDictionary<Guid, GalleryRegisteredPlaceGeographyDto>>.Success(places);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MemoryKeeper Place geography lookup failed; raw Gallery geography remains available.");
            return AuxiliaryFetchResult<IReadOnlyDictionary<Guid, GalleryRegisteredPlaceGeographyDto>>.Failure(
                new Dictionary<Guid, GalleryRegisteredPlaceGeographyDto>());
        }
    }

    private async Task<AuxiliaryFetchResult<IReadOnlyList<string>>> FetchRecentPhotoFileIdsAsync(
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

            var fileIds = page.Items
                .Select(photo => photo.FileId?.Trim())
                .Where(fileId => !string.IsNullOrWhiteSpace(fileId))
                .Select(fileId => fileId!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(RecentTake)
                .ToList();
            return AuxiliaryFetchResult<IReadOnlyList<string>>.Success(fileIds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gallery recent-upload ordering failed; the main catalog remains available.");
            return AuxiliaryFetchResult<IReadOnlyList<string>>.Failure([]);
        }
    }

    private async Task<AuxiliaryFetchResult<IReadOnlyDictionary<string, GalleryPhotoLocationMetadataDto>>> FetchMissingLocationMetadataAsync(
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
            .Where(photo => photo.HasGps)
            .Where(photo => !markerIds.Contains(photo.FileId.Trim()))
            .ToList();
        if (targets.Count == 0)
        {
            return AuxiliaryFetchResult<IReadOnlyDictionary<string, GalleryPhotoLocationMetadataDto>>.Success(
                new Dictionary<string, GalleryPhotoLocationMetadataDto>(StringComparer.OrdinalIgnoreCase));
        }

        var result = new Dictionary<string, GalleryPhotoLocationMetadataDto>(StringComparer.OrdinalIgnoreCase);
        var failed = 0;
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref failed, 1);
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
        return failed == 0
            ? AuxiliaryFetchResult<IReadOnlyDictionary<string, GalleryPhotoLocationMetadataDto>>.Success(result)
            : AuxiliaryFetchResult<IReadOnlyDictionary<string, GalleryPhotoLocationMetadataDto>>.Failure(result);
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

    private static bool IsDefaultSnapshotQuery(int? year, string? country, string? keyword) =>
        year is null
        && string.IsNullOrWhiteSpace(country)
        && string.IsNullOrWhiteSpace(keyword);

    private static void TryWriteSnapshotLog(Action writeLog)
    {
        try
        {
            writeLog();
        }
        catch (Exception)
        {
            // Snapshot completion and in-flight cleanup must never depend on a logging provider.
        }
    }

    private void OnCatalogInvalidated(object? sender, CatalogInvalidatedEventArgs args)
    {
        var cacheRemoved = false;
        var inFlightDetached = false;
        lock (_snapshotGate)
        {
            if (_defaultSnapshot is not null && _defaultSnapshotGeneration < args.Generation)
            {
                _defaultSnapshot = null;
                _defaultSnapshotExpiresAt = default;
                _defaultSnapshotGeneration = -1;
                cacheRemoved = true;
            }

            if (_defaultSnapshotInFlight is not null
                && _defaultSnapshotInFlightGeneration < args.Generation)
            {
                _defaultSnapshotInFlight = null;
                _defaultSnapshotInFlightGeneration = -1;
                inFlightDetached = true;
            }
        }

        _logger.LogInformation(
            "Gallery default Snapshot cache invalidated. Generation={Generation} Surfaces={Surfaces} CacheRemoved={CacheRemoved} InFlightDetached={InFlightDetached}",
            args.Generation,
            args.Surfaces,
            cacheRemoved,
            inFlightDetached);
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

    private sealed record SnapshotFetchResult(
        GalleryPhotoCatalogSnapshot Snapshot,
        bool IsDegraded);

    private readonly record struct AuxiliaryFetchResult<T>(T Value, bool Succeeded)
    {
        public static AuxiliaryFetchResult<T> Success(T value) => new(value, true);

        public static AuxiliaryFetchResult<T> Failure(T value) => new(value, false);
    }
}
