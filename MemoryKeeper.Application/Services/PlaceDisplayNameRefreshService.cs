using MemoryKeeper.Application.Diagnostics;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

/// <summary>
/// Refreshes non-Korean place labels via local aliases/transliteration, then Google Place Details.
/// </summary>
public sealed class PlaceDisplayNameRefreshService : IPlaceDisplayNameRefreshService
{
    private readonly ILocationResolver _locationResolver;
    private readonly IPlaceRepository _placeRepository;
    private readonly ILogger<PlaceDisplayNameRefreshService> _logger;

    public PlaceDisplayNameRefreshService(
        ILocationResolver locationResolver,
        IPlaceRepository placeRepository,
        ILogger<PlaceDisplayNameRefreshService> logger)
    {
        _locationResolver = locationResolver;
        _placeRepository = placeRepository;
        _logger = logger;
    }

    public async Task<int> RefreshKoreanNamesAsync(
        IEnumerable<Place> places,
        CancellationToken cancellationToken = default)
    {
        var updated = 0;
        foreach (var place in places)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryApplyLocalKoreanLabel(place))
            {
                place.UpdatedAt = DateTime.UtcNow;
                await _placeRepository.UpdateAsync(place, cancellationToken);
                updated++;
                ImportPipelineLog.Write($"한국어 장소명 로컬 갱신 PlaceId={place.Id} Name={place.DisplayName}");
                continue;
            }

            if (!PlaceNormalizer.NeedsKoreanLabelRefresh(place))
            {
                continue;
            }

            try
            {
                var location = await _locationResolver.ResolvePlaceIdAsync(place.GooglePlaceId!, cancellationToken);
                if (location is null)
                {
                    continue;
                }

                var normalized = PlaceNormalizer.Normalize(location);
                var koreanLabel = PlaceNormalizer.GetDisplayLabel(new Place
                {
                    DisplayName = normalized.DisplayName,
                    CanonicalName = normalized.CanonicalName,
                    Country = normalized.Country,
                    Province = normalized.Province,
                    City = normalized.City
                });

                if (!koreanLabel.Any(ch => ch is >= '\uAC00' and <= '\uD7A3'))
                {
                    continue;
                }

                place.DisplayName = koreanLabel;
                place.CanonicalName = normalized.CanonicalName;
                if (!string.IsNullOrWhiteSpace(normalized.Country))
                {
                    place.Country = normalized.Country;
                }

                if (!string.IsNullOrWhiteSpace(normalized.Province))
                {
                    place.Province = normalized.Province;
                }

                if (!string.IsNullOrWhiteSpace(normalized.City))
                {
                    place.City = normalized.City;
                }

                place.UpdatedAt = DateTime.UtcNow;
                await _placeRepository.UpdateAsync(place, cancellationToken);
                updated++;
                ImportPipelineLog.Write($"한국어 장소명 갱신 PlaceId={place.Id} Name={place.DisplayName}");
                _logger.LogInformation(
                    "Refreshed Korean place label. PlaceId={PlaceId}, DisplayName={DisplayName}",
                    place.Id,
                    place.DisplayName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Korean place label refresh failed. PlaceId={PlaceId}, GooglePlaceId={GooglePlaceId}",
                    place.Id,
                    place.GooglePlaceId);
            }
        }

        return updated;
    }

    private static bool TryApplyLocalKoreanLabel(Place place)
    {
        var displayHasHangul = !string.IsNullOrWhiteSpace(place.DisplayName)
            && place.DisplayName.Any(ch => ch is >= '\uAC00' and <= '\uD7A3');
        if (displayHasHangul)
        {
            return false;
        }

        var label = PlaceNormalizer.GetDisplayLabel(place);
        if (string.IsNullOrWhiteSpace(label)
            || !label.Any(ch => ch is >= '\uAC00' and <= '\uD7A3')
            || string.Equals(label, place.DisplayName, StringComparison.Ordinal))
        {
            return false;
        }

        place.DisplayName = label;
        place.CanonicalName = PlaceNormalizer.BuildCanonicalName(label);
        place.Country = PlaceNormalizer.NormalizeCountry(place.Country);
        place.Province = PlaceNormalizer.NormalizeRegion(place.Province);
        place.City = PlaceNormalizer.NormalizePlace(place.City);
        return true;
    }
}
