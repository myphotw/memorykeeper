using CommunityToolkit.Mvvm.ComponentModel;
using MemoryKeeper.Application.DTOs;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MemoryKeeper.App.Models;

public partial class RelatedPhotoItem : ObservableObject
{
    public RelatedPhotoItem(RelatedPhotoDto photo)
    {
        Photo = photo;
    }

    public RelatedPhotoDto Photo { get; }

    public Guid MediaId => Photo.MediaId;

    public string FileName => Photo.FileName;

    public string AbsoluteLibraryPath => Photo.AbsoluteLibraryPath;

    public bool IsFavorite => Photo.IsFavorite;

    public string CapturedAtText => Photo.CapturedAt?.ToLocalTime().ToString("yyyy-MM-dd") ?? "-";

    [ObservableProperty]
    private BitmapImage? thumbnailImage;
}
