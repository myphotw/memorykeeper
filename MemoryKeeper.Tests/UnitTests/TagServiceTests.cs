using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;
using MemoryKeeper.Infrastructure.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using StorageEntity = MemoryKeeper.Domain.Entities.Storage;

namespace MemoryKeeper.Tests.UnitTests;

public class TagServiceTests
{
    [Fact]
    public async Task AssignRenameSearchAndDelete_WorkAsExpected()
    {
        var (service, mediaRepository, mediaId) = await CreateFixtureAsync();

        var created = await service.CreateTagAsync(new CreateTagRequest { Name = "맛집" });
        Assert.False(string.IsNullOrWhiteSpace(created.Color));
        Assert.Equal(0, created.UsageCount);

        await service.AssignTagsAsync(new AssignTagRequest
        {
            MediaIds = [mediaId],
            TagIds = [created.Id],
            NewTagName = "라멘"
        });

        await service.AssignTagsAsync(new AssignTagRequest
        {
            MediaIds = [mediaId],
            TagIds = [created.Id]
        });

        var mediaTags = await service.GetMediaTagsAsync(mediaId);
        Assert.Equal(2, mediaTags.Count);
        Assert.Contains(mediaTags, tag => tag.Name == "맛집" && tag.UsageCount == 1);
        Assert.Contains(mediaTags, tag => tag.Name == "라멘" && tag.UsageCount == 1);

        var searched = await service.SearchTagsAsync("맛");
        Assert.Contains(searched, tag => tag.Name == "맛집");

        var byTag = await service.SearchByTagAsync([created.Id], year: 2024);
        Assert.Single(byTag);
        Assert.Equal(mediaId, byTag[0].Id);

        await service.RenameTagAsync(new RenameTagRequest
        {
            TagId = created.Id,
            Name = "미식"
        });

        var renamed = (await service.GetTagListAsync()).Single(tag => tag.Id == created.Id);
        Assert.Equal("미식", renamed.Name);

        await service.RemoveTagsAsync(new RemoveTagRequest
        {
            MediaIds = [mediaId],
            TagIds = [created.Id]
        });

        mediaTags = await service.GetMediaTagsAsync(mediaId);
        Assert.DoesNotContain(mediaTags, tag => tag.Id == created.Id);
        Assert.Equal(0, (await service.GetTagListAsync()).Single(tag => tag.Id == created.Id).UsageCount);

        var ramen = (await service.GetTagListAsync()).Single(tag => tag.Name == "라멘");
        await service.DeleteTagAsync(ramen.Id);
        Assert.Empty(await service.GetMediaTagsAsync(mediaId));
        Assert.DoesNotContain(await service.GetTagListAsync(), tag => tag.Id == ramen.Id);
        Assert.NotNull(await mediaRepository.GetByIdAsync(mediaId));
    }

    [Fact]
    public async Task RecentPinnedAndCommonTags_WorkAsExpected()
    {
        var storageId = Guid.NewGuid();
        var placeId = Guid.NewGuid();
        var mediaA = Guid.NewGuid();
        var mediaB = Guid.NewGuid();
        var mediaRepository = new InMemoryMediaRepository();
        var storageRepository = new FakeStorageRepository();
        var tagRepository = new InMemoryTagRepository();
        var mediaTagRepository = new InMemoryMediaTagRepository();
        var settingRepository = new FakeSettingRepository();

        await storageRepository.AddAsync(CreateStorage(storageId));
        await mediaRepository.AddAsync(CreateMedia(mediaA, storageId, placeId, "a.jpg"));
        await mediaRepository.AddAsync(CreateMedia(mediaB, storageId, placeId, "b.jpg"));

        var service = new TagService(
            tagRepository,
            mediaTagRepository,
            mediaRepository,
            storageRepository,
            settingRepository,
            new FakeFileAccessService(),
            NullLogger<TagService>.Instance);

        var food = await service.CreateTagAsync(new CreateTagRequest { Name = "맛집" });
        var family = await service.CreateTagAsync(new CreateTagRequest { Name = "가족" });
        var night = await service.CreateTagAsync(new CreateTagRequest { Name = "야경" });
        var child = await service.CreateTagAsync(new CreateTagRequest { Name = "아이" });

        await service.SetPinnedAsync(new SetPinnedTagRequest { TagId = family.Id, IsPinned = true });

        await service.AssignTagsAsync(new AssignTagRequest
        {
            MediaIds = [mediaA, mediaB],
            TagIds = [food.Id, family.Id]
        });
        await service.AssignTagsAsync(new AssignTagRequest
        {
            MediaIds = [mediaA],
            TagIds = [night.Id]
        });
        await service.AssignTagsAsync(new AssignTagRequest
        {
            MediaIds = [mediaB],
            TagIds = [child.Id]
        });

        var picker = await service.GetTagPickerStateAsync([mediaA, mediaB], forRemove: false);
        Assert.Contains(picker.PinnedTags, tag => tag.Id == family.Id && tag.IsPinned && tag.IsAssigned);
        Assert.Contains(picker.CommonTags, tag => tag.Id == food.Id && tag.IsAssigned);
        Assert.Contains(picker.CommonTags, tag => tag.Id == family.Id && tag.IsAssigned);
        Assert.DoesNotContain(picker.CommonTags, tag => tag.Id == night.Id);
        Assert.Contains(picker.RecentTags, tag => tag.Id == night.Id && !tag.IsAssigned);
        Assert.Contains(picker.RecentTags, tag => tag.Id == child.Id && !tag.IsAssigned);
        Assert.DoesNotContain(picker.CandidateTags, tag => tag.Id == food.Id);
        Assert.DoesNotContain(picker.CandidateTags, tag => tag.Id == family.Id);

        // Recent order: last touched first (child). Pinned tags are excluded from recent.
        Assert.True(picker.RecentTags.Count <= 10);
        Assert.Equal(child.Id, picker.RecentTags[0].Id);

        await service.RemoveTagsAsync(new RemoveTagRequest
        {
            MediaIds = [mediaA],
            TagIds = [night.Id]
        });

        var afterRemove = await service.GetTagPickerStateAsync([mediaA], forRemove: false);
        Assert.Equal(night.Id, afterRemove.RecentTags[0].Id);

        var removePicker = await service.GetTagPickerStateAsync([mediaA, mediaB], forRemove: true);
        Assert.Contains(removePicker.CommonTags, tag => tag.Id == food.Id);
        Assert.DoesNotContain(removePicker.CandidateTags, tag => tag.Id == food.Id);
    }

