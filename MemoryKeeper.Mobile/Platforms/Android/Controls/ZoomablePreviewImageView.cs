using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using MemoryKeeper.Mobile.Controls;
using MemoryKeeper.Mobile.Images;
using Microsoft.Maui.ApplicationModel;
using AndroidColor = Android.Graphics.Color;
using Matrix = Android.Graphics.Matrix;

namespace MemoryKeeper.Mobile.Platforms.Android.Controls;

public sealed class ZoomablePreviewImageView : ImageView
{
    private readonly Matrix _imageMatrix = new();
    private readonly MobileZoomState _zoomState = new();
    private readonly ScaleGestureDetector _scaleDetector;
    private readonly GestureDetector _navigationGestureDetector;
    private readonly int _touchSlop;
    private readonly int _minimumFlingVelocity;
    private CancellationTokenSource? _decodeCancellation;
    private Bitmap? _bitmap;
    private long _decodeGeneration;
    private float _lastTouchX;
    private float _lastTouchY;
    private bool _touchSequenceHadMultiplePointers;

    public ZoomablePreviewImageView(Context context)
        : base(context)
    {
        SetBackgroundColor(AndroidColor.Black);
        SetScaleType(ImageView.ScaleType.Matrix);
        _scaleDetector = new ScaleGestureDetector(context, new ScaleListener(this));
        _navigationGestureDetector = new GestureDetector(context, new NavigationGestureListener(this));
        var viewConfiguration = ViewConfiguration.Get(context)
            ?? throw new InvalidOperationException("Android ViewConfiguration is unavailable.");
        _touchSlop = viewConfiguration.ScaledTouchSlop;
        _minimumFlingVelocity = viewConfiguration.ScaledMinimumFlingVelocity;
        Clickable = true;
    }

    public event EventHandler? PreviewLoaded;

    public event EventHandler? PreviewFailed;

    public event EventHandler? PreviousRequested;

    public event EventHandler? NextRequested;

    public void SetPreview(
        byte[]? bytes,
        string? fileId,
        string requestDescription)
    {
        var generation = Interlocked.Increment(ref _decodeGeneration);
        _decodeCancellation?.Cancel();
        _decodeCancellation?.Dispose();
        _decodeCancellation = null;
        ClearBitmap();

        if (bytes is not { Length: > 0 })
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _decodeCancellation = cancellation;
        _ = DecodeAsync(bytes, fileId, requestDescription, generation, cancellation);
    }

    public void ReleasePreview()
    {
        Interlocked.Increment(ref _decodeGeneration);
        _decodeCancellation?.Cancel();
        _decodeCancellation?.Dispose();
        _decodeCancellation = null;
        ClearBitmap();
    }

    public override bool OnTouchEvent(MotionEvent? motionEvent)
    {
        if (motionEvent is null)
        {
            return false;
        }

        if (motionEvent.ActionMasked == MotionEventActions.Down)
        {
            _touchSequenceHadMultiplePointers = false;
        }
        else if (motionEvent.PointerCount > 1
                 || motionEvent.ActionMasked == MotionEventActions.PointerDown)
        {
            _touchSequenceHadMultiplePointers = true;
        }

        if (_bitmap is not null)
        {
            _scaleDetector.OnTouchEvent(motionEvent);
        }

        _navigationGestureDetector.OnTouchEvent(motionEvent);
        switch (motionEvent.ActionMasked)
        {
            case MotionEventActions.Down:
                Parent?.RequestDisallowInterceptTouchEvent(true);
                _lastTouchX = motionEvent.GetX();
                _lastTouchY = motionEvent.GetY();
                break;
            case MotionEventActions.Move when _bitmap is not null
                                                   && !_scaleDetector.IsInProgress
                                                   && motionEvent.PointerCount == 1
                                                   && _zoomState.Scale > MobileZoomState.MinimumScale:
                var x = motionEvent.GetX();
                var y = motionEvent.GetY();
                _zoomState.PanBy(x - _lastTouchX, y - _lastTouchY);
                _lastTouchX = x;
                _lastTouchY = y;
                ApplyTransform();
                break;
            case MotionEventActions.PointerUp:
                CaptureRemainingPointer(motionEvent);
                break;
            case MotionEventActions.Up:
                Parent?.RequestDisallowInterceptTouchEvent(false);
                PerformClick();
                break;
            case MotionEventActions.Cancel:
                Parent?.RequestDisallowInterceptTouchEvent(false);
                break;
        }

        return true;
    }

    public override bool PerformClick()
    {
        base.PerformClick();
        return true;
    }

    protected override void OnSizeChanged(int width, int height, int oldWidth, int oldHeight)
    {
        base.OnSizeChanged(width, height, oldWidth, oldHeight);
        _zoomState.SetViewport(width, height);
        ApplyTransform();
    }

