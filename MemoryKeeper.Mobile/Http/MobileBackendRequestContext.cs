namespace MemoryKeeper.Mobile.Http;

public static class MobileBackendRequestContext
{
    private static readonly HttpRequestOptionsKey<Uri> SelectedBaseUriKey =
        new("MemoryKeeper.Mobile.SelectedBackendBaseUri");

    public static void MarkSelectedBackend(HttpRequestMessage request, Uri selectedBaseUri)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(selectedBaseUri);
        request.Options.Set(SelectedBaseUriKey, selectedBaseUri);
    }

    internal static bool TryGetSelectedBackend(
        HttpRequestMessage request,
        out Uri? selectedBaseUri) =>
        request.Options.TryGetValue(SelectedBaseUriKey, out selectedBaseUri);
}
