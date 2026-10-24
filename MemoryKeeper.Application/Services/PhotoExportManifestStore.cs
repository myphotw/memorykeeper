using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MemoryKeeper.Application.Services;

internal sealed class PhotoExportManifestStore
{
    internal const string ManifestFileName = ".memorykeeper-export.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _root;
    private readonly string _rootPrefix;
    private readonly string _manifestPath;
    private readonly Dictionary<string, List<ManifestEntry>> _entries;
    private readonly Dictionary<string, string> _hashesByPath = new(StringComparer.OrdinalIgnoreCase);
    private bool _changed;

    private PhotoExportManifestStore(string root, IEnumerable<ManifestEntry> entries)
    {
        _root = Path.GetFullPath(root);
        _rootPrefix = _root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                      + Path.DirectorySeparatorChar;
        _manifestPath = Path.Combine(_root, ManifestFileName);
        _entries = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.FileId))
            .GroupBy(entry => entry.FileId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    public static async Task<PhotoExportManifestStore> OpenAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var fullRoot = Path.GetFullPath(root);
        var manifestPath = Path.Combine(fullRoot, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return new PhotoExportManifestStore(fullRoot, []);
        }

        try
        {
            await using var stream = new FileStream(
                manifestPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                useAsync: true);
            var manifest = await JsonSerializer.DeserializeAsync<ExportManifest>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
            return manifest is { Version: 1, Entries: not null }
                ? new PhotoExportManifestStore(fullRoot, manifest.Entries)
                : new PhotoExportManifestStore(fullRoot, []);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A damaged management file must never block export or affect existing photos.
            return new PhotoExportManifestStore(fullRoot, []);
        }
    }

    public async Task<IReadOnlySet<string>> FindExistingAsync(
        IEnumerable<string> fileIds,
        CancellationToken cancellationToken)
    {
        var requested = fileIds
            .Where(fileId => !string.IsNullOrWhiteSpace(fileId))
            .Select(fileId => fileId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fileId in requested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_entries.TryGetValue(fileId, out var entries))
            {
                continue;
            }

            ManifestEntry? valid = null;
            foreach (var entry in entries)
            {
                if (await IsValidAsync(entry, cancellationToken).ConfigureAwait(false))
                {
                    valid = entry;
                    break;
                }
            }

            if (valid is not null)
            {
                existing.Add(fileId);
                if (entries.Count != 1 || !ReferenceEquals(entries[0], valid))
                {
                    _entries[fileId] = [valid];
                    _changed = true;
                }
            }
            else
            {
                _entries.Remove(fileId);
                _changed = true;
            }
        }

        var legacyHashes = requested
            .Where(fileId => !existing.Contains(fileId) && ImportBackendIdentityProvider.IsSha256(fileId))
            .ToDictionary(fileId => fileId.ToLowerInvariant(), fileId => fileId, StringComparer.OrdinalIgnoreCase);
        if (legacyHashes.Count > 0)
        {
            await FindLegacyFilesAsync(legacyHashes, existing, cancellationToken).ConfigureAwait(false);
        }

        return existing;
    }

    public void Record(string fileId, string fullPath, string sha256, long fileSize)
    {
        if (string.IsNullOrWhiteSpace(fileId)
            || !ImportBackendIdentityProvider.IsSha256(sha256)
            || fileSize < 0
            || !TryGetSafeRelativePath(fullPath, out var relativePath))
        {
            return;
        }

        var entry = new ManifestEntry
        {
            FileId = fileId.Trim(),
            RelativePath = relativePath,
            Sha256 = sha256.ToLowerInvariant(),
            FileSize = fileSize,
        };
        _entries[entry.FileId] = [entry];
        _changed = true;
    }

    public async Task SaveIfChangedAsync(CancellationToken cancellationToken)
    {
        if (!_changed)
        {
            return;
        }

        var manifest = new ExportManifest
        {
            Entries = _entries
                .SelectMany(pair => pair.Value)
                .OrderBy(entry => entry.FileId, StringComparer.Ordinal)
                .ThenBy(entry => entry.RelativePath, StringComparer.Ordinal)
                .ToList(),
        };
        var temporaryPath = Path.Combine(
            _root,
            $"{ManifestFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    manifest,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _manifestPath, overwrite: true);
            _changed = false;
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    public static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<bool> IsValidAsync(ManifestEntry entry, CancellationToken cancellationToken)
    {
        if (!ImportBackendIdentityProvider.IsSha256(entry.Sha256)
            || entry.FileSize < 0
            || !TryResolveSafePath(entry.RelativePath, out var fullPath)
            || !File.Exists(fullPath)
            || HasReparsePointBelowRoot(fullPath))
        {
            return false;
        }

        try
        {
            if (new FileInfo(fullPath).Length != entry.FileSize)
            {
                return false;
            }

            var hash = await GetHashAsync(fullPath, cancellationToken).ConfigureAwait(false);
            return string.Equals(hash, entry.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task FindLegacyFilesAsync(
        IDictionary<string, string> hashesToFileIds,
        ISet<string> existing,
        CancellationToken cancellationToken)
    {
        var files = EnumerateSafeFiles()
            .Where(IsPotentialOriginal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        foreach (var path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (hashesToFileIds.Count == 0)
            {
                break;
            }

            try
            {
                var hash = await GetHashAsync(path, cancellationToken).ConfigureAwait(false);
                if (!hashesToFileIds.TryGetValue(hash, out var fileId))
                {
                    continue;
                }

                Record(fileId, path, hash, new FileInfo(path).Length);
                existing.Add(fileId);
                hashesToFileIds.Remove(hash);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Ignore unreadable files and continue discovering other verified originals.
            }
        }
    }

    private Task<string> GetHashAsync(string path, CancellationToken cancellationToken)
    {
        if (_hashesByPath.TryGetValue(path, out var hash))
        {
            return Task.FromResult(hash);
        }

        return ComputeAndCacheAsync(path, cancellationToken);
    }

    private async Task<string> ComputeAndCacheAsync(string path, CancellationToken cancellationToken)
    {
        var hash = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
        _hashesByPath[path] = hash;
        return hash;
    }

    private bool TryResolveSafePath(string relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            return false;
        }

        try
        {
            var platformPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            var candidate = Path.GetFullPath(Path.Combine(_root, platformPath));
            if (!candidate.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate, _manifestPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            fullPath = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryGetSafeRelativePath(string fullPath, out string relativePath)
    {
        relativePath = string.Empty;
        try
        {
            var normalized = Path.GetFullPath(fullPath);
            if (!normalized.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var candidate = Path.GetRelativePath(_root, normalized);
            if (Path.IsPathRooted(candidate)
                || candidate.Equals("..", StringComparison.Ordinal)
                || candidate.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return false;
            }

            relativePath = candidate.Replace(Path.DirectorySeparatorChar, '/');
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool IsPotentialOriginal(string path)
    {
        var fileName = Path.GetFileName(path);
        var extension = Path.GetExtension(path);
        return !fileName.StartsWith(ManifestFileName, StringComparison.OrdinalIgnoreCase)
               && !string.Equals(extension, ".xmp", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(extension, ".partial", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(extension, ".tmp", StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<string> EnumerateSafeFiles()
    {
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(_root);
        while (pending.TryPop(out var directory))
        {
            try
            {
                files.AddRange(Directory.EnumerateFiles(directory)
                    .Where(path => !HasReparsePoint(path)));
                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    if (!HasReparsePoint(child))
                    {
                        pending.Push(child);
                    }
                }
            }
            catch
            {
                // Ignore an unreadable branch and continue scanning the rest of the export root.
            }
        }

        return files;
    }

    private bool HasReparsePointBelowRoot(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.Equals(current, _root, StringComparison.OrdinalIgnoreCase))
        {
            if (HasReparsePoint(current))
            {
                return true;
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent)
                || (!string.Equals(parent, _root, StringComparison.OrdinalIgnoreCase)
                    && !parent.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            current = parent;
        }

        return false;
    }

    private static bool HasReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return true;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup of a management-file staging artifact.
        }
    }

    private sealed class ExportManifest
    {
        [JsonPropertyName("version")]
        public int Version { get; init; } = 1;

        [JsonPropertyName("entries")]
        public List<ManifestEntry> Entries { get; init; } = [];
    }

    private sealed class ManifestEntry
    {
        [JsonPropertyName("file_id")]
        public string FileId { get; init; } = string.Empty;

        [JsonPropertyName("relative_path")]
        public string RelativePath { get; init; } = string.Empty;

        [JsonPropertyName("sha256")]
        public string Sha256 { get; init; } = string.Empty;

        [JsonPropertyName("file_size")]
        public long FileSize { get; init; }
    }
}
