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
        var internalBaseUri = new Uri("http://192.168.55.225:8000/");
        var externalBaseUri = new Uri("https://backend.example:8443/");
        var configuration = new TestConfiguration(
            internalBaseUri,
            externalBaseUri,
            nonSecretFixture);
        var internalMediaUri = new Uri(
            "http://192.168.55.225:8000/api/common/gallery/file-1/original");
        var externalMediaUri = new Uri(
            "https://backend.example:8443/api/common/gallery/file-1/original");

        var internalHeaders = MobileVideoPlaybackAuthorization.CreateRequestHeaders(
            configuration,
            internalBaseUri,
            internalMediaUri);
        var externalHeaders = MobileVideoPlaybackAuthorization.CreateRequestHeaders(
            configuration,
            externalBaseUri,
            externalMediaUri);

        Assert.Equal($"Bearer {nonSecretFixture}", internalHeaders["Authorization"]);
        Assert.Equal($"Bearer {nonSecretFixture}", externalHeaders["Authorization"]);
        Assert.Throws<MobileBackendConfigurationException>(() =>
            MobileVideoPlaybackAuthorization.CreateRequestHeaders(
                configuration,
                externalBaseUri,
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
        Assert.Contains("request.BackendBaseUri", nativePlayer, StringComparison.Ordinal);
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
        Assert.Contains("_endpointResolver.Invalidate()", viewModel, StringComparison.Ordinal);
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
    public void VideoRemainsFullViewerCenteredWhileControllerUsesIndependentBottomLayer()
    {
        var nativePlayer = ReadSource(
            "MemoryKeeper.Mobile", "Platforms", "Android", "Controls",
            "AuthenticatedVideoPlayerView.cs");
        var playerLayout = ReadSource(
            "MemoryKeeper.Mobile", "Platforms", "Android", "Resources", "layout",
            "exo_player_view.xml");
        var controllerLayout = ReadSource(
            "MemoryKeeper.Mobile", "Platforms", "Android", "Resources", "layout",
            "exo_player_control_view.xml");
        var controllerDimensions = ReadSource(
            "MemoryKeeper.Mobile", "Platforms", "Android", "Resources", "values",
            "media3_controller_dimensions.xml");

        var androidNamespace = (System.Xml.Linq.XNamespace)"http://schemas.android.com/apk/res/android";
        var playerDocument = System.Xml.Linq.XDocument.Parse(playerLayout);
        var contentFrame = playerDocument.Root!
            .Elements()
            .Single(element =>
                (string?)element.Attribute(androidNamespace + "id") == "@id/exo_content_frame");
        var controllerArea = playerDocument.Root!
            .Elements()
            .Single(element =>
                (string?)element.Attribute(androidNamespace + "id") == "@+id/memorykeeper_controller_area");
        var controllerPlaceholder = playerDocument
            .Descendants()
            .Single(element =>
                (string?)element.Attribute(androidNamespace + "id") == "@id/exo_controller_placeholder");

        var controllerDocument = System.Xml.Linq.XDocument.Parse(controllerLayout);
        var controllerPanel = controllerDocument.Root!
            .Elements()
            .Single(element =>
                (string?)element.Attribute(androidNamespace + "layout_gravity") == "top");
        var primaryControls = controllerPanel
            .Elements()
            .Single(element =>
                (string?)element.Attribute(androidNamespace + "id") == "@id/exo_center_controls");
        var controllerPanelIds = controllerPanel
            .Descendants()
            .Select(element => (string?)element.Attribute(androidNamespace + "id"))
            .Where(id => id is not null)
            .ToList();
        var controllerRows = controllerPanel
            .Elements()
            .Select(element => (string?)element.Attribute(androidNamespace + "id"))
            .ToList();
        var dimensionsDocument = System.Xml.Linq.XDocument.Parse(controllerDimensions);
        var dimensions = dimensionsDocument.Root!
            .Elements("dimen")
            .ToDictionary(
                element => (string)element.Attribute("name")!,
                element => element.Value);

        Assert.Contains("UseController = true", nativePlayer, StringComparison.Ordinal);
        Assert.DoesNotContain("PositionPrimaryControlsAtBottom", nativePlayer, StringComparison.Ordinal);
        Assert.Equal("match_parent", (string?)contentFrame.Attribute(androidNamespace + "layout_width"));
        Assert.Equal("match_parent", (string?)contentFrame.Attribute(androidNamespace + "layout_height"));
        Assert.Equal("center", (string?)contentFrame.Attribute(androidNamespace + "layout_gravity"));
        Assert.Equal(
            "@dimen/memorykeeper_video_controller_height",
            (string?)controllerArea.Attribute(androidNamespace + "layout_height"));
        Assert.Equal("bottom", (string?)controllerArea.Attribute(androidNamespace + "layout_gravity"));
        Assert.Same(playerDocument.Root, contentFrame.Parent);
        Assert.Same(playerDocument.Root, controllerArea.Parent);
        Assert.Same(controllerArea, controllerPlaceholder.Parent);
        Assert.NotSame(contentFrame, controllerPlaceholder.Parent);
        Assert.Empty(playerDocument
            .Descendants()
            .Where(element => element.Attribute(androidNamespace + "layout_weight") is not null));
        Assert.Equal("LinearLayout", controllerPanel.Name.LocalName);
        Assert.Equal("vertical", (string?)controllerPanel.Attribute(androidNamespace + "orientation"));
        Assert.Equal("center_horizontal", (string?)primaryControls.Attribute(androidNamespace + "layout_gravity"));
        Assert.Contains("@id/exo_center_controls", controllerPanelIds);
        Assert.Contains("@id/exo_play_pause", controllerPanelIds);
        Assert.Contains("@id/exo_progress_placeholder", controllerPanelIds);
        Assert.Contains("@id/exo_position", controllerPanelIds);
        Assert.Contains("@id/exo_duration", controllerPanelIds);
        Assert.Contains("@layout/exo_player_control_rewind_button", controllerLayout, StringComparison.Ordinal);
        Assert.Contains("@layout/exo_player_control_ffwd_button", controllerLayout, StringComparison.Ordinal);
        Assert.Equal(
            new string?[]
            {
                "@id/exo_center_controls",
                "@id/exo_progress_placeholder",
                "@id/exo_bottom_bar",
            },
            controllerRows);
        Assert.Equal("0dp", dimensions["exo_styled_progress_margin_bottom"]);
        Assert.Equal("24dp", dimensions["exo_styled_progress_layout_height"]);
        Assert.Equal("24dp", dimensions["exo_styled_bottom_bar_height"]);
        Assert.Equal("104dp", dimensions["memorykeeper_video_controller_height"]);
    }

    [Fact]
    public void ViewerNavigation_UsesPhotoAndVideoSwipesWithoutOverlayButtons()
    {
        var xaml = ReadSource("MemoryKeeper.Mobile", "Views", "HomePage.xaml");
        var codeBehind = ReadSource("MemoryKeeper.Mobile", "Views", "HomePage.xaml.cs");
        var nativePlayer = ReadSource(
            "MemoryKeeper.Mobile", "Platforms", "Android", "Controls",
            "AuthenticatedVideoPlayerView.cs");
        var videoHandler = ReadSource(
            "MemoryKeeper.Mobile", "Platforms", "Android", "Handlers",
            "MobileVideoPlayerViewHandler.cs");
        var photoViewer = ReadSource(
            "MemoryKeeper.Mobile", "Platforms", "Android", "Controls",
            "ZoomablePreviewImageView.cs");

        var document = System.Xml.Linq.XDocument.Parse(xaml);
        var videoView = document
            .Descendants()
            .Single(element => element.Name.LocalName == "MobileVideoPlayerView");
        var visualNavigationButtons = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .Where(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "SemanticProperties.Description"
                && attribute.Value is "이전 항목" or "다음 항목"));

        Assert.Equal("OnViewerPreviousRequested", videoView.Attribute("PreviousRequested")?.Value);
        Assert.Equal("OnViewerNextRequested", videoView.Attribute("NextRequested")?.Value);
        Assert.Empty(visualNavigationButtons);
        Assert.DoesNotContain("Clicked=\"OnViewerPreviousRequested\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Clicked=\"OnViewerNextRequested\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PlaybackRequest=\"{Binding ViewerVideoRequest}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowPreviousViewerItemAsync", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ShowNextViewerItemAsync", codeBehind, StringComparison.Ordinal);
        Assert.Contains("MobileViewerGestureDecision.ResolveSwipe", photoViewer, StringComparison.Ordinal);
        Assert.Contains("MobileViewerGestureDecision.ResolveSwipe", nativePlayer, StringComparison.Ordinal);
        Assert.Contains(
            "_navigationGestureBlocked = !IsTouchInsideMedia3View(\"exo_content_frame\"",
            nativePlayer,
            StringComparison.Ordinal);
        Assert.Contains(
            "IsTouchInsideMedia3View(\"memorykeeper_controller_area\"",
            nativePlayer,
            StringComparison.Ordinal);
        Assert.Contains("IsTouchInsideVisibleController", nativePlayer, StringComparison.Ordinal);
        Assert.Contains("platformView.PreviousRequested += OnPreviousRequested", videoHandler, StringComparison.Ordinal);
        Assert.Contains("platformView.NextRequested += OnNextRequested", videoHandler, StringComparison.Ordinal);
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

    private sealed class TestConfiguration(
        Uri internalBaseUri,
        Uri externalBaseUri,
        string bearerToken)
        : IMobileBackendConfiguration
    {
        public Uri? InternalBaseUri { get; } = internalBaseUri;

        public Uri? ExternalBaseUri { get; } = externalBaseUri;

        public string? BearerToken { get; } = bearerToken;

        public bool IsConfigured => true;
    }
}
