using MemoryKeeper.Application;

namespace MemoryKeeper.Tests.UnitTests;

public class PlaceIdentityTests
{
    [Fact]
    public void MapStableId_MatchesNullCountryCityKey()
    {
        var a = PlaceIdentity.MapStableId("강릉");
        var b = PlaceIdentity.StableId(null, null, "강릉");
        var c = PlaceIdentity.StableId("대한민국", "강원", "강릉");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void HasValidCoordinates_RejectsNullIsland()
    {
        Assert.False(PlaceIdentity.HasValidCoordinates(0, 0));
        Assert.True(PlaceIdentity.HasValidCoordinates(37.751, 128.876));
    }

    [Fact]
    public void ResolveCoordinates_PrefersRepresentativeThenFirstValid()
    {
        var resolved = PlaceIdentity.ResolveCoordinates(
            (0, 0),
            [(37.5, 127.0), (37.6, 127.1)]);

        Assert.NotNull(resolved);
        Assert.Equal(37.5, resolved!.Value.Latitude);
        Assert.Equal(127.0, resolved.Value.Longitude);

        var withRep = PlaceIdentity.ResolveCoordinates(
            (35.0, 135.0),
            [(37.5, 127.0)]);

        Assert.Equal(35.0, withRep!.Value.Latitude);
        Assert.Equal(135.0, withRep.Value.Longitude);
    }

    [Fact]
    public void MapPlaceKey_NormalizesWhitespaceAndCase()
    {
        Assert.Equal(
            PlaceIdentity.MapPlaceKey("  Gangneung "),
            PlaceIdentity.MapPlaceKey("gangneung"));
    }
}