    private async Task DecodeAsync(
        byte[] bytes,
        string? fileId,
        string requestDescription,
        long generation,
        CancellationTokenSource cancellation)
    {
        Bitmap? decoded = null;
        try
        {
            decoded = await Task.Run(() =>
            {
                cancellation.Token.ThrowIfCancellationRequested();
                return BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
            }, cancellation.Token).ConfigureAwait(false);

            cancellation.Token.ThrowIfCancellationRequested();
            var decodedBitmap = decoded;
            decoded = null;
            MainThread.BeginInvokeOnMainThread(() =>
                CompleteDecode(decodedBitmap, fileId, requestDescription, generation, cancellation));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            decoded?.Dispose();
        }
        catch (Exception exception)
        {
            decoded?.Dispose();
            MainThread.BeginInvokeOnMainThread(() =>
                FailDecode(fileId, requestDescription, generation, cancellation, exception));
        }
    }

    private void CompleteDecode(
        Bitmap? decoded,
        string? fileId,
        string requestDescription,
        long generation,
        CancellationTokenSource cancellation)
    {
        if (!IsCurrentDecode(generation, cancellation))
        {
            decoded?.Dispose();
            return;
        }

        FinishDecode(cancellation);
        if (decoded is null)
        {
            MobilePreviewDiagnostics.WriteFailure(fileId, requestDescription, "android-decode");
            PreviewFailed?.Invoke(this, EventArgs.Empty);
            return;
        }

        try
        {
            _bitmap = decoded;
            SetImageBitmap(decoded);
            _zoomState.SetImageSize(decoded.Width, decoded.Height);
            ApplyTransform();
            PreviewLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            ClearBitmap();
            MobilePreviewDiagnostics.WriteFailure(
                fileId,
                requestDescription,
                "android-render",
                exception: exception);
            PreviewFailed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void FailDecode(
        string? fileId,
        string requestDescription,
        long generation,
        CancellationTokenSource cancellation,
        Exception exception)
    {
        if (!IsCurrentDecode(generation, cancellation))
        {
            return;
        }

        FinishDecode(cancellation);
        MobilePreviewDiagnostics.WriteFailure(
            fileId,
            requestDescription,
            "android-decode",
            exception: exception);
        PreviewFailed?.Invoke(this, EventArgs.Empty);
    }

    private bool IsCurrentDecode(long generation, CancellationTokenSource cancellation) =>
        generation == Volatile.Read(ref _decodeGeneration)
        && ReferenceEquals(_decodeCancellation, cancellation)
        && !cancellation.IsCancellationRequested;

    private void FinishDecode(CancellationTokenSource cancellation)
    {
        if (ReferenceEquals(_decodeCancellation, cancellation))
        {
            _decodeCancellation = null;
        }

        cancellation.Dispose();
    }

    private void ApplyScale(float scaleFactor, float focusX, float focusY)
    {
        _zoomState.ApplyScale(scaleFactor, focusX, focusY);
        ApplyTransform();
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
            _zoomState.Scale,
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

    private void ApplyTransform()
    {
        if (_bitmap is null || Width <= 0 || Height <= 0)
        {
            return;
        }

        var transform = _zoomState.GetTransform();
        _imageMatrix.Reset();
        _imageMatrix.SetScale((float)transform.ContentScale, (float)transform.ContentScale);
        _imageMatrix.PostTranslate(
            (float)transform.TranslationX,
            (float)transform.TranslationY);
        ImageMatrix = _imageMatrix;
    }

    private void CaptureRemainingPointer(MotionEvent motionEvent)
    {
        if (motionEvent.PointerCount <= 1)
        {
            return;
        }

        var remainingIndex = motionEvent.ActionIndex == 0 ? 1 : 0;
        _lastTouchX = motionEvent.GetX(remainingIndex);
        _lastTouchY = motionEvent.GetY(remainingIndex);
    }

    private void ClearBitmap()
    {
        SetImageDrawable(null);
        _bitmap?.Dispose();
        _bitmap = null;
        _zoomState.Reset();
        _imageMatrix.Reset();
        ImageMatrix = _imageMatrix;
    }

    private sealed class ScaleListener : ScaleGestureDetector.SimpleOnScaleGestureListener
    {
        private readonly ZoomablePreviewImageView _owner;

        public ScaleListener(ZoomablePreviewImageView owner) => _owner = owner;

        public override bool OnScale(ScaleGestureDetector detector)
        {
            _owner.ApplyScale(detector.ScaleFactor, detector.FocusX, detector.FocusY);
            return true;
        }

        public override bool OnScaleBegin(ScaleGestureDetector detector) => true;
    }

    private sealed class NavigationGestureListener : GestureDetector.SimpleOnGestureListener
    {
        private readonly ZoomablePreviewImageView _owner;

        public NavigationGestureListener(ZoomablePreviewImageView owner) => _owner = owner;

        public override bool OnDown(MotionEvent e) => true;

        public override bool OnFling(
            MotionEvent? e1,
            MotionEvent e2,
            float velocityX,
            float velocityY) =>
            _owner.TryRequestNavigation(e1, e2, velocityX);
    }
}
