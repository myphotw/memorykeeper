using Android.Graphics.Drawables;
using MemoryKeeper.Mobile.Controls;
using MemoryKeeper.Mobile.Images;
using MemoryKeeper.Mobile.Platforms.Android.Images;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;

namespace MemoryKeeper.Mobile.Handlers;

public sealed class MobileThumbnailImageHandler : ImageHandler
{
    public static readonly IPropertyMapper<MobileThumbnailImage, MobileThumbnailImageHandler>
        PropertyMapper = new PropertyMapper<MobileThumbnailImage, MobileThumbnailImageHandler>(
            ImageHandler.Mapper)
        {
            [nameof(MobileThumbnailImage.Source)] = MapThumbnailSource,
        };

    private long _sourceGeneration;

    public MobileThumbnailImageHandler()
        : base(PropertyMapper)
    {
    }

    protected override void DisconnectHandler(Android.Widget.ImageView platformView)
    {
        Interlocked.Increment(ref _sourceGeneration);
        base.DisconnectHandler(platformView);
    }

    private static void MapThumbnailSource(
        MobileThumbnailImageHandler handler,
        MobileThumbnailImage view) =>
        _ = handler.UpdateThumbnailSourceAsync(view);

    private async Task UpdateThumbnailSourceAsync(MobileThumbnailImage view)
    {
        var generation = Interlocked.Increment(ref _sourceGeneration);
        if (view.Source is not MobileThumbnailImageSource source)
        {
            await ImageHandler.MapSourceAsync(this, view);
            return;
        }

        var cache = MauiContext?.Services.GetService(typeof(AndroidDecodedThumbnailCache))
                    as AndroidDecodedThumbnailCache;
        if (cache is not null && cache.TryGet(source.CacheKey, out var cached))
        {
            if (IsCurrent(view, generation))
            {
                SourceLoader.Reset();
                PlatformView.SetImageBitmap(cached);
            }

            return;
        }

        try
        {
            await ImageHandler.MapSourceAsync(this, view);
            if (cache is not null
                && IsCurrent(view, generation)
                && PlatformView.Drawable is BitmapDrawable drawable
                && drawable.Bitmap is { IsRecycled: false } bitmap)
            {
                cache.AddCopy(source.CacheKey, bitmap);
            }
        }
        catch when (!IsCurrent(view, generation))
        {
            // A recycled cell superseded this request. The current binding owns the surface now.
        }
        catch
        {
            // The standard image pipeline already reports load failure; decoded caching is optional.
        }
    }

    private bool IsCurrent(MobileThumbnailImage view, long generation) =>
        generation == Volatile.Read(ref _sourceGeneration)
        && ReferenceEquals(VirtualView, view)
        && view.Source is MobileThumbnailImageSource;
}
