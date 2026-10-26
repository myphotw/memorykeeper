using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class PlaceLinkedPhotoCountPolicyTests
{
    [Fact]
    public void Project_UsesHierarchyPhotoCountInsteadOfPlaceUsageCount()
    {
        var placeId = Guid.NewGuid();
        var place = new PlaceDto
        {
            Id = placeId,
            UsageCount = 1,
            MediaCount = 1,
        };
        var counts = PlaceLinkedPhotoCountPolicy.Project(
        [
            Path(placeId, photoCount: 0),
        ]);

        Assert.True(counts.TryGetValue(placeId, out var linkedPhotoCount));
        Assert.Equal(0, linkedPhotoCount);
        Assert.NotEqual(place.UsageCount, linkedPhotoCount);
    }

    [Fact]
    public void Project_SumsCanonicalPathsForTheSamePlaceId()
    {
        var placeId = Guid.NewGuid();
        var counts = PlaceLinkedPhotoCountPolicy.Project(
        [
            Path(placeId, photoCount: 11, locationKey: "registered:male"),
            Path(placeId, photoCount: 1, locationKey: "registered:lot"),
        ]);

        Assert.Equal(12, counts[placeId]);
    }

    [Fact]
    public void Project_DoesNotDoubleCountTheSameCanonicalPath()
    {
        var placeId = Guid.NewGuid();
        var path = Path(placeId, photoCount: 3, locationKey: "registered:path");

        var counts = PlaceLinkedPhotoCountPolicy.Project([path, path]);

        Assert.Equal(3, counts[placeId]);
    }

    [Fact]
    public void Project_NormalizesCanonicalKeyCaseAndOuterWhitespaceForDeduplication()
    {
        var placeId = Guid.NewGuid();

        var counts = PlaceLinkedPhotoCountPolicy.Project(
        [
            Path(placeId, photoCount: 3, locationKey: " registered:path "),
            Path(placeId, photoCount: 3, locationKey: "REGISTERED:PATH"),
        ]);

        Assert.Equal(3, counts[placeId]);
    }

    [Fact]
    public void Project_UsesFullPathWhenLocationKeyIsUnavailable()
    {
        var placeId = Guid.NewGuid();

        var counts = PlaceLinkedPhotoCountPolicy.Project(
        [
            Path(placeId, photoCount: 4, fullPath: "대한민국 > 가평군 > 장소 A"),
            Path(placeId, photoCount: 2, fullPath: "대한민국 > 가평군 > 장소 B"),
        ]);

        Assert.Equal(6, counts[placeId]);
    }

    [Fact]
    public void Project_KeepsDifferentPlaceIdsIndependentEvenWhenPathKeysMatch()
    {
        var firstPlaceId = Guid.NewGuid();
        var secondPlaceId = Guid.NewGuid();

        var counts = PlaceLinkedPhotoCountPolicy.Project(
        [
            Path(firstPlaceId, photoCount: 5, locationKey: "registered:shared"),
            Path(secondPlaceId, photoCount: 7, locationKey: "registered:shared"),
        ]);

        Assert.Equal(5, counts[firstPlaceId]);
        Assert.Equal(7, counts[secondPlaceId]);
    }

    [Theory]
    [InlineData(null, PlaceLinkedPhotoCountFilter.All, true)]
    [InlineData(null, PlaceLinkedPhotoCountFilter.NoPhotos, false)]
    [InlineData(null, PlaceLinkedPhotoCountFilter.HasPhotos, false)]
    [InlineData(0, PlaceLinkedPhotoCountFilter.NoPhotos, true)]
    [InlineData(0, PlaceLinkedPhotoCountFilter.HasPhotos, false)]
    [InlineData(1, PlaceLinkedPhotoCountFilter.HasPhotos, true)]
    [InlineData(1, PlaceLinkedPhotoCountFilter.NoPhotos, false)]
    public void Matches_DoesNotClassifyUnknownCountsAsZero(
        int? linkedPhotoCount,
        PlaceLinkedPhotoCountFilter filter,
        bool expected)
    {
        Assert.Equal(expected, PlaceLinkedPhotoCountPolicy.Matches(linkedPhotoCount, filter));
    }

    [Fact]
    public void Format_DistinguishesZeroFromUnavailable()
    {
        Assert.Equal("사진 0장", PlaceLinkedPhotoCountPolicy.Format(0));
        Assert.Equal("사진 수 확인 불가", PlaceLinkedPhotoCountPolicy.Format(null));
    }

    private static MemoryKeeperPlaceHierarchyPathApiDto Path(
        Guid placeId,
        int photoCount,
        string? locationKey = null,
        string? fullPath = null) => new()
    {
        MemorykeeperPlaceId = placeId,
        PhotoCount = photoCount,
        LocationKey = locationKey,
        FullPath = fullPath,
    };
}
