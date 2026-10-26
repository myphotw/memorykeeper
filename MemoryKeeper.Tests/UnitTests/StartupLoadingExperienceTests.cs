namespace MemoryKeeper.Tests.UnitTests;

public sealed class StartupLoadingExperienceTests
{
    [Fact]
    public void MobileColdStart_UsesBrandedArtworkOnceWithoutTechnicalStatusText()
    {
        var xaml = ReadSource("MemoryKeeper.Mobile", "Views", "HomePage.xaml");
        var code = ReadSource("MemoryKeeper.Mobile", "Views", "HomePage.xaml.cs");
        var overlay = Slice(
            xaml,
            "x:Name=\"StartupLoadingOverlay\"",
            "</ContentPage>");

        Assert.Contains("Source=\"memorykeeper_loading.png\"", overlay, StringComparison.Ordinal);
        Assert.Contains("Aspect=\"AspectFit\"", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("<Label", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("ActivityIndicator", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("StartupStageText", xaml, StringComparison.Ordinal);
        Assert.Contains("MinimumStartupDisplayDuration = TimeSpan.FromMilliseconds(1500)", code, StringComparison.Ordinal);
        Assert.Contains("StartupFadeDurationMilliseconds = 200", code, StringComparison.Ordinal);
        Assert.Contains("skipMinimumDuration: ViewModel.IsErrorStateVisible", code, StringComparison.Ordinal);
        Assert.Contains("if (_startupOverlayDismissed)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("StartupLoadingOverlay.IsVisible = true", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsColdStart_InitializesThenShowsMainWindowWithoutCustomLoadingWindowOrDelay()
    {
        var app = ReadSource("MemoryKeeper.App", "App.xaml.cs");
        var project = ReadSource("MemoryKeeper.App", "MemoryKeeper.App.csproj");
        var enqueueIndex = app.IndexOf("DispatcherQueuePriority.Low", StringComparison.Ordinal);
        var initializeIndex = app.IndexOf("DatabaseInitializer.InitializeAsync(", StringComparison.Ordinal);
        var activateIndex = app.IndexOf("_mainWindow.Activate();", StringComparison.Ordinal);

        Assert.True(enqueueIndex >= 0 && initializeIndex > enqueueIndex);
        Assert.True(activateIndex > initializeIndex);
        Assert.DoesNotContain("new StartupWindow", app, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowStartupWindow", app, StringComparison.Ordinal);
        Assert.DoesNotContain("CloseStartupWindow", app, StringComparison.Ordinal);
        Assert.DoesNotContain("AlignMainWindowWithStartup", app, StringComparison.Ordinal);
        Assert.DoesNotContain("memorykeeper_loading.png", project, StringComparison.Ordinal);
        Assert.DoesNotContain("MinimumStartupDisplayDuration", app, StringComparison.Ordinal);
        Assert.DoesNotContain("minimumStartupDisplay", app, StringComparison.Ordinal);
        Assert.Contains("async () => await InitializeApplicationAsync()", app, StringComparison.Ordinal);
        Assert.DoesNotContain(".Wait()", app, StringComparison.Ordinal);
        Assert.DoesNotContain(".Result", app, StringComparison.Ordinal);
        Assert.DoesNotContain("Thread.Sleep", app, StringComparison.Ordinal);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }

    private static string ReadSource(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "MemoryKeeper.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine([directory!.FullName, .. segments]));
    }
}
