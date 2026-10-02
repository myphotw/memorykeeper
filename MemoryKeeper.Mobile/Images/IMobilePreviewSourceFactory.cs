namespace MemoryKeeper.Mobile.Images;

public interface IMobilePreviewSourceFactory
{
    Task<MobilePreviewLoadResult> LoadAsync(
        string fileId,
        string? previewUrl,
        CancellationToken cancellationToken = default);
}
