using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Tests.UnitTests;

public class PlaceLocationPreviewTests
{
    [Fact]
    public void CanApply_EmptySelection_IsFalse()
    {
        Assert.False(PlaceLocationPreview.CanApply(PlaceLocationPreview.Empty, PlaceLocationPreview.Empty));
        Assert.False(PlaceLocationPreview.CanApply(null, null));
    }

    [Fact]
    public void CanApply_SamePlaceId_IsFalse()
    {
        var placeId = Guid.NewGuid();
        var original = new PlaceLocationPreview
        {
            PlaceId = placeId,
            DisplayName = "오사카",
            Latitude = 34.6,
            Longitude = 135.5,
            Source = PlaceLocationSource.Original
        };
        var selected = new PlaceLocationPreview
        {
            PlaceId = placeId,
            DisplayName = "오사카",
            Latitude = 34.6,
            Longitude = 135.5,
            Source = PlaceLocationSource.Existing
        };

        Assert.False(PlaceLocationPreview.CanApply(original, selected));
    }

    [Fact]
    public void CanApply_DifferentPlace_IsTrue()
    {
        var original = new PlaceLocationPreview
        {
            PlaceId = Guid.NewGuid(),
            DisplayName = "오사카",
            Source = PlaceLocationSource.Original
        };
        var selected = new PlaceLocationPreview
        {
            PlaceId = Guid.NewGuid(),
            DisplayName = "유니버설 스튜디오 재팬",
            Country = "일본",
            Province = "오사카부",
            City = "오사카시",
            Latitude = 34.665442,
            Longitude = 135.432512,
            RadiusMeters = 100,
            Source = PlaceLocationSource.Existing
        };

        Assert.True(PlaceLocationPreview.CanApply(original, selected));
    }

    [Fact]
    public void CanApply_SameGooglePlaceId_IsFalse()
    {
        var original = new PlaceLocationPreview
        {
            GooglePlaceId = "ChIJ_same",
            DisplayName = "A",
            Source = PlaceLocationSource.Original
        };
        var selected = new PlaceLocationPreview
        {
            GooglePlaceId = "ChIJ_same",
            DisplayName = "A (updated label)",
            Source = PlaceLocationSource.Google
        };

        Assert.False(PlaceLocationPreview.CanApply(original, selected));
    }

    [Fact]
    public void CanApply_FromEmptyToSelection_IsTrue()
    {
        var selected = PlaceLocationPreview.FromMapPick(4.1, 73.4, 100);

        Assert.True(PlaceLocationPreview.CanApply(PlaceLocationPreview.Empty, selected));
    }

    [Fact]
    public void CoordinatesText_UsesSixDecimals()
    {
        var preview = new PlaceLocationPreview
        {
            DisplayName = "USJ",
            Latitude = 34.665442,
            Longitude = 135.432512
        };

        Assert.Equal("34.665442", preview.LatitudeText);
        Assert.Equal("135.432512", preview.LongitudeText);
        Assert.Contains("34.665442", preview.CoordinatesText, StringComparison.Ordinal);
        Assert.Contains("135.432512", preview.CoordinatesText, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_ShowsNoLocationMessage()
    {
        Assert.True(PlaceLocationPreview.Empty.IsEmpty);
        Assert.Equal("위치정보 없음", PlaceLocationPreview.Empty.CoordinatesText);
    }

    [Fact]
    public void FromPlaceDto_MapsFields()
    {
        var dto = new PlaceDto
        {
            Id = Guid.NewGuid(),
            DisplayName = "유니버설 스튜디오 재팬",
            Country = "일본",
            Province = "오사카부",
            City = "오사카시",
            Latitude = 34.665442,
            Longitude = 135.432512,
            Radius = 100,
            GooglePlaceId = "ChIJ_usj"
        };

        var preview = PlaceLocationPreview.FromPlaceDto(dto, PlaceLocationSource.Original);

        Assert.Equal(dto.Id, preview.PlaceId);
        Assert.Equal("유니버설 스튜디오 재팬", preview.DisplayName);
        Assert.Equal("일본", preview.Country);
        Assert.Equal("오사카부", preview.Province);
        Assert.Equal("오사카시", preview.City);
        Assert.Equal(100, preview.RadiusMeters);
        Assert.Equal("100m", preview.RadiusText);
    }
}
