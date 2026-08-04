using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using MemoryKeeper.Infrastructure.Repositories;

namespace MemoryKeeper.Tests.UnitTests;

public class MediaServiceTests
{
    [Fact]
    public async Task GetLibraryAsync_ReturnsMappedMediaItems()
    {
        var mediaRepository = new InMemoryMediaRepository();
        var storageRepository = new FakeStorageRepository();
        await mediaRepository.AddAsync(CreatePhoto(
            fileName: "IMG001.jpg",
            RelativePath: @"2025\Unknown\????\IMG001.jpg",
            storageId: Guid.NewGuid(),
            placeId: null,
            capturedAt: DateTimeOffset.Parse("2025-01-01T00:00:00Z")));

        var service = new MediaService(
            mediaRepository,
            storageRepository,
            new InMemoryMediaTagRepository(),
            new FakeFileAccessService());

        var result = await service.GetLibraryAsync();

        Assert.Single(result);
        Assert.Equal("IMG001.jpg", result[0].FileName);
    }

    [Fact]
    public async Task SearchGalleryAsync_FiltersPhotosByPlaceAndYear_AndResolvesAbsolutePath()
    {
        var storageId = Guid.NewGuid();
        var placeId = Guid.NewGuid();
        var otherPlaceId = Guid.NewGuid();

        var mediaRepository = new InMemoryMediaRepository();
        var storageRepository = new FakeStorageRepository();
        await storageRepository.AddAsync(new Storage
        {
            Id = storageId,
            Name = "Library",
            PhotoRoot = @"D:\Library",
            StorageType = StorageType.Local,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await mediaRepository.AddAsync(CreatePhoto(
            fileName: "match.jpg",
            RelativePath: @"2025\a\match.jpg",
            storageId: storageId,
            placeId: placeId,
            capturedAt: DateTimeOffset.Parse("2025-05-01T10:00:00Z")));
        await mediaRepository.AddAsync(CreatePhoto(
            fileName: "other-place.jpg",
            RelativePath: @"2025\b\other-place.jpg",
            storageId: storageId,
            placeId: otherPlaceId,
            capturedAt: DateTimeOffset.Parse("2025-05-02T10:00:00Z")));
        await mediaRepository.AddAsync(CreatePhoto(
            fileName: "other-year.jpg",
            RelativePath: @"2024\a\other-year.jpg",
            storageId: storageId,
            placeId: placeId,
            capturedAt: DateTimeOffset.Parse("2024-05-01T10:00:00Z")));
        await mediaRepository.AddAsync(new Media
        {
            Id = Guid.NewGuid(),
            FileName = "clip.mp4",
            MediaType = MediaType.Video,
            Status = MediaStatus.Imported,
            OriginalPath = @"D:\Photos\clip.mp4",
            RelativePath = @"2025\a\clip.mp4",
            ContentHash = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CapturedAt = DateTimeOffset.Parse("2025-05-01T12:00:00Z").UtcDateTime,
            ImportedAt = DateTime.UtcNow,
            StorageId = storageId,
            PlaceId = placeId
        });

        var service = new MediaService(
            mediaRepository,
            storageRepository,
            new InMemoryMediaTagRepository(),
            new FakeFileAccessService());

        var result = await service.SearchGalleryAsync(year: 2025, placeId: placeId);

        Assert.Single(result);
        Assert.Equal("match.jpg", result[0].FileName);
        Assert.Equal(@"D:\Library\2025\a\match.jpg", result[0].AbsoluteLibraryPath);
        Assert.Equal(placeId, result[0].PlaceId);
    }

    [Fact]
    public async Task SearchGalleryAsync_IncludesPendingPhotosWithoutPlace()
    {
        var storageId = Guid.NewGuid();

        var mediaRepository = new InMemoryMediaRepository();
        var storageRepository = new FakeStorageRepository();
        await storageRepository.AddAsync(new Storage
        {
            Id = storageId,
            Name = "Library",
            PhotoRoot = @"D:\Library",
            StorageType = StorageType.Local,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await mediaRepository.AddAsync(CreatePhoto(
            fileName: "pending.jpg",
            RelativePath: @"2025\Unknown\PendingFolder\pending.jpg",
            storageId: storageId,
            placeId: null,
            capturedAt: DateTimeOffset.Parse("2025-05-03T10:00:00Z"),
            status: MediaStatus.Pending));

        var service = new MediaService(
            mediaRepository,
            storageRepository,
            new InMemoryMediaTagRepository(),
            new FakeFileAccessService());

        var result = await service.SearchGalleryAsync(year: null, placeId: null);

        Assert.Single(result);
        Assert.Equal("pending.jpg", result[0].FileName);
        Assert.Null(result[0].PlaceId);
    }

    [Fact]
    public async Task GetGallerySidebarSummaryAsync_ReturnsYearsWithPhotosDescendingAndCounts()
    {
        var storageId = Guid.NewGuid();
        var mediaRepository = new InMemoryMediaRepository();
        var storageRepository = new FakeStorageRepository();
        await storageRepository.AddAsync(CreateStorage(storageId));

        await mediaRepository.AddAsync(CreatePhoto(
            "a.jpg", @"2025\a.jpg", storageId, null,
            DateTimeOffset.Parse("2025-01-01T00:00:00Z"), isFavorite: true));
        await mediaRepository.AddAsync(CreatePhoto(
            "b.jpg", @"2025\b.jpg", storageId, null,
            DateTimeOffset.Parse("2025-06-01T00:00:00Z")));
        await mediaRepository.AddAsync(CreatePhoto(
            "c.jpg", @"2023\c.jpg", storageId, null,
            DateTimeOffset.Parse("2023-03-01T00:00:00Z"), status: MediaStatus.Pending));
        await mediaRepository.AddAsync(CreatePhoto(
            "d.jpg", @"2024\d.jpg", storageId, null,
            DateTimeOffset.Parse("2024-08-01T00:00:00Z"), isFavorite: true));

        var service = new MediaService(
            mediaRepository,
            storageRepository,
            new InMemoryMediaTagRepository(),
            new FakeFileAccessService());

        var summary = await service.GetGallerySidebarSummaryAsync();

        Assert.Equal(4, summary.TotalCount);
        Assert.Equal(2, summary.FavoriteCount);
        Assert.Equal(1, summary.PendingCount);
        Assert.Equal(4, summary.RecentCount);
        Assert.Equal([2025, 2024, 2023], summary.Years.Select(year => year.Year).ToArray());
        Assert.Equal([2, 1, 1], summary.Years.Select(year => year.Count).ToArray());
    }

    [Fact]
    public async Task QueryGalleryAsync_All_ReturnsEveryPhotoWithoutYearFilter()
    {
        var storageId = Guid.NewGuid();
        var mediaRepository = new InMemoryMediaRepository();
        var storageRepository = new FakeStorageRepository();
        await storageRepository.AddAsync(CreateStorage(storageId));

        await mediaRepository.AddAsync(CreatePhoto(
            "2025.jpg", @"2025\a.jpg", storageId, null,
            DateTimeOffset.Parse("2025-01-01T00:00:00Z")));
        await mediaRepository.AddAsync(CreatePhoto(
            "2024.jpg", @"2024\a.jpg", storageId, null,
            DateTimeOffset.Parse("2024-01-01T00:00:00Z")));
        await mediaRepository.AddAsync(CreatePhoto(
            "pending.jpg", @"???? ???\pending.jpg", storageId, null,
            DateTimeOffset.Parse("2023-01-01T00:00:00Z"), status: MediaStatus.Pending));

        var service = new MediaService(
            mediaRepository,
            storageRepository,
            new InMemoryMediaTagRepository(),
            new FakeFileAccessService());

        var result = await service.QueryGalleryAsync(GalleryQueryMode.All);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, item => item.FileName == "pending.jpg");
    }

    [Fact]
    public async Task QueryGalleryAsync_YearAndQuickFilters_ReturnExpectedSets()
    {
        var storageId = Guid.NewGuid();
        var mediaRepository = new InMemoryMediaRepository();
        var storageRepository = new FakeStorageRepository();
        await storageRepository.AddAsync(CreateStorage(storageId));

        var placeId = Guid.NewGuid();
        var favorite = CreatePhoto(
            "fav.jpg", @"2025\fav.jpg", storageId, placeId,
            DateTimeOffset.Parse("2025-02-01T00:00:00Z"), isFavorite: true);
        favorite.ImportedAt = DateTime.UtcNow.AddMinutes(-1);
        await mediaRepository.AddAsync(favorite);

        var pending = CreatePhoto(
            "pending.jpg", @"???? ???\pending.jpg", storageId, null,
            DateTimeOffset.Parse("2024-02-01T00:00:00Z"), status: MediaStatus.Pending);
        pending.ImportedAt = DateTime.UtcNow.AddMinutes(-2);
        await mediaRepository.AddAsync(pending);

        var older = CreatePhoto(
            "older.jpg", @"2024\older.jpg", storageId, placeId,
            DateTimeOffset.Parse("2024-03-01T00:00:00Z"));
        older.ImportedAt = DateTime.UtcNow.AddDays(-10);
        await mediaRepository.AddAsync(older);

        var service = new MediaService(
            mediaRepository,
            storageRepository,
            new InMemoryMediaTagRepository(),
            new FakeFileAccessService());

        var year2025 = await service.QueryGalleryAsync(GalleryQueryMode.Year, year: 2025);
        Assert.Single(year2025);
        Assert.Equal("fav.jpg", year2025[0].FileName);

        var favorites = await service.QueryGalleryAsync(GalleryQueryMode.Favorites);
        Assert.Single(favorites);
        Assert.Equal("fav.jpg", favorites[0].FileName);

        var pendingItems = await service.QueryGalleryAsync(GalleryQueryMode.Pending);
        Assert.Single(pendingItems);
        Assert.Equal("pending.jpg", pendingItems[0].FileName);

        var recent = await service.QueryGalleryAsync(GalleryQueryMode.Recent);
        Assert.Equal(3, recent.Count);
        Assert.Equal("fav.jpg", recent[0].FileName);
    }

    [Fact]
    public async Task QueryGalleryAsync_EmptyOrInvalidRelativePath_DoesNotThrow_AndStillReturnsItems()
    {
        var storageId = Guid.NewGuid();
        var mediaRepository = new InMemoryMediaRepository();
        var storageRepository = new FakeStorageRepository();
        await storageRepository.AddAsync(CreateStorage(storageId));

        var emptyRelative = CreatePhoto(
            fileName: "empty-rel.jpg",
            RelativePath: "keep",
            storageId,
            placeId: null,
            DateTimeOffset.Parse("2025-01-01T00:00:00Z"));
        emptyRelative.FileName = string.Empty;
        emptyRelative.RelativePath = "   ";
        await mediaRepository.AddAsync(emptyRelative);

        var pendingNullPlace = CreatePhoto(
            "pending.jpg",
            @"???? ???\pending.jpg",
            storageId,
            placeId: null,
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
            status: MediaStatus.Pending);
        await mediaRepository.AddAsync(pendingNullPlace);

        var service = new MediaService(
            mediaRepository,
            storageRepository,
            new InMemoryMediaTagRepository(),
            new FakeFileAccessService());

        var result = await service.QueryGalleryAsync(GalleryQueryMode.All);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, item => item.FileName == "pending.jpg");
        var broken = Assert.Single(result, item => item.FileName == string.Empty);
        Assert.Equal(string.Empty, broken.AbsoluteLibraryPath);
    }

    private static Storage CreateStorage(Guid storageId)
    {
        return new Storage
        {
            Id = storageId,
            Name = "Library",
            PhotoRoot = @"D:\Library",
            StorageType = StorageType.Local,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static Media CreatePhoto(
        string fileName,
        string RelativePath,
        Guid storageId,
        Guid? placeId,
        DateTimeOffset capturedAt,
        MediaStatus status = MediaStatus.Imported,
        bool isFavorite = false)
    {
        return new Media
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            MediaType = MediaType.Photo,
            Status = status,
            OriginalPath = Path.Combine(@"D:\Photos", fileName),
            RelativePath = RelativePath,
            ContentHash = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CapturedAt = capturedAt.UtcDateTime,
            ImportedAt = DateTime.UtcNow,
            StorageId = storageId,
            PlaceId = placeId,
            IsFavorite = isFavorite
        };
    }

    private sealed class FakeStorageRepository : IStorageRepository
    {
        private readonly List<Storage> _items = [];

        public Task<Storage?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_items.FirstOrDefault(item => item.Id == id));
        }

        public Task<IReadOnlyList<Storage>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Storage>>(_items.ToList());
        }

        public Task AddAsync(Storage storage, CancellationToken cancellationToken = default)
        {
            _items.Add(storage);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Storage storage, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Storage storage, CancellationToken cancellationToken = default)
        {
            _items.Remove(storage);
            return Task.CompletedTask;
        }
    }
}
