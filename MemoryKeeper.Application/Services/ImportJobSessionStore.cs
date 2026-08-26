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
    private readonly string _backupPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ImportJobSessionStore(ILogger<ImportJobSessionStore> logger)
        : this(logger, ResolveDefaultPath())
    {
    }

    public ImportJobSessionStore(ILogger<ImportJobSessionStore> logger, string filePath)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _backupPath = $"{filePath}.bak";
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
            await WriteDocumentAsync(jobs, cancellationToken).ConfigureAwait(false);
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

            return await LoadDocumentAsync(_filePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load Import job session from {Path}", _filePath);
            try
            {
                return File.Exists(_backupPath)
                    ? await LoadDocumentAsync(_backupPath, cancellationToken).ConfigureAwait(false)
                    : [];
            }
            catch (Exception backupException)
            {
                _logger.LogWarning(backupException, "Failed to load Import job session backup from {Path}", _backupPath);
                return [];
            }
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

            if (File.Exists(_backupPath))
            {
                File.Delete(_backupPath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateAsync(
        IReadOnlyList<ImportSessionJobDto> openJobs,
        IReadOnlyCollection<string> managedJobIds,
        CancellationToken cancellationToken = default)
    {
        if (managedJobIds.Count == 0)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IReadOnlyList<ImportSessionJobDto> existing;
            try
            {
                existing = File.Exists(_filePath)
                    ? await LoadDocumentAsync(_filePath, cancellationToken).ConfigureAwait(false)
                    : [];
            }
            catch when (File.Exists(_backupPath))
            {
                existing = await LoadDocumentAsync(_backupPath, cancellationToken).ConfigureAwait(false);
            }
            var managed = managedJobIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var merged = existing
                .Where(job => !managed.Contains(job.JobId))
                .Concat(openJobs)
                .GroupBy(job => job.JobId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToList();

            if (merged.Count == 0)
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }

                if (File.Exists(_backupPath))
                {
                    File.Delete(_backupPath);
                }
            }
            else
            {
                await WriteDocumentAsync(merged, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<IReadOnlyList<ImportSessionJobDto>> LoadDocumentAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        var doc = JsonSerializer.Deserialize<ImportSessionDocument>(json, JsonOptions);
        return doc?.Jobs ?? [];
    }

    private async Task WriteDocumentAsync(
        IReadOnlyList<ImportSessionJobDto> jobs,
        CancellationToken cancellationToken)
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
        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
            if (File.Exists(_filePath))
            {
                if (File.Exists(_backupPath))
                {
                    File.Delete(_backupPath);
                }

                File.Replace(temporaryPath, _filePath, _backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, _filePath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
