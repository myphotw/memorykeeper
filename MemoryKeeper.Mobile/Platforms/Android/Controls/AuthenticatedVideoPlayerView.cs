using Android.Content;
using Android.Views;
using AndroidX.Media3.Common;
using AndroidX.Media3.DataSource;
using AndroidX.Media3.ExoPlayer;
using AndroidX.Media3.ExoPlayer.Source;
using AndroidX.Media3.UI;
using MemoryKeeper.Application;
using MemoryKeeper.Mobile.Configuration;
using MemoryKeeper.Mobile.Controls;
using MemoryKeeper.Mobile.Http;

namespace MemoryKeeper.Mobile.Platforms.Android.Controls;

public sealed class AuthenticatedVideoPlayerView : PlayerView
{
    // androidx.media3.common.Player state constants. Keeping them local avoids binding-version
    // implementation details while preserving Media3's stable public state contract.
    private const int PlaybackStateIdle = 1;
    private const int PlaybackStateBuffering = 2;
    private const int PlaybackStateReady = 3;
    private const int PlaybackStateEnded = 4;
    private const long PlaybackPollIntervalMilliseconds = 250;

    private readonly Context _context;
    private readonly IMobileBackendConfiguration _configuration;
    private readonly Action _pollPlaybackAction;
    private IExoPlayer? _player;
    private MobileVideoPlaybackRequest? _currentRequest;
    private MobileVideoPlaybackState _lastState;

    public AuthenticatedVideoPlayerView(
        Context context,
        IMobileBackendConfiguration configuration)
        : base(context)
    {
        _context = context;
        _configuration = configuration;
        _pollPlaybackAction = PollPlayback;
        UseController = true;
        ControllerAutoShow = true;
        ControllerHideOnTouch = true;
        ControllerShowTimeoutMs = 3000;
        // The MAUI overlay owns the single buffering indicator for consistent Viewer state.
        SetShowBuffering(ShowBufferingNever);
        SetBackgroundColor(global::Android.Graphics.Color.Black);
    }

    public event EventHandler<MobileVideoPlaybackStateChangedEventArgs>? PlaybackStateChanged;

    public void SetPlayback(MobileVideoPlaybackRequest? request)
    {
        if (Equals(_currentRequest, request))
        {
            return;
        }

        ReleasePlaybackResources();
        _lastState = MobileVideoPlaybackState.None;
        _currentRequest = request;
        if (request is null)
        {
            return;
        }

        EmitState(request, MobileVideoPlaybackState.Preparing);
        try
        {
            var baseUri = _configuration.BaseUri ?? throw new MobileBackendConfigurationException();
            var originalUrl = BackendMediaUrlResolver.ResolveOriginalUrl(
                baseUri.ToString(),
                request.FileId);
            if (!Uri.TryCreate(originalUrl, UriKind.Absolute, out var mediaUri)
                || (mediaUri.Scheme != Uri.UriSchemeHttps && mediaUri.Scheme != Uri.UriSchemeHttp))
            {
                throw new MobileBackendConfigurationException();
            }

            var requestHeaders = MobileVideoPlaybackAuthorization.CreateRequestHeaders(
                _configuration,
                mediaUri);
            var dataSourceFactory = new DefaultHttpDataSource.Factory();
            dataSourceFactory.SetAllowCrossProtocolRedirects(false);
            dataSourceFactory.SetDefaultRequestProperties(requestHeaders);

            var mediaItem = MediaItem.FromUri(mediaUri.AbsoluteUri)
                            ?? throw new InvalidOperationException("Media3 could not create a media item.");
            var mediaSource = new ProgressiveMediaSource.Factory(dataSourceFactory)
                                  .CreateMediaSource(mediaItem)
                              ?? throw new InvalidOperationException("Media3 could not create a media source.");
            var player = new ExoPlayerBuilder(_context).Build()
                         ?? throw new InvalidOperationException("Media3 could not create a player.");
            _player = player;
            Player = player;
            player.SetMediaSource(mediaSource);
            player.PlayWhenReady = true;
            player.Prepare();
            Post(_pollPlaybackAction);
        }
        catch (Exception exception)
        {
            ReleasePlaybackResources();
            _currentRequest = request;
            EmitState(request, MobileVideoPlaybackState.Failed, exception.GetType().Name);
        }
    }

    public void PausePlayback()
    {
        if (_player is { IsPlaying: true } player)
        {
            player.Pause();
        }
    }

    public void ReleasePlayback()
    {
        _currentRequest = null;
        _lastState = MobileVideoPlaybackState.None;
        ReleasePlaybackResources();
    }

    protected override void OnWindowVisibilityChanged(ViewStates visibility)
    {
        base.OnWindowVisibilityChanged(visibility);
        if (visibility != ViewStates.Visible)
        {
            PausePlayback();
        }
    }

    protected override void OnDetachedFromWindow()
    {
        PausePlayback();
        base.OnDetachedFromWindow();
    }

    private void PollPlayback()
    {
        var player = _player;
        var request = _currentRequest;
        if (player is null || request is null)
        {
            return;
        }

        var failure = player.PlayerError;
        if (failure is not null)
        {
            EmitState(request, MobileVideoPlaybackState.Failed, failure.GetType().Name);
            return;
        }

        var state = player.PlaybackState switch
        {
            PlaybackStateBuffering => MobileVideoPlaybackState.Buffering,
            PlaybackStateReady when player.IsPlaying => MobileVideoPlaybackState.Playing,
            PlaybackStateReady when player.PlayWhenReady => MobileVideoPlaybackState.Ready,
            PlaybackStateReady => MobileVideoPlaybackState.Paused,
            PlaybackStateEnded => MobileVideoPlaybackState.Paused,
            PlaybackStateIdle => MobileVideoPlaybackState.Preparing,
            _ => MobileVideoPlaybackState.Preparing,
        };
        EmitState(request, state);

        if (ReferenceEquals(_player, player) && Equals(_currentRequest, request))
        {
            PostDelayed(_pollPlaybackAction, PlaybackPollIntervalMilliseconds);
        }
    }

    private void EmitState(
        MobileVideoPlaybackRequest request,
        MobileVideoPlaybackState state,
        string? failureType = null)
    {
        if (!Equals(_currentRequest, request) || (_lastState == state && failureType is null))
        {
            return;
        }

        _lastState = state;
        PlaybackStateChanged?.Invoke(
            this,
            new MobileVideoPlaybackStateChangedEventArgs(request, state, failureType));
    }

    private void ReleasePlaybackResources()
    {
        RemoveCallbacks(_pollPlaybackAction);
        var player = _player;
        _player = null;
        Player = null;
        if (player is null)
        {
            return;
        }

        try
        {
            player.Stop();
            player.ClearMediaItems();
        }
        finally
        {
            try
            {
                player.Release();
            }
            finally
            {
                player.Dispose();
            }
        }
    }
}
