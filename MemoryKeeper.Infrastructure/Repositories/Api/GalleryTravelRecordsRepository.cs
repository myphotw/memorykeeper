using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Infrastructure.Repositories.Api;

/// <summary>
/// Travel aggregates projected from the same NAS hierarchy query used by Gallery and Visit Map.
/// </summary>
public sealed class GalleryTravelRecordsRepository : ITravelRecordsRepository
{
    private readonly GalleryHierarchyService _hierarchy;
    private readonly ILogger<GalleryTravelRecordsRepository> _logger;

    public GalleryTravelRecordsRepository(
        GalleryHierarchyService hierarchy,
        ILogger<GalleryTravelRecordsRepository> logger)
    {
        _hierarchy = hierarchy ?? throw new ArgumentNullException(nameof(hierarchy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<TravelPlaceAggregateRaw>> GetPlaceAggregatesAsync(
        CancellationToken cancellationToken = default)
    {
        var projection = await _hierarchy.QueryVisitRecordsAsync(
            new GalleryHierarchyQuery(), cancellationToken).ConfigureAwait(false);
        var result = projection.AllMapPlaces.Select(place => new TravelPlaceAggregateRaw
        {
            PlaceId = place.PlaceId,
            PlaceName = place.PlaceName,
            Country = place.Country,
            Latitude = place.Latitude,
            Longitude = place.Longitude,
            PhotoCount = place.PhotoCount,
            FavoriteCount = place.FavoriteCount,
            IsUnclassified = place.IsUnclassified,
            RepresentativeMediaId = place.RepresentativeMediaId ?? Guid.Empty,
            AbsoluteLibraryPath = place.RepresentativeAbsolutePath,
            VisitDates = place.AllPhotos
                .Where(photo => photo.CapturedAt.HasValue)
                .Select(photo => photo.CapturedAt!.Value.ToLocalTime().Date)
                .Distinct()
                .OrderBy(date => date)
                .ToList(),
            Photos = place.AllPhotos.Select(photo => new TravelPhotoCandidateRaw
            {
                MediaId = photo.MediaId == Guid.Empty ? null : photo.MediaId,
                BackendFileId = photo.BackendFileId,
                ThumbnailPath = photo.ThumbnailUrl,
                Country = photo.Country,
                CapturedAt = photo.CaptureDatetime,
                IsFavorite = photo.IsFavorite,
            }).ToList(),
        }).ToList();

        _logger.LogInformation(
            "TravelRecords projected from GalleryHierarchyService. Places={Places}",
            result.Count);
        return result;
    }
}
