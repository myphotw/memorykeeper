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
    public const string DevelopmentConfigurationFileName = "local.env";

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

#if DEBUG
        var development = ReadDevelopmentCredential(deploymentDirectory ?? AppContext.BaseDirectory);
        if (development is not null)
        {
            return development;
        }
#endif

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

    /// <summary>
    /// Development-only fallback matching the existing AstroJournal local.env convention.
    /// The file is ignored by Git and is never copied into publish output.
    /// </summary>
    public static string? ReadDevelopmentCredential(string startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
        {
            return null;
        }

        try
        {
            var directory = new DirectoryInfo(startDirectory);
            while (directory is not null)
            {
                var path = Path.Combine(
                    directory.FullName,
                    "config",
                    DevelopmentConfigurationFileName);
                if (File.Exists(path))
                {
                    return ReadEnvironmentValue(path, EnvironmentVariable);
                }

                directory = directory.Parent;
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    private static string? ReadEnvironmentValue(string path, string name)
    {
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separator = trimmed.IndexOf('=');
            if (separator <= 0
                || !string.Equals(trimmed[..separator].Trim(), name, StringComparison.Ordinal))
            {
                continue;
            }

            var value = trimmed[(separator + 1)..].Trim();
            if (value.Length >= 2
                && ((value[0] == '"' && value[^1] == '"')
                    || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1].Trim();
            }

            return GoogleMapsApiKeyValidator.NormalizeOrNull(value);
        }

        return null;
    }
}
