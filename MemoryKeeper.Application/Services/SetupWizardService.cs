using MemoryKeeper.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

public sealed class SetupStatusDto
{
    public bool NeedsSetup { get; init; }

    public bool HasHomeLocation { get; init; }

    public bool IsSetupMarkedComplete { get; init; }
}

/// <summary>
/// First-run setup detection and completion marking.
/// </summary>
public sealed class SetupWizardService
{
    private readonly HomeLocationService _homeLocationService;
    private readonly ISettingRepository _settingRepository;
    private readonly ILogger<SetupWizardService> _logger;

    public SetupWizardService(
        HomeLocationService homeLocationService,
        ISettingRepository settingRepository,
        ILogger<SetupWizardService> logger)
    {
        _homeLocationService = homeLocationService;
        _settingRepository = settingRepository;
        _logger = logger;
    }

    public async Task<SetupStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var home = await _homeLocationService.GetAsync(cancellationToken);
        var completed = await _settingRepository.GetByKeyAsync(SettingKeys.SetupCompleted, cancellationToken);
        var isComplete = string.Equals(completed?.Value, "true", StringComparison.OrdinalIgnoreCase);

        return new SetupStatusDto
        {
            NeedsSetup = !isComplete || !home.IsConfigured,
            HasHomeLocation = home.IsConfigured,
            IsSetupMarkedComplete = isComplete
        };
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
