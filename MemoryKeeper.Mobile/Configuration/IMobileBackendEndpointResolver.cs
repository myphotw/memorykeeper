namespace MemoryKeeper.Mobile.Configuration;

public interface IMobileBackendEndpointResolver
{
    Uri? CurrentBaseUri { get; }

    Task<Uri> ResolveAsync(CancellationToken cancellationToken = default);

    void Invalidate();
}
