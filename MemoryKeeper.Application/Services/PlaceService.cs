using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

public sealed class PlaceService
{
    private readonly IPlaceRepository _placeRepository;
    private readonly IMediaRepository _mediaRepository;
    private readonly ISettingRepository _settingRepository;
    private readonly ILocationResolver _locationResolver;
    private readonly IPlaceReclassificationService _placeReclassificationService;
    private readonly IMediaLibraryPathSyncService _pathSyncService;
    private readonly VisitRecordService _visitRecordService;
    private readonly ICatalogInvalidation _catalogInvalidation;
    private readonly ILogger<PlaceService> _logger;

    public PlaceService(
        IPlaceRepository placeRepository,
        IMediaRepository mediaRepository,
        ISettingRepository settingRepository,
        ILocationResolver locationResolver,
        IPlaceReclassificationService placeReclassificationService,
        IMediaLibraryPathSyncService pathSyncService,
        VisitRecordService visitRecordService,
        ICatalogInvalidation catalogInvalidation,
        ILogger<PlaceService> logger)
    {
        _placeRepository = placeRepository;
        _mediaRepository = mediaRepository;
        _settingRepository = settingRepository;
        _locationResolver = locationResolver;
        _placeReclassificationService = placeReclassificationService;
        _pathSyncService = pathSyncService;
        _visitRecordService = visitRecordService;
        _catalogInvalidation = catalogInvalidation;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PlaceDto>> GetPlaceListAsync(CancellationToken cancellationToken = default)
    {
        var places = await _placeRepository.GetAllAsync(cancellationToken);
        var result = new List<PlaceDto>(places.Count);

        foreach (var place in places
                     .OrderByDescending(item => item.IsFavorite)
                     .ThenByDescending(item => item.IsActive)
                     .ThenByDescending(item => item.LastUsedAt ?? item.UpdatedAt)
                     .ThenBy(item => item.DisplayName))
        {
            result.Add(await MapAsync(place, cancellationToken));
        }

        return result;
    }

    public async Task<IReadOnlyList<PlaceDto>> GetFavoritePlacesAsync(CancellationToken cancellationToken = default)
    {
        var places = await _placeRepository.GetAllAsync(cancellationToken);
        var favorites = places
            .Where(place => place.IsFavorite && place.IsActive)
            .OrderBy(place => place.DisplayName)
            .ToList();

        var result = new List<PlaceDto>(favorites.Count);
        foreach (var place in favorites)
        {
            result.Add(await MapAsync(place, cancellationToken));
        }

        return result;
    }

    public async Task<IReadOnlyList<PlaceDto>> GetRecentPlacesAsync(
        int take = 10,
        CancellationToken cancellationToken = default)
    {
        var places = await _placeRepository.GetAllAsync(cancellationToken);
        var recent = places
            .OrderByDescending(place => place.LastUsedAt ?? place.UpdatedAt)
            .ThenByDescending(place => place.UpdatedAt)
            .Take(Math.Max(1, take))
            .ToList();

        var result = new List<PlaceDto>(recent.Count);
        foreach (var place in recent)
        {
            result.Add(await MapAsync(place, cancellationToken));
        }

        return result;
    }

    /// <summary>
    /// Find existing place by GooglePlaceId, or create from Place Details (MK-042O).
    /// </summary>
    public async Task<PlaceDto> CreateOrGetFromGooglePlaceAsync(
        string googlePlaceId,
        string? fallbackDisplayName = null,
        string? fallbackPlaceType = null,
        double? seedLatitude = null,
        double? seedLongitude = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(googlePlaceId);
        var placeId = googlePlaceId.Trim();

        // Always resolve Place Details first so existing rows get correct geometry
        // (e.g. places previously saved with Seoul default map-pick coordinates).
        var location = await _locationResolver.ResolvePlaceIdAsync(placeId, cancellationToken)
            ?? throw new InvalidOperationException($"Google Place '{placeId}'를 조회할 수 없습니다.");

        if (!string.IsNullOrWhiteSpace(fallbackDisplayName) && string.IsNullOrWhiteSpace(location.DisplayName))
        {
            location = location with { DisplayName = fallbackDisplayName.Trim() };
        }

        var normalized = PlaceNormalizer.Normalize(location);
        var latitude = PreferCoordinate(location.Latitude, seedLatitude);
        var longitude = PreferCoordinate(location.Longitude, seedLongitude);

        var active = await _placeRepository.GetActiveAsync(cancellationToken);
        var existing = active.FirstOrDefault(place =>
            string.Equals(place.GooglePlaceId, placeId, StringComparison.Ordinal));
        existing ??= active.FirstOrDefault(place =>
            !string.IsNullOrWhiteSpace(place.CanonicalName)
            && PlaceNormalizer.CanonicalEquals(place.CanonicalName, normalized.CanonicalName));

        if (existing is not null)
        {
            await RefreshExistingGooglePlaceAsync(
                existing,
                placeId,
                normalized,
                location,
                latitude,
                longitude,
                fallbackPlaceType,
                cancellationToken);
            return await MapAsync(existing, cancellationToken);
        }

        return await CreatePlaceAsync(
            new CreatePlaceRequest
            {
                DisplayName = normalized.DisplayName,
                CanonicalName = normalized.CanonicalName,
                Country = normalized.Country,
                Province = normalized.Province,
                City = normalized.City,
                Address = string.IsNullOrWhiteSpace(location.Address) ? normalized.DisplayName : location.Address,
                PostalCode = location.PostalCode,
                GooglePlaceId = placeId,
                Category = location.PlaceType ?? fallbackPlaceType,
                Latitude = latitude,
                Longitude = longitude,
                IsActive = true
            },
            cancellationToken);
    }

    private async Task RefreshExistingGooglePlaceAsync(
        Place existing,
        string googlePlaceId,
        PlaceNormalizer.NormalizedLocation normalized,
        LocationResult location,
        double latitude,
        double longitude,
        string? fallbackPlaceType,
        CancellationToken cancellationToken)
    {
        var changed = false;
        if (string.IsNullOrWhiteSpace(existing.GooglePlaceId))
        {
            existing.GooglePlaceId = googlePlaceId;
            changed = true;
        }

        if (Math.Abs(existing.Latitude - latitude) > 0.00001
            || Math.Abs(existing.Longitude - longitude) > 0.00001)
        {
            existing.Latitude = latitude;
            existing.Longitude = longitude;
            changed = true;
        }

        if (!string.Equals(existing.Country, normalized.Country, StringComparison.Ordinal))
        {
            existing.Country = normalized.Country;
            changed = true;
        }

        if (!string.Equals(existing.Province, normalized.Province, StringComparison.Ordinal))
        {
            existing.Province = normalized.Province;
            changed = true;
        }

        if (!string.Equals(existing.City, normalized.City, StringComparison.Ordinal))
        {
            existing.City = normalized.City;
            changed = true;
        }

        var address = string.IsNullOrWhiteSpace(location.Address) ? normalized.DisplayName : location.Address;
        if (!string.Equals(existing.Address, address, StringComparison.Ordinal))
        {
            existing.Address = address;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(location.PostalCode)
            && !string.Equals(existing.PostalCode, location.PostalCode, StringComparison.Ordinal))
        {
            existing.PostalCode = location.PostalCode;
            changed = true;
        }

        var category = location.PlaceType ?? fallbackPlaceType;
        if (!string.IsNullOrWhiteSpace(category)
            && !string.Equals(existing.Category, category, StringComparison.Ordinal))
        {
            existing.Category = category;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        existing.UpdatedAt = DateTime.UtcNow;
        await _placeRepository.UpdateAsync(existing, cancellationToken);
        _catalogInvalidation.Invalidate();
        _logger.LogInformation(
            "Refreshed existing Google place from Place Details. PlaceId={PlaceId}, Lat={Lat}, Lng={Lng}",
            existing.Id,
            existing.Latitude,
            existing.Longitude);
    }

    /// <summary>
    /// Google Place Details coordinates win; seed is only used when details lack a usable value.
    /// </summary>
    private static double PreferCoordinate(double detailsValue, double? seedValue)
    {
        if (Math.Abs(detailsValue) > double.Epsilon)
        {
            return detailsValue;
        }

        return seedValue ?? detailsValue;
    }

    public async Task<PlaceDto> GetPlaceAsync(Guid placeId, CancellationToken cancellationToken = default)
    {
        var place = await _placeRepository.GetByIdAsync(placeId, cancellationToken)
            ?? throw new InvalidOperationException($"Place '{placeId}' was not found.");

        return await MapAsync(place, cancellationToken);
    }

    public async Task<PlaceDto> CreatePlaceAsync(
        CreatePlaceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateDisplayName(request.DisplayName);
        ValidateCoordinates(request.Latitude, request.Longitude);

        var now = DateTime.UtcNow;
        var place = new Place
        {
            Id = Guid.NewGuid(),
            DisplayName = request.DisplayName.Trim(),
            Country = request.Country.Trim(),
            Province = request.Province.Trim(),
            City = request.City.Trim(),
            Address = request.Address.Trim(),
            PostalCode = request.PostalCode.Trim(),
            GooglePlaceId = string.IsNullOrWhiteSpace(request.GooglePlaceId) ? null : request.GooglePlaceId.Trim(),
            CanonicalName = ResolveCanonicalName(request),
            Category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Radius = request.Radius ?? PlaceCategoryDefaults.GetRecommendedRadius(request.Category),
            IsActive = request.IsActive,
            IsFavorite = request.IsFavorite,
            UsageCount = 0,
            LastUsedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        if (place.Radius <= 0)
        {
            throw new ArgumentException("Radius must be greater than zero.", nameof(request));
        }

        await _placeRepository.AddAsync(place, cancellationToken);
        _catalogInvalidation.Invalidate();
        _logger.LogInformation("Place created. PlaceId={PlaceId}, DisplayName={DisplayName}", place.Id, place.DisplayName);

        if (request.ReclassifyMedia)
        {
            var reclassification = await _placeReclassificationService.ReclassifyAsync(
                place.Id,
                reassignFromOtherPlaces: request.ReassignFromOtherPlaces,
                cancellationToken);
            _logger.LogInformation(
                "Place create reclassification finished. PlaceId={PlaceId}, Assigned={Assigned}, FromOther={FromOther}",
                reclassification.PlaceId,
                reclassification.AssignedCount,
                reclassification.ReassignedFromOtherCount);
        }

        return await MapAsync(place, cancellationToken);
    }

    public async Task<PlaceDto> UpdatePlaceAsync(
        UpdatePlaceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateDisplayName(request.DisplayName);
        ValidateCoordinates(request.Latitude, request.Longitude);

        if (request.Radius <= 0)
        {
            throw new ArgumentException("Radius must be greater than zero.", nameof(request));
        }

        var place = await _placeRepository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Place '{request.Id}' was not found.");

        var geometryChanged =
            Math.Abs(place.Latitude - request.Latitude) > double.Epsilon
            || Math.Abs(place.Longitude - request.Longitude) > double.Epsilon
            || Math.Abs(place.Radius - request.Radius) > double.Epsilon;
        var displayNameChanged = !string.Equals(
            place.DisplayName,
            request.DisplayName.Trim(),
            StringComparison.Ordinal);

        // MK-042O: DisplayName may change; GooglePlaceId / CanonicalName stay immutable.
        place.DisplayName = request.DisplayName.Trim();
        place.Country = request.Country.Trim();
        place.Province = request.Province.Trim();
        place.City = request.City.Trim();
        place.Address = request.Address.Trim();
        place.PostalCode = request.PostalCode.Trim();
        place.Category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim();
        place.Latitude = request.Latitude;
        place.Longitude = request.Longitude;
        place.Radius = request.Radius;
        place.IsActive = request.IsActive;
        place.IsFavorite = request.IsFavorite;
        place.UpdatedAt = DateTime.UtcNow;
        place.LastUsedAt = DateTime.UtcNow;

        await _placeRepository.UpdateAsync(place, cancellationToken);
        _catalogInvalidation.Invalidate();
        _logger.LogInformation("Place updated. PlaceId={PlaceId}, DisplayName={DisplayName}", place.Id, place.DisplayName);

        if (displayNameChanged)
        {
            var moved = await _pathSyncService.SyncPlaceMediaAsync(place.Id, cancellationToken);
            _logger.LogInformation(
                "Place rename folder sync finished. PlaceId={PlaceId}, Moved={Moved}",
                place.Id,
                moved);
        }

        if (request.ReclassifyMedia && geometryChanged)
        {
            var reclassification = await _placeReclassificationService.ReclassifyAsync(
                place.Id,
                reassignFromOtherPlaces: request.ReassignFromOtherPlaces,
                cancellationToken);
            _logger.LogInformation(
                "Place reclassification finished. PlaceId={PlaceId}, Assigned={Assigned}, FromOther={FromOther}, Unassigned={Unassigned}",
                reclassification.PlaceId,
                reclassification.AssignedCount,
                reclassification.ReassignedFromOtherCount,
                reclassification.UnassignedCount);
        }

        return await MapAsync(place, cancellationToken);
    }

    public async Task<PlaceDto> SetPlaceFavoriteAsync(
        Guid placeId,
        bool isFavorite,
        CancellationToken cancellationToken = default)
    {
        var place = await _placeRepository.GetByIdAsync(placeId, cancellationToken)
            ?? throw new InvalidOperationException($"Place '{placeId}' was not found.");

        place.IsFavorite = isFavorite;
        place.UpdatedAt = DateTime.UtcNow;
        await _placeRepository.UpdateAsync(place, cancellationToken);
        return await MapAsync(place, cancellationToken);
    }

    public async Task<PlaceDto> SetPlaceActiveAsync(
        Guid placeId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var place = await _placeRepository.GetByIdAsync(placeId, cancellationToken)
            ?? throw new InvalidOperationException($"Place '{placeId}' was not found.");

        place.IsActive = isActive;
        place.UpdatedAt = DateTime.UtcNow;
        await _placeRepository.UpdateAsync(place, cancellationToken);

        return await MapAsync(place, cancellationToken);
    }

    public async Task TouchUsageAsync(Guid placeId, CancellationToken cancellationToken = default)
    {
        var place = await _placeRepository.GetByIdAsync(placeId, cancellationToken);
        if (place is null)
        {
            return;
        }

        place.UsageCount += 1;
        place.LastUsedAt = DateTime.UtcNow;
        place.UpdatedAt = DateTime.UtcNow;
        await _placeRepository.UpdateAsync(place, cancellationToken);
    }

    public async Task<(bool Succeeded, string Message, int MediaCount)> DeletePlaceAsync(
        Guid placeId,
        CancellationToken cancellationToken = default)
    {
        var place = await _placeRepository.GetByIdAsync(placeId, cancellationToken)
            ?? throw new InvalidOperationException($"Place '{placeId}' was not found.");

        var mediaItems = await _mediaRepository.GetByPlaceIdAsync(placeId, cancellationToken);
        foreach (var media in mediaItems)
        {
            media.PlaceId = null;
            media.Status = Domain.Enums.MediaStatus.Pending;
            media.UpdatedAt = DateTime.UtcNow;
            await _mediaRepository.UpdateAsync(media, cancellationToken);
            await _pathSyncService.SyncMediaPathAsync(media, place: null, cancellationToken);
        }

        await _placeRepository.DeleteAsync(place, cancellationToken);
        _logger.LogInformation(
            "Place deleted. PlaceId={PlaceId}, UnlinkedMedia={Count}",
            placeId,
            mediaItems.Count);

        return (true, $"장소 '{place.DisplayName}'을(를) 삭제했습니다. (연결 해제 사진 {mediaItems.Count}장)", mediaItems.Count);
    }

    public Task<PlaceReclassificationResult> ReclassifyMediaAsync(
        Guid placeId,
        bool reassignFromOtherPlaces = false,
        CancellationToken cancellationToken = default)
    {
        return _placeReclassificationService.ReclassifyAsync(placeId, reassignFromOtherPlaces, cancellationToken);
    }

    /// <summary>
    /// Finds active places whose radius circles intersect the candidate circle.
    /// </summary>
    public async Task<IReadOnlyList<PlaceOverlapItemDto>> FindOverlappingPlacesAsync(
        double latitude,
        double longitude,
        double radiusMeters,
        Guid? excludePlaceId = null,
        CancellationToken cancellationToken = default)
    {
        if (radiusMeters <= 0)
        {
            return [];
        }

        var active = await _placeRepository.GetActiveAsync(cancellationToken);
        var overlaps = new List<PlaceOverlapItemDto>();

        foreach (var place in active)
        {
            if (excludePlaceId is Guid exclude && place.Id == exclude)
            {
                continue;
            }

            if (place.Radius <= 0)
            {
                continue;
            }

            var distance = GeoMath.DistanceMeters(latitude, longitude, place.Latitude, place.Longitude);
            if (distance >= radiusMeters + place.Radius)
            {
                continue;
            }

            var media = await _mediaRepository.GetByPlaceIdAsync(place.Id, cancellationToken);
            overlaps.Add(new PlaceOverlapItemDto
            {
                PlaceId = place.Id,
                DisplayName = place.DisplayName,
                Latitude = place.Latitude,
                Longitude = place.Longitude,
                RadiusMeters = place.Radius,
                DistanceMeters = distance,
                MediaCount = media.Count
            });
        }

        return overlaps
            .OrderBy(item => item.DistanceMeters)
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Counts GPS media that would be claimed by a radius (preview only).
    /// </summary>
    public async Task<PlaceRadiusImpactDto> CountRadiusImpactAsync(
        double latitude,
        double longitude,
        double radiusMeters,
        Guid? targetPlaceId = null,
        CancellationToken cancellationToken = default)
    {
        if (radiusMeters <= 0)
        {
            return new PlaceRadiusImpactDto();
        }

        var withGps = await _mediaRepository.GetWithGpsAsync(cancellationToken);
        var unassigned = 0;
        var fromOther = 0;

        foreach (var media in withGps)
        {
            if (media.Latitude is not double lat || media.Longitude is not double lon)
            {
                continue;
            }

            if (GeoMath.DistanceMeters(latitude, longitude, lat, lon) > radiusMeters)
            {
                continue;
            }

            if (media.PlaceId is null)
            {
                unassigned++;
            }
            else if (targetPlaceId is null || media.PlaceId != targetPlaceId)
            {
                fromOther++;
            }
        }

        return new PlaceRadiusImpactDto
        {
            UnassignedCount = unassigned,
            FromOtherPlacesCount = fromOther
        };
    }

    /// <summary>
    /// Counts GPS media within the given radius (for radius UI feedback). Does not change assignment.
    /// </summary>
    public async Task<int> CountMediaInRadiusAsync(
        double latitude,
        double longitude,
        double radiusMeters,
        CancellationToken cancellationToken = default)
    {
        var impact = await CountRadiusImpactAsync(latitude, longitude, radiusMeters, null, cancellationToken);
        return impact.TotalInRadius;
    }

    private async Task<PlaceDto> MapAsync(Place place, CancellationToken cancellationToken)
    {
        var mediaItems = await _mediaRepository.GetByPlaceIdAsync(place.Id, cancellationToken);
        var visitRecordCount = _visitRecordService.CalculateVisitRecordCount(
            mediaItems.Select(media => (media.CapturedAt, media.ImportedAt)));
        var representative = mediaItems
            .OrderByDescending(media => media.IsFavorite)
            .ThenByDescending(media => media.CapturedAt)
            .ThenByDescending(media => media.ImportedAt)
            .FirstOrDefault();

        return new PlaceDto
        {
            Id = place.Id,
            DisplayName = place.DisplayName,
            Country = place.Country,
            Province = place.Province,
            City = place.City,
            Address = place.Address,
            PostalCode = place.PostalCode,
            GooglePlaceId = place.GooglePlaceId,
            CanonicalName = place.CanonicalName,
            Category = place.Category,
            Latitude = place.Latitude,
            Longitude = place.Longitude,
            Radius = place.Radius,
            IsActive = place.IsActive,
            IsFavorite = place.IsFavorite,
            UsageCount = place.UsageCount,
            LastUsedAt = place.LastUsedAt,
            MediaCount = mediaItems.Count,
            VisitRecordCount = visitRecordCount,
            FavoriteCount = mediaItems.Count(media => media.IsFavorite),
            RepresentativeMediaId = representative?.Id
        };
    }

    private static string? ResolveCanonicalName(CreatePlaceRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.CanonicalName))
        {
            return PlaceNormalizer.BuildCanonicalName(request.CanonicalName);
        }

        if (!string.IsNullOrWhiteSpace(request.GooglePlaceId)
            || !string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return PlaceNormalizer.BuildCanonicalName(request.DisplayName);
        }

        return null;
    }

    private static void ValidateDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display name is required.", nameof(displayName));
        }
    }

    private static void ValidateCoordinates(double latitude, double longitude)
    {
        if (latitude is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be between -90 and 90.");
        }

        if (longitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be between -180 and 180.");
        }
    }
}
