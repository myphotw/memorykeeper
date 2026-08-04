using MemoryKeeper.Application;

namespace MemoryKeeper.Tests.UnitTests;

public class PlaceTypeCatalogTests
{
    [Fact]
    public void GetPriorityRank_PrefersTouristAttractionOverLocality()
    {
        var attraction = PlaceTypeCatalog.GetPriorityRank(["tourist_attraction", "point_of_interest"]);
        var locality = PlaceTypeCatalog.GetPriorityRank(["locality", "political"]);
        Assert.True(attraction < locality);
    }

    [Fact]
    public void IsVisitPoi_TrueForAmusementPark_FalseForLocality()
    {
        Assert.True(PlaceTypeCatalog.IsVisitPoi(["amusement_park"]));
        Assert.False(PlaceTypeCatalog.IsVisitPoi(["locality"]));
    }

    [Fact]
    public void GetIcon_ReturnsExpectedGlyphs()
    {
        Assert.Equal("🎡", PlaceTypeCatalog.GetIcon("tourist_attraction"));
        Assert.Equal("🌲", PlaceTypeCatalog.GetIcon("park"));
        Assert.Equal("✈", PlaceTypeCatalog.GetIcon("airport"));
        Assert.Equal("📍", PlaceTypeCatalog.GetIcon(null));
    }
}
