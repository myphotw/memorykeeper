using System.Text;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.DTOs.Gallery;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;

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
                Detail = new MemoryKeeper.Application.DTOs.Gallery.PhotoDetailDto
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
