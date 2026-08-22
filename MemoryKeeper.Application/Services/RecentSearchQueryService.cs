using System.Text.Json;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

/// <summary>
/// Stores only the user's recent search text as a local UI preference.
/// Photo, place and tag content remain NAS-only.
/// </summary>
public sealed class RecentSearchQueryService
{
    public const int MaxRecentQueries = 10;

    private readonly ISettingRepository _settings;
    private readonly ILogger<RecentSearchQueryService> _logger;

    public RecentSearchQueryService(
        ISettingRepository settings,
        ILogger<RecentSearchQueryService> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<string>> GetAsync(
        CancellationToken cancellationToken = default) =>
        await LoadAsync(cancellationToken).ConfigureAwait(false);

    public async Task AddAsync(
        string? query,
        CancellationToken cancellationToken = default)
    {
        var normalized = query?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        var recent = await LoadAsync(cancellationToken).ConfigureAwait(false);
        recent.RemoveAll(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase));
        recent.Insert(0, normalized);
        await SaveAsync(recent.Take(MaxRecentQueries).ToList(), cancellationToken).ConfigureAwait(false);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        SaveAsync([], cancellationToken);

    private async Task<List<string>> LoadAsync(CancellationToken cancellationToken)
    {
        var setting = await _settings.GetByKeyAsync(
            SettingKeys.RecentSearchQueries, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(setting?.Value))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(setting.Value)
                       ?.Where(item => !string.IsNullOrWhiteSpace(item))
                       .Select(item => item.Trim())
                       .Distinct(StringComparer.OrdinalIgnoreCase)
                       .Take(MaxRecentQueries)
                       .ToList()
                   ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Recent search preference could not be parsed.");
            return [];
        }
    }

    private async Task SaveAsync(
        IReadOnlyList<string> queries,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(queries);
        var existing = await _settings.GetByKeyAsync(
            SettingKeys.RecentSearchQueries, cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        if (existing is null)
        {
            await _settings.AddAsync(new Setting
            {
                Id = Guid.NewGuid(),
                Key = SettingKeys.RecentSearchQueries,
                Value = payload,
                CreatedAt = now,
                UpdatedAt = now,
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        existing.Value = payload;
        existing.UpdatedAt = now;
        await _settings.UpdateAsync(existing, cancellationToken).ConfigureAwait(false);
    }
}
