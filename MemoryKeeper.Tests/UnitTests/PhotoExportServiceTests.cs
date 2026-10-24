using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.DTOs.Gallery;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using GalleryPhotoDetailDto = MemoryKeeper.Application.DTOs.Gallery.PhotoDetailDto;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class PhotoExportServiceTests
{
    private const string ExportManifestFileName = ".memorykeeper-export.json";

    [Fact]
    public async Task Export_UsesHierarchyPreservesSourceWritesXmpAndResolvesCollisions()
    {
        var root = NewTempDirectory();
        try
        {
            var source = new FakeSource(
                Item("one", "IMG_001.jpg"),
                Item("two", "IMG_001.jpg"));
            var original = source.Bytes["one"].ToArray();
            var progress = new List<PhotoExportProgressDto>();
            var service = new PhotoExportService(source);

            var result = await service.ExportAsync(root, new InlineProgress(progress.Add));

            var directory = Path.Combine(root, "2026", "대한민국", "구례군", "피아골");
            Assert.True(File.Exists(Path.Combine(directory, "IMG_001.jpg")));
            Assert.True(File.Exists(Path.Combine(directory, "IMG_001_2.jpg")));
            var xmp = await File.ReadAllTextAsync(Path.Combine(directory, "IMG_001.xmp"));
            Assert.Contains("가족", xmp);
            Assert.Contains("추억", xmp);
            Assert.Contains("favorite=\"true\"", xmp);
            Assert.Equal(original, source.Bytes["one"]);
            Assert.Equal(2, result.ExportedCount);
            Assert.Equal(2, Assert.Single(progress.TakeLast(1)).Completed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Export_UsesUserCorrectedEffectiveDatetimeWithoutChangingTheOriginal()
    {
        var root = NewTempDirectory();
        try
        {
            var corrected = new DateTimeOffset(2026, 10, 8, 14, 18, 17, TimeSpan.FromHours(9));
            var staleCatalogDate = new DateOnly(2025, 4, 3);
            var source = new FakeSource(Item(
                "corrected",
                "selected-abc.jpg",
                new DateTimeOffset(2025, 4, 3, 8, 0, 0, TimeSpan.FromHours(9)),
                staleCatalogDate,
                "DATETIME"));
            var original = source.Bytes["corrected"].ToArray();
            source.Details["corrected"] = Detail("corrected", "selected-abc.jpg", ".jpg", corrected);

            var result = await new PhotoExportService(source).ExportAsync(root);

            var directory = ExportDirectory(root);
            const string name = "20261008_001.jpg";
            Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(directory, name)));
            Assert.Equal(original, source.Bytes["corrected"]);
            Assert.Equal(corrected, source.Details["corrected"].EffectiveCaptureDatetime);
            Assert.Equal(1, result.ExportedCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Export_UsesDetailDateWhenCatalogDateFieldsAreMissing()
    {
        var root = NewTempDirectory();
        try
        {
            var capture = new DateTimeOffset(2026, 2, 19, 11, 3, 30, TimeSpan.FromHours(9));
            var source = new FakeSource(Item("one", "20260219_110330.jpg"));
            source.Details["one"] = Detail("one", "20260219_110330.jpg", ".jpg", capture);

            var result = await new PhotoExportService(source).ExportAsync(root);

            Assert.Equal(source.Bytes["one"], await File.ReadAllBytesAsync(
                Path.Combine(ExportDirectory(root), "20260219_001.jpg")));
            Assert.Equal(["one"], source.DetailRequests);
            Assert.Equal(["one"], source.DownloadRequests);
            Assert.Equal(1, result.ExportedCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Export_UsesEffectiveDatetimeWhenNoUserCorrectionExists()
    {
        var root = NewTempDirectory();
        try
        {
            var capture = new DateTimeOffset(2026, 10, 8, 9, 30, 5, TimeSpan.FromHours(9));
            var source = new FakeSource(Item(
                "one",
                "selected-abc.jpg",
                capture,
                new DateOnly(2026, 10, 8),
                "DATETIME"));
            source.Details["one"] = new GalleryPhotoDetailDto
            {
                FileId = "one",
                Filename = "selected-abc.jpg",
                Extension = ".jpg",
                RawCaptureDatetime = capture,
                EffectiveCaptureDatetime = capture,
                EffectiveCapturePrecision = "DATETIME",
            };

            await new PhotoExportService(source).ExportAsync(root);

            Assert.True(File.Exists(Path.Combine(ExportDirectory(root), "20261008_001.jpg")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Export_DatetimeItemsUseCaptureTimeThenStableFileIdOrder()
    {
        var root = NewTempDirectory();
        try
        {
            var early = new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.FromHours(9));
            var late = new DateTimeOffset(2026, 10, 8, 14, 0, 0, TimeSpan.FromHours(9));
            var date = new DateOnly(2026, 10, 8);
            var source = new FakeSource(
                Item("z-early", "early.jpg", early, date, "DATETIME"),
                Item("b-same", "b.jpg", late, date, "DATETIME"),
                Item("a-same", "a.jpg", late, date, "DATETIME"));

            var result = await new PhotoExportService(source).ExportAsync(root);

            var directory = ExportDirectory(root);
            Assert.Equal(source.Bytes["z-early"], await File.ReadAllBytesAsync(Path.Combine(directory, "20261008_001.jpg")));
            Assert.Equal(source.Bytes["a-same"], await File.ReadAllBytesAsync(Path.Combine(directory, "20261008_002.jpg")));
            Assert.Equal(source.Bytes["b-same"], await File.ReadAllBytesAsync(Path.Combine(directory, "20261008_003.jpg")));
            Assert.Equal(3, result.ExportedCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("selected-a.jpg", ".jpg")]
    [InlineData("selected-a.png", ".png")]
    [InlineData("selected-a.heic", ".heic")]
    public async Task Export_DateNameRetainsOriginalFormatExtension(string storedName, string extension)
    {
        var root = NewTempDirectory();
        try
        {
            var capture = new DateTimeOffset(2026, 10, 8, 9, 30, 5, TimeSpan.FromHours(9));
            var source = new FakeSource(Item(
                "one",
                storedName,
                capture,
                new DateOnly(2026, 10, 8),
                "DATETIME",
                extension));
            source.Details["one"] = Detail("one", storedName, extension, capture);

            await new PhotoExportService(source).ExportAsync(root);

            var name = "20261008_001" + extension;
            Assert.True(File.Exists(Path.Combine(ExportDirectory(root), name)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Export_DatePrecisionFollowsDatetimeAndSharesSequenceAcrossExtensions()
    {
        var root = NewTempDirectory();
        try
        {
            var date = new DateOnly(2026, 10, 8);
            var source = new FakeSource(
                Item(
                    "date-only",
                    "date-only.heic",
                    null,
                    date,
                    "DATE",
                    ".heic"),
                Item(
                    "timed",
                    "timed.jpg",
                    new DateTimeOffset(2026, 10, 8, 18, 30, 0, TimeSpan.FromHours(9)),
                    date,
                    "DATETIME",
                    ".jpg"));

            await new PhotoExportService(source).ExportAsync(root);

            var directory = ExportDirectory(root);
            Assert.Equal(source.Bytes["timed"], await File.ReadAllBytesAsync(Path.Combine(directory, "20261008_001.jpg")));
            Assert.Equal(source.Bytes["date-only"], await File.ReadAllBytesAsync(Path.Combine(directory, "20261008_002.heic")));
            Assert.True(File.Exists(Path.Combine(directory, "20261008_001.xmp")));
            Assert.True(File.Exists(Path.Combine(directory, "20261008_002.xmp")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Export_DateSequenceSkipsExistingOriginalPartialAndXmpWithoutOverwriting()
    {
        var root = NewTempDirectory();
        try
        {
            var directory = ExportDirectory(root);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "20261008_001.jpg"), "existing original");
            await File.WriteAllTextAsync(Path.Combine(directory, "20261008_002.jpg.partial"), "existing partial");
            await File.WriteAllTextAsync(Path.Combine(directory, "20261008_003.xmp"), "existing xmp");
            var source = new FakeSource(Item(
                "one",
                "one.heic",
                new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.FromHours(9)),
                new DateOnly(2026, 10, 8),
                "DATETIME",
                ".heic"));

            await new PhotoExportService(source).ExportAsync(root);

            Assert.Equal("existing original", await File.ReadAllTextAsync(Path.Combine(directory, "20261008_001.jpg")));
            Assert.Equal("existing partial", await File.ReadAllTextAsync(Path.Combine(directory, "20261008_002.jpg.partial")));
            Assert.Equal("existing xmp", await File.ReadAllTextAsync(Path.Combine(directory, "20261008_003.xmp")));
            Assert.Equal(source.Bytes["one"], await File.ReadAllBytesAsync(Path.Combine(directory, "20261008_004.heic")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("YEAR")]
    [InlineData(null)]
    public async Task Export_YearOrMissingPrecisionKeepsStoredName(string? precision)
    {
        var root = NewTempDirectory();
        try
        {
            var source = new FakeSource(Item(
                "one",
                "selected-abc.jpg",
                new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.FromHours(9)),
                precision is null ? null : new DateOnly(2026, 10, 8),
                precision));
            source.Details["one"] = Detail("one", "selected-abc.jpg", ".jpg",
                new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.FromHours(9)), precision);

            await new PhotoExportService(source).ExportAsync(root);

            Assert.True(File.Exists(Path.Combine(ExportDirectory(root), "selected-abc.jpg")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Export_MissingEffectiveDatetimeKeepsStoredName()
    {
        var root = NewTempDirectory();
        try
        {
            var source = new FakeSource(Item("one", "selected-abc.jpg"));
            source.Details["one"] = Detail("one", "selected-abc.jpg", ".jpg", null);

            await new PhotoExportService(source).ExportAsync(root);

            Assert.True(File.Exists(Path.Combine(ExportDirectory(root), "selected-abc.jpg")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExportSelected_FiltersBeforeDetailDownloadAndDeduplicatesIds()
    {
        var root = NewTempDirectory();
        try
        {
            var source = new FakeSource(
                Item("one", "one.jpg"),
                Item("two", "two.jpg"),
                Item("three", "three.jpg"));

            var result = await new PhotoExportService(source).ExportSelectedAsync(
                root,
                ["two", "TWO"]);

            Assert.Equal(1, result.TotalCount);
            Assert.Equal(1, result.ExportedCount);
            Assert.Equal(["two"], source.DetailRequests);
            Assert.Equal(["two"], source.DownloadRequests);
            Assert.False(File.Exists(Path.Combine(ExportDirectory(root), "one.jpg")));
            Assert.True(File.Exists(Path.Combine(ExportDirectory(root), "two.jpg")));
            Assert.False(File.Exists(Path.Combine(ExportDirectory(root), "three.jpg")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExportSelected_UsesTheSameDetailBasedNamingAndReusesEachDetail()
    {
        var root = NewTempDirectory();
        try
        {
            var source = new FakeSource(
                Item("one", "one.jpg"),
                Item("two", "20260219_110348.jpg"));
            source.Details["two"] = Detail(
                "two",
                "20260219_110348.jpg",
                ".jpg",
                new DateTimeOffset(2026, 2, 19, 11, 3, 48, TimeSpan.FromHours(9)));

            var result = await new PhotoExportService(source).ExportSelectedAsync(root, ["two"]);
            var repeated = await new PhotoExportService(source).ExportSelectedAsync(root, ["two"]);

            Assert.True(File.Exists(Path.Combine(ExportDirectory(root), "20260219_001.jpg")));
            Assert.Equal(["two"], source.DetailRequests);
            Assert.Equal(["two"], source.DownloadRequests);
            Assert.Equal(1, result.ExportedCount);
            Assert.Equal(0, repeated.ExportedCount);
            Assert.Equal(1, repeated.DuplicateSkippedCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExportSelected_CountsMissingCatalogIdsAsFailuresAndCompletesProgress()
    {
        var root = NewTempDirectory();
        try
        {
            var source = new FakeSource(Item("one", "one.jpg"));
            var progress = new List<PhotoExportProgressDto>();

            var result = await new PhotoExportService(source).ExportSelectedAsync(
                root,
                ["one", "missing"],
                new InlineProgress(progress.Add));

            Assert.Equal(2, result.TotalCount);
            Assert.Equal(1, result.ExportedCount);
            Assert.Equal(1, result.CopyFailedCount);
            Assert.Equal(2, progress[^1].Completed);
            Assert.Equal(2, progress[^1].Total);
            Assert.Equal(1, progress[^1].Failed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Export_ContinuesAfterCopyAndMetadataFailuresAndReportsProgress()
    {
        var root = NewTempDirectory();
        try
        {
            var metadataFailureDirectory = Path.Combine(root, "2026", "대한민국", "구례군", "피아골", "META.xmp");
            Directory.CreateDirectory(metadataFailureDirectory);
            var source = new FakeSource(
                Item("ok", "OK.jpg"),
                Item("metadata", "META.jpg"),
                Item("copy", "COPY.jpg"));
            source.CopyFailures.Add("copy");
            var progress = new List<PhotoExportProgressDto>();

            var result = await new PhotoExportService(source)
                .ExportAsync(root, new InlineProgress(progress.Add));

            Assert.Equal(2, result.ExportedCount);
            Assert.Equal(1, result.MetadataPartialCount);
            Assert.Equal(1, result.CopyFailedCount);
            Assert.Equal(3, progress.Count);
            Assert.Equal(3, progress[^1].Completed);
            Assert.Equal(1, progress[^1].Failed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Export_ContinuesAfterDetailFailureAndDoesNotRequestTheDetailTwice()
    {
        var root = NewTempDirectory();
        try
        {
            var source = new FakeSource(
                Item("detail-failure", "FAIL.jpg"),
                Item("ok", "OK.jpg"));
            source.DetailFailures.Add("detail-failure");
            var progress = new List<PhotoExportProgressDto>();

            var result = await new PhotoExportService(source)
                .ExportAsync(root, new InlineProgress(progress.Add));

            Assert.Equal(1, result.ExportedCount);
            Assert.Equal(1, result.CopyFailedCount);
            Assert.Equal(2, progress[^1].Completed);
            Assert.Equal(2, progress[^1].Total);
            Assert.Equal(1, progress[^1].Failed);
            Assert.Equal(["detail-failure", "ok"], source.DetailRequests);
            Assert.Equal(["ok"], source.DownloadRequests);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Export_RepeatedToSameRootSkipsVerifiedDuplicateWithoutConsumingASequence()
    {
        var root = NewTempDirectory();
        try
        {
            var capture = new DateTimeOffset(2026, 2, 19, 11, 3, 30, TimeSpan.FromHours(9));
            var source = new FakeSource(Item("one", "one.jpg"));
            source.Details["one"] = Detail("one", "one.jpg", ".jpg", capture);
            var service = new PhotoExportService(source);

            var first = await service.ExportAsync(root);
            var second = await service.ExportAsync(root);

            Assert.Equal(1, first.ExportedCount);
            Assert.Equal(0, first.DuplicateSkippedCount);
            Assert.Equal(0, second.ExportedCount);
            Assert.Equal(1, second.DuplicateSkippedCount);
            Assert.Equal(0, second.CopyFailedCount);
            Assert.Equal(["one"], source.DetailRequests);
            Assert.Equal(["one"], source.DownloadRequests);
            Assert.True(File.Exists(Path.Combine(ExportDirectory(root), "20260219_001.jpg")));
            Assert.False(File.Exists(Path.Combine(ExportDirectory(root), "20260219_002.jpg")));
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(root, ExportManifestFileName)));
            var entry = Assert.Single(manifest.RootElement.GetProperty("entries").EnumerateArray());
            Assert.Equal("one", entry.GetProperty("file_id").GetString());
            Assert.Equal("2026/대한민국/구례군/피아골/20260219_001.jpg", entry.GetProperty("relative_path").GetString());
            Assert.Matches("^[0-9a-f]{64}$", entry.GetProperty("sha256").GetString()!);
            Assert.True(entry.GetProperty("file_size").GetInt64() > 0);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Export_SamePhotoToDifferentRootsExportsIndependently()
    {
        var firstRoot = NewTempDirectory();
        var secondRoot = NewTempDirectory();
        try
        {
            var source = new FakeSource(Item("one", "one.jpg"));
            var service = new PhotoExportService(source);

            var first = await service.ExportAsync(firstRoot);
            var second = await service.ExportAsync(secondRoot);

            Assert.Equal(1, first.ExportedCount);
            Assert.Equal(1, second.ExportedCount);
            Assert.Equal(0, second.DuplicateSkippedCount);
            Assert.Equal(["one", "one"], source.DetailRequests);
            Assert.Equal(["one", "one"], source.DownloadRequests);
        }
        finally
        {
            Directory.Delete(firstRoot, recursive: true);
            Directory.Delete(secondRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Export_DuplicateDoesNotConsumeTheNextDateSequence()
    {
        var root = NewTempDirectory();
        try
        {
            var date = new DateOnly(2026, 2, 19);
            var firstCapture = new DateTimeOffset(2026, 2, 19, 11, 3, 30, TimeSpan.FromHours(9));
            var secondCapture = new DateTimeOffset(2026, 2, 19, 11, 4, 0, TimeSpan.FromHours(9));
            var firstSource = new FakeSource(Item("one", "one.jpg", firstCapture, date, "DATETIME"));
            await new PhotoExportService(firstSource).ExportAsync(root);
            var secondSource = new FakeSource(
                Item("one", "one.jpg", firstCapture, date, "DATETIME"),
                Item("two", "two.jpg", secondCapture, date, "DATETIME"));

            var result = await new PhotoExportService(secondSource).ExportAsync(root);

            Assert.Equal(1, result.ExportedCount);
            Assert.Equal(1, result.DuplicateSkippedCount);
            Assert.Equal(["two"], secondSource.DetailRequests);
            Assert.True(File.Exists(Path.Combine(ExportDirectory(root), "20260219_002.jpg")));
            Assert.False(File.Exists(Path.Combine(ExportDirectory(root), "20260219_003.jpg")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Export_MissingRecordedOriginalIsExportedAgainWithoutChangingRemainingXmp()
    {
        var root = NewTempDirectory();
        try
        {
            var capture = new DateTimeOffset(2026, 2, 19, 11, 3, 30, TimeSpan.FromHours(9));
            var source = new FakeSource(Item("one", "one.jpg"));
            source.Details["one"] = Detail("one", "one.jpg", ".jpg", capture);
            var service = new PhotoExportService(source);
            await service.ExportAsync(root);
            var directory = ExportDirectory(root);
            var firstXmp = Path.Combine(directory, "20260219_001.xmp");
            var originalXmp = await File.ReadAllTextAsync(firstXmp);
            File.Delete(Path.Combine(directory, "20260219_001.jpg"));

            var result = await service.ExportAsync(root);

            Assert.Equal(1, result.ExportedCount);
            Assert.Equal(0, result.DuplicateSkippedCount);
            Assert.Equal(originalXmp, await File.ReadAllTextAsync(firstXmp));
            Assert.True(File.Exists(Path.Combine(directory, "20260219_002.jpg")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Export_ModifiedRecordedOriginalIsNotTreatedAsTheSamePhoto()
    {
        var root = NewTempDirectory();
        try
        {
            var source = new FakeSource(Item("one", "one.jpg"));
            var service = new PhotoExportService(source);
            await service.ExportAsync(root);
            var firstPath = Path.Combine(ExportDirectory(root), "one.jpg");
            var firstLength = new FileInfo(firstPath).Length;
            await File.WriteAllBytesAsync(firstPath, Enumerable.Repeat((byte)'x', (int)firstLength).ToArray());

            var result = await service.ExportAsync(root);

            Assert.Equal(1, result.ExportedCount);
            Assert.Equal(0, result.DuplicateSkippedCount);
            Assert.True(File.Exists(Path.Combine(ExportDirectory(root), "one_2.jpg")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Export_RejectsManifestPathOutsideRootAndDoesNotTouchExternalFile()
    {
        var root = NewTempDirectory();
        var outsideRoot = NewTempDirectory();
        try
        {
            var outsidePath = Path.Combine(outsideRoot, "outside.jpg");
            var outsideBytes = Encoding.UTF8.GetBytes("external original");
            await File.WriteAllBytesAsync(outsidePath, outsideBytes);
            var outsideHash = Convert.ToHexString(SHA256.HashData(outsideBytes)).ToLowerInvariant();
            await File.WriteAllTextAsync(
                Path.Combine(root, ExportManifestFileName),
                $$"""
                  {"version":1,"entries":[{"file_id":"one","relative_path":"../{{Path.GetFileName(outsideRoot)}}/outside.jpg","sha256":"{{outsideHash}}","file_size":{{outsideBytes.Length}}}]}
                  """);
            var source = new FakeSource(Item("one", "one.jpg"));

            var result = await new PhotoExportService(source).ExportAsync(root);

            Assert.Equal(1, result.ExportedCount);
            Assert.Equal(0, result.DuplicateSkippedCount);
            Assert.Equal(outsideBytes, await File.ReadAllBytesAsync(outsidePath));
            Assert.True(File.Exists(Path.Combine(ExportDirectory(root), "one.jpg")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outsideRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Export_DamagedManifestDoesNotOverwriteExistingPhotoAndIsRebuiltAfterSuccess()
    {
        var root = NewTempDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, ExportManifestFileName), "{ damaged json");
            var directory = ExportDirectory(root);
            Directory.CreateDirectory(directory);
            var existingPath = Path.Combine(directory, "same.jpg");
            await File.WriteAllTextAsync(existingPath, "existing photo");
            var source = new FakeSource(Item("one", "same.jpg"));

            var result = await new PhotoExportService(source).ExportAsync(root);

            Assert.Equal(1, result.ExportedCount);
            Assert.Equal("existing photo", await File.ReadAllTextAsync(existingPath));
            Assert.True(File.Exists(Path.Combine(directory, "same_2.jpg")));
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(root, ExportManifestFileName)));
            Assert.Equal(1, manifest.RootElement.GetProperty("version").GetInt32());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Export_BackfillsVerifiedLegacyFileWhenFileIdIsItsSha256()
    {
        var root = NewTempDirectory();
        try
        {
            var bytes = Encoding.UTF8.GetBytes("legacy exported original");
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var source = new FakeSource(Item(hash, "legacy.jpg"));
            source.Bytes[hash] = bytes;
            var legacyDirectory = Path.Combine(root, "old-export");
            Directory.CreateDirectory(legacyDirectory);
            await File.WriteAllBytesAsync(Path.Combine(legacyDirectory, "20260219_001.jpg"), bytes);

            var result = await new PhotoExportService(source).ExportAsync(root);

            Assert.Equal(0, result.ExportedCount);
            Assert.Equal(1, result.DuplicateSkippedCount);
            Assert.Empty(source.DetailRequests);
            Assert.Empty(source.DownloadRequests);
            var manifest = await File.ReadAllTextAsync(Path.Combine(root, ExportManifestFileName));
            Assert.Contains(hash, manifest, StringComparison.Ordinal);
            Assert.Contains("old-export/20260219_001.jpg", manifest, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Export_SameFilenameWithDifferentFileIdAndContentIsNotSkipped()
    {
        var root = NewTempDirectory();
        try
        {
            var firstSource = new FakeSource(Item("one", "same.jpg"));
            var secondSource = new FakeSource(Item("two", "same.jpg"));
            await new PhotoExportService(firstSource).ExportAsync(root);

            var result = await new PhotoExportService(secondSource).ExportAsync(root);

            Assert.Equal(1, result.ExportedCount);
            Assert.Equal(0, result.DuplicateSkippedCount);
            Assert.Equal(firstSource.Bytes["one"], await File.ReadAllBytesAsync(
                Path.Combine(ExportDirectory(root), "same.jpg")));
            Assert.Equal(secondSource.Bytes["two"], await File.ReadAllBytesAsync(
                Path.Combine(ExportDirectory(root), "same_2.jpg")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Export_DownloadFailureDoesNotCreateFalseManifestRecordAndCanRetry()
    {
        var root = NewTempDirectory();
        try
        {
            var source = new FakeSource(Item("one", "one.jpg"));
            source.CopyFailures.Add("one");
            var service = new PhotoExportService(source);

            var failed = await service.ExportAsync(root);
            source.CopyFailures.Remove("one");
            var retried = await service.ExportAsync(root);

            Assert.Equal(0, failed.ExportedCount);
            Assert.Equal(1, failed.CopyFailedCount);
            Assert.Equal(1, retried.ExportedCount);
            Assert.Equal(0, retried.DuplicateSkippedCount);
            Assert.Equal(["one", "one"], source.DetailRequests);
            Assert.Equal(["one", "one"], source.DownloadRequests);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static PhotoExportCatalogItemDto Item(
        string fileId,
        string filename,
        DateTimeOffset? effectiveCaptureDatetime = null,
        DateOnly? effectiveCaptureDate = null,
        string? effectiveCapturePrecision = null,
        string? extension = null) => new()
    {
        FileId = fileId,
        Filename = filename,
        Extension = extension ?? Path.GetExtension(filename),
        Year = "2026",
        Country = "대한민국",
        Region = "구례군",
        Place = "피아골",
        CaptureDatetime = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.FromHours(9)),
        EffectiveCaptureDatetime = effectiveCaptureDatetime,
        EffectiveCaptureDate = effectiveCaptureDate,
        EffectiveCaptureYear = effectiveCaptureDate?.Year,
        EffectiveCapturePrecision = effectiveCapturePrecision ?? string.Empty,
        Latitude = 35.2,
        Longitude = 127.5,
    };

    private static GalleryPhotoDetailDto Detail(string fileId, string filename, string extension,
        DateTimeOffset? effective, string? precision = "DATETIME") => new()
    {
        FileId = fileId,
        Filename = filename,
        Extension = extension,
        RawCaptureDatetime = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.FromHours(9)),
        UserCaptureDatetime = effective,
        EffectiveCaptureDatetime = effective,
        EffectiveCaptureDate = effective?.ToString("yyyy-MM-dd"),
        EffectiveCapturePrecision = precision,
    };

    private static string ExportDirectory(string root) =>
        Path.Combine(root, "2026", "대한민국", "구례군", "피아골");

    private static string NewTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "MemoryKeeper.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class InlineProgress(Action<PhotoExportProgressDto> report) : IProgress<PhotoExportProgressDto>
    {
        public void Report(PhotoExportProgressDto value) => report(value);
    }

    private sealed class FakeSource(params PhotoExportCatalogItemDto[] items) : IPhotoExportSource
    {
        public Dictionary<string, GalleryPhotoDetailDto> Details { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, byte[]> Bytes { get; } = items.ToDictionary(
            item => item.FileId,
            item => Encoding.UTF8.GetBytes($"original-{item.FileId}"));
        public HashSet<string> CopyFailures { get; } = new(StringComparer.Ordinal);
        public HashSet<string> DetailFailures { get; } = new(StringComparer.Ordinal);
        public List<string> DetailRequests { get; } = [];
        public List<string> DownloadRequests { get; } = [];

        public Task<IReadOnlyList<PhotoExportCatalogItemDto>> GetCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PhotoExportCatalogItemDto>>(items);

        public Task<PhotoExportSourceDetailDto> GetDetailAsync(string fileId, CancellationToken cancellationToken = default)
        {
            DetailRequests.Add(fileId);
            if (DetailFailures.Contains(fileId))
            {
                throw new IOException("simulated detail failure");
            }

            return Task.FromResult(new PhotoExportSourceDetailDto
            {
                OriginalUrl = $"/original/{fileId}",
                Detail = Details.TryGetValue(fileId, out var detail) ? detail : new GalleryPhotoDetailDto
                {
                    FileId = fileId,
                    Filename = items.Single(item => item.FileId == fileId).Filename,
                    Favorite = true,
                    Memo = "추억",
                    PlaceDisplayName = "피아골",
                    Tags = [new GalleryTagDto { Tag = "family", DisplayName = "가족", Identity = "ai:family" }],
                },
            });
        }

        public async Task DownloadOriginalAsync(string fileId, string originalUrl, Stream destination, CancellationToken cancellationToken = default)
        {
            DownloadRequests.Add(fileId);
            if (CopyFailures.Contains(fileId))
            {
                throw new IOException("simulated copy failure");
            }

            await destination.WriteAsync(Bytes[fileId], cancellationToken);
        }
    }
}
