using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

public sealed class MediaPlaceAssignmentService
{
    private readonly IMediaRepository _mediaRepository;
    private readonly IPlaceRepository _placeRepository;
    private readonly IMediaLibraryPathSyncService _pathSyncService;
    private readonly ICatalogInvalidation _catalogInvalidation;
    private readonly ILogger<MediaPlaceAssignmentService> _logger;

    public MediaPlaceAssignmentService(
        IMediaRepository mediaRepository,
        IPlaceRepository placeRepository,
        IMediaLibraryPathSyncService pathSyncService,
        ICatalogInvalidation catalogInvalidation,
        ILogger<MediaPlaceAssignmentService> logger)
    {
        _mediaRepository = mediaRepository;
        _placeRepository = placeRepository;
        _pathSyncService = pathSyncService;
        _catalogInvalidation = catalogInvalidation;
        _logger = logger;
    }

    public async Task<AssignMediaPlaceResult> AssignAsync(
        AssignMediaPlaceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MediaIds is null || request.MediaIds.Count == 0)
        {
            throw new ArgumentException("At least one media id is required.", nameof(request));
        }

        var place = await _placeRepository.GetByIdAsync(request.PlaceId, cancellationToken)
            ?? throw new InvalidOperationException($"Place '{request.PlaceId}' was not found.");

        var mediaItems = await _mediaRepository.GetByIdsAsync(request.MediaIds, cancellationToken);
        if (mediaItems.Count == 0)
        {
            return new AssignMediaPlaceResult
            {
                PlaceId = place.Id,
                UpdatedCount = 0
            };
        }

        var now = DateTime.UtcNow;
        var updatedCount = 0;
        foreach (var media in mediaItems)
        {
            media.PlaceId = place.Id;
            media.Status = MediaStatus.Imported;
            // Manual place assignment updates the photo marker to the place coordinates.
            media.Latitude = place.Latitude;
            media.Longitude = place.Longitude;

            media.UpdatedAt = now;
            await _mediaRepository.UpdateAsync(media, cancellationToken);
            await _pathSyncService.SyncMediaPathAsync(media, place, cancellationToken);
            updatedCount++;
        }

        _catalogInvalidation.Invalidate();
        _logger.LogInformation(
            "Assigned place to media. PlaceId={PlaceId}, UpdatedCount={UpdatedCount}",
            place.Id,
            updatedCount);

        return new AssignMediaPlaceResult
        {
            PlaceId = place.Id,
            UpdatedCount = updatedCount
        };
    }
}
