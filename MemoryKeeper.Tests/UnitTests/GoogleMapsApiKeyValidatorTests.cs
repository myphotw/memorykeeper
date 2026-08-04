using MemoryKeeper.Application;

namespace MemoryKeeper.Tests.UnitTests;

public class GoogleMapsApiKeyValidatorTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("MK-043A 사진첩 UI/UX 개선", false)]
    [InlineData("not-a-key", false)]
    [InlineData("AIza short", false)]
    [InlineData("AIzaSyDummyKeyForUnitTest0123456789Ab", true)]
    public void LooksValid_MatchesExpected(string? key, bool expected)
    {
        Assert.Equal(expected, GoogleMapsApiKeyValidator.LooksValid(key));
    }

    [Fact]
    public void EnsureValidOrEmpty_AllowsEmpty_RejectsGarbage()
    {
        GoogleMapsApiKeyValidator.EnsureValidOrEmpty(null);
        GoogleMapsApiKeyValidator.EnsureValidOrEmpty("");
        Assert.Throws<InvalidOperationException>(() =>
            GoogleMapsApiKeyValidator.EnsureValidOrEmpty("MK-043A ticket title"));
    }
}
