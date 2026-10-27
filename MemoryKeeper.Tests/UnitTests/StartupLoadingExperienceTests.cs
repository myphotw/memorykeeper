namespace MemoryKeeper.Tests.UnitTests;

public sealed class StartupLoadingExperienceTests
{
    [Fact]
    public void MobileColdStart_UsesBrandedArtworkOnceWithoutTechnicalStatusText()
    {
        var xaml = ReadSource("MemoryKeeper.Mobile", "Views", "HomePage.xaml");
        var code = ReadSource("MemoryKeeper.Mobile", "Views", "HomePage.xaml.cs");
        var project = ReadSource("MemoryKeeper.Mobile", "MemoryKeeper.Mobile.csproj");
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
        Assert.Contains("StartupDisplayTimeout = TimeSpan.FromSeconds(8)", code, StringComparison.Ordinal);
        Assert.Contains("StartupFadeDuration = TimeSpan.FromMilliseconds(200)", code, StringComparison.Ordinal);
        Assert.Contains("StartupFadeDurationMilliseconds = 200", code, StringComparison.Ordinal);
        Assert.Contains("EarliestFadeStart = MinimumStartupDisplayDuration - StartupFadeDuration", code, StringComparison.Ordinal);
        Assert.Contains("LatestFadeStart = StartupDisplayTimeout - StartupFadeDuration", code, StringComparison.Ordinal);
        Assert.Contains("_ = EnforceStartupOverlayMaximumAsync()", code, StringComparison.Ordinal);
        Assert.Contains("RemainingUntil(EarliestFadeStart)", code, StringComparison.Ordinal);
        Assert.Contains("RemainingUntil(LatestFadeStart)", code, StringComparison.Ordinal);
        Assert.Contains("DismissStartupOverlayAsync(enforceMinimumDuration: true)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("skipMinimumDuration", code, StringComparison.Ordinal);
        Assert.Contains("if (_startupOverlayDismissed)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("StartupLoadingOverlay.IsVisible = true", code, StringComparison.Ordinal);
        Assert.Contains(
            "<MauiImage Include=\"Resources\\Images\\memorykeeper_loading.png\"",
            project,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsColdStart_ShowsBrandedInShellOverlayWhileRequiredDataWarms()
    {
        var app = ReadSource("MemoryKeeper.App", "App.xaml.cs");
        var project = ReadSource("MemoryKeeper.App", "MemoryKeeper.App.csproj");
        var window = ReadSource("MemoryKeeper.App", "Views", "MainWindow.xaml");
        var windowCode = ReadSource("MemoryKeeper.App", "Views", "MainWindow.xaml.cs");
        var enqueueIndex = app.IndexOf("DispatcherQueuePriority.Low", StringComparison.Ordinal);
        var initializeIndex = app.IndexOf("DatabaseInitializer.InitializeAsync(", StringComparison.Ordinal);
        var activateIndex = app.IndexOf("_mainWindow.Activate();", StringComparison.Ordinal);
        var readyIndex = app.IndexOf("mainWindow.CompleteApplicationInitialization();", StringComparison.Ordinal);

        Assert.True(enqueueIndex >= 0 && activateIndex > enqueueIndex);
        Assert.True(initializeIndex > activateIndex);
        Assert.True(readyIndex > initializeIndex);
        Assert.DoesNotContain("new StartupWindow", app, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowStartupWindow", app, StringComparison.Ordinal);
        Assert.DoesNotContain("CloseStartupWindow", app, StringComparison.Ordinal);
        Assert.DoesNotContain("AlignMainWindowWithStartup", app, StringComparison.Ordinal);
        Assert.Contains("memorykeeper_loading.png", project, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StartupLoadingOverlay\"", window, StringComparison.Ordinal);
        Assert.Contains("ms-appx:///Assets/memorykeeper_loading.png", window, StringComparison.Ordinal);
        Assert.Contains("Stretch=\"Uniform\"", window, StringComparison.Ordinal);
        Assert.Contains("await _applicationInitialization.Task", windowCode, StringComparison.Ordinal);
        Assert.Contains("_startupPreload.EnsureRequiredAsync()", windowCode, StringComparison.Ordinal);
        Assert.Contains("MinimumStartupDisplayDuration = TimeSpan.FromMilliseconds(1500)", windowCode, StringComparison.Ordinal);
        Assert.Contains("StartupFadeDuration = TimeSpan.FromMilliseconds(200)", windowCode, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromSeconds(8)", windowCode, StringComparison.Ordinal);
        Assert.Contains("EarliestFadeStart = MinimumStartupDisplayDuration - StartupFadeDuration", windowCode, StringComparison.Ordinal);
        Assert.Contains("LatestFadeStart = StartupDataTimeout - StartupFadeDuration", windowCode, StringComparison.Ordinal);
        Assert.Contains("RemainingUntil(LatestFadeStart)", windowCode, StringComparison.Ordinal);
        Assert.Contains("RemainingUntil(EarliestFadeStart)", windowCode, StringComparison.Ordinal);
        Assert.Contains("_startupOverlayLifetime.Start()", windowCode, StringComparison.Ordinal);
        Assert.Contains("_ = EnforceStartupOverlayMaximumAsync()", windowCode, StringComparison.Ordinal);
        Assert.Contains("DoubleAnimation", windowCode, StringComparison.Ordinal);
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
