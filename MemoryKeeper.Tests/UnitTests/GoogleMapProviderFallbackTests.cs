namespace MemoryKeeper.Tests.UnitTests;

public sealed class GoogleMapProviderFallbackTests
{
    [Fact]
    public void ConfiguredCredential_SelectsGoogle_AndFailureRetainsOsmFallback()
    {
        var builder = File.ReadAllText(FindSourceFile(
            "MemoryKeeper.App", "Maps", "Google", "GoogleMapHtmlBuilder.cs"));
        var controller = File.ReadAllText(FindSourceFile(
            "MemoryKeeper.App", "Maps", "Google", "GoogleMapController.cs"));

        Assert.Contains("https://maps.googleapis.com/maps/api/js?key=", builder, StringComparison.Ordinal);
        Assert.Contains("return OpenStreetMapHtmlBuilder.Build();", builder, StringComparison.Ordinal);
        Assert.Contains("GoogleMapsApiKeyValidator.NormalizeOrNull(apiKey) is not null", controller, StringComparison.Ordinal);
        Assert.Contains("await ReloadHtmlAsync(apiKey: null, cancellationToken);", controller, StringComparison.Ordinal);
        Assert.Contains("Google Maps authentication failed (gm_authFailure).", builder, StringComparison.Ordinal);
    }

    private static string FindSourceFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Source file was not found: {Path.Combine(parts)}");
    }
}
