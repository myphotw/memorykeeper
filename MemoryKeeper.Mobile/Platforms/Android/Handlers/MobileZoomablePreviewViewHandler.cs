using MemoryKeeper.Mobile.Controls;
using MemoryKeeper.Mobile.Platforms.Android.Controls;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;

namespace MemoryKeeper.Mobile.Handlers;

public sealed class MobileZoomablePreviewViewHandler
    : ViewHandler<MobileZoomablePreviewView, ZoomablePreviewImageView>
{
    public static readonly IPropertyMapper<MobileZoomablePreviewView, MobileZoomablePreviewViewHandler>
        PropertyMapper = new PropertyMapper<MobileZoomablePreviewView, MobileZoomablePreviewViewHandler>(
            ViewHandler.ViewMapper)
        {
            [nameof(MobileZoomablePreviewView.PreviewBytes)] = MapPreviewBytes,
        };

    public MobileZoomablePreviewViewHandler()
        : base(PropertyMapper)
    {
    }

    protected override ZoomablePreviewImageView CreatePlatformView() => new(Context);

    protected override void ConnectHandler(ZoomablePreviewImageView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.PreviewLoaded += OnPreviewLoaded;
        platformView.PreviewFailed += OnPreviewFailed;
        platformView.PreviousRequested += OnPreviousRequested;
        platformView.NextRequested += OnNextRequested;
    }

    protected override void DisconnectHandler(ZoomablePreviewImageView platformView)
    {
        platformView.PreviewLoaded -= OnPreviewLoaded;
        platformView.PreviewFailed -= OnPreviewFailed;
        platformView.PreviousRequested -= OnPreviousRequested;
        platformView.NextRequested -= OnNextRequested;
        platformView.ReleasePreview();
        base.DisconnectHandler(platformView);
    }

    private static void MapPreviewBytes(
        MobileZoomablePreviewViewHandler handler,
        MobileZoomablePreviewView view) =>
        handler.PlatformView.SetPreview(
            view.PreviewBytes,
            view.PreviewFileId,
            view.PreviewRequestDescription);

    private void OnPreviewLoaded(object? sender, EventArgs e) => VirtualView.RaisePreviewLoaded();

    private void OnPreviewFailed(object? sender, EventArgs e) => VirtualView.RaisePreviewFailed();

    private void OnPreviousRequested(object? sender, EventArgs e) =>
        VirtualView.RaisePreviousRequested();

    private void OnNextRequested(object? sender, EventArgs e) => VirtualView.RaiseNextRequested();
}
