using MemoryKeeper.App.Diagnostics;
using Microsoft.UI.Xaml;

namespace MemoryKeeper.App.Views;

internal enum StartupImageLoadResult
{
    Opened,
    Failed,
    TimedOut,
    Closed,
}

public sealed partial class StartupWindow : Window
{
    private readonly TaskCompletionSource<StartupImageLoadResult> _imageReady = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public StartupWindow()
    {
        InitializeComponent();
        Title = "Memory Keeper";
        Closed += StartupWindow_OnClosed;

        if (AppWindow is not null)
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));
        }
    }

    internal async Task<StartupImageLoadResult> WaitForImageReadyAsync(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var timeoutTask = Task.Delay(timeout);
        var completedTask = await Task.WhenAny(_imageReady.Task, timeoutTask);
        if (completedTask == _imageReady.Task || _imageReady.Task.IsCompleted)
        {
            return await _imageReady.Task;
        }

        StartupDiagnostics.WriteStep("[StartupWindow] loading image readiness timed out.");
        return StartupImageLoadResult.TimedOut;
    }

    private void StartupImage_OnOpened(object sender, RoutedEventArgs e) =>
        _imageReady.TrySetResult(StartupImageLoadResult.Opened);

    private void StartupImage_OnFailed(object sender, ExceptionRoutedEventArgs e)
    {
        StartupDiagnostics.WriteStep(
            $"[StartupWindow] loading image failed: {e.ErrorMessage}");
        _imageReady.TrySetResult(StartupImageLoadResult.Failed);
    }

    private void StartupWindow_OnClosed(object sender, WindowEventArgs e) =>
        _imageReady.TrySetResult(StartupImageLoadResult.Closed);
}
