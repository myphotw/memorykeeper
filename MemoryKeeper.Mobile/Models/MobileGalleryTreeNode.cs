using CommunityToolkit.Mvvm.ComponentModel;
using MemoryKeeper.Application;

namespace MemoryKeeper.Mobile.Models;

public partial class MobileGalleryTreeNode : ObservableObject
{
    public MobileGalleryTreeNode(GalleryBrowseTreeNode source)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Children = source.Children.Select(child => new MobileGalleryTreeNode(child)).ToArray();
    }

    public GalleryBrowseTreeNode Source { get; }

    public IReadOnlyList<MobileGalleryTreeNode> Children { get; }

    public string Key => Source.Key;

    public string DisplayName => Source.DisplayName;

    public string Context => Source.Context;

    public int PhotoCount => Source.PhotoCount;

    public string CountText => $"{PhotoCount:N0}장";

    public GalleryBrowseScope Scope => Source.Scope;

    public GalleryBrowseTreeNodeKind Kind => Source.Kind;

    public int Depth => Source.Depth;

    public double IndentWidth => Depth * 20;

    public bool HasChildren => Children.Count > 0;

    public string ExpandGlyph => !HasChildren ? string.Empty : IsExpanded ? "⌄" : ">";

    [ObservableProperty]
    private bool isExpanded;

    [ObservableProperty]
    private bool isCurrent;

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpandGlyph));
}
