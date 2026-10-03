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
    private readonly GestureDetector _navigationGestureDetector;
    private readonly int _touchSlop;
    private readonly int _minimumFlingVelocity;
    private IExoPlayer? _player;
    private MobileVideoPlaybackRequest? _currentRequest;
    private MobileVideoPlaybackState _lastState;
    private bool _navigationGestureBlocked;
    private bool _touchSequenceHadMultiplePointers;

    public AuthenticatedVideoPlayerView(
        Context context,
        IMobileBackendConfiguration configuration)
        : base(context)
    {
        _context = context;
        _configuration = configuration;
        _pollPlaybackAction = PollPlayback;
        _navigationGestureDetector = new GestureDetector(
            context,
            new NavigationGestureListener(this));
        var viewConfiguration = ViewConfiguration.Get(context)
            ?? throw new InvalidOperationException("Android ViewConfiguration is unavailable.");
        _touchSlop = viewConfiguration.ScaledTouchSlop;
        _minimumFlingVelocity = viewConfiguration.ScaledMinimumFlingVelocity;
        UseController = true;
        ControllerAutoShow = true;
        ControllerHideOnTouch = true;
        ControllerShowTimeoutMs = 3000;
        // The MAUI overlay owns the single buffering indicator for consistent Viewer state.
        SetShowBuffering(ShowBufferingNever);
        SetBackgroundColor(global::Android.Graphics.Color.Black);
    }

    public event EventHandler<MobileVideoPlaybackStateChangedEventArgs>? PlaybackStateChanged;

    public event EventHandler? PreviousRequested;

    public event EventHandler? NextRequested;

    public override bool DispatchTouchEvent(MotionEvent? motionEvent)
    {
        if (motionEvent is null)
        {
            return false;
        }

        switch (motionEvent.ActionMasked)
        {
            case MotionEventActions.Down:
                _touchSequenceHadMultiplePointers = false;
                _navigationGestureBlocked = !IsTouchInsideMedia3View("exo_content_frame", motionEvent)
                                            || IsTouchInsideVisibleController(motionEvent);
                break;
            case MotionEventActions.PointerDown:
                _touchSequenceHadMultiplePointers = true;
                break;
        }

        var handled = base.DispatchTouchEvent(motionEvent);
        if (!_navigationGestureBlocked)
        {
            _navigationGestureDetector.OnTouchEvent(motionEvent);
        }

        if (motionEvent.ActionMasked is MotionEventActions.Up or MotionEventActions.Cancel)
        {
            _navigationGestureBlocked = false;
            _touchSequenceHadMultiplePointers = false;
        }

        return handled;
    }

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
            var originalUrl = BackendMediaUrlResolver.ResolveOriginalUrl(
                request.BackendBaseUri.ToString(),
                request.FileId);
            if (!Uri.TryCreate(originalUrl, UriKind.Absolute, out var mediaUri)
                || (mediaUri.Scheme != Uri.UriSchemeHttps && mediaUri.Scheme != Uri.UriSchemeHttp))
            {
                throw new MobileBackendConfigurationException();
            }

            var requestHeaders = MobileVideoPlaybackAuthorization.CreateRequestHeaders(
                _configuration,
                request.BackendBaseUri,
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

    private bool IsTouchInsideVisibleController(MotionEvent motionEvent) =>
        IsTouchInsideMedia3View("memorykeeper_controller_area", motionEvent, requireVisible: true)
        || IsTouchInsideMedia3View("exo_center_controls", motionEvent, requireVisible: true)
        || IsTouchInsideMedia3View("exo_progress", motionEvent, requireVisible: true)
        || IsTouchInsideMedia3View("exo_bottom_bar", motionEvent, requireVisible: true)
        || IsTouchInsideMedia3View("exo_minimal_controls", motionEvent, requireVisible: true)
        || IsTouchInsideMedia3View("exo_extra_controls_scroll_view", motionEvent, requireVisible: true);

    private bool IsTouchInsideMedia3View(
        string resourceName,
        MotionEvent motionEvent,
        bool requireVisible = false)
    {
        var resources = Resources;
        var packageName = _context.PackageName;
        if (resources is null || string.IsNullOrWhiteSpace(packageName))
        {
            return false;
        }

        var resourceId = resources.GetIdentifier(resourceName, "id", packageName);
        var view = resourceId == 0 ? null : FindViewById(resourceId);
        if (view is null
            || (requireVisible
                && (!view.IsShown || view.Visibility != ViewStates.Visible || view.Alpha <= 0.01f)))
        {
            return false;
        }

        var location = new int[2];
        view.GetLocationOnScreen(location);
        return motionEvent.RawX >= location[0]
               && motionEvent.RawX < location[0] + view.Width
               && motionEvent.RawY >= location[1]
               && motionEvent.RawY < location[1] + view.Height;
    }

    private bool TryRequestNavigation(
        MotionEvent? start,
        MotionEvent? end,
        float velocityX)
    {
        if (start is null || end is null)
        {
            return false;
        }

        var direction = MobileViewerGestureDecision.ResolveSwipe(
            MobileZoomState.MinimumScale,
            _touchSequenceHadMultiplePointers,
            end.GetX() - start.GetX(),
            end.GetY() - start.GetY(),
            velocityX,
            _touchSlop,
            _minimumFlingVelocity);
        switch (direction)
        {
            case MobileViewerNavigationDirection.Previous:
                PreviousRequested?.Invoke(this, EventArgs.Empty);
                return true;
            case MobileViewerNavigationDirection.Next:
                NextRequested?.Invoke(this, EventArgs.Empty);
                return true;
            default:
                return false;
        }
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

    private sealed class NavigationGestureListener : GestureDetector.SimpleOnGestureListener
    {
        private readonly AuthenticatedVideoPlayerView _owner;

        public NavigationGestureListener(AuthenticatedVideoPlayerView owner) => _owner = owner;

        public override bool OnDown(MotionEvent e) => true;

        public override bool OnFling(
            MotionEvent? e1,
            MotionEvent e2,
            float velocityX,
            float velocityY) =>
            _owner.TryRequestNavigation(e1, e2, velocityX);
    }
}
