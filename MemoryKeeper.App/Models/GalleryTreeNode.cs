using System.Collections.ObjectModel;
using MemoryKeeper.Application;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace MemoryKeeper.App.Models;

public enum GalleryTreeNodeKind
{
    All,
    Year,
    Country,
    City,
    Place,
    /// <summary>Place browse root (Place → Year).</summary>
    PlaceBrowse,
    /// <summary>Year under a place in Place browse mode.</summary>
    PlaceYear,
    Unclassified,
    Favorites,
    Recent,
    Pending,
    Separator
}

public partial class GalleryTreeNode : ObservableObject
{
    public GalleryTreeNodeKind Kind { get; init; }

    public int? Year { get; init; }

    public string? Country { get; init; }

    public string? City { get; init; }

    public Guid? PlaceId { get; init; }

    public string? PlaceType { get; init; }

    public string Title { get; init; } = string.Empty;

    public int Depth { get; init; }

    public bool CanExpand { get; init; }

    public bool IsSeparator => Kind == GalleryTreeNodeKind.Separator;

    [ObservableProperty]
    private int count;

    [ObservableProperty]
    private bool isExpanded;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private bool childrenLoaded;

    [ObservableProperty]
    private bool isBusy;

    public ObservableCollection<GalleryTreeNode> Children { get; } = [];

    public string DisplayLabel
    {
        get
        {
            if (Kind == GalleryTreeNodeKind.Separator)
            {
                return Title;
            }

            if (Kind is GalleryTreeNodeKind.Place or GalleryTreeNodeKind.PlaceBrowse)
            {
                var icon = PlaceTypeCatalog.GetIcon(PlaceType);
                return $"{icon} {Title} ({Count})";
            }

            return $"{Title} ({Count})";
        }
    }

    public string ExpandGlyph => !CanExpand ? "  " : IsExpanded ? "▾" : "▸";

    public Thickness IndentMargin => new(Depth * 14, 0, 0, 0);

    public string BuildNodeKey() => Kind switch
    {
        GalleryTreeNodeKind.All => "all",
        GalleryTreeNodeKind.Year => $"year:{Year}",
        GalleryTreeNodeKind.Unclassified => $"year:{Year}:unclassified",
        GalleryTreeNodeKind.Country => $"year:{Year}:country:{Country}",
        GalleryTreeNodeKind.City => $"year:{Year}:country:{Country}:city:{City}",
        GalleryTreeNodeKind.Place => $"year:{Year}:country:{Country}:city:{City}:place:{PlaceId}",
        GalleryTreeNodeKind.PlaceBrowse => $"place-browse:{PlaceId}",
        GalleryTreeNodeKind.PlaceYear => $"place-browse:{PlaceId}:year:{Year}",
        GalleryTreeNodeKind.Favorites => "favorites",
        GalleryTreeNodeKind.Recent => "recent",
        GalleryTreeNodeKind.Pending => "pending",
        _ => Title
    };

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpandGlyph));

    partial void OnCountChanged(int value) => OnPropertyChanged(nameof(DisplayLabel));
}
