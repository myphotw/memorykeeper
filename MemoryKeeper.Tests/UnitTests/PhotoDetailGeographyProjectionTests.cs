using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Services;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class PhotoDetailGeographyProjectionTests
{
    [Fact]
    public void RawPhotoGeography_RemainsAuthoritative()
    {
        var photo = new PhotoDetailDto
        {
            Country = "대한민국",
            Province = "전라남도",
            City = "구례군",
            District = "토지면",
            Address = "사진 원본 주소",
        };
        var place = Place(
            country: "Korea",
            province: "다른 도",
            city: "다른 시",
            district: "다른 구",
            address: "장소 주소");

        var result = PhotoDetailGeographyProjection.Resolve(photo, place);

        Assert.Equal("대한민국", result.Country);
        Assert.Equal("전라남도", result.Province);
        Assert.Equal("구례군", result.City);
        Assert.Equal("토지면", result.District);
        Assert.Equal("사진 원본 주소", result.Address);
    }

    [Fact]
    public void RegisteredPlace_FillsOnlyMissingPhotoGeography()
    {
        var photo = new PhotoDetailDto
        {
            Country = "대한민국",
            Province = string.Empty,
            City = string.Empty,
            District = string.Empty,
        };
        var place = Place(
            country: "Korea",
            province: "전라남도",
            city: "구례군",
            district: "토지면",
            address: "전라남도 구례군 토지면");

        var result = PhotoDetailGeographyProjection.Resolve(photo, place);

        Assert.Equal("대한민국", result.Country);
        Assert.Equal("전라남도", result.Province);
        Assert.Equal("구례군", result.City);
        Assert.Equal("토지면", result.District);
        Assert.Equal("전라남도 구례군 토지면", result.Address);
    }

    [Fact]
    public void MissingPhotoAndPlaceGeography_RemainsEmptyForUiDash()
    {
        var result = PhotoDetailGeographyProjection.Resolve(new PhotoDetailDto(), null);

        Assert.Empty(result.Country);
        Assert.Empty(result.Province);
        Assert.Empty(result.City);
        Assert.Empty(result.District);
        Assert.Empty(result.Address);
    }

    private static PlaceDto Place(
        string country,
        string province,
        string city,
        string district,
        string address) => new()
    {
        Country = country,
        Province = province,
        City = city,
        District = district,
        Address = address,
    };
}
