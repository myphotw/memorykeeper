using CommunityToolkit.Mvvm.ComponentModel;

namespace MemoryKeeper.App.Models;

public partial class TagFilterItem : ObservableObject
{
    public TagFilterItem(Guid? tagId, string name, string? color = null)
    {
        TagId = tagId;
        Name = name;
        Color = color ?? string.Empty;
    }

    public Guid? TagId { get; }

    public string Name { get; }

    public string Color { get; }

    public bool IsAll => TagId is null;

    [ObservableProperty]
    private bool isSelected;

    public string DisplayLabel => IsSelected ? $"● {Name}" : Name;

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayLabel));
    }
}
