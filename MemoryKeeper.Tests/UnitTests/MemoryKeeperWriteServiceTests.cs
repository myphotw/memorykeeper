using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class MemoryKeeperWriteServiceTests
{
    private const string FileId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string SecondFileId = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
    private static readonly Guid MediaId = BackendFileIdCodec.ToGuid(FileId);
    private static readonly Guid PlaceId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public async Task FavoriteMemoAndRawLocation_UseMetadataRevisionWithoutChangingPlaceRevision()
    {
        var repository = new FakeRepository { MetadataRevision = 3, PlaceRevision = 9 };
        var invalidation = new CatalogInvalidation();
        var service = new MemoryKeeperWriteService(repository, invalidation);

        var favorite = await service.SetFavoriteAsync(MediaId, true, expectedRevision: 3);
        var memo = await service.SetMemoAsync(MediaId, "  가족 여행  ", expectedRevision: favorite.Revision);
        var location = await service.SetRawLocationAsync(
            MediaId,
            memo.Revision,
            37.5,
            127.0,
            "대한민국",
            "서울특별시",
            "서울",
            "종로구",
            "원시 주소");

        Assert.Equal([3, 4, 5], repository.MetadataExpectedRevisions);
        Assert.Equal(9, favorite.PlaceRevision);
        Assert.Equal(9, memo.PlaceRevision);
        Assert.Equal(9, location.PlaceRevision);
        Assert.Equal("가족 여행", repository.MetadataRequests[1].Memo);
        Assert.Contains("gps_lat", repository.MetadataRequests[2].ChangedFields);
        Assert.True(invalidation.Consume(CatalogSurface.Pending));
        Assert.True(invalidation.Consume(CatalogSurface.Gallery));
        Assert.True(invalidation.Consume(CatalogSurface.Favorites));
        Assert.False(invalidation.Consume(CatalogSurface.Tags));
    }

    [Fact]
    public async Task EmptyMemo_IsSentAsExplicitNull_AndOverLimitIsRejectedBeforeApiCall()
    {
        var repository = new FakeRepository();
        var service = new MemoryKeeperWriteService(repository, new CatalogInvalidation());

        await service.SetMemoAsync(MediaId, "   ", 0);
        Assert.Null(Assert.Single(repository.MetadataRequests).Memo);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SetMemoAsync(MediaId, new string('x', 10_001), 1));
        Assert.Single(repository.MetadataRequests);
    }

    [Fact]
    public async Task PendingWithRawGpsRemainsPending_AndBatchAssignUsesLoadedPlaceRevision()
    {
        var repository = new FakeRepository
        {
            Pending = new MemoryKeeperPendingListDto
            {
                Items =
                [
                    new MemoryKeeperPendingItemDto
                    {
                        FileId = FileId,
                        GpsLat = 37.5,
                        GpsLon = 127.0,
                        Country = "대한민국",
                        Province = "서울특별시",
                        PlaceName = "원시 주소",
                        PlaceRevision = 7,
                    },
                    new MemoryKeeperPendingItemDto
                    {
                        FileId = SecondFileId,
                        GpsLat = 37.51,
                        GpsLon = 127.01,
                        Country = "대한민국",
                        PlaceName = "두 번째 원시 주소",
                        PlaceRevision = 8,
                    },
                ],
                Total = 2,
            },
        };
        var invalidation = new CatalogInvalidation();
        var service = new MemoryKeeperWriteService(repository, invalidation);

        var overview = await service.GetPendingMemoriesAsync();
        Assert.Equal(2, overview.ReclassificationCandidates.Count);
        var pending = overview.ReclassificationCandidates[0];
        Assert.True(pending.HasGps);
        Assert.Equal("원시 주소", pending.RawPlaceName);
        var result = await service.AssignPlaceAsync(new AssignMediaPlaceRequest
        {
            MediaIds = overview.ReclassificationCandidates.Select(item => item.MediaId).ToList(),
            PlaceId = PlaceId,
        });

        Assert.Equal(2, result.UpdatedCount);
        Assert.Equal(7, repository.LastPendingAssign!.ExpectedRevisions[FileId]);
        Assert.Equal(8, repository.LastPendingAssign.ExpectedRevisions[SecondFileId]);
        Assert.True(invalidation.Consume(CatalogSurface.Pending));
        Assert.True(invalidation.Consume(CatalogSurface.Favorites));
        Assert.False(invalidation.Consume(CatalogSurface.Tags));
        Assert.True(invalidation.Consume(CatalogSurface.Visits));
    }

    [Fact]
    public async Task TagRelationUsesIntegerIdentityAndMetadataRevisionSequence()
    {
        var repository = new FakeRepository();
        var service = new MemoryKeeperWriteService(repository, new CatalogInvalidation());

        var revision = await service.AssignFileTagAsync(MediaId, tagId: 42, expectedRevision: 3);
        revision = await service.RemoveFileTagAsync(MediaId, tagId: 42, expectedRevision: revision);

        Assert.Equal(5, revision);
        Assert.Equal([(42, 3), (42, 4)], repository.FileTagMutations);
    }

    [Fact]
    public async Task DeleteInvalidatesEveryDerivedSurface()
    {
        var invalidation = new CatalogInvalidation();
        var service = new MemoryKeeperWriteService(new FakeRepository(), invalidation);

        await service.DeleteFileAsync(MediaId);

        Assert.True(invalidation.Consume(CatalogSurface.Gallery));
        Assert.True(invalidation.Consume(CatalogSurface.Home));
        Assert.True(invalidation.Consume(CatalogSurface.Visits));
        Assert.True(invalidation.Consume(CatalogSurface.Travel));
        Assert.True(invalidation.Consume(CatalogSurface.Pending));
        Assert.True(invalidation.Consume(CatalogSurface.Favorites));
        Assert.True(invalidation.Consume(CatalogSurface.Tags));
    }

    private sealed class FakeRepository : IMemoryKeeperWriteApiRepository
    {
        public int MetadataRevision { get; set; }
        public int PlaceRevision { get; set; }
        public List<int> MetadataExpectedRevisions { get; } = [];
        public List<MemoryKeeperFileMetadataPatchRequest> MetadataRequests { get; } = [];
        public MemoryKeeperPendingListDto Pending { get; set; } = new();
        public MemoryKeeperPendingAssignRequest? LastPendingAssign { get; private set; }
        public List<(int TagId, int Revision)> FileTagMutations { get; } = [];

        public Task<MemoryKeeperFileMetadataPatchResponse> PatchMetadataAsync(string fileId, MemoryKeeperFileMetadataPatchRequest request, CancellationToken cancellationToken = default)
        {
            MetadataExpectedRevisions.Add(request.ExpectedRevision);
            MetadataRequests.Add(request);
            MetadataRevision = request.ExpectedRevision + 1;
            return Task.FromResult(new MemoryKeeperFileMetadataPatchResponse
            {
                FileId = fileId,
                Favorite = request.Favorite ?? false,
                Memo = request.Memo,
                Revision = MetadataRevision,
                PlaceRevision = PlaceRevision,
            });
        }

        public Task<MemoryKeeperDeleteResultDto> DeleteFileAsync(string fileId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MemoryKeeperDeleteResultDto { FileId = fileId, CleanupStatus = "CLEANED" });

        public Task<MemoryKeeperPendingListDto> GetPendingAsync(bool includeSuggestions = true, CancellationToken cancellationToken = default) =>
            Task.FromResult(Pending);

        public Task<MemoryKeeperPendingAssignResponse> AssignPendingPlaceAsync(MemoryKeeperPendingAssignRequest request, CancellationToken cancellationToken = default)
        {
            LastPendingAssign = request;
            return Task.FromResult(new MemoryKeeperPendingAssignResponse { AssignedCount = request.FileIds.Count });
        }

        public Task<MemoryKeeperFileTagMutationResponse> AssignFileTagAsync(string fileId, int tagId, int expectedRevision, CancellationToken cancellationToken = default)
        {
            FileTagMutations.Add((tagId, expectedRevision));
            return Task.FromResult(new MemoryKeeperFileTagMutationResponse { FileId = fileId, TagId = tagId, Assigned = true, Revision = expectedRevision + 1 });
        }

        public Task<MemoryKeeperFileTagMutationResponse> RemoveFileTagAsync(string fileId, int tagId, int expectedRevision, CancellationToken cancellationToken = default)
        {
            FileTagMutations.Add((tagId, expectedRevision));
            return Task.FromResult(new MemoryKeeperFileTagMutationResponse { FileId = fileId, TagId = tagId, Assigned = false, Revision = expectedRevision + 1 });
        }

        public Task<MemoryKeeperTagListDto> GetTagsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new MemoryKeeperTagListDto());
        public Task<MemoryKeeperTagDto> CreateTagAsync(MemoryKeeperTagCreateRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MemoryKeeperTagDto> UpdateTagAsync(int tagId, MemoryKeeperTagUpdateRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteTagAsync(int tagId, int expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MemoryKeeperTagDto> MergeTagAsync(int sourceTagId, MemoryKeeperTagMergeRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
