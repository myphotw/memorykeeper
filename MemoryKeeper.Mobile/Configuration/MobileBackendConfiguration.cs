using System.Reflection;

namespace MemoryKeeper.Mobile.Configuration;

public sealed class MobileBackendConfiguration : IMobileBackendConfiguration
{
    private const string BackendUrlMetadataKey = "MemoryKeeper.Mobile.BackendUrl";
    private const string BackendTokenMetadataKey = "MemoryKeeper.Mobile.BackendToken";

    public MobileBackendConfiguration()
        : this(ReadBuildValue(BackendUrlMetadataKey), ReadBuildValue(BackendTokenMetadataKey))
    {
    }

    internal MobileBackendConfiguration(string? backendUrl, string? bearerToken)
    {
        BaseUri = ParseBaseUri(backendUrl);
        BearerToken = string.IsNullOrWhiteSpace(bearerToken) ? null : bearerToken.Trim();
    }

    public Uri? BaseUri { get; }

    public string? BearerToken { get; }

    public bool IsConfigured => BaseUri is not null && !string.IsNullOrWhiteSpace(BearerToken);

    private static Uri? ParseBaseUri(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
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
