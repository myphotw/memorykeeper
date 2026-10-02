using MemoryKeeper.Mobile.Configuration;
using MemoryKeeper.Mobile.Controls;
using MemoryKeeper.Mobile.Platforms.Android.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;

namespace MemoryKeeper.Mobile.Handlers;

public sealed class MobileVideoPlayerViewHandler
    : ViewHandler<MobileVideoPlayerView, AuthenticatedVideoPlayerView>
{
    public static readonly IPropertyMapper<MobileVideoPlayerView, MobileVideoPlayerViewHandler>
        PropertyMapper = new PropertyMapper<MobileVideoPlayerView, MobileVideoPlayerViewHandler>(
            ViewHandler.ViewMapper)
        {
            [nameof(MobileVideoPlayerView.PlaybackRequest)] = MapPlaybackRequest,
        };

    public MobileVideoPlayerViewHandler()
        : base(PropertyMapper)
    {
    }

    protected override AuthenticatedVideoPlayerView CreatePlatformView()
    {
        var configuration = MauiContext?.Services.GetRequiredService<IMobileBackendConfiguration>()
                            ?? throw new InvalidOperationException("Mobile Backend configuration is unavailable.");
        var context = Context
                      ?? throw new InvalidOperationException("Android context is unavailable.");
        return new AuthenticatedVideoPlayerView(context, configuration);
    }

    protected override void ConnectHandler(AuthenticatedVideoPlayerView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.PlaybackStateChanged += OnPlaybackStateChanged;
        VirtualView.PauseRequested += OnPauseRequested;
    }

    protected override void DisconnectHandler(AuthenticatedVideoPlayerView platformView)
    {
        VirtualView.PauseRequested -= OnPauseRequested;
        platformView.PlaybackStateChanged -= OnPlaybackStateChanged;
        platformView.ReleasePlayback();
        base.DisconnectHandler(platformView);
    }

    private static void MapPlaybackRequest(
        MobileVideoPlayerViewHandler handler,
        MobileVideoPlayerView view) =>
        handler.PlatformView.SetPlayback(view.PlaybackRequest);

    private void OnPauseRequested(object? sender, EventArgs e) => PlatformView.PausePlayback();

    private void OnPlaybackStateChanged(
        object? sender,
        MobileVideoPlaybackStateChangedEventArgs e) =>
        VirtualView.RaisePlaybackStateChanged(e);
}
