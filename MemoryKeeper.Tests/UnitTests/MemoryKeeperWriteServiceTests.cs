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
    public async Task ExplicitPlaceSupplement_PreservesExistingExifGps_AndPatchesProviderGeography()
    {
        var repository = new FakeRepository { MetadataRevision = 4, PlaceRevision = 12 };
        var service = new MemoryKeeperWriteService(repository, new CatalogInvalidation());

        var response = await service.SupplementRawLocationFromPlaceAsync(
            MediaId,
            expectedRevision: 4,
            currentLatitude: 37.501,
            currentLongitude: 127.002,
            new PlaceLocationPreview
            {
                DisplayName = "서울숲",
                Country = "대한민국",
                Province = "서울특별시",
                City = "성동구",
                District = "성수동1가",
                Address = "서울특별시 성동구 뚝섬로 273",
                Latitude = 37.544,
                Longitude = 127.037,
                Source = PlaceLocationSource.Google,
            });

        var request = Assert.Single(repository.MetadataRequests);
        Assert.DoesNotContain("gps_lat", request.ChangedFields);
        Assert.DoesNotContain("gps_lon", request.ChangedFields);
        Assert.Null(request.GpsLat);
        Assert.Null(request.GpsLon);
        Assert.Equal("대한민국", request.Country);
        Assert.Equal("서울특별시", request.Province);
        Assert.Equal("성동구", request.City);
        Assert.Equal("성수동1가", request.District);
        Assert.Equal("서울특별시 성동구 뚝섬로 273", request.PlaceName);
        Assert.Equal(4, request.ExpectedRevision);
        Assert.Equal(12, response!.PlaceRevision);
    }

    [Fact]
    public async Task ExplicitPlaceSupplement_FillsProviderGpsOnlyWhenRawGpsIsMissing()
    {
        var repository = new FakeRepository { MetadataRevision = 7, PlaceRevision = 3 };
        var service = new MemoryKeeperWriteService(repository, new CatalogInvalidation());

        await service.SupplementRawLocationFromPlaceAsync(
            MediaId,
            expectedRevision: 7,
            currentLatitude: null,
            currentLongitude: null,
            new PlaceLocationPreview
            {
                DisplayName = "부산역",
                Latitude = 35.115,
                Longitude = 129.041,
                Source = PlaceLocationSource.Existing,
            });

        var request = Assert.Single(repository.MetadataRequests);
        Assert.Contains("gps_lat", request.ChangedFields);
        Assert.Contains("gps_lon", request.ChangedFields);
        Assert.Equal(35.115, request.GpsLat);
        Assert.Equal(129.041, request.GpsLon);
        Assert.Equal("부산역", request.PlaceName);
        Assert.Equal(7, request.ExpectedRevision);
        Assert.Equal(3, repository.PlaceRevision);
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
    public async Task BatchAssign_AfterReclassificationUsesExplicitLatestRevisionsInsteadOfLoadedSnapshot()
    {
        var secondMediaId = BackendFileIdCodec.ToGuid(SecondFileId);
        var repository = new FakeRepository
        {
            PlaceCleanupPages =
            {
                [1] = new MemoryKeeperPendingListDto
                {
                    Items =
                    [
                        new MemoryKeeperPendingItemDto { FileId = FileId, PlaceRevision = 7 },
                        new MemoryKeeperPendingItemDto { FileId = SecondFileId, PlaceRevision = 8 },
                    ],
                    Total = 2,
                    Page = 1,
                    PageSize = 50,
                },
            },
        };
        var service = new MemoryKeeperWriteService(repository, new CatalogInvalidation());
        await service.GetPlaceCleanupMemoriesAsync();

        await service.AssignPlaceAsync(new AssignMediaPlaceRequest
        {
            MediaIds = [MediaId, secondMediaId],
            PlaceId = PlaceId,
            ExpectedPlaceRevisions = new Dictionary<Guid, int>
            {
                [MediaId] = 17,
                [secondMediaId] = 18,
            },
        });

        Assert.Equal(17, repository.LastPendingAssign!.ExpectedRevisions[FileId]);
        Assert.Equal(18, repository.LastPendingAssign.ExpectedRevisions[SecondFileId]);
        Assert.DoesNotContain(7, repository.LastPendingAssign.ExpectedRevisions.Values);
        Assert.DoesNotContain(8, repository.LastPendingAssign.ExpectedRevisions.Values);
    }

    [Fact]
    public async Task BatchAssign_DoesNotFallBackToStaleSnapshotWhenLatestRevisionIsMissing()
    {
        var secondMediaId = BackendFileIdCodec.ToGuid(SecondFileId);
        var repository = new FakeRepository
        {
            PlaceCleanupPages =
            {
                [1] = new MemoryKeeperPendingListDto
                {
                    Items =
                    [
                        new MemoryKeeperPendingItemDto { FileId = FileId, PlaceRevision = 7 },
                        new MemoryKeeperPendingItemDto { FileId = SecondFileId, PlaceRevision = 8 },
                    ],
                    Total = 2,
                    Page = 1,
                    PageSize = 50,
                },
            },
        };
        var service = new MemoryKeeperWriteService(repository, new CatalogInvalidation());
        await service.GetPlaceCleanupMemoriesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AssignPlaceAsync(new AssignMediaPlaceRequest
            {
                MediaIds = [MediaId, secondMediaId],
                PlaceId = PlaceId,
                ExpectedPlaceRevisions = new Dictionary<Guid, int> { [MediaId] = 17 },
            }));

        Assert.Contains("최신 revision", error.Message, StringComparison.Ordinal);
        Assert.Null(repository.LastPendingAssign);
    }

    [Fact]
    public async Task PlaceCleanup_AccumulatesPagesAndAllowsRemappingAnExistingPlaceItem()
    {
        var existingPlaceId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var repository = new FakeRepository
        {
            PlaceCleanupPages =
            {
                [1] = new MemoryKeeperPendingListDto
                {
                    Items = [new MemoryKeeperPendingItemDto { FileId = FileId, PlaceRevision = 7 }],
                    Total = 2,
                    Page = 1,
                    PageSize = 1,
                },
                [2] = new MemoryKeeperPendingListDto
                {
                    Items =
                    [
                        new MemoryKeeperPendingItemDto
                        {
                            FileId = SecondFileId,
                            GpsLat = 37.5,
                            GpsLon = 127.0,
                            MemorykeeperPlaceId = existingPlaceId,
                            PlaceRevision = 8,
                        },
                    ],
                    Total = 2,
                    Page = 2,
                    PageSize = 1,
                },
            },
        };
        var service = new MemoryKeeperWriteService(repository, new CatalogInvalidation());

        var first = await service.GetPlaceCleanupMemoriesAsync(page: 1, pageSize: 1);
        var second = await service.GetPlaceCleanupMemoriesAsync(page: 2, pageSize: 1);

        Assert.True(first.HasMore);
        Assert.False(second.HasMore);
        Assert.Equal(2, second.Items.Count);
        Assert.Equal(existingPlaceId, second.Items.Single(item => item.HasGps).MemorykeeperPlaceId);
        Assert.Equal([(1, 1), (2, 1)], repository.PlaceCleanupRequests);

        await service.AssignPlaceAsync(new AssignMediaPlaceRequest
        {
            MediaIds = [BackendFileIdCodec.ToGuid(SecondFileId)],
            PlaceId = PlaceId,
        });

        Assert.Equal(8, repository.LastPendingAssign!.ExpectedRevisions[SecondFileId]);
    }

    [Fact]
    public async Task PlaceCleanup_PartialAssignmentReportsOnlySuccessfulItemsAndKeepsFailedRevision()
    {
        var repository = new FakeRepository
        {
            PlaceCleanupPages =
            {
                [1] = new MemoryKeeperPendingListDto
                {
                    Items =
                    [
                        new MemoryKeeperPendingItemDto { FileId = FileId, PlaceRevision = 7 },
                        new MemoryKeeperPendingItemDto { FileId = SecondFileId, PlaceRevision = 8 },
                    ],
                    Total = 2,
                    Page = 1,
                    PageSize = 50,
                },
            },
            PendingAssignResponse = new MemoryKeeperPendingAssignResponse
            {
                Items = [new MemoryKeeperFilePlaceUpdateApiResult { FileId = FileId, PlaceRevision = 8 }],
                AssignedCount = 1,
            },
        };
        var service = new MemoryKeeperWriteService(repository, new CatalogInvalidation());
        await service.GetPlaceCleanupMemoriesAsync();

        var result = await service.AssignPlaceAsync(new AssignMediaPlaceRequest
        {
            MediaIds = [MediaId, BackendFileIdCodec.ToGuid(SecondFileId)],
            PlaceId = PlaceId,
        });

        Assert.Equal([MediaId], result.UpdatedMediaIds);

        await service.AssignPlaceAsync(new AssignMediaPlaceRequest
        {
            MediaIds = [BackendFileIdCodec.ToGuid(SecondFileId)],
            PlaceId = PlaceId,
        });
        Assert.Equal(8, repository.LastPendingAssign!.ExpectedRevisions[SecondFileId]);
    }

    [Fact]
    public async Task PlaceCleanup_FirstPageRefreshClearsAccumulatedStaleItems()
    {
        var repository = new FakeRepository
        {
            PlaceCleanupPages =
            {
                [1] = new MemoryKeeperPendingListDto
                {
                    Items = [new MemoryKeeperPendingItemDto { FileId = FileId }],
                    Total = 2,
                    Page = 1,
                    PageSize = 1,
                },
                [2] = new MemoryKeeperPendingListDto
                {
                    Items = [new MemoryKeeperPendingItemDto { FileId = SecondFileId }],
                    Total = 2,
                    Page = 2,
                    PageSize = 1,
                },
            },
        };
        var service = new MemoryKeeperWriteService(repository, new CatalogInvalidation());
        await service.GetPlaceCleanupMemoriesAsync(page: 1, pageSize: 1);
        var accumulated = await service.GetPlaceCleanupMemoriesAsync(page: 2, pageSize: 1);
        Assert.Equal(2, accumulated.Items.Count);

        repository.PlaceCleanupPages[1] = new MemoryKeeperPendingListDto
        {
            Items = [new MemoryKeeperPendingItemDto { FileId = SecondFileId }],
            Total = 1,
            Page = 1,
            PageSize = 1,
        };
        var refreshed = await service.GetPlaceCleanupMemoriesAsync(page: 1, pageSize: 1);

        Assert.Single(refreshed.Items);
        Assert.Equal(BackendFileIdCodec.ToGuid(SecondFileId), refreshed.Items[0].MediaId);
        Assert.False(refreshed.HasMore);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public async Task PlaceCleanup_FirstPageRefreshRefillsShiftedPageBoundary(int processedCount)
    {
        var repository = new FakeRepository
        {
            PlaceCleanupPages =
            {
                [1] = CreateCleanupPage(first: 1, count: 50, total: 100, page: 1),
            },
        };
        var service = new MemoryKeeperWriteService(repository, new CatalogInvalidation());
        await service.GetPlaceCleanupMemoriesAsync(page: 1, pageSize: 50);

        repository.PlaceCleanupPages[1] = CreateCleanupPage(
            first: processedCount + 1,
            count: 50,
            total: 100 - processedCount,
            page: 1);
        var refreshed = await service.GetPlaceCleanupMemoriesAsync(page: 1, pageSize: 50);

        Assert.Equal(50, refreshed.Items.Count);
        Assert.Equal(
            BackendFileIdCodec.ToGuid(CleanupFileId(processedCount + 1)),
            refreshed.Items[0].MediaId);
        Assert.Equal(
            BackendFileIdCodec.ToGuid(CleanupFileId(processedCount + 50)),
            refreshed.Items[^1].MediaId);
        Assert.True(refreshed.HasMore);
    }

    [Fact]
    public async Task PlaceCleanup_FirstPageBelowPageSizeReportsCompleteSnapshot()
    {
        var repository = new FakeRepository
        {
            PlaceCleanupPages =
            {
                [1] = CreateCleanupPage(first: 1, count: 37, total: 37, page: 1),
            },
        };
        var service = new MemoryKeeperWriteService(repository, new CatalogInvalidation());

        var overview = await service.GetPlaceCleanupMemoriesAsync(page: 1, pageSize: 50);

        Assert.Equal(37, overview.Items.Count);
        Assert.Equal(37, overview.Total);
        Assert.False(overview.HasMore);
    }

    [Fact]
    public async Task PlaceCleanup_RefreshAfterAccumulatedPagesRestartsPaginationWithoutDuplicates()
    {
        var repository = new FakeRepository
        {
            PlaceCleanupPages =
            {
                [1] = CreateCleanupPage(first: 1, count: 50, total: 100, page: 1),
                [2] = CreateCleanupPage(first: 51, count: 50, total: 100, page: 2),
            },
        };
        var service = new MemoryKeeperWriteService(repository, new CatalogInvalidation());
        await service.GetPlaceCleanupMemoriesAsync(page: 1, pageSize: 50);
        var oldAccumulated = await service.GetPlaceCleanupMemoriesAsync(page: 2, pageSize: 50);
        Assert.Equal(100, oldAccumulated.Items.Count);

        repository.PlaceCleanupPages[1] = CreateCleanupPage(first: 11, count: 50, total: 100, page: 1);
        repository.PlaceCleanupPages[2] = CreateCleanupPage(first: 61, count: 50, total: 100, page: 2);

        var refreshed = await service.GetPlaceCleanupMemoriesAsync(page: 1, pageSize: 50);
        var newAccumulated = await service.GetPlaceCleanupMemoriesAsync(page: 2, pageSize: 50);

        Assert.Equal(50, refreshed.Items.Count);
        Assert.Equal(100, newAccumulated.Items.Count);
        Assert.Equal(100, newAccumulated.Items.Select(item => item.MediaId).Distinct().Count());
        Assert.DoesNotContain(
            BackendFileIdCodec.ToGuid(CleanupFileId(1)),
            newAccumulated.Items.Select(item => item.MediaId));
        Assert.Contains(
            BackendFileIdCodec.ToGuid(CleanupFileId(110)),
            newAccumulated.Items.Select(item => item.MediaId));
    }

    [Fact]
    public void PendingDisplayGeography_UsesRegisteredPlaceAndFallsBackPerField()
    {
        var raw = new PendingMemoryItemDto
        {
            BackendFileId = FileId,
            MediaId = MediaId,
            Country = "원시 국가",
            Province = "원시 시도",
            City = "원시 도시",
            District = "원시 구",
            RawPlaceName = "원시 장소",
            MemorykeeperPlaceId = PlaceId,
            PlaceRevision = 7,
        };
        var registeredPlace = new PlaceDto
        {
            Id = PlaceId,
            Country = "대한민국",
            City = "서울특별시",
            DisplayName = "집",
        };

        var effective = raw.WithEffectiveGeography(registeredPlace);

        Assert.Equal("대한민국", effective.Country);
        Assert.Equal("서울특별시", effective.Province);
        Assert.Equal("서울특별시", effective.City);
        Assert.Equal("원시 구", effective.District);
        Assert.Equal("집", effective.RawPlaceName);
        Assert.Equal("원시 국가", raw.Country);

        var fallback = raw.WithEffectiveGeography(new PlaceDto { Id = PlaceId });
        Assert.Equal("원시 국가", fallback.Country);
        Assert.Equal("원시 도시", fallback.Province);
        Assert.Equal("원시 도시", fallback.City);
        Assert.Equal("원시 구", fallback.District);
        Assert.Equal("원시 장소", fallback.RawPlaceName);
    }

    [Fact]
    public void PendingGroupLocation_DoesNotRepresentMixedEffectivePlacesAsTheFirstItem()
    {
        var locations = new[]
        {
            new PendingMemoryItemDto { Country = "대한민국", City = "서울특별시", RawPlaceName = "집" },
            new PendingMemoryItemDto { Country = "일본", City = "도쿄", RawPlaceName = "호텔" },
        };

        Assert.Equal("여러 장소", PendingMemoryGroupDto.GetEffectiveLocationSummary(locations));
        Assert.Equal(
            "대한민국 서울특별시 집",
            PendingMemoryGroupDto.GetEffectiveLocationSummary([locations[0], locations[0]]));
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

    private static MemoryKeeperPendingListDto CreateCleanupPage(
        int first,
        int count,
        int total,
        int page,
        int pageSize = 50) => new()
    {
        Items = Enumerable.Range(first, count)
            .Select(index => new MemoryKeeperPendingItemDto
            {
                FileId = CleanupFileId(index),
                PlaceRevision = index,
            })
            .ToList(),
        Total = total,
        Page = page,
        PageSize = pageSize,
    };

    private static string CleanupFileId(int index) =>
        $"{index:x8}{new string('a', 56)}";

    [Fact]
    public async Task FileCatalogTagRestoreAndHide_ChainMetadataRevisionAndInvalidateEveryTagSurface()
    {
        var repository = new FakeRepository();
        var invalidation = new CatalogInvalidation();
        var service = new MemoryKeeperWriteService(repository, invalidation);

        var revision = await service.RestoreFileCatalogTagAsync(MediaId, "ai:dog", expectedRevision: 3);
        revision = await service.HideFileCatalogTagAsync(MediaId, "tag:42", expectedRevision: revision);

        Assert.Equal(5, revision);
        Assert.Equal(
            [("ai:dog", 3, false), ("tag:42", 4, true)],
            repository.FileCatalogTagMutations);
        Assert.True(invalidation.Consume(CatalogSurface.Tags));
        Assert.True(invalidation.Consume(CatalogSurface.Gallery));
        Assert.True(invalidation.Consume(CatalogSurface.Home));
        Assert.True(invalidation.Consume(CatalogSurface.Travel));
    }

    [Fact]
    public async Task CatalogRename_UsesReturnedIdentity_AndInvalidatesTagSurfaces()
    {
        var repository = new FakeRepository();
        var invalidation = new CatalogInvalidation();
        var service = new MemoryKeeperWriteService(repository, invalidation);

        var renamed = await service.RenameCatalogTagAsync("ai:dog", 1, "반려동물");

        Assert.Equal("tag:123", renamed.Identity);
        Assert.Equal(("ai:dog", 1, "반려동물"), repository.CatalogRename);
        Assert.True(invalidation.Consume(CatalogSurface.Tags));
        Assert.True(invalidation.Consume(CatalogSurface.Gallery));
        Assert.True(invalidation.Consume(CatalogSurface.Home));
        Assert.True(invalidation.Consume(CatalogSurface.Travel));
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
        public Dictionary<int, MemoryKeeperPendingListDto> PlaceCleanupPages { get; } = [];
        public List<(int Page, int PageSize)> PlaceCleanupRequests { get; } = [];
        public MemoryKeeperPendingAssignResponse? PendingAssignResponse { get; set; }
        public MemoryKeeperPendingAssignRequest? LastPendingAssign { get; private set; }
        public List<(int TagId, int Revision)> FileTagMutations { get; } = [];
        public List<(string Identity, int Revision, bool Hidden)> FileCatalogTagMutations { get; } = [];
        public (string Identity, int Revision, string Name)? CatalogRename { get; private set; }

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

        public Task<MemoryKeeperPendingListDto> GetPlaceCleanupAsync(int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
        {
            PlaceCleanupRequests.Add((page, pageSize));
            return Task.FromResult(PlaceCleanupPages.TryGetValue(page, out var response)
                ? response
                : new MemoryKeeperPendingListDto { Page = page, PageSize = pageSize });
        }

        public Task<MemoryKeeperPendingAssignResponse> AssignPendingPlaceAsync(MemoryKeeperPendingAssignRequest request, CancellationToken cancellationToken = default)
        {
            LastPendingAssign = request;
            return Task.FromResult(PendingAssignResponse
                ?? new MemoryKeeperPendingAssignResponse { AssignedCount = request.FileIds.Count });
        }

        public Task<MemoryKeeperFilePlaceStateQueryResponse> QueryFilePlaceStatesAsync(MemoryKeeperFilePlaceStateQueryRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MemoryKeeperFilePlaceStateQueryResponse());

        public Task<MemoryKeeperFilesAssignPlaceResponse> AssignFilePlacesAsync(MemoryKeeperFilesAssignPlaceRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MemoryKeeperFilesAssignPlaceResponse());

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

        public Task<MemoryKeeperFileCatalogTagMutationResponse> RestoreFileCatalogTagAsync(string fileId, string identity, int expectedRevision, CancellationToken cancellationToken = default)
        {
            FileCatalogTagMutations.Add((identity, expectedRevision, false));
            return Task.FromResult(new MemoryKeeperFileCatalogTagMutationResponse
            {
                FileId = fileId,
                Identity = identity,
                Hidden = false,
                Revision = expectedRevision + 1,
            });
        }

        public Task<MemoryKeeperFileCatalogTagMutationResponse> HideFileCatalogTagAsync(string fileId, string identity, int expectedRevision, CancellationToken cancellationToken = default)
        {
            FileCatalogTagMutations.Add((identity, expectedRevision, true));
            return Task.FromResult(new MemoryKeeperFileCatalogTagMutationResponse
            {
                FileId = fileId,
                Identity = identity,
                Hidden = true,
                Revision = expectedRevision + 1,
            });
        }

        public Task<MemoryKeeperTagListDto> GetTagsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new MemoryKeeperTagListDto());
        public Task<MemoryKeeperTagCatalogListDto> GetTagCatalogAsync(string? query = null, CancellationToken cancellationToken = default) => Task.FromResult(new MemoryKeeperTagCatalogListDto());
        public Task<MemoryKeeperTagCatalogItemDto> RenameCatalogTagAsync(string identity, MemoryKeeperTagCatalogRenameRequest request, CancellationToken cancellationToken = default)
        {
            CatalogRename = (identity, request.Revision, request.Name);
            return Task.FromResult(new MemoryKeeperTagCatalogItemDto
            {
                Identity = "tag:123",
                DisplayName = request.Name,
                Revision = request.Revision + 1,
                Editable = true,
            });
        }
        public Task DeleteCatalogTagAsync(string identity, int expectedRevision, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<MemoryKeeperTagDto> CreateTagAsync(MemoryKeeperTagCreateRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MemoryKeeperTagDto> UpdateTagAsync(int tagId, MemoryKeeperTagUpdateRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteTagAsync(int tagId, int expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MemoryKeeperTagDto> MergeTagAsync(int sourceTagId, MemoryKeeperTagMergeRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
