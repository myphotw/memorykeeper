using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class MemoryKeeperPlaceServiceTests
{
    [Fact]
    public async Task GeometryChange_WithoutOverlap_PatchesThenReclassifiesWithoutPrompt()
    {
        var fake = new FakeRepository();
        var invalidation = new CatalogInvalidation();
        var service = new MemoryKeeperPlaceService(fake, invalidation);
        var original = Place(radius: 100);
        fake.UpdatedPlace = ApiPlace(original, radius: 250, revision: 4);
        fake.ReclassifyResult = Reclassify(original.Id, assigned: 2, reassigned: 1);
        var promptCalls = 0;

        var result = await service.UpdateWithRadiusImpactAsync(
            original,
            Update(original, radius: 250),
            (_, _) =>
            {
                promptCalls++;
                return Task.FromResult(true);
            });

        Assert.False(result.Cancelled);
        Assert.True(result.GeometryChanged);
        Assert.Equal(0, promptCalls);
        Assert.Equal(["impact", "patch", "reclass:true"], fake.Calls);
        Assert.Equal(1, result.Reclassification.ReassignedFromOtherCount);
        Assert.True(invalidation.Consume(CatalogSurface.Gallery));
    }

    [Fact]
    public async Task GeometryChange_WithOverlap_CancelDoesNotPatchOrInvalidate()
    {
        var fake = new FakeRepository { Impact = ImpactWithOverlap() };
        var invalidation = new CatalogInvalidation();
        var service = new MemoryKeeperPlaceService(fake, invalidation);
        var original = Place(radius: 100);

        var result = await service.UpdateWithRadiusImpactAsync(
            original,
            Update(original, radius: 250),
            (_, _) => Task.FromResult(false));

        Assert.True(result.Cancelled);
        Assert.Equal(["impact"], fake.Calls);
        Assert.False(invalidation.Consume(CatalogSurface.Gallery));
    }

    [Fact]
    public async Task GeometryChange_WithOverlap_ConfirmPatchesThenReclassifiesWithReassign()
    {
        var fake = new FakeRepository { Impact = ImpactWithOverlap() };
        var invalidation = new CatalogInvalidation();
        var service = new MemoryKeeperPlaceService(fake, invalidation);
        var original = Place(radius: 100);
        fake.UpdatedPlace = ApiPlace(original, radius: 250, revision: 4);

        var result = await service.UpdateWithRadiusImpactAsync(
            original,
            Update(original, radius: 250),
            (impact, _) => Task.FromResult(impact.OverlappingPlaces.Count == 1));

        Assert.False(result.Cancelled);
        Assert.Equal(["impact", "patch", "reclass:true"], fake.Calls);
        Assert.True(fake.LastReassignFromOtherPlaces);
        Assert.True(invalidation.Consume(CatalogSurface.Travel));
    }

    [Fact]
    public async Task DisplayNameOnly_PatchesWithoutImpactOrReclassification_AndKeepsUuid()
    {
        var fake = new FakeRepository();
        var invalidation = new CatalogInvalidation();
        var service = new MemoryKeeperPlaceService(fake, invalidation);
        var original = Place(radius: 100);
        fake.UpdatedPlace = ApiPlace(original, displayName: "지리산 피아골", revision: 4);
        var request = Update(original, radius: original.Radius, displayName: "지리산 피아골");

        var result = await service.UpdateWithRadiusImpactAsync(
            original,
            request,
            (_, _) => throw new InvalidOperationException("이름 변경에는 확인 UI가 호출되면 안 됩니다."));

        Assert.Equal(["patch"], fake.Calls);
        Assert.Equal(original.Id, result.UpdatedPlace!.Id);
        Assert.Equal("지리산 피아골", result.UpdatedPlace.DisplayName);
        Assert.Equal("지리산 피아골", fake.LastUpdate!.DisplayName);
        Assert.True(invalidation.Consume(CatalogSurface.Visits));
    }

    [Fact]
    public async Task GeometryChangeWhileDeactivating_RetainsRelationsBySkippingBackendReclassify()
    {
        var fake = new FakeRepository();
        var service = new MemoryKeeperPlaceService(fake, new CatalogInvalidation());
        var original = Place(radius: 100);
        fake.UpdatedPlace = ApiPlace(original, radius: 250, revision: 4, active: false);
        var request = Update(original, radius: 250, active: false);

        var result = await service.UpdateWithRadiusImpactAsync(
            original,
            request,
            (_, _) => Task.FromResult(true));

        Assert.Equal(["impact", "patch"], fake.Calls);
        Assert.True(result.ReclassificationSkippedBecauseInactive);
    }

    [Fact]
    public async Task BackendAutoCreatedPlace_IsReturnedByNasListWithoutLocalMerge()
    {
        var fake = new FakeRepository();
        var auto = ApiPlace(Place(), displayName: "피아골", revision: 1);
        fake.List = new MemoryKeeperPlaceListApiDto { Items = [auto], Total = 1 };
        var service = new MemoryKeeperPlaceService(fake, new CatalogInvalidation());

        var places = await service.GetPlaceListAsync();

        var place = Assert.Single(places);
        Assert.Equal(auto.Id, place.Id);
        Assert.Equal("피아골", place.DisplayName);
        Assert.Equal(["list"], fake.Calls);
    }

    [Fact]
    public async Task CreatePlace_CompletesMissingProviderGeographyFromRawPhotoWithoutOverwritingCandidate()
    {
        var fake = new FakeRepository();
        var service = new MemoryKeeperPlaceService(fake, new CatalogInvalidation());
        fake.CreatedPlace = ApiPlace(Place(), displayName: "Oakwood Premier Coex Center Seoul");

        await service.CreatePlaceAsync(new CreatePlaceRequest
        {
            DisplayName = "Oakwood Premier Coex Center Seoul",
            Country = string.Empty,
            Province = string.Empty,
            City = "강남구",
            Latitude = 37.51,
            Longitude = 127.05,
        }, new PlaceGeographyFallback
        {
            Country = "대한민국",
            Province = "서울특별시",
            City = "서울특별시",
            District = "삼성동",
            Address = "대한민국 서울특별시 강남구",
        });

        Assert.Equal("대한민국", fake.LastCreate!.Country);
        Assert.Equal("서울특별시", fake.LastCreate.Province);
        Assert.Equal("강남구", fake.LastCreate.City);
        Assert.Equal("삼성동", fake.LastCreate.District);
        Assert.Equal("대한민국 서울특별시 강남구", fake.LastCreate.Address);
    }

    private static PlaceDto Place(double radius = 100) => new()
    {
        Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        DisplayName = "피아골",
        CanonicalName = "지리산 피아골",
        Address = "대한민국 전라남도 구례군 토지면",
        Country = "대한민국",
        Province = "전라남도",
        City = "구례군",
        District = "토지면",
        Latitude = 35.22742,
        Longitude = 127.59052,
        Radius = radius,
        IsActive = true,
        Revision = 3,
    };

    private static UpdatePlaceRequest Update(
        PlaceDto place,
        double radius,
        string? displayName = null,
        bool active = true) => new()
    {
        Id = place.Id,
        Revision = place.Revision,
        DisplayName = displayName ?? place.DisplayName,
        CanonicalName = place.CanonicalName,
        Address = place.Address,
        Country = place.Country,
        Province = place.Province,
        City = place.City,
        District = place.District,
        Latitude = place.Latitude,
        Longitude = place.Longitude,
        Radius = radius,
        IsActive = active,
        IsFavorite = place.IsFavorite,
    };

    private static MemoryKeeperPlaceApiDto ApiPlace(
        PlaceDto place,
        double? radius = null,
        string? displayName = null,
        int revision = 3,
        bool active = true) => new()
    {
        Id = place.Id,
        DisplayName = displayName ?? place.DisplayName,
        CanonicalName = place.CanonicalName,
        Address = place.Address,
        Country = place.Country,
        Province = place.Province,
        City = place.City,
        District = place.District,
        Latitude = place.Latitude,
        Longitude = place.Longitude,
        RadiusM = radius ?? place.Radius,
        Active = active,
        Revision = revision,
    };

    private static MemoryKeeperPlaceReclassifyApiResult Reclassify(Guid placeId, int assigned, int reassigned) => new()
    {
        PlaceId = placeId,
        Assigned = assigned,
        Reassigned = reassigned,
    };

    private static MemoryKeeperRadiusImpactApiResult ImpactWithOverlap()
    {
        var other = Place();
        return new MemoryKeeperRadiusImpactApiResult
        {
            MatchedFileCount = 2,
            AffectedFileIds = ["file-a", "file-b"],
            OverlappingPlaces =
            [
                new MemoryKeeperRadiusOverlapApiDto
                {
                    Place = ApiPlace(other, displayName: "원기교"),
                    CenterDistanceM = 80,
                },
            ],
        };
    }

    private sealed class FakeRepository : IMemoryKeeperPlaceApiRepository
    {
        public List<string> Calls { get; } = [];
        public MemoryKeeperPlaceListApiDto List { get; set; } = new();
        public MemoryKeeperRadiusImpactApiResult Impact { get; set; } = new();
        public MemoryKeeperPlaceApiDto? UpdatedPlace { get; set; }
        public MemoryKeeperPlaceApiDto? CreatedPlace { get; set; }
        public MemoryKeeperPlaceReclassifyApiResult ReclassifyResult { get; set; } = new();
        public MemoryKeeperPlaceUpdateApiRequest? LastUpdate { get; private set; }
        public MemoryKeeperPlaceCreateApiRequest? LastCreate { get; private set; }
        public bool LastReassignFromOtherPlaces { get; private set; }

        public Task<MemoryKeeperPlaceListApiDto> GetPlacesAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("list");
            return Task.FromResult(List);
        }

        public Task<MemoryKeeperPlaceApiDto> UpdatePlaceAsync(Guid placeId, MemoryKeeperPlaceUpdateApiRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add("patch");
            LastUpdate = request;
            return Task.FromResult(UpdatedPlace ?? ApiPlace(Place(), revision: request.Revision + 1));
        }

        public Task<MemoryKeeperPlaceReclassifyApiResult> ReclassifyAsync(Guid placeId, bool reassignFromOtherPlaces, CancellationToken cancellationToken = default)
        {
            Calls.Add($"reclass:{reassignFromOtherPlaces.ToString().ToLowerInvariant()}");
            LastReassignFromOtherPlaces = reassignFromOtherPlaces;
            return Task.FromResult(ReclassifyResult.PlaceId == Guid.Empty
                ? Reclassify(placeId, assigned: 0, reassigned: 0)
                : ReclassifyResult);
        }

        public Task<MemoryKeeperRadiusImpactApiResult> GetRadiusImpactAsync(MemoryKeeperRadiusImpactApiRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add("impact");
            return Task.FromResult(Impact);
        }

        public Task<MemoryKeeperPlaceApiDto> GetPlaceAsync(Guid placeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MemoryKeeperPlaceApiDto> CreatePlaceAsync(MemoryKeeperPlaceCreateApiRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add("create");
            LastCreate = request;
            return Task.FromResult(CreatedPlace ?? ApiPlace(Place()));
        }
        public Task DeletePlaceAsync(Guid placeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MemoryKeeperPlaceMatchApiResult> MatchAsync(MemoryKeeperPlaceMatchApiRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MemoryKeeperFilePlaceUpdateApiResult> AssignFilePlaceAsync(string fileId, Guid? placeId, int expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