    private static async Task<(TagService Service, InMemoryMediaRepository MediaRepository, Guid MediaId)> CreateFixtureAsync()
    {
        var storageId = Guid.NewGuid();
        var placeId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        var mediaRepository = new InMemoryMediaRepository();
        var storageRepository = new FakeStorageRepository();

        await storageRepository.AddAsync(CreateStorage(storageId));
        await mediaRepository.AddAsync(CreateMedia(mediaId, storageId, placeId, "ramen.jpg"));

        var service = new TagService(
            new InMemoryTagRepository(),
            new InMemoryMediaTagRepository(),
            mediaRepository,
            storageRepository,
            new FakeSettingRepository(),
            new FakeFileAccessService(),
            NullLogger<TagService>.Instance);

        return (service, mediaRepository, mediaId);
    }

    private static StorageEntity CreateStorage(Guid storageId) => new()
    {
        Id = storageId,
        Name = "Local",
        PhotoRoot = @"D:\Library",
        StorageType = StorageType.Local,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static Media CreateMedia(Guid mediaId, Guid storageId, Guid placeId, string fileName) => new()
    {
        Id = mediaId,
        FileName = fileName,
        MediaType = MediaType.Photo,
        Status = MediaStatus.Imported,
        OriginalPath = Path.Combine(@"D:\Photos", fileName),
        RelativePath = Path.Combine("2024", "a", fileName),
        ContentHash = Guid.NewGuid().ToString("N"),
        CapturedAt = DateTimeOffset.Parse("2024-04-01T12:00:00Z").UtcDateTime,
        ImportedAt = DateTime.UtcNow,
        StorageId = storageId,
        PlaceId = placeId,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private sealed class FakeStorageRepository : IStorageRepository
    {
        private readonly List<StorageEntity> _items = [];

        public Task<StorageEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_items.FirstOrDefault(item => item.Id == id));

        public Task<IReadOnlyList<StorageEntity>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<StorageEntity>>(_items.ToList());

        public Task AddAsync(StorageEntity storage, CancellationToken cancellationToken = default)
        {
            _items.Add(storage);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(StorageEntity storage, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(StorageEntity storage, CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(item => item.Id == storage.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSettingRepository : ISettingRepository
    {
        private readonly List<Setting> _items = [];

        public Task<Setting?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_items.FirstOrDefault(item => item.Id == id));

        public Task<Setting?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_items.FirstOrDefault(item => item.Key == key));

        public Task<IReadOnlyList<Setting>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Setting>>(_items.ToList());

        public Task AddAsync(Setting setting, CancellationToken cancellationToken = default)
        {
            _items.Add(setting);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Setting setting, CancellationToken cancellationToken = default)
        {
            var index = _items.FindIndex(item => item.Id == setting.Id || item.Key == setting.Key);
            if (index >= 0)
            {
                _items[index] = setting;
            }

            return Task.CompletedTask;
        }

        public Task DeleteAsync(Setting setting, CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(item => item.Id == setting.Id);
            return Task.CompletedTask;
        }
    }
}

