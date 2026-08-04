using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

public sealed class PlaceReclassificationService : IPlaceReclassificationService
{
    private readonly IPlaceRepository _placeRepository;
    private readonly IMediaRepository _mediaRepository;
    private readonly IMediaLibraryPathSyncService _pathSyncService;
    private readonly ILogger<PlaceReclassificationService> _logger;

    public PlaceReclassificationService(
        IPlaceRepository placeRepository,
        IMediaRepository mediaRepository,
        IMediaLibraryPathSyncService pathSyncService,
        ILogger<PlaceReclassificationService> logger)
    {
        _placeRepository = placeRepository;
        _mediaRepository = mediaRepository;
        _pathSyncService = pathSyncService;
        _logger = logger;
    }

    public async Task<PlaceReclassificationResult> ReclassifyAsync(
        Guid placeId,
        bool reassignFromOtherPlaces = false,
        CancellationToken cancellationToken = default)
    {
        var place = await _placeRepository.GetByIdAsync(placeId, cancellationToken)
            ?? throw new InvalidOperationException($"Place '{placeId}' was not found.");

        var mediaWithGps = await _mediaRepository.GetWithGpsAsync(cancellationToken);
        var assignedCount = 0;
        var reassignedFromOtherCount = 0;
        var unassignedCount = 0;

        foreach (var media in mediaWithGps)
        {
            var latitude = media.Latitude!.Value;
            var longitude = media.Longitude!.Value;
            var distance = GeoMath.DistanceMeters(latitude, longitude, place.Latitude, place.Longitude);
            var withinRadius = distance <= place.Radius;

            if (withinRadius)
            {
                if (media.PlaceId == place.Id)
                {
                    continue;
                }

                if (media.PlaceId is not null && !reassignFromOtherPlaces)
                {
                    // Only claim unassigned media unless steal is enabled.
                    continue;
                }

                var movedFromOther = media.PlaceId is not null && media.PlaceId != place.Id;
                media.PlaceId = place.Id;
                media.Status = MediaStatus.Imported;
                media.UpdatedAt = DateTime.UtcNow;
                await _mediaRepository.UpdateAsync(media, cancellationToken);
                await _pathSyncService.SyncMediaPathAsync(media, place, cancellationToken);
                assignedCount++;
                if (movedFromOther)
                {
                    reassignedFromOtherCount++;
                }

                continue;
            }

            if (media.PlaceId == place.Id)
            {
                media.PlaceId = null;
                media.Status = MediaStatus.Pending;
                media.UpdatedAt = DateTime.UtcNow;
                await _mediaRepository.UpdateAsync(media, cancellationToken);
                await _pathSyncService.SyncMediaPathAsync(media, place: null, cancellationToken);
                unassignedCount++;
            }
        }

        _logger.LogInformation(
            "Reclassified media for place. PlaceId={PlaceId}, Assigned={Assigned}, FromOther={FromOther}, Unassigned={Unassigned}",
            placeId,
            assignedCount,
            reassignedFromOtherCount,
            unassignedCount);

        return new PlaceReclassificationResult
        {
            PlaceId = placeId,
            AssignedCount = assignedCount,
            ReassignedFromOtherCount = reassignedFromOtherCount,
            UnassignedCount = unassignedCount
        };
    }
}
