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
    public void WindowsColdStart_RendersStartupWindowBeforeInitializationWithoutArtificialDelay()
    {
        var app = ReadSource("MemoryKeeper.App", "App.xaml.cs");
        var startupXaml = ReadSource("MemoryKeeper.App", "Views", "StartupWindow.xaml");
        var startupCode = ReadSource("MemoryKeeper.App", "Views", "StartupWindow.xaml.cs");
        var project = ReadSource("MemoryKeeper.App", "MemoryKeeper.App.csproj");
        var deployment = ReadSource("MemoryKeeper.App", "build", "DeployFixedRelease.ps1");

        var showIndex = app.IndexOf("ShowStartupWindow();", StringComparison.Ordinal);
        var enqueueIndex = app.IndexOf("DispatcherQueuePriority.Low", StringComparison.Ordinal);
        var initializeIndex = app.IndexOf("DatabaseInitializer.InitializeAsync(", StringComparison.Ordinal);
        var activateIndex = app.IndexOf("_mainWindow.Activate();", StringComparison.Ordinal);
        var closeIndex = app.IndexOf("CloseStartupWindow();", activateIndex, StringComparison.Ordinal);

        Assert.True(showIndex >= 0 && enqueueIndex > showIndex && initializeIndex > enqueueIndex);
        Assert.True(activateIndex > initializeIndex);
        Assert.True(closeIndex > activateIndex);
        Assert.DoesNotContain("MinimumStartupDisplayDuration", app, StringComparison.Ordinal);
        Assert.DoesNotContain("minimumStartupDisplay", app, StringComparison.Ordinal);
        Assert.Contains("async () => await InitializeApplicationAsync()", app, StringComparison.Ordinal);
        Assert.Contains("StartupImageReadyTimeout = TimeSpan.FromSeconds(2)", app, StringComparison.Ordinal);
        Assert.Contains("_ = ObserveStartupImageAsync(_startupWindow);", app, StringComparison.Ordinal);
        Assert.Contains("mainAppWindow.Move(startupAppWindow.Position);", app, StringComparison.Ordinal);
        Assert.Contains("Source=\"ms-appx:///Assets/memorykeeper_loading.png\"", startupXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StartupImage\"", startupXaml, StringComparison.Ordinal);
        Assert.Contains("ImageOpened=\"StartupImage_OnOpened\"", startupXaml, StringComparison.Ordinal);
        Assert.Contains("ImageFailed=\"StartupImage_OnFailed\"", startupXaml, StringComparison.Ordinal);
        Assert.Contains("Stretch=\"Uniform\"", startupXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TextBlock", startupXaml, StringComparison.Ordinal);
        Assert.Contains("TaskCompletionSource<StartupImageLoadResult>", startupCode, StringComparison.Ordinal);
        Assert.Contains("TaskCreationOptions.RunContinuationsAsynchronously", startupCode, StringComparison.Ordinal);
        Assert.Contains("Task.WhenAny(_imageReady.Task, timeoutTask)", startupCode, StringComparison.Ordinal);
        Assert.Contains("StartupImageLoadResult.TimedOut", startupCode, StringComparison.Ordinal);
        Assert.Contains("StartupImageLoadResult.Closed", startupCode, StringComparison.Ordinal);
        Assert.Contains("loading image failed", startupCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".Wait()", app, StringComparison.Ordinal);
        Assert.DoesNotContain(".Result", app, StringComparison.Ordinal);
        Assert.DoesNotContain("Thread.Sleep", app, StringComparison.Ordinal);
        Assert.Contains("..\\MemoryKeeper.Mobile\\Resources\\Images\\memorykeeper_loading.png", project, StringComparison.Ordinal);
        Assert.Contains("Link=\"Assets\\memorykeeper_loading.png\"", project, StringComparison.Ordinal);
        Assert.Contains("$(MSBuildProjectDirectory)\\publish\\**", project, StringComparison.Ordinal);
        Assert.DoesNotContain("$_.Name -ne \"Diagnostics\"", deployment, StringComparison.Ordinal);
        Assert.Contains("Diagnostics\\ErrorDialogWindow.xaml", deployment, StringComparison.Ordinal);
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
