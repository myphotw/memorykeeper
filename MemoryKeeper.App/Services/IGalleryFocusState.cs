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
}

public interface IGalleryFocusState
{
    bool HasPendingRestore { get; }

    void Save(GalleryFocusSnapshot snapshot);

    GalleryFocusSnapshot? ConsumeRestore();
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
}
