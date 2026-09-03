using System.Xml.Linq;

namespace MemoryKeeper.Tests.UnitTests;

// Structural guards complement (and do not replace) real WinUI interaction testing.
public sealed class PhotoViewerVideoUxRegressionTests
{
    [Fact]
    public void Autoplay_IsOnlyStartedByCurrentPlayersReadyEvent_AndLoadingEndsOnRelease()
    {
        var source = ReadSource("Views", "PhotoViewerPage.xaml.cs");
        var ready = Section(source, "private void OnVideoOpened", "private void OnVideoFailed");
        var stop = Section(source, "private void StopVideo()", "private static void TryReleaseVideoResource");
        var viewModel = ReadSource("ViewModels", "PhotoViewerViewModel.cs");
        var release = Section(viewModel, "private void ReleaseVideo()", "private void RefreshNavigationState()");

        Assert.Contains("!_viewerActive || !ViewModel.IsVideo || !ReferenceEquals(sender, _videoPlayer) || _videoOpened", ready);
        Assert.True(ready.IndexOf("_videoOpened = true", StringComparison.Ordinal) < ready.IndexOf("sender.Play();", StringComparison.Ordinal));
        Assert.Contains("ViewModel.IsVideoLoading = false;", ready);
        Assert.Contains("_videoOpened = false;", stop);
        Assert.Contains("player.MediaOpened -= OnVideoOpened;", stop);
        Assert.Contains("TryReleaseVideoResource(player.Pause);", stop);
        Assert.Contains("player.Source = null", stop);
        Assert.Contains("IsVideoLoading = false;", release);
        Assert.Contains("VideoPath = null;", release);
        Assert.Contains("generation != _mediaGeneration", viewModel);
    }

    [Fact]
    public void SurfaceTap_ExcludesTransportPanelAndInteractiveControls_AndReusesSpaceToggle()
    {
        var source = ReadSource("Views", "PhotoViewerPage.xaml.cs");
        var tap = Section(source, "private void VideoPlayerElement_OnTapped", "private void EnsureViewerKeyboardFocus()");
        Assert.Contains("IsVideoSurface(e.OriginalSource as DependencyObject)", tap);
        Assert.Contains("HandleVideoPlaybackKey(VirtualKey.Space);", tap);
        Assert.Contains("Name: \"ControlPanelGrid\"", tap);
        Assert.Contains("Primitives.ButtonBase", tap);
        Assert.Contains("Primitives.RangeBase", tap);
        Assert.Contains("or CommandBar or TextBox", tap);
        Assert.Contains("ReferenceEquals(current, VideoPlayerElement)", tap);

        var xaml = XDocument.Parse(ReadSource("Views", "PhotoViewerPage.xaml"));
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var player = Assert.Single(xaml.Descendants(ns + "MediaPlayerElement"));
        Assert.Equal("True", (string?)player.Attribute("AreTransportControlsEnabled"));
        Assert.Equal("False", (string?)player.Attribute("AutoPlay"));
        Assert.Equal("True", (string?)Assert.Single(player.Descendants(ns + "MediaTransportControls")).Attribute("ShowAndHideAutomatically"));
        Assert.Contains("IsVideoLoading", (string?)Assert.Single(xaml.Descendants(ns + "ProgressRing")).Attribute("IsActive"));
    }

    private static string Section(string source, string from, string until)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf(until, start, StringComparison.Ordinal);
        Assert.True(end > start);
        return source[start..end];
    }

    private static string ReadSource(string folder, string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MemoryKeeper.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, "MemoryKeeper.App", folder, file));
    }
}
