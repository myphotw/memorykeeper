namespace MemoryKeeper.Infrastructure.Services.Api;

public static class TcBackendRequestPolicy
{
    public static Uri ResolveUri(string pathOrUrl, string apiBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            throw new InvalidOperationException("TcBackend:ApiBaseUrl is not configured.");
        }

        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var absolute))
        {
            return absolute;
        }

        var baseUri = new Uri(apiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var relative = string.IsNullOrWhiteSpace(pathOrUrl)
            ? string.Empty
            : pathOrUrl.TrimStart('/');
        return new Uri(baseUri, relative);
    }

    public static bool IsSameOrigin(Uri requestUri, string apiBaseUrl)
    {
        if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var backendUri))
        {
            return false;
        }

        return string.Equals(requestUri.Scheme, backendUri.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(requestUri.Host, backendUri.Host, StringComparison.OrdinalIgnoreCase)
            && requestUri.Port == backendUri.Port;
    }

    public static bool RequiresBearer(Uri requestUri, string apiBaseUrl)
    {
        if (!IsSameOrigin(requestUri, apiBaseUrl))
        {
            return false;
        }

        var path = requestUri.AbsolutePath;
        return path.Equals("/api", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);
    }
}
