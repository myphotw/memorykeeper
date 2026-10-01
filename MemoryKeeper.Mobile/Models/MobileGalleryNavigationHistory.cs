namespace MemoryKeeper.Mobile.Models;

public sealed class MobileGalleryNavigationHistory
{
    private readonly Stack<MobileGalleryNavigationEntry> _entries = [];

    public int Count => _entries.Count;

    public void Remember(
        MobileGalleryContext current,
        MobileGalleryViewMode returnMode,
        MobileGalleryContext destination)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(destination);

        if (!Equals(current.Scope, destination.Scope))
        {
            _entries.Push(new MobileGalleryNavigationEntry(current, returnMode));
        }
    }

    public bool TryPop(out MobileGalleryNavigationEntry entry)
    {
        if (_entries.TryPop(out var storedEntry))
        {
            entry = storedEntry;
            return true;
        }

        entry = null!;
        return false;
    }
}

public sealed record MobileGalleryNavigationEntry(
    MobileGalleryContext Context,
    MobileGalleryViewMode ReturnMode);
