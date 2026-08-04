using CommunityToolkit.Mvvm.ComponentModel;

namespace MemoryKeeper.App.Models;

public enum GallerySidebarFilterKind
{
    All,
    Year,
    Favorites,
    Recent,
    Pending
}

public partial class GallerySidebarFilterItem : ObservableObject
{
    public GallerySidebarFilterKind Kind { get; init; }

    public int? Year { get; init; }

    public string Title { get; init; } = string.Empty;

    public int Count { get; init; }

    public bool IsSeparator { get; init; }

    public string DisplayLabel
    {
        get
        {
            if (IsSeparator)
            {
                return string.Empty;
            }

            return IsSelected
                ? $"● {Title} ({Count})"
                : $"{Title} ({Count})";
        }
    }

    [ObservableProperty]
    private bool isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayLabel));
    }
}
