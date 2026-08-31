namespace MemoryKeeper.Application.Navigation;

public enum NavigationKind
{
    DrillDown = 0,
    TopLevel = 1,
    Viewer = 2
}

/// <summary>
/// Shared navigation entry for ContentFrame routing.
/// </summary>
public readonly record struct NavigationEntry(
    string Tag,
    string? SettingsSection = null,
    NavigationKind Kind = NavigationKind.DrillDown,
    string? RootTag = null,
    string? ContextKey = null,
    string? DisplayLabel = null)
{
    public static NavigationEntry Home { get; } =
        TopLevel("home", "홈");

    public static NavigationEntry Of(string tag, string? settingsSection = null) =>
        new(tag, settingsSection);

    public static NavigationEntry TopLevel(
        string tag,
        string displayLabel,
        string? settingsSection = null) =>
        new(tag, settingsSection, NavigationKind.TopLevel, tag, null, displayLabel);

    public static NavigationEntry DrillDown(
        string tag,
        string rootTag,
        string displayLabel,
        string? contextKey = null,
        string? settingsSection = null) =>
        new(tag, settingsSection, NavigationKind.DrillDown, rootTag, contextKey, displayLabel);

    public static NavigationEntry Viewer(
        string tag,
        string rootTag,
        string displayLabel,
        string? contextKey = null) =>
        new(tag, null, NavigationKind.Viewer, rootTag, contextKey, displayLabel);

    /// <summary>
    /// Navigation identity excludes the user-facing label so copy changes do not create history entries.
    /// </summary>
    public bool HasSameIdentity(NavigationEntry other) =>
        string.Equals(Tag, other.Tag, StringComparison.OrdinalIgnoreCase)
        && string.Equals(SettingsSection, other.SettingsSection, StringComparison.OrdinalIgnoreCase)
        && Kind == other.Kind
        && string.Equals(RootTag, other.RootTag, StringComparison.OrdinalIgnoreCase)
        && string.Equals(ContextKey, other.ContextKey, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Optional page UI state bag (scroll, selection, filters, zoom, etc.).
/// </summary>
public sealed class NavigationPageState
{
    public string? SelectedItemKey { get; init; }

    public double ScrollPosition { get; init; }

    public IReadOnlyList<string> ExpandedNodeKeys { get; init; } = [];

    public string? SearchText { get; init; }

    public string? Filter { get; init; }

    public double ZoomFactor { get; init; } = 1;

    public int SelectedThumbnailIndex { get; init; } = -1;

    public IDictionary<string, string> Extra { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public interface INavigationService
{
    NavigationEntry? Current { get; }

    bool CanGoBack { get; }

    NavigationEntry? BackEntry { get; }

    bool CanGoForward { get; }

    void Navigate(NavigationEntry entry);

    /// <summary>Navigate only if not already on <paramref name="entry"/>.</summary>
    bool NavigateIfNeeded(NavigationEntry entry);

    bool IsCurrent(NavigationEntry entry);

    /// <summary>Remove consecutive duplicates at the top of the back stack.</summary>
    int RemoveConsecutiveDuplicates();

    /// <summary>Back-stack tags from oldest to newest (diagnostic).</summary>
    IReadOnlyList<string> GetBackStackTags();

    void NavigateRoot(NavigationEntry entry);

    void ReplaceCurrent(NavigationEntry entry);

    bool TryGoBack(out NavigationEntry entry);

    bool TryGoForward(out NavigationEntry entry);

    void Clear();

    void SavePageState(string tag, NavigationPageState state);

    NavigationPageState? PeekPageState(string tag);

    NavigationPageState? TakePageState(string tag);
}

/// <summary>
/// Back + forward navigation with per-page state bags.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private readonly Stack<NavigationEntry> _back = new();
    private readonly Stack<NavigationEntry> _forward = new();
    private readonly Dictionary<string, NavigationPageState> _pageStates =
        new(StringComparer.OrdinalIgnoreCase);

    private const int MaxDepth = 32;

    public NavigationEntry? Current { get; private set; }

    public bool CanGoBack => _back.Count > 0;

    public NavigationEntry? BackEntry => _back.Count > 0 ? _back.Peek() : null;

    public bool CanGoForward => _forward.Count > 0;

    public void Navigate(NavigationEntry entry)
    {
        if (Current is { } current && !current.HasSameIdentity(entry))
        {
            _back.Push(current);
            TrimBack();
            _forward.Clear();
        }

        Current = entry;
    }

    /// <summary>
    /// Navigates only when the target differs from <see cref="Current"/>.
    /// Returns false when already on the same entry (no back-stack push).
    /// </summary>
    public bool NavigateIfNeeded(NavigationEntry entry)
    {
        if (Current is { } current && current.HasSameIdentity(entry))
        {
            return false;
        }

        Navigate(entry);
        return true;
    }

    public bool IsCurrent(NavigationEntry entry) =>
        Current is { } current && current.HasSameIdentity(entry);

    /// <summary>
    /// Collapses consecutive duplicate entries at the top of the back stack.
    /// </summary>
    public int RemoveConsecutiveDuplicates()
    {
        if (_back.Count == 0)
        {
            return 0;
        }

        var removed = 0;
        while (_back.Count > 0 && Current is { } current && _back.Peek().HasSameIdentity(current))
        {
            _back.Pop();
            removed++;
        }

        return removed;
    }

    public IReadOnlyList<string> GetBackStackTags()
    {
        if (_back.Count == 0)
        {
            return [];
        }

        return _back.Select(e => e.Tag).Reverse().ToArray();
    }

    public void NavigateRoot(NavigationEntry entry)
    {
        _back.Clear();
        _forward.Clear();
        Current = entry;
    }

    public void ReplaceCurrent(NavigationEntry entry) => Current = entry;

    public bool TryGoBack(out NavigationEntry entry)
    {
        if (_back.Count == 0)
        {
            entry = default;
            return false;
        }

        if (Current is { } current)
        {
            _forward.Push(current);
        }

        entry = _back.Pop();
        Current = entry;
        return true;
    }

    public bool TryGoForward(out NavigationEntry entry)
    {
        if (_forward.Count == 0)
        {
            entry = default;
            return false;
        }

        if (Current is { } current)
        {
            _back.Push(current);
            TrimBack();
        }

        entry = _forward.Pop();
        Current = entry;
        return true;
    }

    public void Clear()
    {
        _back.Clear();
        _forward.Clear();
        Current = null;
    }

    public void SavePageState(string tag, NavigationPageState state)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        _pageStates[tag] = state;
    }

    public NavigationPageState? PeekPageState(string tag) =>
        _pageStates.TryGetValue(tag, out var state) ? state : null;

    public NavigationPageState? TakePageState(string tag)
    {
        if (!_pageStates.TryGetValue(tag, out var state))
        {
            return null;
        }

        _pageStates.Remove(tag);
        return state;
    }

    private void TrimBack()
    {
        if (_back.Count <= MaxDepth)
        {
            return;
        }

        var items = _back.ToArray();
        _back.Clear();
        for (var i = 0; i < MaxDepth; i++)
        {
            _back.Push(items[i]);
        }
    }
}
