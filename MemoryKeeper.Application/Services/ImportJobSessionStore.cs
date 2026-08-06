using System.Text.Json;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

/// <summary>
/// Stores open Import job_ids under LocalApplicationData\MemoryKeeper\import-jobs-session.json.
/// </summary>
public sealed class ImportJobSessionStore : IImportJobSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ILogger<ImportJobSessionStore> _logger;
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ImportJobSessionStore(ILogger<ImportJobSessionStore> logger)
        : this(logger, ResolveDefaultPath())
    {
    }

    public ImportJobSessionStore(ILogger<ImportJobSessionStore> logger, string filePath)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public static string ResolveDefaultPath()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MemoryKeeper");
        Directory.CreateDirectory(root);
        return Path.Combine(root, "import-jobs-session.json");
    }

    public async Task SaveAsync(
        IReadOnlyList<ImportSessionJobDto> jobs,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var doc = new ImportSessionDocument
            {
                UpdatedAt = DateTimeOffset.UtcNow,
                Jobs = jobs.ToList(),
            };
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(doc, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ImportSessionJobDto>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            var doc = JsonSerializer.Deserialize<ImportSessionDocument>(json, JsonOptions);
            return doc?.Jobs ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load Import job session from {Path}", _filePath);
            return [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
