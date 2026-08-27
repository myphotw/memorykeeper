using System.Collections.Concurrent;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class IncrementalImportPreflightTests
{
    [Fact]
    public async Task OneThousandCompletedFiles_ProducesZeroUploads()
    {
        var files = Files(1000);
        var identities = Identities(files);
        var backend = Snapshot(existing: identities.Values);

        var result = await Create(files, identities, backend).InspectAsync("source");

        Assert.Equal(1000, result.ExistingCount);
        Assert.Equal(0, result.NewCount);
        Assert.Empty(result.UploadTargets);
    }

    [Fact]
    public async Task ExistingOneThousandPlusNewOneHundred_UploadsOnlyNewFiles()
    {
        var files = Files(1100);
        var identities = Identities(files);
        var backend = Snapshot(existing: identities.Values.Take(1000));

        var result = await Create(files, identities, backend).InspectAsync("source");

        Assert.Equal(1000, result.ExistingCount);
        Assert.Equal(100, result.NewCount);
        Assert.Equal(100, result.UploadTargets.Count);
    }

    [Theory]
    [InlineData("WAITING")]
    [InlineData("PROCESSING")]
    public async Task AcceptedBackendJob_IsNeverUploadedAgain(string backendState)
    {
        var files = Files(1);
        var identities = Identities(files);
        var backend = Snapshot(accepted: identities.Values);

        var result = await Create(files, identities, backend).InspectAsync("source");

        Assert.Equal(1, result.InProgressCount);
        Assert.Empty(result.UploadTargets);
        Assert.True(backendState is "WAITING" or "PROCESSING");
    }

    [Fact]
    public async Task CompletedBackendIdentity_IsNeverUploadedAgain()
    {
        var files = Files(1);
        var identities = Identities(files);
        var result = await Create(files, identities, Snapshot(existing: identities.Values)).InspectAsync("source");

        Assert.Equal(IncrementalImportClassification.Existing, Assert.Single(result.Items).Classification);
        Assert.Empty(result.UploadTargets);
    }

    [Fact]
    public async Task BackendMissing_WithCompleteSnapshot_IsNew()
    {
        var files = Files(1);
        var result = await Create(files, Identities(files), Snapshot()).InspectAsync("source");

        Assert.Equal(IncrementalImportClassification.New, Assert.Single(result.Items).Classification);
    }

    [Fact]
    public async Task TransientBackendFailure_IsUncertainAndBlocked()
    {
        var files = Files(1);
        var result = await Create(files, Identities(files), Snapshot(complete: false)).InspectAsync("source");

        Assert.Equal(1, result.UncertainCount);
        Assert.Empty(result.UploadTargets);
    }

    [Fact]
    public async Task UnlinkedLegacyActiveJobs_KeepUnmatchedFilesUncertain()
    {
        var files = Files(2);
        var backend = new ImportBackendIdentitySnapshot
        {
            IsComplete = false,
            UnidentifiedAcceptedJobCount = 3,
            Warning = "파일과 연결되지 않은 기존 NAS 작업이 있습니다.",
        };

        var result = await Create(files, Identities(files), backend).InspectAsync("source");

        Assert.Equal(2, result.UncertainCount);
        Assert.Empty(result.UploadTargets);
    }

    [Theory]
    [InlineData("source/renamed.jpg")]
    [InlineData("source/trip/moved.jpg")]
    public async Task RenameOrMove_WithSameHash_IsExisting(string changedPath)
    {
        var hash = Hash(1);
        var files = new[] { changedPath };
        var identities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [changedPath] = hash };

        var result = await Create(files, identities, Snapshot(existing: [hash])).InspectAsync("source");

        Assert.Equal(1, result.ExistingCount);
        Assert.Empty(result.UploadTargets);
    }

    [Fact]
    public async Task SamePathChangedContent_IsNewContent()
    {
        var path = "source/photo.jpg";
        var identities = new Dictionary<string, string> { [path] = Hash(2) };
        var result = await Create([path], identities, Snapshot(existing: [Hash(1)])).InspectAsync("source");

        Assert.Equal(1, result.NewCount);
        Assert.Equal(Hash(2), Assert.Single(result.UploadTargets).ContentHash);
    }

    [Fact]
    public async Task DuplicateContentInsideSource_IsUploadedOnce()
    {
        var files = new[] { "source/a.jpg", "source/copy.jpg" };
        var hash = Hash(10);
        var identities = files.ToDictionary(path => path, _ => hash);

        var result = await Create(files, identities, Snapshot()).InspectAsync("source");

        Assert.Equal(1, result.NewCount);
        Assert.Equal(1, result.DuplicateCount);
        Assert.Single(result.UploadTargets);
    }

    [Fact]
    public async Task Recovery8370_Excludes112CompletedAnd8009Accepted()
    {
        var files = Files(8370);
        var identities = Identities(files);
        var hashes = identities.Values.ToList();
        var backend = Snapshot(
            existing: hashes.Take(112),
            accepted: hashes.Skip(112).Take(8009));

        var result = await Create(files, identities, backend).InspectAsync("source");

        Assert.Equal(8370, result.TotalCount);
        Assert.Equal(112, result.ExistingCount);
        Assert.Equal(8009, result.InProgressCount);
        Assert.Equal(249, result.NewCount);
        Assert.Equal(249, result.UploadTargets.Count);
    }

    [Fact]
    public async Task ResumeSessionPath_IsIncludedInIncrementalDecision()
    {
        var files = new[] { "source/resumed.jpg", "source/new.jpg" };
        var identities = Identities(files);
        var resumedPath = Path.GetFullPath(files[0]);
        var backend = new ImportBackendIdentitySnapshot
        {
            SessionJobIdsByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [resumedPath] = Guid.NewGuid().ToString("D"),
            },
            IsComplete = true,
        };

        var result = await Create(files, identities, backend).InspectAsync("source");

        Assert.Equal(1, result.InProgressCount);
        Assert.Equal(1, result.NewCount);
    }

    [Fact]
    public async Task IdentityCache_ReusesStableFile_AndRehashesChangedFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mk-incremental-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "photo.jpg");
        var indexPath = Path.Combine(root, "index.json");
        await File.WriteAllTextAsync(path, "first-content");
        var hasher = new CountingHasher();
        var store = new ImportFileIdentityStore(
            hasher,
            NullLogger<ImportFileIdentityStore>.Instance,
            indexPath);

        try
        {
            var first = await store.ResolveAsync([path]);
            var second = await store.ResolveAsync([path]);
            await File.WriteAllTextAsync(path, "changed-content-with-new-size");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(2));
            var changed = await store.ResolveAsync([path]);

            Assert.Equal(2, hasher.CallCount);
            Assert.False(first[0].FromCache);
            Assert.True(second[0].FromCache);
            Assert.NotEqual(first[0].ContentHash, changed[0].ContentHash);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ZeroNewUx_ExplainsExistingOrProcessingPhotos()
    {
        var source = LoadSource("MemoryKeeper.App", "ViewModels", "ImportViewModel.cs");
        Assert.Contains("새로 등록할 사진이 없습니다.", source, StringComparison.Ordinal);
        Assert.Contains("이미 등록되었거나 NAS에서 처리 중입니다.", source, StringComparison.Ordinal);
    }

    private static IncrementalImportPreflightService Create(
        IReadOnlyList<string> files,
        IReadOnlyDictionary<string, string> identities,
        ImportBackendIdentitySnapshot backend) =>
        new(
            new FakeScanner(files),
            new FakeIdentityStore(identities),
            new FakeBackendProvider(backend));

    private static string[] Files(int count) =>
        Enumerable.Range(0, count).Select(index => $"source/IMG_{index:00000}.jpg").ToArray();

    private static Dictionary<string, string> Identities(IEnumerable<string> files) =>
        files.Select((path, index) => (path, hash: Hash(index + 1)))
            .ToDictionary(item => item.path, item => item.hash, StringComparer.OrdinalIgnoreCase);

    private static string Hash(int value) => value.ToString("x64");

    private static ImportBackendIdentitySnapshot Snapshot(
        IEnumerable<string>? existing = null,
        IEnumerable<string>? accepted = null,
        bool complete = true) =>
        new()
        {
            ExistingContentHashes = new HashSet<string>(existing ?? [], StringComparer.OrdinalIgnoreCase),
            AcceptedContentHashes = new HashSet<string>(accepted ?? [], StringComparer.OrdinalIgnoreCase),
            IsComplete = complete,
        };

    private sealed class FakeScanner(IReadOnlyList<string> files) : IFileScanner
    {
        public Task<IReadOnlyList<string>> ScanAsync(string folderPath, CancellationToken cancellationToken = default) => Task.FromResult(files);
        public MediaType? ResolveMediaType(string filePath) => MediaType.Photo;
    }

    private sealed class FakeIdentityStore(IReadOnlyDictionary<string, string> identities) : IImportFileIdentityStore
    {
        public Task<IReadOnlyList<ImportFileIdentityDto>> ResolveAsync(
            IReadOnlyList<string> filePaths,
            IProgress<ImportPreflightProgressDto>? progress = null,
            bool forceRecheck = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ImportFileIdentityDto>>(filePaths.Select(path => new ImportFileIdentityDto
            {
                FilePath = path,
                ContentHash = identities[path],
            }).ToList());
    }

    private sealed class FakeBackendProvider(ImportBackendIdentitySnapshot snapshot) : IImportBackendIdentityProvider
    {
        public Task<ImportBackendIdentitySnapshot> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
    }

    private sealed class CountingHasher : IFileHasher
    {
        public int CallCount { get; private set; }

        public async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken = default)
        {
            CallCount++;
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        }
    }

    private static string LoadSource(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MemoryKeeper.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory!.FullName }.Concat(segments).ToArray()));
    }
}
