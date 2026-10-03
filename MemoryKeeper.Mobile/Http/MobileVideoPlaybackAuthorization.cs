using MemoryKeeper.Mobile.Configuration;

namespace MemoryKeeper.Mobile.Http;

public static class MobileVideoPlaybackAuthorization
{
    public static IDictionary<string, string> CreateRequestHeaders(
        IMobileBackendConfiguration configuration,
        Uri selectedBaseUri,
        Uri mediaUri)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(selectedBaseUri);
        ArgumentNullException.ThrowIfNull(mediaUri);

        if (!MobileBackendAuthenticationHandler.IsConfiguredBackendOrigin(
                selectedBaseUri,
                configuration)
            || !MobileBackendAuthenticationHandler.RequiresBearer(mediaUri, selectedBaseUri)
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
