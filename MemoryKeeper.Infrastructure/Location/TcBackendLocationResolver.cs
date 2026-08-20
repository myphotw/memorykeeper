using System.Globalization;
using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Infrastructure.Services.Api;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Infrastructure.Location;

/// <summary>
/// Resolves locations through TC-Backend so provider credentials remain server-side.
/// </summary>
public sealed class TcBackendLocationResolver : ILocationResolver
{
    private readonly BaseApiClient _apiClient;
    private readonly ILogger<TcBackendLocationResolver> _logger;

    public TcBackendLocationResolver(
        BaseApiClient apiClient,
        ILogger<TcBackendLocationResolver> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    public async Task<LocationResult?> ResolveAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default)
    {
        var inv = CultureInfo.InvariantCulture;
        var path = "/api/common/geocoding/reverse"
            + $"?latitude={latitude.ToString(inv)}&longitude={longitude.ToString(inv)}&language=ko";
        var response = await _apiClient.GetAsync<BackendLocationCandidate>(path, cancellationToken);
        return response.Data is null ? null : ToLocationResult(response.Data);
    }

    public async Task<LocationResult?> ResolveAddressAsync(
        string address,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        var path = $"/api/common/geocoding/forward?query={Uri.EscapeDataString(address.Trim())}&language=ko";
        var response = await _apiClient.GetAsync<BackendLocationCandidateList>(path, cancellationToken);
        var candidate = response.Data?.Items.FirstOrDefault();
        return candidate is null ? null : ToLocationResult(candidate);
    }

    public async Task<IReadOnlyList<PlaceSuggestionDto>> SuggestPlacesAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Trim().Length < 2)
        {
            return [];
        }

        var path = $"/api/common/places/autocomplete?query={Uri.EscapeDataString(input.Trim())}&language=ko";
        var response = await _apiClient.GetAsync<BackendPlacesAutocompleteResponse>(path, cancellationToken);
        return response.Data?.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.PlaceId))
            .Select(item => new PlaceSuggestionDto
            {
                PlaceId = item.PlaceId,
                PrimaryText = item.MainText,
                SecondaryText = item.SecondaryText ?? string.Empty,
                Description = item.DisplayName,
            })
            .ToList() ?? [];
    }

    public async Task<LocationResult?> ResolvePlaceIdAsync(
        string placeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(placeId);
        var path = $"/api/common/places/details?place_id={Uri.EscapeDataString(placeId.Trim())}&language=ko";
        var response = await _apiClient.GetAsync<BackendLocationCandidate>(path, cancellationToken);
        return response.Data is null ? null : ToLocationResult(response.Data);
    }

    public async Task<IReadOnlyList<NearbyPlaceCandidateDto>> SearchNearbyAsync(
        double latitude,
        double longitude,
        int maxResults = 5,
        CancellationToken cancellationToken = default)
    {
        // TC-Backend currently exposes normalized reverse geocoding and text place search,
        // but no coordinate-based nearby-list contract. Use the reverse-geocoded label as
        // the server-side search seed and rank the returned candidates locally by distance.
        var location = await ResolveAsync(latitude, longitude, cancellationToken);
        var searchText = location is null
            ? string.Empty
            : FirstNotEmpty(location.DisplayName, location.City, location.Address);
        if (string.IsNullOrWhiteSpace(searchText) || maxResults <= 0)
        {
            return [];
        }

        var path = $"/api/common/places/search?query={Uri.EscapeDataString(searchText)}&language=ko";
        var response = await _apiClient.GetAsync<BackendLocationCandidateList>(path, cancellationToken);
        return response.Data?.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.PlaceId))
            .Select(item => new NearbyPlaceCandidateDto
            {
                GooglePlaceId = item.PlaceId!,
                Name = FirstNotEmpty(item.PlaceName, item.DisplayName),
                Vicinity = item.DisplayName ?? string.Empty,
                Latitude = item.Latitude,
                Longitude = item.Longitude,
                DistanceMeters = GeoMath.DistanceMeters(latitude, longitude, item.Latitude, item.Longitude),
            })
            .OrderBy(item => item.DistanceMeters)
            .Take(Math.Clamp(maxResults, 1, 20))
            .ToList() ?? [];
    }

    private static LocationResult ToLocationResult(BackendLocationCandidate value)
    {
        var displayName = FirstNotEmpty(value.PlaceName, value.DisplayName, value.City, value.Province);
        return new LocationResult
        {
            DisplayName = displayName,
            Country = value.Country ?? string.Empty,
            Province = FirstNotEmpty(value.District, value.Province),
            City = value.City ?? string.Empty,
            Address = string.IsNullOrWhiteSpace(value.DisplayName) ? displayName : value.DisplayName,
            Latitude = value.Latitude,
            Longitude = value.Longitude,
            PlaceId = value.PlaceId,
        };
    }

    private static string FirstNotEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private sealed class BackendLocationCandidate
    {
        public string? DisplayName { get; init; }
        public double Latitude { get; init; }
        public double Longitude { get; init; }
        public string? Country { get; init; }
        public string? Province { get; init; }
        public string? City { get; init; }
        public string? District { get; init; }
        public string? PlaceName { get; init; }
        public string? PlaceId { get; init; }
    }

    private sealed class BackendLocationCandidateList
    {
        public List<BackendLocationCandidate> Items { get; init; } = [];
    }

    private sealed class BackendPlacesAutocompleteItem
    {
        public string PlaceId { get; init; } = string.Empty;
        public string MainText { get; init; } = string.Empty;
        public string? SecondaryText { get; init; }
        public string DisplayName { get; init; } = string.Empty;
    }

    private sealed class BackendPlacesAutocompleteResponse
    {
        public List<BackendPlacesAutocompleteItem> Items { get; init; } = [];
    }
}
