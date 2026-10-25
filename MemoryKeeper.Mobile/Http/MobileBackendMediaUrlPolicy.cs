using MemoryKeeper.Mobile.Configuration;

namespace MemoryKeeper.Mobile.Http;

internal static class MobileBackendMediaUrlPolicy
{
    public static bool CanUseConfiguredApiUrl(
        string? pathOrUrl,
        IMobileBackendConfiguration configuration)
    {
        if (!Uri.TryCreate(pathOrUrl?.Trim(), UriKind.Absolute, out var absolute)
            || (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps))
        {
            return true;
        }

        var path = absolute.AbsolutePath;
        var isProtectedApiPath = path.Equals("/api", StringComparison.OrdinalIgnoreCase)
                                 || path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);
        return !isProtectedApiPath
               || MobileBackendAuthenticationHandler.IsConfiguredBackendOrigin(
                   absolute,
                   configuration);
    }
}
