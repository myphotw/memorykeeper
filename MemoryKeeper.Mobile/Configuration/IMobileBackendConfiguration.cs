namespace MemoryKeeper.Mobile.Configuration;

public interface IMobileBackendConfiguration
{
    Uri? GetBaseUri();

    void SaveBaseUri(Uri baseUri);

    void ClearBaseUri();
}
