using Microsoft.Maui.Controls;

namespace MemoryKeeper.Mobile.Images;

public interface IMobileThumbnailSourceFactory
{
    ImageSource? Create(string fileId, string? thumbnailUrl);
}
