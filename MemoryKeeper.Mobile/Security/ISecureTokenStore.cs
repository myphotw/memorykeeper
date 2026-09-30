namespace MemoryKeeper.Mobile.Security;

public interface ISecureTokenStore
{
    Task<string?> ReadAsync(CancellationToken cancellationToken = default);

    Task WriteAsync(string token, CancellationToken cancellationToken = default);

    Task DeleteAsync(CancellationToken cancellationToken = default);
}
