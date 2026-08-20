using MemoryKeeper.Application.Interfaces;

namespace MemoryKeeper.Application;

/// <summary>
/// Resolves the map-rendering key provisioned by the deployment pipeline.
/// The legacy local setting remains read-only compatibility fallback only.
/// </summary>
public static class MapDisplayCredentialProvider
{
    public const string EnvironmentVariable = "MEMORYKEEPER_GOOGLE_MAPS_JAVASCRIPT_API_KEY";
    public const string DeploymentFileName = "MemoryKeeper.maps.key";

    public static async Task<string?> GetAsync(
        ISettingRepository settingRepository,
        CancellationToken cancellationToken = default,
        string? deploymentDirectory = null)
    {
        var deployed = ReadDeploymentCredential(deploymentDirectory ?? AppContext.BaseDirectory);
        if (deployed is not null)
        {
            return deployed;
        }

        var environment = GoogleMapsApiKeyValidator.NormalizeOrNull(
            Environment.GetEnvironmentVariable(EnvironmentVariable));
        if (environment is not null)
        {
            return environment;
        }

        var legacy = await settingRepository.GetByKeyAsync(SettingKeys.GoogleMapsApiKey, cancellationToken);
        return GoogleMapsApiKeyValidator.NormalizeOrNull(legacy?.Value);
    }

    public static string? ReadDeploymentCredential(string deploymentDirectory)
    {
        if (string.IsNullOrWhiteSpace(deploymentDirectory))
        {
            return null;
        }

        try
        {
            var path = Path.Combine(deploymentDirectory, DeploymentFileName);
            return File.Exists(path)
                ? GoogleMapsApiKeyValidator.NormalizeOrNull(File.ReadAllText(path))
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
