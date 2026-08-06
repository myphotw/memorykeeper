namespace MemoryKeeper.App.Services;

public enum PhotoNavigationTarget
{
    Viewer,
    Detail
}

public interface IPhotoNavigationState
{
    Guid? FocusMediaId { get; set; }

    /// <summary>
    /// Ordered media playlist for ◀/▶ navigation (gallery / pending).
    /// </summary>
    IReadOnlyList<Guid> Playlist { get; }

    /// <summary>
    /// When true, after place registration auto-advance to next pending photo.
    /// </summary>
    bool AutoAdvanceAfterPlaceRegister { get; set; }

    PhotoNavigationTarget Target { get; }

    /// <summary>Tag to return when viewer closes (gallery, pending, ...).</summary>
    string ReturnSourceTag { get; }

    /// <summary>
    /// Detail was opened from viewer. Informational only — back navigation must use GoBack, not this flag.
    /// </summary>
    bool DetailOpenedFromViewer { get; set; }

    event EventHandler? OpenRequested;

    void RequestOpen(Guid mediaId);

    void RequestOpen(Guid mediaId, IReadOnlyList<Guid> playlist, bool autoAdvanceAfterPlaceRegister = false);

    void RequestOpenViewer(
        Guid mediaId,
        IReadOnlyList<Guid> playlist,
        string returnSourceTag,
        bool autoAdvanceAfterPlaceRegister = false);

    void RequestOpenDetail(Guid mediaId);

    bool TryGetPrevious(out Guid mediaId);

    bool TryGetNext(out Guid mediaId);

    void RemoveFromPlaylist(Guid mediaId);

    /// <summary>Updates playlist without opening a page (e.g. after Gallery reload).</summary>
    void SetPlaylist(IReadOnlyList<Guid> playlist);
}

public sealed class PhotoNavigationState : IPhotoNavigationState
{
    private List<Guid> _playlist = [];

    public Guid? FocusMediaId { get; set; }

    public IReadOnlyList<Guid> Playlist => _playlist;

    public bool AutoAdvanceAfterPlaceRegister { get; set; }

    public PhotoNavigationTarget Target { get; private set; } = PhotoNavigationTarget.Viewer;

    public string ReturnSourceTag { get; private set; } = "gallery";

    public bool DetailOpenedFromViewer { get; set; }

    public event EventHandler? OpenRequested;

    public void RequestOpen(Guid mediaId) =>
        RequestOpenViewer(mediaId, [mediaId], "gallery");

    public void RequestOpen(Guid mediaId, IReadOnlyList<Guid> playlist, bool autoAdvanceAfterPlaceRegister = false) =>
        RequestOpenViewer(mediaId, playlist, "gallery", autoAdvanceAfterPlaceRegister);

    public void RequestOpenViewer(
        Guid mediaId,
        IReadOnlyList<Guid> playlist,
        string returnSourceTag,
        bool autoAdvanceAfterPlaceRegister = false)
    {
        ApplyPlaylist(mediaId, playlist);
        AutoAdvanceAfterPlaceRegister = autoAdvanceAfterPlaceRegister;
        ReturnSourceTag = string.IsNullOrWhiteSpace(returnSourceTag) ? "gallery" : returnSourceTag;
        Target = PhotoNavigationTarget.Viewer;
        DetailOpenedFromViewer = false;
        FocusMediaId = mediaId;
        OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestOpenDetail(Guid mediaId)
    {
        if (!_playlist.Contains(mediaId))
        {
            _playlist = [mediaId];
        }

        Target = PhotoNavigationTarget.Detail;
        DetailOpenedFromViewer = true;
        FocusMediaId = mediaId;
        // Do not raise OpenRequested — page transitions are owned by MainWindow / GoBack.
    }

    public bool TryGetPrevious(out Guid mediaId)
    {
        mediaId = Guid.Empty;
        if (FocusMediaId is not Guid current || _playlist.Count == 0)
        {
            return false;
        }

        var index = _playlist.IndexOf(current);
        if (index < 0)
        {
            return false;
        }

        // MK-046: circular playlist — first ← wraps to last.
        if (_playlist.Count == 1)
        {
            return false;
        }

        mediaId = _playlist[(index - 1 + _playlist.Count) % _playlist.Count];
        return true;
    }

    public bool TryGetNext(out Guid mediaId)
    {
        mediaId = Guid.Empty;
        if (FocusMediaId is not Guid current || _playlist.Count == 0)
        {
            return false;
        }

        var index = _playlist.IndexOf(current);
        if (index < 0)
        {
            return false;
        }

        // MK-046: circular playlist — last → wraps to first.
        if (_playlist.Count == 1)
        {
            return false;
        }

        mediaId = _playlist[(index + 1) % _playlist.Count];
        return true;
    }

    public void RemoveFromPlaylist(Guid mediaId) => _playlist.RemoveAll(id => id == mediaId);

    public void SetPlaylist(IReadOnlyList<Guid> playlist)
    {
        _playlist = playlist?.Where(id => id != Guid.Empty).Distinct().ToList() ?? [];
    }

    private void ApplyPlaylist(Guid mediaId, IReadOnlyList<Guid> playlist)
    {
        _playlist = playlist?.Where(id => id != Guid.Empty).Distinct().ToList() ?? [mediaId];
        if (!_playlist.Contains(mediaId))
        {
            _playlist.Insert(0, mediaId);
        }
    }
}
