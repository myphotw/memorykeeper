using MemoryKeeper.Application.Diagnostics;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

public sealed class PlaceAssignmentService
{
    public const double DefaultRadiusMeters = 200d;
    private const int MaxResolveAttempts = 3;

    private readonly ILocationResolver _locationResolver;
    private readonly IPlaceRepository _placeRepository;
    private readonly ISettingRepository _settingRepository;
    private readonly ILogger<PlaceAssignmentService> _logger;

    public PlaceAssignmentService(
        ILocationResolver locationResolver,
        IPlaceRepository placeRepository,
        ISettingRepository settingRepository,
        ILogger<PlaceAssignmentService> logger)
    {
        _locationResolver = locationResolver;
        _placeRepository = placeRepository;
        _settingRepository = settingRepository;
        _logger = logger;
    }

    /// <summary>
    /// Resolves or creates a place for GPS. With valid GPS this never returns null (MK-042Q).
    /// </summary>
    public async Task<Place> AssignAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[STEP1] GPS Latitude={Latitude}, Longitude={Longitude}",
            latitude,
            longitude);
        ImportPipelineLog.Write($"GPS Latitude={latitude} Longitude={longitude}");

        var location = await ResolveWithRetryAsync(latitude, longitude, cancellationToken);
        var usedFallback = location is null;
        if (location is null)
        {
            ImportPipelineLog.Write("Google Place 조회 실패 → Fallback Place");
            _logger.LogWarning(
                "[STEP2] Google Place 조회 실패 후 Fallback 생성 Latitude={Latitude}, Longitude={Longitude}",
                latitude,
                longitude);
            location = new LocationResult
            {
                DisplayName = $"GPS {latitude:F5},{longitude:F5}",
                Latitude = latitude,
                Longitude = longitude
            };
        }

        ImportPipelineLog.Write($"Google PlaceID={location.PlaceId ?? "(none)"}");
        ImportPipelineLog.Write($"Google PlaceName={location.DisplayName}");

        var normalized = PlaceNormalizer.Normalize(location);
        ImportPipelineLog.Write($"Canonical Name={normalized.CanonicalName}");
        ImportPipelineLog.Write(
            $"Normalized Country={normalized.Country} Region={normalized.Province} Place={normalized.City}");

        _logger.LogInformation(
            "[STEP2] Google Place Name={Name}, Type={Type}, GooglePlaceId={GooglePlaceId}, Canonical={Canonical}",
            location.DisplayName,
            location.PlaceType ?? "(none)",
            location.PlaceId ?? "(none)",
            normalized.CanonicalName);

        var activePlaces = await _placeRepository.GetActiveAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(location.PlaceId))
        {
            var byGoogleId = activePlaces.FirstOrDefault(place =>
                string.Equals(place.GooglePlaceId, location.PlaceId, StringComparison.Ordinal));
            if (byGoogleId is not null)
            {
                EnsureCanonical(byGoogleId, normalized.CanonicalName);
                await _placeRepository.UpdateAsync(byGoogleId, cancellationToken);
                ImportPipelineLog.Write($"Place Search GooglePlaceId 일치 → {byGoogleId.DisplayName}");
                ImportPipelineLog.Write($"TB_MEDIA.PlaceID candidate={byGoogleId.Id}");
                ImportPipelineLog.Write("Place 생성 성공 여부=재사용");
                return byGoogleId;
            }
        }

        var byCanonical = activePlaces.FirstOrDefault(place =>
            !string.IsNullOrWhiteSpace(place.CanonicalName)
            && PlaceNormalizer.CanonicalEquals(place.CanonicalName, normalized.CanonicalName));
        if (byCanonical is null)
        {
            // Legacy rows without CanonicalName: match DisplayName through normalizer.
            byCanonical = activePlaces.FirstOrDefault(place =>
                PlaceNormalizer.CanonicalEquals(place.DisplayName, normalized.CanonicalName)
                || PlaceNormalizer.CanonicalEquals(place.City, normalized.CanonicalName));
        }

        if (byCanonical is not null)
        {
            EnsureCanonical(byCanonical, normalized.CanonicalName);
            if (string.IsNullOrWhiteSpace(byCanonical.GooglePlaceId)
                && !string.IsNullOrWhiteSpace(location.PlaceId))
            {
                byCanonical.GooglePlaceId = location.PlaceId.Trim();
            }

            if (string.IsNullOrWhiteSpace(byCanonical.Country) && !string.IsNullOrWhiteSpace(normalized.Country))
            {
                byCanonical.Country = normalized.Country;
            }

            byCanonical.UpdatedAt = DateTime.UtcNow;
            await _placeRepository.UpdateAsync(byCanonical, cancellationToken);

            _logger.LogInformation(
                "[STEP4] TB_PLACE 검색 Canonical 일치 PlaceId={PlaceId}, Canonical={Canonical}",
                byCanonical.Id,
                byCanonical.CanonicalName);
            ImportPipelineLog.Write($"Place Search Canonical 일치 → {byCanonical.CanonicalName}");
            ImportPipelineLog.Write($"TB_MEDIA.PlaceID candidate={byCanonical.Id}");
            ImportPipelineLog.Write("Place 생성 성공 여부=재사용");
            return byCanonical;
        }

        var matchedPlace = activePlaces
            .Select(place => new
            {
                Place = place,
                Distance = GeoMath.DistanceMeters(latitude, longitude, place.Latitude, place.Longitude)
            })
            .Where(item => item.Distance <= item.Place.Radius)
            .OrderBy(item => item.Distance)
            .Select(item => item.Place)
            .FirstOrDefault();

        if (matchedPlace is not null)
        {
            EnsureCanonical(matchedPlace, normalized.CanonicalName);
            await _placeRepository.UpdateAsync(matchedPlace, cancellationToken);
            ImportPipelineLog.Write($"Place Search 좌표+반경 일치 → {matchedPlace.DisplayName}");
            ImportPipelineLog.Write($"TB_MEDIA.PlaceID candidate={matchedPlace.Id}");
            ImportPipelineLog.Write("Place 생성 성공 여부=재사용");
            return matchedPlace;
        }

        ImportPipelineLog.Write("Place Search 결과=없음");

        var now = DateTime.UtcNow;
        var radius = usedFallback
            ? await GetDefaultRadiusAsync(cancellationToken)
            : location.PlaceType is not null
                ? PlaceTypeCatalog.GetRecommendedRadiusMeters(location.PlaceType)
                : await GetDefaultRadiusAsync(cancellationToken);

        var place = new Place
        {
            Id = Guid.NewGuid(),
            DisplayName = normalized.DisplayName,
            Country = normalized.Country,
            Province = normalized.Province,
            City = normalized.City,
            Address = string.IsNullOrWhiteSpace(location.Address)
                ? normalized.DisplayName
                : location.Address.Trim(),
            PostalCode = location.PostalCode.Trim(),
            GooglePlaceId = string.IsNullOrWhiteSpace(location.PlaceId) ? null : location.PlaceId.Trim(),
            CanonicalName = normalized.CanonicalName,
            Category = string.IsNullOrWhiteSpace(location.PlaceType) ? null : location.PlaceType.Trim(),
            Latitude = latitude,
            Longitude = longitude,
            Radius = radius,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _placeRepository.AddAsync(place, cancellationToken);
        _logger.LogInformation(
            "[STEP5] TB_PLACE 생성 성공 PlaceId={PlaceId}, DisplayName={DisplayName}, Canonical={Canonical}, GooglePlaceId={GooglePlaceId}",
            place.Id,
            place.DisplayName,
            place.CanonicalName,
            place.GooglePlaceId ?? "(none)");
        ImportPipelineLog.Write(
            $"Place 생성 성공 PlaceId={place.Id} Canonical={place.CanonicalName} GooglePlaceId={place.GooglePlaceId ?? "(none)"} Name={place.DisplayName}");
        ImportPipelineLog.Write($"TB_MEDIA.PlaceID candidate={place.Id}");
        ImportPipelineLog.Write("Place 생성 성공 여부=생성");

        return place;
    }

    private async Task<LocationResult?> ResolveWithRetryAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= MaxResolveAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var location = await _locationResolver.ResolveAsync(latitude, longitude, cancellationToken);
                if (location is not null)
                {
                    if (attempt > 1)
                    {
                        ImportPipelineLog.Write($"Google Place 재시도 성공 Attempt={attempt}");
                    }

                    return location;
                }

                ImportPipelineLog.Write($"Google Place 조회 실패 Attempt={attempt}/{MaxResolveAttempts}");
            }
            catch (Exception ex)
            {
                lastError = ex;
                ImportPipelineLog.Write($"Google Place 예외 Attempt={attempt} Message={ex.Message}");
                _logger.LogWarning(
                    ex,
                    "Google Place resolve failed. Attempt={Attempt}, Latitude={Latitude}, Longitude={Longitude}",
                    attempt,
                    latitude,
                    longitude);
            }

            if (attempt < MaxResolveAttempts)
            {
                await Task.Delay(200 * attempt, cancellationToken);
            }
        }

        if (lastError is not null)
        {
            _logger.LogError(lastError, "Google Place resolve exhausted retries.");
        }

        return null;
    }

    private static void EnsureCanonical(Place place, string canonical)
    {
        if (string.IsNullOrWhiteSpace(place.CanonicalName))
        {
            place.CanonicalName = canonical;
        }

        place.UpdatedAt = DateTime.UtcNow;
    }

    private async Task<double> GetDefaultRadiusAsync(CancellationToken cancellationToken)
    {
        var setting = await _settingRepository.GetByKeyAsync(SettingKeys.PlaceDefaultRadiusMeters, cancellationToken);
        if (setting is not null
            && double.TryParse(setting.Value, out var radius)
            && radius > 0)
        {
            return radius;
        }

        return DefaultRadiusMeters;
    }
}
