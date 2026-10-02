using MemoryKeeper.Mobile.Configuration;

namespace MemoryKeeper.Mobile.Http;

public static class MobileVideoPlaybackAuthorization
{
    public static IDictionary<string, string> CreateRequestHeaders(
        IMobileBackendConfiguration configuration,
        Uri mediaUri)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(mediaUri);

        if (configuration.BaseUri is null
            || !MobileBackendAuthenticationHandler.RequiresBearer(mediaUri, configuration.BaseUri)
            || string.IsNullOrWhiteSpace(configuration.BearerToken))
        {
            throw new MobileBackendConfigurationException();
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = $"Bearer {configuration.BearerToken}",
        };
    }
}
