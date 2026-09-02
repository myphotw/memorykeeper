namespace MemoryKeeper.Application;

/// <summary>Builds client-neutral absolute media URLs and the established authenticated thumbnail fallback.</summary>
public static class BackendMediaUrlResolver
{
    /// <summary>Returns a query-free, token-free description suitable for bounded diagnostics.</summary>
    public static string DescribeForDiagnostics(string apiBaseUrl, string? pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
        {
            return "missing";
        }

        var trimmed = pathOrUrl.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute)
            || (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps))
        {
            return $"relative:{SafePath(trimmed)}";
        }

        var configured = Uri.TryCreate(apiBaseUrl?.TrimEnd('/'), UriKind.Absolute, out var configuredOrigin);
        var sameOrigin = configured
                         && string.Equals(absolute.Scheme, configuredOrigin!.Scheme, StringComparison.OrdinalIgnoreCase)
                         && string.Equals(absolute.Host, configuredOrigin.Host, StringComparison.OrdinalIgnoreCase)
                         && absolute.Port == configuredOrigin.Port;
        var apiPath = IsProtectedApiPath(absolute.AbsolutePath);
        var originKind = sameOrigin
            ? "backend"
            : apiPath && System.Net.IPAddress.TryParse(absolute.Host, out _)
                ? "nas-ip-api"
                : apiPath ? "foreign-api" : "external";
        return $"{originKind}:{SafePath(absolute.AbsolutePath)}";
    }

    public static string? ToAbsoluteUrl(string apiBaseUrl, string? pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
        {
            return null;
        }

        var trimmed = pathOrUrl.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            // Fast endpoints can return a NAS/private-host absolute URL for a protected
            // /api media route.  Requests to that different origin intentionally do not
            // receive the Backend bearer token, so retain the path but use the configured
            // Backend origin.  Genuine external/CDN URLs remain untouched.
            if (IsProtectedApiPath(absolute.AbsolutePath))
            {
                var configuredBase = (apiBaseUrl ?? string.Empty).TrimEnd('/');
                if (Uri.TryCreate(configuredBase, UriKind.Absolute, out var configuredOrigin))
                {
                    var authority = configuredOrigin.GetLeftPart(UriPartial.Authority);
                    return authority + absolute.PathAndQuery;
                }
            }

            return absolute.ToString();
        }

        var baseUrl = (apiBaseUrl ?? string.Empty).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
        }

        return trimmed.StartsWith('/')
            ? baseUrl + trimmed
            : baseUrl + "/" + trimmed;
    }

    public static string? ResolveThumbnailUrl(
        string apiBaseUrl,
        string? fileId,
        string? thumbnailField)
    {
        var fromField = ToAbsoluteUrl(apiBaseUrl, thumbnailField);
        if (!string.IsNullOrWhiteSpace(fromField)
            && (fromField.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || fromField.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            return fromField;
        }

        if (string.IsNullOrWhiteSpace(fileId))
        {
            return fromField;
        }

        return ToAbsoluteUrl(
            apiBaseUrl,
            $"/api/common/gallery/{Uri.EscapeDataString(fileId.Trim())}/thumbnail");
    }

    /// <summary>
    /// Chooses an explicit thumbnail, then an explicit preview, and only then synthesizes
    /// the common thumbnail route. This is useful for aggregate DTOs that retain one URL.
    /// </summary>
    public static string? ResolveDisplayUrl(
        string apiBaseUrl,
        string? fileId,
        string? thumbnailField,
        string? previewField) =>
        ToAbsoluteUrl(apiBaseUrl, thumbnailField)
        ?? ToAbsoluteUrl(apiBaseUrl, previewField)
        ?? ResolveThumbnailUrl(apiBaseUrl, fileId, null);

    private static bool IsProtectedApiPath(string path) =>
        path.Equals("/api", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);

    private static string SafePath(string pathOrUrl)
    {
        var queryIndex = pathOrUrl.IndexOf('?');
        return queryIndex >= 0 ? pathOrUrl[..queryIndex] : pathOrUrl;
    }
}
