using Microsoft.Maui.Controls;

namespace MemoryKeeper.Mobile.Images;

public sealed class MobileThumbnailImageSource : StreamImageSource
{
    public required string CacheKey { get; init; }
}
