using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Domain.Entities;

namespace MemoryKeeper.Tests.UnitTests;

public class PlaceNormalizerTests
{
    [Theory]
    [InlineData("Osaka", "오사카")]
    [InlineData("Osaka-shi", "오사카")]
    [InlineData("大阪市", "오사카")]
    [InlineData("오사카시", "오사카")]
    public void NormalizePlace_MapsOsakaVariants(string input, string expected)
    {
        Assert.Equal(expected, PlaceNormalizer.NormalizePlace(input));
        Assert.Equal(expected, PlaceNormalizer.BuildCanonicalName(input));
    }

    [Fact]
    public void Normalize_LocationResult_ProducesKoreanCanonical()
    {
        var normalized = PlaceNormalizer.Normalize(new LocationResult
        {
            DisplayName = "Osaka",
            Country = "Japan",
            City = "Osaka-shi",
            Province = "Osaka"
        });

        Assert.Equal("일본", normalized.Country);
        Assert.Equal("오사카", normalized.CanonicalName);
        Assert.Equal("오사카", normalized.DisplayName);
        Assert.Equal("오사카", normalized.City);
    }

    [Fact]
    public void CanonicalEquals_TreatsOsakaVariantsAsSame()
    {
        Assert.True(PlaceNormalizer.CanonicalEquals("Osaka", "大阪市"));
        Assert.True(PlaceNormalizer.CanonicalEquals("Osaka-shi", "오사카"));
    }

    [Fact]
    public void GetDisplayLabel_PrefersKoreanCanonical_OverJapaneseDisplayName()
    {
        var label = PlaceNormalizer.GetDisplayLabel(new Place
        {
            DisplayName = "ユニバーサル・スタジオ・ジャパン",
            CanonicalName = "유니버설 스튜디오 재팬"
        });

        Assert.Equal("유니버설 스튜디오 재팬", label);
    }

    [Fact]
    public void GetDisplayLabel_MapsHatenaBlockKatakana_ToKorean()
    {
        var label = PlaceNormalizer.GetDisplayLabel(new Place
        {
            DisplayName = "ハテナブロック",
            CanonicalName = "ハテナブロック",
            Country = "일본",
            City = "오사카"
        });

        Assert.Equal("하테나 블록", label);
        Assert.Equal("하테나 블록", PlaceNormalizer.NormalizePlace("ハテナブロック"));
    }

    [Fact]
    public void ResolveCityLabel_UsesProvince_WhenWardNameIsJapanese()
    {
        var label = PlaceNormalizer.ResolveCityLabel(new Place
        {
            City = "此花区",
            Province = "大阪府",
            Country = "日本"
        });

        Assert.Equal("고노하나", label);
    }

    [Fact]
    public void NormalizePlace_MapsUniversalStudiosJapan()
    {
        Assert.Equal("유니버설 스튜디오 재팬", PlaceNormalizer.NormalizePlace("ユニバーサル・スタジオ・ジャパン"));
        Assert.Equal("유니버설 스튜디오 재팬", PlaceNormalizer.NormalizePlace("Universal Studios Japan"));
    }
}
