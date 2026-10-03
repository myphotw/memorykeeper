namespace MemoryKeeper.Mobile.Configuration;

public interface IMobileBackendConfiguration
{
    Uri? InternalBaseUri { get; }

    Uri? ExternalBaseUri { get; }

    string? BearerToken { get; }

    bool IsConfigured { get; }
}
