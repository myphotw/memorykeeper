using MemoryKeeper.Application;

using MemoryKeeper.Application.DTOs.Gallery;

using MemoryKeeper.Application.Interfaces;

using MemoryKeeper.Infrastructure.Services.Api;

using Microsoft.Extensions.Logging;



namespace MemoryKeeper.Infrastructure.Repositories.Api;



/// <summary>

/// Builds travel-record place aggregates from TC-Backend Gallery search + map APIs (no SQLite).

/// </summary>

public sealed class GalleryTravelRecordsRepository : ITravelRecordsRepository

{

    private const int PageSize = 200;

    private const int MaxPages = 50;



    private readonly IGalleryApiRepository _galleryApi;

    private readonly BaseApiClient _apiClient;

    private readonly ILogger<GalleryTravelRecordsRepository> _logger;



    public GalleryTravelRecordsRepository(

        IGalleryApiRepository galleryApi,

        BaseApiClient apiClient,

        ILogger<GalleryTravelRecordsRepository> logger)

    {

        _galleryApi = galleryApi ?? throw new ArgumentNullException(nameof(galleryApi));

        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    }



    public async Task<IReadOnlyList<TravelPlaceAggregateRaw>> GetPlaceAggregatesAsync(

        CancellationToken cancellationToken = default)

    {

        _logger.LogInformation(

            "TravelRecords Backend load start. Endpoint=/api/common/gallery/search|/map ApiBase={Base}",

            _apiClient.ApiBaseUrl);



        var photos = await FetchAllPhotosAsync(cancellationToken).ConfigureAwait(false);

        MapResultDto map;

        try

        {

            map = await _galleryApi.GetMapAsync(cancellationToken: cancellationToken)

                .ConfigureAwait(false);

        }

        catch (Exception ex)

        {

            _logger.LogWarning(ex, "TravelRecords map fetch failed; farthest distance may be unavailable.");

            map = new MapResultDto();

        }



        // Safest join: search photo.file_id ↔ map item.file_id, then aggregate by place_name.

        var coordsByFileId = BuildCoordsByFileId(map.Items);



        var groups = photos

            .GroupBy(p => PlaceIdentity.MapPlaceKey(p.PlaceName))

            .ToList();



        var result = new List<TravelPlaceAggregateRaw>(groups.Count);

        foreach (var group in groups)

        {

            var list = group.ToList();

            var first = list[0];

            var placeName = PlaceIdentity.DisplayName(first.PlaceName);

            var country = first.Country?.Trim() ?? string.Empty;

            // Align PlaceId with Visit Record map markers (place_name only).

            var placeId = PlaceIdentity.MapStableId(first.PlaceName);



            var dated = list

                .Where(p => p.CaptureDatetime.HasValue)

                .Select(p => p.CaptureDatetime!.Value.ToLocalTime().Date)

                .Distinct()

                .OrderBy(d => d)

                .ToList();



            var representative = list

                .OrderByDescending(p => p.Favorite)

                .ThenByDescending(p => p.CaptureDatetime)

                .First();



            var thumb = ToAbsoluteThumbnail(

                representative.ThumbnailUrl,

                representative.FileId);



            var groupCoords = list

                .Select(p => LookupCoords(coordsByFileId, p.FileId))

                .Where(c => c.HasValue)

                .Select(c => c!.Value)

                .ToList();



            (double Latitude, double Longitude)? repCoords = LookupCoords(

                coordsByFileId,

                representative.FileId);



            var resolved = PlaceIdentity.ResolveCoordinates(repCoords, groupCoords);

            var latitude = resolved?.Latitude ?? 0d;

            var longitude = resolved?.Longitude ?? 0d;



            _logger.LogInformation(

                "TravelRecords place aggregate. PlaceId={PlaceId} PlaceKey={PlaceKey} PlaceName={PlaceName} Lat={Lat} Lon={Lon} PhotoCount={PhotoCount} RepFileId={RepFileId} HasLocation={HasLocation}",

                placeId,

                group.Key,

                placeName,

                latitude,

                longitude,

                list.Count,

                representative.FileId,

                PlaceIdentity.HasValidCoordinates(latitude, longitude));



            result.Add(new TravelPlaceAggregateRaw

            {

                PlaceId = placeId,

                PlaceName = placeName,

                Country = country,

                Latitude = latitude,

                Longitude = longitude,

                PhotoCount = list.Count,

                FavoriteCount = list.Count(p => p.Favorite),

                RepresentativeMediaId = BackendFileIdCodec.ToGuid(representative.FileId),

                AbsoluteLibraryPath = thumb,

                VisitDates = dated,

            });

        }



        _logger.LogInformation(

            "TravelRecords Backend load done. Photos={Photos}, Places={Places}, MapMarkers={Markers}, FileIdCoords={Coords}",

            photos.Count,

            result.Count,

            map.Items.Count,

            coordsByFileId.Count);



        return result

            .OrderByDescending(p => p.VisitDates.Count > 0 ? p.VisitDates.Max() : DateTime.MinValue)

            .ThenBy(p => p.PlaceName, StringComparer.OrdinalIgnoreCase)

            .ToList();

    }



