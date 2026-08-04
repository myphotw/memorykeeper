using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application.Interfaces;

public interface IPlaceReclassificationService
{
    /// <param name="reassignFromOtherPlaces">
    /// When true, media already linked to another place inside the radius are moved to this place.
    /// </param>
    Task<PlaceReclassificationResult> ReclassifyAsync(
        Guid placeId,
        bool reassignFromOtherPlaces = false,
        CancellationToken cancellationToken = default);
}
