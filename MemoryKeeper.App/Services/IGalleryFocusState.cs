namespace MemoryKeeper.App.Services;

public enum GalleryPlaceScope
{
    All,
    Domestic,
    International,
}

public enum GalleryPlaceNavigationLevel
{
    Countries,
    Places,
    Photos,
}

public sealed class GalleryPlaceNavigationRequestedEventArgs(
    GalleryPlaceScope scope,
    GalleryPlaceNavigationLevel level) : EventArgs
{
    public GalleryPlaceScope Scope { get; } = scope;

    public GalleryPlaceNavigationLevel Level { get; } = level;
}

public sealed class GalleryFocusSnapshot
{
    public string? SearchText { get; init; }

    public string? SelectedNodeKey { get; init; }

    public IReadOnlyList<string> ExpandedNodeKeys { get; init; } = [];

    public Guid? FocusMediaId { get; init; }

    public double GridScrollOffset { get; init; }

    /// <summary>0 = Year browse, 1 = Place browse.</summary>
    public int BrowseModeIndex { get; init; }

    /// <summary>Optional cross-surface country selection applied through the existing hierarchy query.</summary>
    public string? CountryFilter { get; init; }

    public GalleryPlaceScope PlaceScope { get; init; }

    /// <summary>One-shot Travel Records entry intent. Ordinary Gallery restoration leaves this null.</summary>
    public GalleryPlaceNavigationLevel? RequestedPlaceLevel { get; init; }
}

public interface IGalleryFocusState
{
    bool HasPendingRestore { get; }

    void Save(GalleryFocusSnapshot snapshot);

    GalleryFocusSnapshot? ConsumeRestore();

    void Clear();

    void RequestCountryFilter(string country);

    void RequestPlaceBrowse(GalleryPlaceScope scope, GalleryPlaceNavigationLevel level);
}

public sealed class GalleryFocusState : IGalleryFocusState
{
    private GalleryFocusSnapshot? _snapshot;

    public bool HasPendingRestore => _snapshot is not null;

    public void Save(GalleryFocusSnapshot snapshot) => _snapshot = snapshot;

    public GalleryFocusSnapshot? ConsumeRestore()
    {
        var snapshot = _snapshot;
        _snapshot = null;
        return snapshot;
    }

    public void Clear() => _snapshot = null;

    public void RequestCountryFilter(string country)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return;
        }

        _snapshot = new GalleryFocusSnapshot
        {
            BrowseModeIndex = 1,
            CountryFilter = country.Trim(),
        };
    }

    public void RequestPlaceBrowse(GalleryPlaceScope scope, GalleryPlaceNavigationLevel level) =>
        _snapshot = new GalleryFocusSnapshot
        {
            BrowseModeIndex = 1,
            PlaceScope = scope,
            RequestedPlaceLevel = level,
        };
}
