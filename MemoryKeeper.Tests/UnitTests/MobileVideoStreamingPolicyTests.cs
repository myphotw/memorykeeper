using MemoryKeeper.Application;
using MemoryKeeper.Mobile.Configuration;
using MemoryKeeper.Mobile.Http;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class MobileVideoStreamingPolicyTests
{
    [Fact]
    public void OriginalRoute_IsCanonicalEscapedAndCredentialFree()
    {
        var resolved = BackendMediaUrlResolver.ResolveOriginalUrl(
            "https://backend.example:8443/",
            "folder/video 01.mp4");

        Assert.Equal(
            "https://backend.example:8443/api/common/gallery/folder%2Fvideo%2001.mp4/original",
            resolved);
        Assert.NotNull(resolved);
        Assert.DoesNotContain("token", resolved, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization", resolved, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("?", resolved, StringComparison.Ordinal);
    }

    [Fact]
    public void VideoAuthorization_IsOnlyCreatedForSameOriginProtectedApi()
    {
        const string nonSecretFixture = "unit-test-credential";
        var configuration = new TestConfiguration(
            new Uri("https://backend.example:8443/"),
            nonSecretFixture);
        var mediaUri = new Uri(
            "https://backend.example:8443/api/common/gallery/file-1/original");

        var headers = MobileVideoPlaybackAuthorization.CreateRequestHeaders(
            configuration,
            mediaUri);

        Assert.Equal($"Bearer {nonSecretFixture}", headers["Authorization"]);
        Assert.Throws<MobileBackendConfigurationException>(() =>
            MobileVideoPlaybackAuthorization.CreateRequestHeaders(
                configuration,
                new Uri("https://foreign.example/api/common/gallery/file-1/original")));
    }

    [Fact]
    public void AndroidVideoViewer_UsesAuthenticatedRangeCapableMedia3WithoutDownloadsOrCache()
    {
        var nativePlayer = ReadSource(
            "MemoryKeeper.Mobile", "Platforms", "Android", "Controls",
            "AuthenticatedVideoPlayerView.cs");
        var handler = ReadSource(
            "MemoryKeeper.Mobile", "Platforms", "Android", "Handlers",
            "MobileVideoPlayerViewHandler.cs");
        var program = ReadSource("MemoryKeeper.Mobile", "MauiProgram.cs");
        var project = ReadSource("MemoryKeeper.Mobile", "MemoryKeeper.Mobile.csproj");

        Assert.Contains("DefaultHttpDataSource.Factory", nativePlayer, StringComparison.Ordinal);
        Assert.Contains("SetDefaultRequestProperties", nativePlayer, StringComparison.Ordinal);
        Assert.Contains("SetAllowCrossProtocolRedirects(false)", nativePlayer, StringComparison.Ordinal);
        Assert.Contains("ProgressiveMediaSource.Factory", nativePlayer, StringComparison.Ordinal);
        Assert.Contains("player.SetMediaSource(mediaSource)", nativePlayer, StringComparison.Ordinal);
        Assert.Contains("player.Prepare()", nativePlayer, StringComparison.Ordinal);
        Assert.Contains("player.PlayWhenReady = true", nativePlayer, StringComparison.Ordinal);
        Assert.Contains("MobileVideoPlaybackAuthorization.CreateRequestHeaders", nativePlayer, StringComparison.Ordinal);
        Assert.Contains("ResolveOriginalUrl", nativePlayer, StringComparison.Ordinal);
        Assert.Contains("ReleasePlayback", handler, StringComparison.Ordinal);
        Assert.Contains("AddHandler<MobileVideoPlayerView, MobileVideoPlayerViewHandler>()", program, StringComparison.Ordinal);
        Assert.Contains("Xamarin.AndroidX.Media3.ExoPlayer", project, StringComparison.Ordinal);
        Assert.Contains("Xamarin.AndroidX.Media3.Ui", project, StringComparison.Ordinal);

        Assert.DoesNotContain("ReadAsByteArray", nativePlayer, StringComparison.Ordinal);
        Assert.DoesNotContain("MemoryStream", nativePlayer, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Write", nativePlayer, StringComparison.Ordinal);
        Assert.DoesNotContain("VideoPlaybackCache", nativePlayer, StringComparison.Ordinal);
        Assert.DoesNotContain("?token", nativePlayer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", nativePlayer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ViewerTransitions_ReleasePreviousSourceAndRejectStaleCallbacks()
    {
        var viewModel = ReadSource("MemoryKeeper.Mobile", "ViewModels", "HomeViewModel.cs");
        var nativePlayer = ReadSource(
            "MemoryKeeper.Mobile", "Platforms", "Android", "Controls",
            "AuthenticatedVideoPlayerView.cs");
        var handler = ReadSource(
            "MemoryKeeper.Mobile", "Platforms", "Android", "Handlers",
            "MobileVideoPlayerViewHandler.cs");

        Assert.Contains("ClearViewerMediaPresentation();", viewModel, StringComparison.Ordinal);
        Assert.Contains("ViewerVideoRequest = null", viewModel, StringComparison.Ordinal);
        Assert.Contains("ViewerVideoRequest = new MobileVideoPlaybackRequest", viewModel, StringComparison.Ordinal);
        Assert.Contains("Equals(currentRequest, args.Request)", viewModel, StringComparison.Ordinal);
        Assert.Contains("currentRequest.Generation != Volatile.Read(ref _viewerLoadGeneration)", viewModel, StringComparison.Ordinal);
        Assert.Contains("ReleasePlaybackResources();", nativePlayer, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(_player, player)", nativePlayer, StringComparison.Ordinal);
        Assert.Contains("Equals(_currentRequest, request)", nativePlayer, StringComparison.Ordinal);
        Assert.Contains("player.Stop()", nativePlayer, StringComparison.Ordinal);
        Assert.Contains("player.ClearMediaItems()", nativePlayer, StringComparison.Ordinal);
        Assert.Contains("player.Release()", nativePlayer, StringComparison.Ordinal);
        Assert.Contains("platformView.ReleasePlayback()", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void VideoControls_OwnSeekGesturesWhileExplicitButtonsNavigateLoadedItems()
    {
        var xaml = ReadSource("MemoryKeeper.Mobile", "Views", "HomePage.xaml");
        var codeBehind = ReadSource("MemoryKeeper.Mobile", "Views", "HomePage.xaml.cs");
        var nativePlayer = ReadSource(
            "MemoryKeeper.Mobile", "Platforms", "Android", "Controls",
            "AuthenticatedVideoPlayerView.cs");
        var photoViewer = ReadSource(
            "MemoryKeeper.Mobile", "Platforms", "Android", "Controls",
            "ZoomablePreviewImageView.cs");

        Assert.Contains("UseController = true", nativePlayer, StringComparison.Ordinal);
        Assert.Contains("PlaybackRequest=\"{Binding ViewerVideoRequest}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Clicked=\"OnViewerPreviousRequested\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Clicked=\"OnViewerNextRequested\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowPreviousViewerItemAsync", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ShowNextViewerItemAsync", codeBehind, StringComparison.Ordinal);
        Assert.Contains("MobileViewerGestureDecision.ResolveSwipe", photoViewer, StringComparison.Ordinal);
        Assert.DoesNotContain("MobileViewerGestureDecision", nativePlayer, StringComparison.Ordinal);
    }

    [Fact]
    public void VideoLifecycle_PausesInBackgroundWithoutAutomaticResume()
    {
        var nativePlayer = ReadSource(
            "MemoryKeeper.Mobile", "Platforms", "Android", "Controls",
            "AuthenticatedVideoPlayerView.cs");
        var page = ReadSource("MemoryKeeper.Mobile", "Views", "HomePage.xaml.cs");

        Assert.Contains("OnWindowVisibilityChanged", nativePlayer, StringComparison.Ordinal);
        Assert.Contains("PausePlayback();", nativePlayer, StringComparison.Ordinal);
        Assert.Contains("ViewerVideoSurface.PausePlayback();", page, StringComparison.Ordinal);
        Assert.DoesNotContain("OnWindowVisibilityChanged(ViewStates.Visible)", nativePlayer, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Source file was not found: {Path.Combine(parts)}");
    }

    private sealed class TestConfiguration(Uri baseUri, string bearerToken)
        : IMobileBackendConfiguration
    {
        public Uri? BaseUri { get; } = baseUri;

        public string? BearerToken { get; } = bearerToken;

        public bool IsConfigured => true;
    }
}
