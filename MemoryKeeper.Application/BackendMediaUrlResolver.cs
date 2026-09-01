namespace MemoryKeeper.Application;

/// <summary>Builds client-neutral absolute media URLs and the established authenticated thumbnail fallback.</summary>
public static class BackendMediaUrlResolver
{
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
}
