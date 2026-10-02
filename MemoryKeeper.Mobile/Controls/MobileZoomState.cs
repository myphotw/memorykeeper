namespace MemoryKeeper.Mobile.Controls;

public sealed class MobileZoomState
{
    public const double MinimumScale = 1;
    public const double MaximumScale = 4;

    private double _viewportWidth;
    private double _viewportHeight;
    private double _imageWidth;
    private double _imageHeight;

    public double Scale { get; private set; } = MinimumScale;

    public double TranslationX { get; private set; }

    public double TranslationY { get; private set; }

    public void SetViewport(double width, double height)
    {
        _viewportWidth = Math.Max(0, width);
        _viewportHeight = Math.Max(0, height);
        ClampTranslation();
    }

    public void SetImageSize(double width, double height)
    {
        _imageWidth = Math.Max(0, width);
        _imageHeight = Math.Max(0, height);
        Reset();
    }

    public void ApplyScale(double scaleFactor, double focusX, double focusY)
    {
        if (!double.IsFinite(scaleFactor) || scaleFactor <= 0)
        {
            return;
        }

        var previousScale = Scale;
        var nextScale = Math.Clamp(previousScale * scaleFactor, MinimumScale, MaximumScale);
        if (nextScale <= MinimumScale)
        {
            Reset();
            return;
        }

        var ratio = nextScale / previousScale;
        var focusOffsetX = focusX - (_viewportWidth / 2);
        var focusOffsetY = focusY - (_viewportHeight / 2);
        TranslationX = focusOffsetX - ((focusOffsetX - TranslationX) * ratio);
        TranslationY = focusOffsetY - ((focusOffsetY - TranslationY) * ratio);
        Scale = nextScale;
        ClampTranslation();
    }

    public void PanBy(double deltaX, double deltaY)
    {
        if (Scale <= MinimumScale)
        {
            return;
        }

        TranslationX += deltaX;
        TranslationY += deltaY;
        ClampTranslation();
    }

    public void Reset()
    {
        Scale = MinimumScale;
        TranslationX = 0;
        TranslationY = 0;
    }

    public MobileZoomTransform GetTransform()
    {
        if (_viewportWidth <= 0 || _viewportHeight <= 0 || _imageWidth <= 0 || _imageHeight <= 0)
        {
            return new MobileZoomTransform(1, 0, 0);
        }

        var fitScale = Math.Min(_viewportWidth / _imageWidth, _viewportHeight / _imageHeight);
        var contentScale = fitScale * Scale;
        var displayedWidth = _imageWidth * contentScale;
        var displayedHeight = _imageHeight * contentScale;
        var left = ((_viewportWidth - displayedWidth) / 2) + TranslationX;
        var top = ((_viewportHeight - displayedHeight) / 2) + TranslationY;
        return new MobileZoomTransform(contentScale, left, top);
    }

    private void ClampTranslation()
    {
        if (Scale <= MinimumScale)
        {
            TranslationX = 0;
            TranslationY = 0;
            return;
        }

        var transform = GetUntranslatedDisplaySize();
        var maximumX = Math.Max(0, (transform.Width - _viewportWidth) / 2);
        var maximumY = Math.Max(0, (transform.Height - _viewportHeight) / 2);
        TranslationX = Math.Clamp(TranslationX, -maximumX, maximumX);
        TranslationY = Math.Clamp(TranslationY, -maximumY, maximumY);
    }

    private (double Width, double Height) GetUntranslatedDisplaySize()
    {
        if (_viewportWidth <= 0 || _viewportHeight <= 0 || _imageWidth <= 0 || _imageHeight <= 0)
        {
            return (0, 0);
        }

        var fitScale = Math.Min(_viewportWidth / _imageWidth, _viewportHeight / _imageHeight);
        return (_imageWidth * fitScale * Scale, _imageHeight * fitScale * Scale);
    }
}

public readonly record struct MobileZoomTransform(
    double ContentScale,
    double TranslationX,
    double TranslationY);
