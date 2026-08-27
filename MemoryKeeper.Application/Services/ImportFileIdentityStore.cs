using System.Collections.Concurrent;
using System.Text.Json;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Application.Services;

/// <summary>Local SHA-256 hint cache. Source files are read only and never modified.</summary>
public sealed class ImportFileIdentityStore : IImportFileIdentityStore
{
    public static readonly TimeSpan FullRecheckInterval = TimeSpan.FromDays(7);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly IFileHasher _fileHasher;
    private readonly ILogger<ImportFileIdentityStore> _logger;
    private readonly string _filePath;
    private readonly string _backupPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ImportFileIdentityStore(IFileHasher fileHasher, ILogger<ImportFileIdentityStore> logger)
        : this(fileHasher, logger, ResolveDefaultPath())
    {
    }

    public ImportFileIdentityStore(IFileHasher fileHasher, ILogger<ImportFileIdentityStore> logger, string filePath)
    {
        _fileHasher = fileHasher;
        _logger = logger;
        _filePath = filePath;
        _backupPath = $"{filePath}.bak";
    }

    public static string ResolveDefaultPath()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MemoryKeeper");
        Directory.CreateDirectory(root);
        return Path.Combine(root, "incremental-import-index.json");
    }

    public async Task<IReadOnlyList<ImportFileIdentityDto>> ResolveAsync(
        IReadOnlyList<string> filePaths,
        IProgress<ImportPreflightProgressDto>? progress = null,
        bool forceRecheck = false,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cache = await LoadCacheAsync(cancellationToken).ConfigureAwait(false);
            var results = new ConcurrentDictionary<string, ImportFileIdentityDto>(StringComparer.OrdinalIgnoreCase);
            var processed = 0;
            await Parallel.ForEachAsync(
                filePaths,
                new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = cancellationToken },
                async (path, token) =>
                {
                    var canonicalPath = Path.GetFullPath(path);
                    ImportFileIdentityDto identity;
                    try
                    {
                        var info = new FileInfo(canonicalPath);
                        if (!info.Exists)
                        {
                            throw new FileNotFoundException("사진 파일을 찾을 수 없습니다.", canonicalPath);
                        }

                        var now = DateTimeOffset.UtcNow;
                        if (!forceRecheck
                            && cache.TryGetValue(canonicalPath, out var cached)
                            && cached.FileSize == info.Length
                            && cached.LastWriteUtcTicks == info.LastWriteTimeUtc.Ticks
                            && now - cached.VerifiedAt <= FullRecheckInterval)
                        {
                            identity = new ImportFileIdentityDto
                            {
                                FilePath = canonicalPath,
                                FileSize = info.Length,
                                LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks,
                                ContentHash = cached.ContentHash,
                                FromCache = true,
                            };
                        }
                        else
                        {
                            var hash = await _fileHasher.ComputeSha256Async(canonicalPath, token).ConfigureAwait(false);
                            identity = new ImportFileIdentityDto
                            {
                                FilePath = canonicalPath,
                                FileSize = info.Length,
                                LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks,
                                ContentHash = hash,
                            };
                            cache[canonicalPath] = new ImportFileIdentityCacheEntry
                            {
                                FilePath = canonicalPath,
                                FileSize = info.Length,
                                LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks,
                                ContentHash = hash,
                                VerifiedAt = now,
                            };
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "Incremental import hash failed. File={File}", Path.GetFileName(canonicalPath));
                        identity = new ImportFileIdentityDto
                        {
                            FilePath = canonicalPath,
                            ErrorMessage = "파일 내용을 확인하지 못했습니다.",
                        };
                    }

                    results[canonicalPath] = identity;
                    var current = Interlocked.Increment(ref processed);
                    progress?.Report(new ImportPreflightProgressDto
                    {
                        TotalCount = filePaths.Count,
                        ProcessedCount = current,
                        Stage = "사진 내용 확인 중...",
                        CurrentFileName = Path.GetFileName(canonicalPath),
                    });
                }).ConfigureAwait(false);

            await SaveCacheAsync(cache, cancellationToken).ConfigureAwait(false);
            return filePaths.Select(path => results[Path.GetFullPath(path)]).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, ImportFileIdentityCacheEntry>> LoadCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new Dictionary<string, ImportFileIdentityCacheEntry>(StringComparer.OrdinalIgnoreCase);
            }

            var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
            var document = JsonSerializer.Deserialize<ImportFileIdentityCacheDocument>(json, JsonOptions);
            return (document?.Entries ?? [])
                .GroupBy(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Incremental import index could not be loaded; hashes will be verified again.");
            try
            {
                if (File.Exists(_backupPath))
                {
                    var backupJson = await File.ReadAllTextAsync(_backupPath, cancellationToken).ConfigureAwait(false);
                    var backup = JsonSerializer.Deserialize<ImportFileIdentityCacheDocument>(backupJson, JsonOptions);
                    return (backup?.Entries ?? [])
                        .GroupBy(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception backupException) when (backupException is not OperationCanceledException)
            {
                _logger.LogWarning(backupException, "Incremental import index backup could not be loaded.");
            }

            return new Dictionary<string, ImportFileIdentityCacheEntry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task SaveCacheAsync(
        IReadOnlyDictionary<string, ImportFileIdentityCacheEntry> cache,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var document = new ImportFileIdentityCacheDocument { Entries = cache.Values.ToList() };
        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(document, JsonOptions),
                cancellationToken).ConfigureAwait(false);
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

    private sealed class ImportFileIdentityCacheDocument
    {
        public List<ImportFileIdentityCacheEntry> Entries { get; init; } = [];
    }

    private sealed class ImportFileIdentityCacheEntry
    {
        public string FilePath { get; init; } = string.Empty;
        public long FileSize { get; init; }
        public long LastWriteUtcTicks { get; init; }
        public string ContentHash { get; init; } = string.Empty;
        public DateTimeOffset VerifiedAt { get; init; }
    }
}
