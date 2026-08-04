using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using StorageEntity = MemoryKeeper.Domain.Entities.Storage;

namespace MemoryKeeper.Tests.UnitTests;

public class StorageServiceTests
{
    [Fact]
    public async Task CreateAndSetActiveStorage_ManagesSingleActiveStorage()
    {
        var repository = new InMemoryStorageRepository();
        var service = new StorageService(
            repository,
            new FakeFileAccessService(),
            NullLogger<StorageService>.Instance);

        var first = await service.CreateStorageAsync(new CreateStorageRequest
        {
            Name = "Local A",
            StorageType = StorageType.Local,
            PhotoRoot = @"D:\LibraryA",
            SetAsActive = true
        });

        var second = await service.CreateStorageAsync(new CreateStorageRequest
        {
            Name = "NAS B",
            StorageType = StorageType.Nas,
            PhotoRoot = @"\\nas\share",
            SetAsActive = true
        });

        var list = await service.GetStorageListAsync();
        Assert.Equal(2, list.Count);
        Assert.True(list.Single(item => item.Id == second.Id).IsActive);
        Assert.False(list.Single(item => item.Id == first.Id).IsActive);

        var updated = await service.UpdateStorageAsync(new UpdateStorageRequest
        {
            Id = first.Id,
            Name = "Local A Updated",
            StorageType = StorageType.External,
            PhotoRoot = @"E:\LibraryA"
        });

        Assert.Equal("Local A Updated", updated.Name);
        Assert.Equal(StorageType.External, updated.StorageType);

        var activated = await service.SetActiveStorageAsync(first.Id);
        Assert.True(activated.IsActive);

        list = await service.GetStorageListAsync();
        Assert.True(list.Single(item => item.Id == first.Id).IsActive);
        Assert.False(list.Single(item => item.Id == second.Id).IsActive);
    }

    [Fact]
    public async Task UpdatePhotoRootAsync_ActivatesStorageWhenInactive()
    {
        var repository = new InMemoryStorageRepository();
        var service = new StorageService(
            repository,
            new FakeFileAccessService(),
            NullLogger<StorageService>.Instance);

        var storage = await service.CreateStorageAsync(new CreateStorageRequest
        {
            Name = "Local",
            StorageType = StorageType.Local,
            PhotoRoot = @"D:\LibraryA",
            SetAsActive = false
        });

        Assert.False(storage.IsActive);

        var updated = await service.UpdatePhotoRootAsync(storage.Id, @"E:\LibraryA");

        Assert.Equal(@"E:\LibraryA", updated.PhotoRoot);
        Assert.True(updated.IsActive);
    }

    private sealed class InMemoryStorageRepository : IStorageRepository
    {
        private readonly List<StorageEntity> _items = [];

        public Task<StorageEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_items.FirstOrDefault(item => item.Id == id));

        public Task<IReadOnlyList<StorageEntity>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<StorageEntity>>(_items.Select(Clone).ToList());

        public Task AddAsync(StorageEntity storage, CancellationToken cancellationToken = default)
        {
            _items.Add(Clone(storage));
            return Task.CompletedTask;
        }

        public Task UpdateAsync(StorageEntity storage, CancellationToken cancellationToken = default)
        {
            var index = _items.FindIndex(item => item.Id == storage.Id);
            if (index >= 0)
            {
                _items[index] = Clone(storage);
            }

            return Task.CompletedTask;
        }

        public Task DeleteAsync(StorageEntity storage, CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(item => item.Id == storage.Id);
            return Task.CompletedTask;
        }

        private static StorageEntity Clone(StorageEntity storage) => new()
        {
            Id = storage.Id,
            Name = storage.Name,
            StorageType = storage.StorageType,
            PhotoRoot = storage.PhotoRoot,
            IsActive = storage.IsActive,
            CreatedAt = storage.CreatedAt,
            UpdatedAt = storage.UpdatedAt
        };
    }
}
