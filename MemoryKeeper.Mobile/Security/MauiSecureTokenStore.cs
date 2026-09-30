using Microsoft.Maui.Storage;

namespace MemoryKeeper.Mobile.Security;

public sealed class MauiSecureTokenStore : ISecureTokenStore
{
    private const string TokenKey = "memorykeeper.backend.bearer_token";

    public async Task<string?> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await SecureStorage.Default.GetAsync(TokenKey).ConfigureAwait(false);
    }

    public async Task WriteAsync(string token, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        cancellationToken.ThrowIfCancellationRequested();
        await SecureStorage.Default.SetAsync(TokenKey, token.Trim()).ConfigureAwait(false);
    }

    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecureStorage.Default.Remove(TokenKey);
        return Task.CompletedTask;
    }
}
