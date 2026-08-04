namespace MemoryKeeper.Application.Navigation;

/// <summary>
/// Shared navigation entry for ContentFrame routing.
/// </summary>
public readonly record struct NavigationEntry(string Tag, string? SettingsSection = null)
{
    public static NavigationEntry Home { get; } = new("home");

    public static NavigationEntry Of(string tag, string? settingsSection = null) =>
        new(tag, settingsSection);
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

    bool CanGoForward { get; }

    void Navigate(NavigationEntry entry);

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

    public bool CanGoForward => _forward.Count > 0;

    public void Navigate(NavigationEntry entry)
    {
        if (Current is { } current && !current.Equals(entry))
        {
            _back.Push(current);
            TrimBack();
            _forward.Clear();
        }

        Current = entry;
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
