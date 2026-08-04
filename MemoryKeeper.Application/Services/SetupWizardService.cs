using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

public sealed class SetupStatusDto
{
    public bool NeedsSetup { get; init; }

    public bool HasStorage { get; init; }

    public bool HasReachablePhotoRoot { get; init; }

    public bool HasHomeLocation { get; init; }

    public bool HasGoogleMapsApiKey { get; init; }

    public bool IsSetupMarkedComplete { get; init; }
}

/// <summary>
/// First-run setup detection and completion marking.
/// </summary>
public sealed class SetupWizardService
{
    private readonly StorageService _storageService;
    private readonly HomeLocationService _homeLocationService;
    private readonly ISettingRepository _settingRepository;
    private readonly IFileAccessService _fileAccessService;
    private readonly ILogger<SetupWizardService> _logger;

    public SetupWizardService(
        StorageService storageService,
        HomeLocationService homeLocationService,
        ISettingRepository settingRepository,
        IFileAccessService fileAccessService,
        ILogger<SetupWizardService> logger)
    {
        _storageService = storageService;
        _homeLocationService = homeLocationService;
        _settingRepository = settingRepository;
        _fileAccessService = fileAccessService;
        _logger = logger;
    }

    public async Task<SetupStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var storages = await _storageService.GetStorageListAsync(cancellationToken);
        var active = storages.FirstOrDefault(item => item.IsActive) ?? storages.FirstOrDefault();
        var hasStorage = storages.Count > 0;
        var hasReachable = active is not null && _fileAccessService.PhotoRootExists(active.PhotoRoot);
        var home = await _homeLocationService.GetAsync(cancellationToken);
        var apiKey = await _settingRepository.GetByKeyAsync(SettingKeys.GoogleMapsApiKey, cancellationToken);
        var completed = await _settingRepository.GetByKeyAsync(SettingKeys.SetupCompleted, cancellationToken);
        var isComplete = string.Equals(completed?.Value, "true", StringComparison.OrdinalIgnoreCase);

        var needsSetup = !isComplete || !hasStorage || !hasReachable;

        return new SetupStatusDto
        {
            NeedsSetup = needsSetup,
            HasStorage = hasStorage,
            HasReachablePhotoRoot = hasReachable,
            HasHomeLocation = home.IsConfigured,
            HasGoogleMapsApiKey = !string.IsNullOrWhiteSpace(apiKey?.Value),
            IsSetupMarkedComplete = isComplete
        };
    }

    public async Task<StorageDto> CreateInitialStorageAsync(
        string name,
        string photoRoot,
        StorageType storageType = StorageType.Local,
        CancellationToken cancellationToken = default)
    {
        return await _storageService.CreateStorageAsync(new CreateStorageRequest
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Photo Library" : name.Trim(),
            PhotoRoot = photoRoot,
            StorageType = storageType,
            SetAsActive = true
        }, cancellationToken);
    }

    public Task<HomeLocationDto> SaveHomeByAddressAsync(
        string address,
        CancellationToken cancellationToken = default) =>
        _homeLocationService.SaveAddressAsync(address, cancellationToken);

    public Task<HomeLocationDto> SaveHomeByCoordinatesAsync(
        double latitude,
        double longitude,
        string? address = null,
        CancellationToken cancellationToken = default) =>
        _homeLocationService.SaveCoordinatesAsync(latitude, longitude, address, cancellationToken: cancellationToken);

    public async Task SaveGoogleMapsApiKeyAsync(
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        var value = apiKey?.Trim() ?? string.Empty;
        GoogleMapsApiKeyValidator.EnsureValidOrEmpty(value);
        await UpsertSettingAsync(SettingKeys.GoogleMapsApiKey, value, cancellationToken);
        _logger.LogInformation("Google Maps API key {Action}.", string.IsNullOrWhiteSpace(value) ? "cleared" : "saved");
    }

    public async Task MarkSetupCompletedAsync(CancellationToken cancellationToken = default)
    {
        await UpsertSettingAsync(SettingKeys.SetupCompleted, "true", cancellationToken);
        _logger.LogInformation("First-run setup marked complete.");
    }

    private async Task UpsertSettingAsync(string key, string value, CancellationToken cancellationToken)
    {
        var existing = await _settingRepository.GetByKeyAsync(key, cancellationToken);
        var now = DateTime.UtcNow;
        if (existing is null)
        {
            await _settingRepository.AddAsync(new Domain.Entities.Setting
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
