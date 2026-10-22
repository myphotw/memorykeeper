using System.Globalization;
using System.Text;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.DTOs.Gallery;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using GalleryPhotoDetailDto = MemoryKeeper.Application.DTOs.Gallery.PhotoDetailDto;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class PhotoExportServiceTests
{
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
            var source = new FakeSource(Item("corrected", "selected-abc.jpg"));
            var original = source.Bytes["corrected"].ToArray();
            var corrected = new DateTimeOffset(2026, 10, 8, 14, 18, 17, TimeSpan.FromHours(9));
            source.Details["corrected"] = Detail("corrected", "selected-abc.jpg", ".jpg", corrected);

            var result = await new PhotoExportService(source).ExportAsync(root);

            var directory = ExportDirectory(root);
            var name = corrected.ToLocalTime().ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".jpg";
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
    public async Task Export_UsesEffectiveDatetimeWhenNoUserCorrectionExists()
    {
        var root = NewTempDirectory();
        try
        {
            var capture = new DateTimeOffset(2026, 10, 8, 9, 30, 5, TimeSpan.FromHours(9));
            var source = new FakeSource(Item("one", "selected-abc.jpg"));
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

            var name = capture.ToLocalTime().ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".jpg";
            Assert.True(File.Exists(Path.Combine(ExportDirectory(root), name)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Export_SameSecondUsesStableFileIdOrderAndNeverOverwritesExistingNames()
    {
        var root = NewTempDirectory();
        try
        {
            var capture = new DateTimeOffset(2026, 10, 8, 14, 18, 17, TimeSpan.FromHours(9));
            var source = new FakeSource(Item("b", "b.jpg"), Item("a", "a.jpg"));
            source.Details["a"] = Detail("a", "a.jpg", ".jpg", capture);
            source.Details["b"] = Detail("b", "b.jpg", ".jpg", capture);
            var directory = ExportDirectory(root);
            Directory.CreateDirectory(directory);
            var stem = capture.ToLocalTime().ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var existing = Path.Combine(directory, stem + ".jpg");
            await File.WriteAllTextAsync(existing, "existing file");

            var result = await new PhotoExportService(source).ExportAsync(root);

            Assert.Equal("existing file", await File.ReadAllTextAsync(existing));
            Assert.Equal(source.Bytes["a"], await File.ReadAllBytesAsync(Path.Combine(directory, stem + "_001.jpg")));
            Assert.Equal(source.Bytes["b"], await File.ReadAllBytesAsync(Path.Combine(directory, stem + "_002.jpg")));
            Assert.Equal(2, result.ExportedCount);
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
            var source = new FakeSource(Item("one", storedName));
            source.Details["one"] = Detail("one", storedName, extension, capture);

            await new PhotoExportService(source).ExportAsync(root);

            var name = capture.ToLocalTime().ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + extension;
            Assert.True(File.Exists(Path.Combine(ExportDirectory(root), name)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("DATE")]
    [InlineData("YEAR")]
    [InlineData(null)]
    public async Task Export_UnknownTimePrecisionKeepsStoredName(string? precision)
    {
        var root = NewTempDirectory();
        try
        {
            var source = new FakeSource(Item("one", "selected-abc.jpg"));
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

    private static PhotoExportCatalogItemDto Item(string fileId, string filename) => new()
    {
        FileId = fileId,
        Filename = filename,
        Year = "2026",
        Country = "대한민국",
        Region = "구례군",
        Place = "피아골",
        CaptureDatetime = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.FromHours(9)),
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

        public Task<IReadOnlyList<PhotoExportCatalogItemDto>> GetCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PhotoExportCatalogItemDto>>(items);

        public Task<PhotoExportSourceDetailDto> GetDetailAsync(string fileId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PhotoExportSourceDetailDto
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

        public async Task DownloadOriginalAsync(string fileId, string originalUrl, Stream destination, CancellationToken cancellationToken = default)
        {
            if (CopyFailures.Contains(fileId))
            {
                throw new IOException("simulated copy failure");
            }

            await destination.WriteAsync(Bytes[fileId], cancellationToken);
        }
    }
}
