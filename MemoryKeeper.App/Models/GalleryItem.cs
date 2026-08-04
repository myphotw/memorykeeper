using CommunityToolkit.Mvvm.ComponentModel;
using MemoryKeeper.Application.DTOs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MemoryKeeper.App.Models;

public partial class GalleryItem : ObservableObject
{
    public GalleryItem(GalleryMediaDto media)
    {
        Media = media;
    }

    public GalleryMediaDto Media { get; }

    public Guid MediaId => Media.Id;

    public string FileName => Media.FileName;

    public string CapturedAtText => Media.CapturedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "-";

    public string AbsoluteLibraryPath => Media.AbsoluteLibraryPath;

    public bool IsFavorite => Media.IsFavorite;

    [ObservableProperty]
    private BitmapImage? thumbnailImage;

    [ObservableProperty]
    private bool isThumbnailLoading;

    [ObservableProperty]
    private bool hasThumbnail;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private Thickness selectionBorderThickness = new(1);

    partial void OnIsSelectedChanged(bool value)
    {
        SelectionBorderThickness = value ? new Thickness(2) : new Thickness(1);
    }
}