    private static Dictionary<string, (double Lat, double Lon)> BuildCoordsByFileId(

        IReadOnlyList<MapMarkerDto> markers)

    {

        var dict = new Dictionary<string, (double Lat, double Lon)>(StringComparer.OrdinalIgnoreCase);

        foreach (var marker in markers)

        {

            var fileId = marker.FileId?.Trim();

            if (string.IsNullOrWhiteSpace(fileId))

            {

                continue;

            }



            if (!PlaceIdentity.HasValidCoordinates(marker.Latitude, marker.Longitude))

            {

                continue;

            }



            if (!dict.ContainsKey(fileId))

            {

                dict[fileId] = (marker.Latitude, marker.Longitude);

            }

        }



        return dict;

    }



    private static (double Latitude, double Longitude)? LookupCoords(

        IReadOnlyDictionary<string, (double Lat, double Lon)> coordsByFileId,

        string? fileId)

    {

        var key = fileId?.Trim();

        if (string.IsNullOrWhiteSpace(key))

        {

            return null;

        }



        return coordsByFileId.TryGetValue(key, out var coords)

            ? (coords.Lat, coords.Lon)

            : null;

    }



    private async Task<List<PhotoDto>> FetchAllPhotosAsync(CancellationToken cancellationToken)

    {

        var all = new List<PhotoDto>();

        var page = 1;

        var total = int.MaxValue;



        while (page <= MaxPages && all.Count < total)

        {

            var batch = await _galleryApi.SearchAsync(

                    page: page,

                    pageSize: PageSize,

                    sort: "capture_datetime_desc",

                    cancellationToken: cancellationToken)

                .ConfigureAwait(false);



            total = batch.TotalCount;

            if (batch.Items.Count == 0)

            {

                break;

            }



            all.AddRange(batch.Items);

            _logger.LogInformation(

                "TravelRecords search page={Page} items={Count} total={Total} accumulated={Acc}",

                page,

                batch.Items.Count,

                batch.TotalCount,

                all.Count);



            if (all.Count >= batch.TotalCount || batch.Items.Count < PageSize)

            {

                break;

            }



            page++;

        }



        return all;

    }



    private string? ToAbsoluteThumbnail(string? thumbnailUrl, string? fileId)

    {

        var baseUrl = (_apiClient.ApiBaseUrl ?? string.Empty).TrimEnd('/');

        var raw = thumbnailUrl?.Trim();

        if (!string.IsNullOrWhiteSpace(raw))

        {

            if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)

                || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))

            {

                return raw;

            }



            return raw.StartsWith('/')

                ? baseUrl + raw

                : baseUrl + "/" + raw;

        }



        if (string.IsNullOrWhiteSpace(fileId))

        {

            return null;

        }



        return $"{baseUrl}/api/common/gallery/{fileId.Trim()}/thumbnail";

    }

}


