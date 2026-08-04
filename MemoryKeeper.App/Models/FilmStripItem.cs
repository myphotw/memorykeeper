using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace MemoryKeeper.App.Models;

public partial class FilmStripItem : ObservableObject
{
    private static readonly SolidColorBrush CurrentBrush = new(Colors.White);
    private static readonly SolidColorBrush IdleBrush = new(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));

    public Guid MediaId { get; init; }

    public string AbsoluteLibraryPath { get; set; } = string.Empty;

    [ObservableProperty]
    private BitmapImage? thumbnailImage;

    [ObservableProperty]
    private bool isCurrent;

    [ObservableProperty]
    private Thickness selectionBorderThickness = new(1);

    [ObservableProperty]
    private SolidColorBrush selectionBorderBrush = IdleBrush;

    [ObservableProperty]
    private double currentScale = 1.0;

    partial void OnIsCurrentChanged(bool value)
    {
        SelectionBorderThickness = value ? new Thickness(3) : new Thickness(1);
        SelectionBorderBrush = value ? CurrentBrush : IdleBrush;
        CurrentScale = value ? 1.1 : 1.0;
    }
}
