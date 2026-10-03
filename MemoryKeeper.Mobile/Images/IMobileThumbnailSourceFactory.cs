using Microsoft.Maui.Controls;

namespace MemoryKeeper.Mobile.Images;

public interface IMobileThumbnailSourceFactory
{
    ImageSource? Create(string fileId, string? thumbnailUrl);

    Task PreloadAsync(
        string fileId,
        string? thumbnailUrl,
        CancellationToken cancellationToken = default);
}
