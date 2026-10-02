using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Controls;

namespace MemoryKeeper.Mobile.Models;

public partial class MobileGalleryItem : ObservableObject
{
    [ObservableProperty]
    private double tileSize = 120;

    public required string FileId { get; init; }

    public ImageSource? ThumbnailSource { get; init; }

    public string? PreviewUrl { get; init; }

    public bool IsVideo { get; init; }
}
