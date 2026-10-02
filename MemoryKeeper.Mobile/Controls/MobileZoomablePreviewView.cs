using Microsoft.Maui.Controls;

namespace MemoryKeeper.Mobile.Controls;

public sealed class MobileZoomablePreviewView : View
{
    public static readonly BindableProperty PreviewBytesProperty = BindableProperty.Create(
        nameof(PreviewBytes),
        typeof(byte[]),
        typeof(MobileZoomablePreviewView));

    public static readonly BindableProperty PreviewFileIdProperty = BindableProperty.Create(
        nameof(PreviewFileId),
        typeof(string),
        typeof(MobileZoomablePreviewView));

    public static readonly BindableProperty PreviewRequestDescriptionProperty = BindableProperty.Create(
        nameof(PreviewRequestDescription),
        typeof(string),
        typeof(MobileZoomablePreviewView),
        "missing");

    public byte[]? PreviewBytes
    {
        get => (byte[]?)GetValue(PreviewBytesProperty);
        set => SetValue(PreviewBytesProperty, value);
    }

    public string? PreviewFileId
    {
        get => (string?)GetValue(PreviewFileIdProperty);
        set => SetValue(PreviewFileIdProperty, value);
    }

    public string PreviewRequestDescription
    {
        get => (string)GetValue(PreviewRequestDescriptionProperty);
        set => SetValue(PreviewRequestDescriptionProperty, value);
    }

    public event EventHandler? PreviewLoaded;

    public event EventHandler? PreviewFailed;

    public event EventHandler? PreviousRequested;

    public event EventHandler? NextRequested;

    internal void RaisePreviewLoaded() => PreviewLoaded?.Invoke(this, EventArgs.Empty);

    internal void RaisePreviewFailed() => PreviewFailed?.Invoke(this, EventArgs.Empty);

    internal void RaisePreviousRequested() => PreviousRequested?.Invoke(this, EventArgs.Empty);

    internal void RaiseNextRequested() => NextRequested?.Invoke(this, EventArgs.Empty);
}
