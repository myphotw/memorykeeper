namespace MemoryKeeper.App.Services;

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
}

public interface IGalleryFocusState
{
    bool HasPendingRestore { get; }

    void Save(GalleryFocusSnapshot snapshot);

    GalleryFocusSnapshot? ConsumeRestore();

    void RequestCountryFilter(string country);
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

    public void RequestCountryFilter(string country)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return;
        }

        _snapshot = new GalleryFocusSnapshot { CountryFilter = country.Trim() };
    }
}
