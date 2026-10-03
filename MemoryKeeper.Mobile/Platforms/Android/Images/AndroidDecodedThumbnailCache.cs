using Android.Graphics;
using MemoryKeeper.Application.Services;

namespace MemoryKeeper.Mobile.Platforms.Android.Images;

public sealed class AndroidDecodedThumbnailCache
{
    private readonly BoundedDecodedThumbnailCache<Bitmap> _cache = new(GetAllocationBytes);

    public bool TryGet(string key, out Bitmap? bitmap)
    {
        if (!_cache.TryGet(key, out bitmap))
        {
            return false;
        }

        return !bitmap.IsRecycled;
    }

    public void AddCopy(string key, Bitmap source)
    {
        if (source.IsRecycled)
        {
            return;
        }

        Bitmap? ownedCopy = null;
        try
        {
            var copyConfig = source.GetConfig() ?? Bitmap.Config.Argb8888;
            if (copyConfig is null)
            {
                return;
            }

            ownedCopy = source.Copy(copyConfig, false);
            if (ownedCopy is null || !_cache.AddOrUpdate(key, ownedCopy))
            {
                ownedCopy?.Dispose();
                return;
            }

            ownedCopy = null;
        }
        catch
        {
            // Decoded caching is best-effort; the already loaded thumbnail remains valid.
        }
        finally
        {
            ownedCopy?.Dispose();
        }
    }

    private static long GetAllocationBytes(Bitmap bitmap) => bitmap.AllocationByteCount;
}
