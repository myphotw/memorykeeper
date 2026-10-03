using System.Reflection;

namespace MemoryKeeper.Mobile.Configuration;

public sealed class MobileBackendConfiguration : IMobileBackendConfiguration
{
    private const string InternalBackendUrlMetadataKey = "MemoryKeeper.Mobile.InternalBackendUrl";
    private const string ExternalBackendUrlMetadataKey = "MemoryKeeper.Mobile.ExternalBackendUrl";
    private const string BackendTokenMetadataKey = "MemoryKeeper.Mobile.BackendToken";
    private const string AllowedCleartextInternalHost = "192.168.55.225";
    private const int AllowedCleartextInternalPort = 8000;

    public MobileBackendConfiguration()
        : this(
            ReadBuildValue(InternalBackendUrlMetadataKey),
            ReadBuildValue(ExternalBackendUrlMetadataKey),
            ReadBuildValue(BackendTokenMetadataKey))
    {
    }

    internal MobileBackendConfiguration(
        string? internalBackendUrl,
        string? externalBackendUrl,
        string? bearerToken)
    {
        InternalBaseUri = ParseInternalBaseUri(internalBackendUrl);
        ExternalBaseUri = ParseExternalBaseUri(externalBackendUrl);
        BearerToken = string.IsNullOrWhiteSpace(bearerToken) ? null : bearerToken.Trim();
    }

    public Uri? InternalBaseUri { get; }

    public Uri? ExternalBaseUri { get; }

    public string? BearerToken { get; }

    public bool IsConfigured =>
        InternalBaseUri is not null
        && ExternalBaseUri is not null
        && !string.IsNullOrWhiteSpace(BearerToken);

    private static Uri? ParseInternalBaseUri(string? value)
    {
        var uri = ParseRootUri(value);
        if (uri is null)
        {
            return null;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return uri;
        }

        return uri.Scheme == Uri.UriSchemeHttp
               && string.Equals(uri.Host, AllowedCleartextInternalHost, StringComparison.OrdinalIgnoreCase)
               && uri.Port == AllowedCleartextInternalPort
            ? uri
            : null;
    }

    private static Uri? ParseExternalBaseUri(string? value)
    {
        var uri = ParseRootUri(value);
        return uri?.Scheme == Uri.UriSchemeHttps ? uri : null;
    }

    private static Uri? ParseRootUri(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || (uri.AbsolutePath != "/" && !string.IsNullOrEmpty(uri.AbsolutePath.Trim('/'))))
        {
            return null;
        }

        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/", UriKind.Absolute);
    }

    private static string? ReadBuildValue(string key) =>
        typeof(MobileBackendConfiguration).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))
            ?.Value;
}
