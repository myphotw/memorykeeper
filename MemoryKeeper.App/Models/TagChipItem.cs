using CommunityToolkit.Mvvm.ComponentModel;
using MemoryKeeper.Application.DTOs;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MemoryKeeper.App.Models;

public partial class TagChipItem : ObservableObject
{
    public TagChipItem(TagDto tag)
    {
        Id = tag.Id;
        Name = tag.Name;
        ColorHex = tag.Color;
        UsageCount = tag.UsageCount;
        ColorBrush = CreateBrush(tag.Color);
        IsAssigned = tag.IsAssigned;
        IsSelected = tag.IsAssigned;
    }

    public Guid Id { get; }

    public string Name { get; }

    public string ColorHex { get; }

    public int UsageCount { get; }

    public SolidColorBrush ColorBrush { get; }

    public string DisplayText => UsageCount > 0 ? $"{Name} ({UsageCount})" : Name;

    [ObservableProperty]
    private bool isAssigned;

    [ObservableProperty]
    private bool isSelected;

    private static SolidColorBrush CreateBrush(string hex)
    {
        try
        {
            var value = hex.TrimStart('#');
            if (value.Length == 6)
            {
                var r = Convert.ToByte(value[..2], 16);
                var g = Convert.ToByte(value[2..4], 16);
                var b = Convert.ToByte(value[4..6], 16);
                return new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
            }
        }
        catch
        {
            // Fall through to default.
        }

        return new SolidColorBrush(Colors.SteelBlue);
    }
}
