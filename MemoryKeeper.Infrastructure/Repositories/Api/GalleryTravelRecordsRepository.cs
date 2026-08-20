using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs.Gallery;
using MemoryKeeper.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Infrastructure.Repositories.Api;

/// <summary>
/// Builds travel aggregates exclusively from the canonical NAS Gallery catalog.
/// </summary>
public sealed class GalleryTravelRecordsRepository : ITravelRecordsRepository
{
    private readonly IGalleryPhotoCatalog _catalog;
    private readonly ILogger<GalleryTravelRecordsRepository> _logger;

    public GalleryTravelRecordsRepository(
        IGalleryPhotoCatalog catalog,
        ILogger<GalleryTravelRecordsRepository> logger)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<TravelPlaceAggregateRaw>> GetPlaceAggregatesAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _catalog.QueryAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var coordsByFileId = BuildCoordsByFileId(
            snapshot.MapMarkers,
            snapshot.LocationMetadataByFileId);

        var result = snapshot.Photos
            .GroupBy(photo => PlaceIdentity.MapPlaceKey(
                ResolvePlaceName(photo, snapshot.LocationMetadataByFileId)))
            .Select(group => ToAggregate(
                group.ToList(),
                coordsByFileId,
                snapshot.LocationMetadataByFileId,
                snapshot.ApiBaseUrl))
            .OrderByDescending(place => place.VisitDates.Count > 0
                ? place.VisitDates.Max()
                : DateTime.MinValue)
            .ThenBy(place => place.PlaceName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.LogInformation(
            "TravelRecords built from canonical Gallery catalog. Photos={Photos} Places={Places} Markers={Markers}",
            snapshot.Photos.Count,
            result.Count,
            snapshot.MapMarkers.Count);

        return result;
    }

    private static TravelPlaceAggregateRaw ToAggregate(
        IReadOnlyList<PhotoDto> photos,
        IReadOnlyDictionary<string, (double Lat, double Lon)> coordsByFileId,
        IReadOnlyDictionary<string, GalleryPhotoLocationMetadataDto> metadataByFileId,
        string apiBaseUrl)
    {
        var representative = photos
            .OrderByDescending(photo => photo.Favorite)
            .ThenByDescending(photo => photo.CaptureDatetime)
            .First();
        var placeName = photos
            .Select(photo => ResolvePlaceName(photo, metadataByFileId))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        var country = photos
            .Select(photo => FirstNonEmpty(
                photo.Country,
                LookupMetadata(metadataByFileId, photo.FileId)?.Country))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        var visitDates = photos
            .Where(photo => photo.CaptureDatetime.HasValue)
            .Select(photo => photo.CaptureDatetime!.Value.ToLocalTime().Date)
            .Distinct()
            .OrderBy(date => date)
            .ToList();
        var groupCoords = photos
            .Select(photo => LookupCoords(coordsByFileId, photo.FileId))
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();
        var resolved = PlaceIdentity.ResolveCoordinates(
            LookupCoords(coordsByFileId, representative.FileId),
            groupCoords);

        return new TravelPlaceAggregateRaw
        {
            PlaceId = PlaceIdentity.MapStableId(placeName),
            PlaceName = PlaceIdentity.DisplayName(placeName),
            Country = country,
            Latitude = resolved?.Latitude ?? 0d,
            Longitude = resolved?.Longitude ?? 0d,
            PhotoCount = photos.Count,
            FavoriteCount = photos.Count(photo => photo.Favorite),
            RepresentativeMediaId = BackendFileIdCodec.ToGuid(representative.FileId),
            AbsoluteLibraryPath = ResolveThumbnailUrl(apiBaseUrl, representative),
            VisitDates = visitDates,
        };
    }

    private static Dictionary<string, (double Lat, double Lon)> BuildCoordsByFileId(
        IReadOnlyList<MapMarkerDto> markers,
        IReadOnlyDictionary<string, GalleryPhotoLocationMetadataDto> metadataByFileId)
    {
        var result = new Dictionary<string, (double Lat, double Lon)>(StringComparer.OrdinalIgnoreCase);
        foreach (var marker in markers)
        {
            var key = marker.FileId?.Trim();
            if (!string.IsNullOrWhiteSpace(key)
                && PlaceIdentity.HasValidCoordinates(marker.Latitude, marker.Longitude))
            {
                result.TryAdd(key, (marker.Latitude, marker.Longitude));
            }
        }

        foreach (var (fileId, metadata) in metadataByFileId)
        {
            if (metadata.Latitude is double latitude
                && metadata.Longitude is double longitude
                && PlaceIdentity.HasValidCoordinates(latitude, longitude))
            {
                result.TryAdd(fileId, (latitude, longitude));
            }
        }

        return result;
    }

    private static string? ResolvePlaceName(
        PhotoDto photo,
        IReadOnlyDictionary<string, GalleryPhotoLocationMetadataDto> metadataByFileId) =>
        FirstNonEmpty(
            photo.PlaceName,
            LookupMetadata(metadataByFileId, photo.FileId)?.PlaceName);

    private static GalleryPhotoLocationMetadataDto? LookupMetadata(
        IReadOnlyDictionary<string, GalleryPhotoLocationMetadataDto> metadataByFileId,
        string? fileId)
    {
        var key = fileId?.Trim();
        return !string.IsNullOrWhiteSpace(key) && metadataByFileId.TryGetValue(key, out var metadata)
            ? metadata
            : null;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.Select(value => value?.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static (double Latitude, double Longitude)? LookupCoords(
        IReadOnlyDictionary<string, (double Lat, double Lon)> coords,
        string? fileId)
    {
        var key = fileId?.Trim();
        return !string.IsNullOrWhiteSpace(key) && coords.TryGetValue(key, out var value)
            ? (value.Lat, value.Lon)
            : null;
    }

    private static string? ResolveThumbnailUrl(string apiBaseUrl, PhotoDto photo)
    {
        var raw = photo.ThumbnailUrl?.Trim();
        if (!string.IsNullOrWhiteSpace(raw))
        {
            if (Uri.TryCreate(raw, UriKind.Absolute, out _))
            {
                return raw;
            }

            return $"{apiBaseUrl.TrimEnd('/')}/{raw.TrimStart('/')}";
        }

        return string.IsNullOrWhiteSpace(photo.FileId)
            ? null
            : $"{apiBaseUrl.TrimEnd('/')}/api/common/gallery/{photo.FileId.Trim()}/thumbnail";
    }
}
