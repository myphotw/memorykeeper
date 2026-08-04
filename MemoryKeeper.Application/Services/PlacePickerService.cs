using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Entities;

namespace MemoryKeeper.Application.Services;

/// <summary>
/// Loads and filters registered places for the place registration dialog (MK-042T).
/// </summary>
public sealed class PlacePickerService
{
    private readonly IPlaceRepository _placeRepository;

    public PlacePickerService(IPlaceRepository placeRepository)
    {
        _placeRepository = placeRepository;
    }

    public async Task<PlacePickerData> LoadAsync(
        int recentTake = 5,
        CancellationToken cancellationToken = default)
    {
        var activePlaces = (await _placeRepository.GetActiveAsync(cancellationToken))
            .OrderBy(place => PlaceNormalizer.GetDisplayLabel(place), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var allItems = activePlaces.Select(MapItem).ToList();
        var itemById = allItems.ToDictionary(item => item.Id);

        var recent = activePlaces
            .Where(place => place.LastUsedAt.HasValue || place.UsageCount > 0)
            .OrderByDescending(place => place.LastUsedAt ?? place.UpdatedAt)
            .Take(Math.Max(0, recentTake))
            .Select(place => itemById[place.Id])
            .ToList();

        var favorites = activePlaces
            .Where(place => place.IsFavorite)
            .OrderBy(place => PlaceNormalizer.GetDisplayLabel(place), StringComparer.OrdinalIgnoreCase)
            .Select(place => itemById[place.Id])
            .ToList();

        return new PlacePickerData
        {
            RecentPlaces = recent,
            FavoritePlaces = favorites,
            Hierarchy = BuildHierarchy(activePlaces),
            AllPlaces = allItems
        };
    }

    public async Task<IReadOnlyList<PlacePickerItemDto>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var activePlaces = await _placeRepository.GetActiveAsync(cancellationToken);
        var allItems = activePlaces.Select(MapItem).ToList();
        return Search(activePlaces, allItems, query);
    }

    private static IReadOnlyList<PlacePickerItemDto> Search(
        IReadOnlyList<Place> activePlaces,
        IReadOnlyList<PlacePickerItemDto> places,
        string query)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0)
        {
            return places;
        }

        var matchedIds = activePlaces
            .Where(place => PlaceNormalizer.MatchesSearch(place, trimmed))
            .Select(place => place.Id)
            .ToHashSet();

        return places
            .Where(item => matchedIds.Contains(item.Id))
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<PlacePickerCountryNode> BuildHierarchy(IReadOnlyList<Place> places)
    {
        return places
            .GroupBy(place =>
            {
                var country = PlaceNormalizer.NormalizeCountry(place.Country);
                return string.IsNullOrWhiteSpace(country) ? "기타" : country;
            })
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(countryGroup => new PlacePickerCountryNode
            {
                Title = countryGroup.Key,
                Regions = countryGroup
                    .GroupBy(place => PlaceNormalizer.ResolveCityLabel(place, "기타"))
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(regionGroup => new PlacePickerRegionNode
                    {
                        Title = regionGroup.Key,
                        Places = regionGroup
                            .OrderBy(place => PlaceNormalizer.GetDisplayLabel(place), StringComparer.OrdinalIgnoreCase)
                            .Select(MapItem)
                            .ToList()
                    })
                    .ToList()
            })
            .ToList();
    }

    private static PlacePickerItemDto MapItem(Place place) => new()
    {
        Id = place.Id,
        DisplayName = PlaceNormalizer.GetDisplayLabel(place),
        Country = PlaceNormalizer.NormalizeCountry(place.Country),
        City = PlaceNormalizer.ResolveCityLabel(place, string.Empty),
        CanonicalName = place.CanonicalName,
        IsFavorite = place.IsFavorite
    };
}
