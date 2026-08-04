using System.Globalization;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

public sealed class HomeLocationDto
{
    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public string Address { get; init; } = string.Empty;

    public string PlaceId { get; init; } = string.Empty;

    public bool IsConfigured => Latitude is not null && Longitude is not null;
}

public sealed class HomeLocationService
{
    private readonly ISettingRepository _settingRepository;
    private readonly ILocationResolver _locationResolver;
    private readonly ILogger<HomeLocationService> _logger;

    public HomeLocationService(
        ISettingRepository settingRepository,
        ILocationResolver locationResolver,
        ILogger<HomeLocationService> logger)
    {
        _settingRepository = settingRepository;
        _locationResolver = locationResolver;
        _logger = logger;
    }

    public async Task<HomeLocationDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var lat = await ReadDoubleAsync(SettingKeys.TravelHomeLatitude, cancellationToken);
        var lon = await ReadDoubleAsync(SettingKeys.TravelHomeLongitude, cancellationToken);
        var address = await _settingRepository.GetByKeyAsync(SettingKeys.TravelHomeAddress, cancellationToken);
        var placeId = await _settingRepository.GetByKeyAsync(SettingKeys.TravelHomePlaceId, cancellationToken);

        return new HomeLocationDto
        {
            Latitude = lat,
            Longitude = lon,
            Address = address?.Value ?? string.Empty,
            PlaceId = placeId?.Value ?? string.Empty
        };
    }

    public async Task<HomeLocationDto> SaveCoordinatesAsync(
        double latitude,
        double longitude,
        string? address = null,
        string? placeId = null,
        CancellationToken cancellationToken = default)
    {
        await UpsertAsync(SettingKeys.TravelHomeLatitude, latitude.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await UpsertAsync(SettingKeys.TravelHomeLongitude, longitude.ToString(CultureInfo.InvariantCulture), cancellationToken);
        if (!string.IsNullOrWhiteSpace(address))
        {
            await UpsertAsync(SettingKeys.TravelHomeAddress, address.Trim(), cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(placeId))
        {
            await UpsertAsync(SettingKeys.TravelHomePlaceId, placeId.Trim(), cancellationToken);
        }

        _logger.LogInformation(
            "Home location saved. Latitude={Latitude}, Longitude={Longitude}, PlaceId={PlaceId}",
            latitude,
            longitude,
            placeId);

        return await GetAsync(cancellationToken);
    }

    public async Task<HomeLocationDto> SaveAddressAsync(
        string address,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);

        var resolved = await _locationResolver.ResolveAddressAsync(address.Trim(), cancellationToken);
        if (resolved is null)
        {
            throw new InvalidOperationException("주소를 좌표로 변환하지 못했습니다. API Key와 주소를 확인하세요.");
        }

        return await SaveCoordinatesAsync(
            resolved.Latitude,
            resolved.Longitude,
            string.IsNullOrWhiteSpace(resolved.Address) ? address.Trim() : resolved.Address,
            resolved.PlaceId,
            cancellationToken);
    }

    public async Task<HomeLocationDto> SavePlaceSelectionAsync(
        string placeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(placeId);

        var resolved = await _locationResolver.ResolvePlaceIdAsync(placeId.Trim(), cancellationToken);
        if (resolved is null)
        {
            throw new InvalidOperationException("선택한 장소를 확인하지 못했습니다. API Key와 Places API 설정을 확인하세요.");
        }

        return await SaveCoordinatesAsync(
            resolved.Latitude,
            resolved.Longitude,
            resolved.Address,
            resolved.PlaceId ?? placeId.Trim(),
            cancellationToken);
    }

    public Task<IReadOnlyList<PlaceSuggestionDto>> SuggestPlacesAsync(
        string input,
        CancellationToken cancellationToken = default)
        => _locationResolver.SuggestPlacesAsync(input, cancellationToken);

    private async Task<double?> ReadDoubleAsync(string key, CancellationToken cancellationToken)
    {
        var setting = await _settingRepository.GetByKeyAsync(key, cancellationToken);
        if (setting is null || string.IsNullOrWhiteSpace(setting.Value))
        {
            return null;
        }

        return double.TryParse(setting.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private async Task UpsertAsync(string key, string value, CancellationToken cancellationToken)
    {
        var existing = await _settingRepository.GetByKeyAsync(key, cancellationToken);
        var now = DateTime.UtcNow;
        if (existing is null)
        {
            await _settingRepository.AddAsync(new Setting
            {
                Id = Guid.NewGuid(),
                Key = key,
                Value = value,
                CreatedAt = now,
                UpdatedAt = now
            }, cancellationToken);
            return;
        }

        existing.Value = value;
        existing.UpdatedAt = now;
        await _settingRepository.UpdateAsync(existing, cancellationToken);
    }
}
