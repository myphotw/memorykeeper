using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Application.Interfaces;

public interface ILocationResolver
{
    Task<LocationResult?> ResolveAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Forward geocoding: address text → coordinates.
    /// </summary>
    Task<LocationResult?> ResolveAddressAsync(
        string address,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Google Places Autocomplete suggestions for the given input.
    /// </summary>
    Task<IReadOnlyList<PlaceSuggestionDto>> SuggestPlacesAsync(
        string input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve a Google Place ID to coordinates and formatted address.
    /// </summary>
    Task<LocationResult?> ResolvePlaceIdAsync(
        string placeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Nearby Google Places ranked by distance from GPS (MK-042O).
    /// </summary>
    Task<IReadOnlyList<NearbyPlaceCandidateDto>> SearchNearbyAsync(
        double latitude,
        double longitude,
        int maxResults = 5,
        CancellationToken cancellationToken = default);
}
