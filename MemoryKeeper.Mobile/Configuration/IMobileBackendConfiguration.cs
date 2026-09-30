namespace MemoryKeeper.Mobile.Configuration;

public interface IMobileBackendConfiguration
{
    Uri? BaseUri { get; }

    string? BearerToken { get; }

    bool IsConfigured { get; }
}
