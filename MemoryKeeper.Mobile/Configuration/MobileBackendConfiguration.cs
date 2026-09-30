using Microsoft.Maui.Storage;

namespace MemoryKeeper.Mobile.Configuration;

public sealed class MobileBackendConfiguration : IMobileBackendConfiguration
{
    private const string BaseUriPreferenceKey = "memorykeeper.backend.base_uri";

    public Uri? GetBaseUri()
    {
        var value = Preferences.Default.Get(BaseUriPreferenceKey, string.Empty);
        return Uri.TryCreate(value, UriKind.Absolute, out var baseUri)
            && baseUri.Scheme == Uri.UriSchemeHttps
                ? baseUri
                : null;
    }

    public void SaveBaseUri(Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        if (!baseUri.IsAbsoluteUri
            || baseUri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(baseUri.UserInfo))
        {
            throw new ArgumentException("Backend 주소는 자격 증명이 없는 HTTPS 주소여야 합니다.", nameof(baseUri));
        }

        Preferences.Default.Set(BaseUriPreferenceKey, baseUri.ToString().TrimEnd('/'));
    }

    public void ClearBaseUri() => Preferences.Default.Remove(BaseUriPreferenceKey);
}
