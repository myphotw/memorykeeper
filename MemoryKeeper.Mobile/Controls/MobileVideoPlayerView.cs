using Microsoft.Maui.Controls;

namespace MemoryKeeper.Mobile.Controls;

public sealed class MobileVideoPlayerView : View
{
    public static readonly BindableProperty PlaybackRequestProperty = BindableProperty.Create(
        nameof(PlaybackRequest),
        typeof(MobileVideoPlaybackRequest),
        typeof(MobileVideoPlayerView));

    public MobileVideoPlaybackRequest? PlaybackRequest
    {
        get => (MobileVideoPlaybackRequest?)GetValue(PlaybackRequestProperty);
        set => SetValue(PlaybackRequestProperty, value);
    }

    public event EventHandler<MobileVideoPlaybackStateChangedEventArgs>? PlaybackStateChanged;

    internal event EventHandler? PauseRequested;

    public void PausePlayback() => PauseRequested?.Invoke(this, EventArgs.Empty);

    internal void RaisePlaybackStateChanged(MobileVideoPlaybackStateChangedEventArgs args) =>
        PlaybackStateChanged?.Invoke(this, args);
}
