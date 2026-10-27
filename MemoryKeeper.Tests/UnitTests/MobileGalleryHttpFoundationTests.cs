using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Mobile.Configuration;
using MemoryKeeper.Mobile.Http;
using MemoryKeeper.Mobile.Images;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class MobileGalleryHttpFoundationTests
{
    private static readonly Uri InternalBackendUri = new("http://192.168.55.225:8000/");
    private static readonly Uri ExternalBackendUri = new("https://backend.example:8443/");

    [Fact]
    public void BuildConfiguration_AllowsExternalHttpsWithoutInternalAndRequiresCredential()
    {
        Assert.False(new MobileBackendConfiguration(null, null, null).IsConfigured);
        Assert.True(new MobileBackendConfiguration(
            "http://backend.example:8000",
            ExternalBackendUri.ToString(),
            "token").IsConfigured);
        Assert.False(new MobileBackendConfiguration(
            InternalBackendUri.ToString(),
            "http://backend.example:8443",
            "token").IsConfigured);
        Assert.False(new MobileBackendConfiguration(
            InternalBackendUri.ToString(),
            ExternalBackendUri.ToString(),
            null).IsConfigured);
        Assert.False(new MobileBackendConfiguration(
            InternalBackendUri.ToString(),
            null,
            "token").IsConfigured);

        var externalOnly = new MobileBackendConfiguration(
            null,
            ExternalBackendUri.ToString(),
            "token");

        var configured = new MobileBackendConfiguration(
            InternalBackendUri.ToString(),
            ExternalBackendUri.ToString(),
            "token");

        Assert.True(externalOnly.IsConfigured);
        Assert.Null(externalOnly.InternalBaseUri);
        Assert.Equal(ExternalBackendUri, externalOnly.ExternalBaseUri);
        Assert.True(configured.IsConfigured);
        Assert.Equal(InternalBackendUri, configured.InternalBaseUri);
        Assert.Equal(ExternalBackendUri, configured.ExternalBaseUri);
    }

    [Fact]
    public void EndpointResolver_UsesBoundedProbeTimeoutPolicy()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), MobileBackendEndpointResolver.InternalProbeTimeout);
        Assert.Equal(TimeSpan.FromSeconds(2), MobileBackendEndpointResolver.ExternalProbeTimeout);
        Assert.Equal(TimeSpan.FromSeconds(4), MobileBackendEndpointResolver.SingleEndpointProbeTimeout);
    }

    [Fact]
    public async Task EndpointResolver_ExternalOnlyProbesAndSelectsSingleEndpoint()
    {
        var probe = new RecordingHandler();
        using var client = new HttpClient(probe);
        using var resolver = CreateResolver(
            client,
            configuration: new StubConfiguration(null, ExternalBackendUri, "credential-value"));

        var selected = await resolver.ResolveAsync();

        Assert.Equal(ExternalBackendUri, selected);
        Assert.Equal(ExternalBackendUri, resolver.CurrentBaseUri);
        Assert.Equal(new Uri(ExternalBackendUri, "health"), Assert.Single(probe.RequestUris));
        Assert.Equal(MobileBackendEndpointKind.Single, resolver.ConnectionState.EndpointKind);
    }

    [Fact]
    public async Task EndpointResolver_MissingExternalOrCredentialFailsWithoutProbe()
    {
        var configurations = new IMobileBackendConfiguration[]
        {
            new StubConfiguration(InternalBackendUri, null, "credential-value"),
            new StubConfiguration(null, ExternalBackendUri, null),
        };

        foreach (var configuration in configurations)
        {
            var probe = new RecordingHandler();
            using var client = new HttpClient(probe);
            using var resolver = CreateResolver(client, configuration: configuration);

            await Assert.ThrowsAsync<MobileBackendConfigurationException>(() =>
                resolver.ResolveAsync());
            Assert.Equal(0, probe.RequestCount);
        }
    }

    [Fact]
    public void MobileProject_PreservesLegacySingleEndpointAsExternalFallback()
    {
        var project = ReadSource("MemoryKeeper.Mobile", "MemoryKeeper.Mobile.csproj");

        Assert.Contains(
            "'$(MemoryKeeperMobileBackendUrl)' != ''",
            project,
            StringComparison.Ordinal);
        Assert.Contains(
            "<MemoryKeeperMobileExternalBackendUrl",
            project,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidCleartextPolicy_AllowsOnlyConfiguredInternalHost()
    {
        var manifest = ReadSource(
            "MemoryKeeper.Mobile", "Platforms", "Android", "AndroidManifest.xml");
        var securityConfig = ReadSource(
            "MemoryKeeper.Mobile", "Platforms", "Android", "Resources", "xml",
            "network_security_config.xml");

        Assert.Contains("android:usesCleartextTraffic=\"false\"", manifest, StringComparison.Ordinal);
        Assert.Contains("android:networkSecurityConfig=\"@xml/network_security_config\"", manifest, StringComparison.Ordinal);
        Assert.Contains("android.permission.ACCESS_NETWORK_STATE", manifest, StringComparison.Ordinal);
        Assert.Contains("<base-config cleartextTrafficPermitted=\"false\"", securityConfig, StringComparison.Ordinal);
        Assert.Contains("<domain-config cleartextTrafficPermitted=\"true\"", securityConfig, StringComparison.Ordinal);
        Assert.Contains(">192.168.55.225</domain>", securityConfig, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EndpointResolver_InternalHealthSuccessSelectsInternalWithoutAuthorization()
    {
        var probe = new RecordingHandler();
        using var client = new HttpClient(probe);
        using var resolver = CreateResolver(client);

        var selected = await resolver.ResolveAsync();

        Assert.Equal(InternalBackendUri, selected);
        Assert.Equal(new Uri(InternalBackendUri, "health"), Assert.Single(probe.RequestUris));
        Assert.Null(Assert.Single(probe.Authorization));
    }

    [Fact]
    public async Task EndpointResolver_InternalConnectionFailureSelectsExternal()
    {
        var probe = new RecordingHandler
        {
            ThrowSelector = uri => uri.Host == InternalBackendUri.Host,
        };
        using var client = new HttpClient(probe);
        using var resolver = CreateResolver(client);

        var selected = await resolver.ResolveAsync();

        Assert.Equal(ExternalBackendUri, selected);
        Assert.Equal(2, probe.RequestCount);
        Assert.Equal(new Uri(ExternalBackendUri, "health"), probe.RequestUris[1]);
    }

    [Fact]
    public async Task EndpointResolver_InternalTimeoutSelectsExternal()
    {
        var probe = new RecordingHandler
        {
            DelaySelector = uri => uri.Host == InternalBackendUri.Host
                ? TimeSpan.FromMilliseconds(100)
                : TimeSpan.Zero,
        };
        using var client = new HttpClient(probe);
        using var resolver = CreateResolver(
            client,
            probeTimeout: TimeSpan.FromMilliseconds(10));

        var selected = await resolver.ResolveAsync();

        Assert.Equal(ExternalBackendUri, selected);
        Assert.Equal(2, probe.RequestCount);
    }

    [Fact]
    public async Task EndpointResolver_WhenBothEndpointsFailPublishesFailedState()
    {
        var probe = new RecordingHandler { StatusCode = HttpStatusCode.ServiceUnavailable };
        using var client = new HttpClient(probe);
        using var resolver = CreateResolver(client);

        var error = await Assert.ThrowsAsync<MobileBackendConnectionException>(() =>
            resolver.ResolveAsync());

        Assert.Equal(MobileBackendFailureKind.Server, error.FailureKind);
        Assert.Equal(MobileBackendConnectionPhase.Failed, resolver.ConnectionState.Phase);
        Assert.Equal(2, probe.RequestCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, MobileBackendFailureKind.Authentication)]
    [InlineData(HttpStatusCode.InternalServerError, MobileBackendFailureKind.Server)]
    [InlineData(HttpStatusCode.NotFound, MobileBackendFailureKind.ApiContract)]
    public async Task EndpointResolver_NonTransportInternalFailureDoesNotSwitchOrigin(
        HttpStatusCode statusCode,
        MobileBackendFailureKind expectedFailure)
    {
        var probe = new RecordingHandler
        {
            StatusCodeSelector = uri => uri.Host == InternalBackendUri.Host
                ? statusCode
                : HttpStatusCode.OK,
        };
        using var client = new HttpClient(probe);
        using var resolver = CreateResolver(client);

        var error = await Assert.ThrowsAsync<MobileBackendConnectionException>(() =>
            resolver.ResolveAsync());

        Assert.Equal(expectedFailure, error.FailureKind);
        Assert.Single(probe.RequestUris);
        Assert.Equal(InternalBackendUri.Host, probe.RequestUris[0].Host);
    }

    [Fact]
    public async Task EndpointResolver_ConcurrentFailedRequestsShareOneProbeSequence()
    {
        var probe = new RecordingHandler
        {
            StatusCode = HttpStatusCode.ServiceUnavailable,
            Delay = TimeSpan.FromMilliseconds(10),
        };
        using var client = new HttpClient(probe);
        using var resolver = CreateResolver(client);

        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            try
            {
                await resolver.ResolveAsync();
                return false;
            }
            catch (MobileBackendConnectionException)
            {
                return true;
            }
        }));

        Assert.All(attempts, value => Assert.True(value));
        Assert.Equal(2, probe.RequestCount);
    }

    [Fact]
    public async Task EndpointResolver_ManualReconnectClearsFailureAndProbesAgain()
    {
        var probe = new RecordingHandler { StatusCode = HttpStatusCode.ServiceUnavailable };
        using var client = new HttpClient(probe);
        using var resolver = CreateResolver(client);

        await Assert.ThrowsAsync<MobileBackendConnectionException>(() => resolver.ResolveAsync());
        probe.StatusCode = HttpStatusCode.OK;

        Assert.Equal(InternalBackendUri, await resolver.ReconnectAsync());
        Assert.Equal(3, probe.RequestCount);
    }

    [Fact]
    public async Task EndpointResolver_ConcurrentRequestsShareProbeAndReuseCache()
    {
        var probe = new RecordingHandler { Delay = TimeSpan.FromMilliseconds(25) };
        using var client = new HttpClient(probe);
        using var resolver = CreateResolver(client);

        var selected = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => resolver.ResolveAsync()));
        var cached = await resolver.ResolveAsync();

        Assert.All(selected, uri => Assert.Equal(InternalBackendUri, uri));
        Assert.Equal(InternalBackendUri, cached);
        Assert.Equal(1, probe.RequestCount);
    }

    [Fact]
    public async Task EndpointResolver_NetworkChangeInvalidatesAndReevaluates()
    {
        var probe = new RecordingHandler();
        var network = new TestNetworkChangeSource();
        using var client = new HttpClient(probe);
        using var resolver = CreateResolver(client, networkChangeSource: network);

        Assert.Equal(InternalBackendUri, await resolver.ResolveAsync());
        probe.StatusCodeSelector = uri => uri.Host == InternalBackendUri.Host
            ? HttpStatusCode.ServiceUnavailable
            : HttpStatusCode.OK;
        network.RaiseChanged();
        Assert.Equal(ExternalBackendUri, await resolver.ResolveAsync());

        Assert.Equal(3, probe.RequestCount);
    }

    [Fact]
    public async Task EndpointResolver_ForegroundReconnectReevaluatesWithoutPolling()
    {
        var probe = new RecordingHandler();
        using var client = new HttpClient(probe);
        using var resolver = CreateResolver(client);

        Assert.Equal(InternalBackendUri, await resolver.ResolveAsync());
        resolver.SetForeground(false);
        resolver.Invalidate();
        await Task.Delay(25);
        Assert.Equal(1, probe.RequestCount);

        resolver.SetForeground(true);
        await Task.Delay(250);
        Assert.Equal(2, probe.RequestCount);
    }

    [Fact]
    public void MobileStartup_AppliesFirstPageThenWarmsSixPriorityThumbnails()
    {
        var viewModel = ReadSource(
            "MemoryKeeper.Mobile", "ViewModels", "HomeViewModel.cs");
        var paging = ReadSource(
            "MemoryKeeper.Application", "Services", "FastGalleryPagingService.cs");
        var page = ReadSource(
            "MemoryKeeper.Mobile", "Views", "HomePage.xaml");
        var pageCode = ReadSource(
            "MemoryKeeper.Mobile", "Views", "HomePage.xaml.cs");
        var project = ReadSource(
            "MemoryKeeper.Mobile", "MemoryKeeper.Mobile.csproj");
        var firstPageStartIndex = viewModel.IndexOf(
            "private async Task LoadFirstPageAsync(",
            StringComparison.Ordinal);
        var applyItemsIndex = viewModel.IndexOf(
            "Items.Add(item);",
            firstPageStartIndex,
            StringComparison.Ordinal);
        var priorityPreloadIndex = viewModel.IndexOf(
            "await PreloadInitialThumbnailsAsync(update.Items, preloadToken);",
            StringComparison.Ordinal);
        var backgroundPreloadIndex = viewModel.IndexOf(
            "_ = PreloadRemainingThumbnailsSafelyAsync(",
            StringComparison.Ordinal);

        Assert.Contains("public const int PageSize = 50", paging, StringComparison.Ordinal);
        Assert.Contains("InitialThumbnailPreloadCount = 6", viewModel, StringComparison.Ordinal);
        Assert.Contains("InitialThumbnailPreloadTimeout = TimeSpan.FromSeconds(3)", viewModel, StringComparison.Ordinal);
        Assert.Contains("Take(InitialThumbnailPreloadCount)", viewModel, StringComparison.Ordinal);
        Assert.True(firstPageStartIndex >= 0);
        Assert.True(applyItemsIndex > firstPageStartIndex);
        Assert.True(priorityPreloadIndex > applyItemsIndex);
        Assert.True(backgroundPreloadIndex > priorityPreloadIndex);
        Assert.Contains("Skip(InitialThumbnailPreloadCount)", viewModel, StringComparison.Ordinal);
        Assert.Contains("_ = _uploads.RestoreAsync()", viewModel, StringComparison.Ordinal);
        Assert.Contains("_ = LoadBrowseOptionsSafelyAsync()", viewModel, StringComparison.Ordinal);
        Assert.Contains("IsInitialLoading && !HasItems", viewModel, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(400)", viewModel, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StartupLoadingOverlay\"", page, StringComparison.Ordinal);
        Assert.Contains("Source=\"memorykeeper_loading.png\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("StartupStageText", page, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(1500)", pageCode, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromSeconds(8)", pageCode, StringComparison.Ordinal);
        Assert.Contains("_startupOverlayDismissed", pageCode, StringComparison.Ordinal);
        Assert.Contains("서버에 다시 연결하는 중...", viewModel, StringComparison.Ordinal);
        Assert.Contains("<MauiSplashScreen", project, StringComparison.Ordinal);
        Assert.Contains("<MauiImage Include=\"Resources\\Images\\memorykeeper_loading.png\"", project, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authentication_AddsBearerOnlyToConfiguredBackendApiOrigins()
    {
        var terminal = new RecordingHandler();
        var configuration = CreateConfiguration();
        using var handler = new MobileBackendAuthenticationHandler(configuration)
        {
            InnerHandler = terminal,
        };
        using var client = new HttpClient(handler);

        await SendSelectedAsync(
            client,
            InternalBackendUri,
            new Uri(InternalBackendUri, "api/memorykeeper/gallery/photos"));
        await SendSelectedAsync(
            client,
            ExternalBackendUri,
            new Uri(ExternalBackendUri, "api/memorykeeper/gallery/photos"));
        await SendSelectedAsync(client, InternalBackendUri, new Uri(InternalBackendUri, "health"));
        await SendSelectedAsync(client, InternalBackendUri, new Uri("https://cdn.example/image.jpg"));
        await client.GetAsync(new Uri(ExternalBackendUri, "api/memorykeeper/gallery/photos"));

        Assert.Equal("credential-value", terminal.Authorization[0]?.Parameter);
        Assert.Equal("credential-value", terminal.Authorization[1]?.Parameter);
        Assert.Null(terminal.Authorization[2]);
        Assert.Null(terminal.Authorization[3]);
        Assert.Null(terminal.Authorization[4]);
    }

    [Fact]
    public async Task EndpointFailure_InvalidatesWithoutRetryingRequest()
    {
        var terminal = new RecordingHandler { StatusCode = HttpStatusCode.ServiceUnavailable };
        var resolver = new FixedEndpointResolver(InternalBackendUri);
        using var handler = new MobileBackendEndpointFailureHandler(CreateConfiguration(), resolver)
        {
            InnerHandler = terminal,
        };
        using var client = new HttpClient(handler);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(InternalBackendUri, "api/memorykeeper/gallery/photos"));
        MobileBackendRequestContext.MarkSelectedBackend(request, InternalBackendUri);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, resolver.InvalidationCount);
        Assert.Equal(1, terminal.RequestCount);
    }

    [Fact]
    public async Task Repository_UsesSelectedInternalEndpointAndParsesContract()
    {
        var terminal = new RecordingHandler
        {
            ResponseBody = """{"items":[{"file_id":"a","filename":"a.jpg","thumbnail_url":"/api/common/gallery/a/thumbnail","preview_url":"/api/common/gallery/a/preview"}],"next_cursor":"opaque+/=","has_more":true}""",
        };
        var configuration = CreateConfiguration();
        using var authentication = new MobileBackendAuthenticationHandler(configuration)
        {
            InnerHandler = terminal,
        };
        using var client = new HttpClient(authentication);
        var repository = new MobileFastGalleryApiRepository(
            new SingleClientFactory(client),
            configuration,
            new FixedEndpointResolver(InternalBackendUri));

        var page = await repository.GetPhotosAsync(new FastGalleryPhotoQuery
        {
            Limit = 50,
            Cursor = "cursor+/=",
        });

        var requestUri = Assert.Single(terminal.RequestUris);
        Assert.Equal(InternalBackendUri.GetLeftPart(UriPartial.Authority), requestUri.GetLeftPart(UriPartial.Authority));
        Assert.Contains("limit=50", requestUri.Query, StringComparison.Ordinal);
        Assert.Contains("cursor=cursor%2B%2F%3D", requestUri.Query, StringComparison.Ordinal);
        Assert.Equal("a", Assert.Single(page.Items).FileId);
        Assert.Equal("/api/common/gallery/a/thumbnail", page.Items[0].ThumbnailUrl);
        Assert.Equal("/api/common/gallery/a/preview", page.Items[0].PreviewUrl);
        Assert.Equal("opaque+/=", page.NextCursor);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task Repository_RetriesOneSafeGetAfterRetryableEndpointFailure()
    {
        var responseNumber = 0;
        var terminal = new RecordingHandler
        {
            ResponseBody = """{"items":[],"next_cursor":null,"has_more":false}""",
            StatusCodeSelector = _ => Interlocked.Increment(ref responseNumber) == 1
                ? HttpStatusCode.ServiceUnavailable
                : HttpStatusCode.OK,
        };
        var configuration = CreateConfiguration();
        var resolver = new FixedEndpointResolver(InternalBackendUri);
        using var failure = new MobileBackendEndpointFailureHandler(configuration, resolver)
        {
            InnerHandler = terminal,
        };
        using var authentication = new MobileBackendAuthenticationHandler(configuration)
        {
            InnerHandler = failure,
        };
        using var client = new HttpClient(authentication);
        var repository = new MobileFastGalleryApiRepository(
            new SingleClientFactory(client),
            configuration,
            resolver);

        var page = await repository.GetPhotosAsync(new FastGalleryPhotoQuery());

        Assert.Empty(page.Items);
        Assert.Equal(2, terminal.RequestCount);
        Assert.Equal(1, resolver.InvalidationCount);
    }

    [Fact]
    public async Task Repository_DoesNotRetryAuthenticationOrContractFailure()
    {
        var configuration = CreateConfiguration();

        var unauthorized = new RecordingHandler { StatusCode = HttpStatusCode.Unauthorized };
        using var unauthorizedClient = new HttpClient(unauthorized);
        var unauthorizedRepository = new MobileFastGalleryApiRepository(
            new SingleClientFactory(unauthorizedClient),
            configuration,
            new FixedEndpointResolver(InternalBackendUri));
        await Assert.ThrowsAsync<MobileBackendApiException>(() =>
            unauthorizedRepository.GetPhotosAsync(new FastGalleryPhotoQuery()));
        Assert.Equal(1, unauthorized.RequestCount);

        var invalidJson = new RecordingHandler { ResponseBody = "not-json" };
        using var invalidJsonClient = new HttpClient(invalidJson);
        var invalidJsonRepository = new MobileFastGalleryApiRepository(
            new SingleClientFactory(invalidJsonClient),
            configuration,
            new FixedEndpointResolver(InternalBackendUri));
        await Assert.ThrowsAsync<MobileBackendContractException>(() =>
            invalidJsonRepository.GetPhotosAsync(new FastGalleryPhotoQuery()));
        Assert.Equal(1, invalidJson.RequestCount);
    }

    [Fact]
    public async Task Repository_ParsesApiOrderAndYearCatalogAppliesDescendingUiContract()
    {
        var terminal = new RecordingHandler
        {
            ResponseBody = """{"items":[{"year":2023,"count":4},{"year":2026,"count":12},{"year":2024,"count":6},{"year":2025,"count":8}]}""",
        };
        var configuration = CreateConfiguration();
        using var authentication = new MobileBackendAuthenticationHandler(configuration)
        {
            InnerHandler = terminal,
        };
        using var client = new HttpClient(authentication);
        var repository = new MobileFastGalleryApiRepository(
            new SingleClientFactory(client),
            configuration,
            new FixedEndpointResolver(ExternalBackendUri));

        var hierarchy = await repository.GetHierarchyAsync();

        Assert.EndsWith(
            "/api/memorykeeper/gallery/hierarchy",
            terminal.RequestUris.Single().AbsolutePath,
            StringComparison.Ordinal);
        Assert.Equal(
            new[] { 2023, 2026, 2024, 2025 },
            hierarchy.Roots.Select(node => node.Year.GetValueOrDefault()));
        Assert.Equal(
            new[] { 2026, 2025, 2024, 2023 },
            FastGalleryYearCatalog.FromHierarchy(hierarchy));
    }

    [Fact]
    public async Task Repository_MissingConfigurationFailsWithoutNetworkCall()
    {
        var terminal = new RecordingHandler();
        using var client = new HttpClient(terminal);
        var repository = new MobileFastGalleryApiRepository(
            new SingleClientFactory(client),
            new StubConfiguration(null, null, null),
            new FixedEndpointResolver(InternalBackendUri));

        await Assert.ThrowsAsync<MobileBackendConfigurationException>(() =>
            repository.GetPhotosAsync(new FastGalleryPhotoQuery()));

        Assert.Empty(terminal.RequestUris);
    }

    [Fact]
    public async Task ApiFailure_DoesNotExposeCredentialInException()
    {
        const string credential = "credential-must-not-leak";
        var terminal = new RecordingHandler { StatusCode = HttpStatusCode.Unauthorized };
        var configuration = new StubConfiguration(InternalBackendUri, ExternalBackendUri, credential);
        using var authentication = new MobileBackendAuthenticationHandler(configuration)
        {
            InnerHandler = terminal,
        };
        using var client = new HttpClient(authentication);
        var repository = new MobileFastGalleryApiRepository(
            new SingleClientFactory(client),
            configuration,
            new FixedEndpointResolver(ExternalBackendUri));

        var exception = await Assert.ThrowsAsync<MobileBackendApiException>(() =>
            repository.GetPhotosAsync(new FastGalleryPhotoQuery()));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.DoesNotContain(credential, exception.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, terminal.RequestCount);
    }

    [Fact]
    public async Task PreviewLoader_UsesSelectedEndpointAndAuthenticatedCanonicalRoute()
    {
        var terminal = new RecordingHandler
        {
            ResponseBody = "preview-bytes",
            ContentType = "image/jpeg",
        };
        var configuration = CreateConfiguration();
        using var authentication = new MobileBackendAuthenticationHandler(configuration)
        {
            InnerHandler = terminal,
        };
        using var client = new HttpClient(authentication);
        var loader = new MobilePreviewSourceFactory(
            new SingleClientFactory(client),
            configuration,
            new FixedEndpointResolver(InternalBackendUri));

        var result = await loader.LoadAsync("abc/123", null);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Bytes!);
        Assert.Equal(InternalBackendUri.Host, terminal.RequestUris.Single().Host);
        Assert.Equal(
            "/api/common/gallery/abc%2F123/preview",
            terminal.RequestUris.Single().AbsolutePath);
        Assert.Equal("Bearer", terminal.Authorization.Single()?.Scheme);
        Assert.Equal("backend:/api/common/gallery/abc%2F123/preview", result.RequestDescription);
    }

    [Fact]
    public async Task PreviewLoader_HttpFailureReturnsBoundedFailureWithoutFallbackImage()
    {
        var terminal = new RecordingHandler { StatusCode = HttpStatusCode.NotFound };
        var configuration = CreateConfiguration();
        using var authentication = new MobileBackendAuthenticationHandler(configuration)
        {
            InnerHandler = terminal,
        };
        using var client = new HttpClient(authentication);
        var loader = new MobilePreviewSourceFactory(
            new SingleClientFactory(client),
            configuration,
            new FixedEndpointResolver(ExternalBackendUri));

        var result = await loader.LoadAsync(
            "file-id",
            "/api/common/gallery/file-id/preview");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Bytes);
        Assert.Equal("http", result.FailureStage);
        Assert.DoesNotContain("original", terminal.RequestUris.Single().AbsolutePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("thumbnail", terminal.RequestUris.Single().AbsolutePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreviewLoader_RejectsUnknownProtectedApiOriginWithoutSendingCredential()
    {
        var terminal = new RecordingHandler
        {
            ResponseBody = "preview-bytes",
            ContentType = "image/jpeg",
        };
        var configuration = CreateConfiguration();
        using var client = new HttpClient(terminal);
        var loader = new MobilePreviewSourceFactory(
            new SingleClientFactory(client),
            configuration,
            new FixedEndpointResolver(InternalBackendUri));

        var result = await loader.LoadAsync(
            "file-id",
            "https://unknown.example/api/common/gallery/file-id/preview?size=2560");

        Assert.False(result.IsSuccess);
        Assert.Equal("url", result.FailureStage);
        Assert.Empty(terminal.RequestUris);
    }

    [Theory]
    [InlineData("/api/common/gallery/file-id/thumbnail", true)]
    [InlineData("http://192.168.55.225:8000/api/common/gallery/file-id/thumbnail?width=480", true)]
    [InlineData("https://backend.example:8443/api/common/gallery/file-id/thumbnail?width=480", true)]
    [InlineData("https://unknown.example/api/common/gallery/file-id/thumbnail?width=480", false)]
    [InlineData("https://cdn.example/public/file-id.jpg", true)]
    public void MediaUrlPolicy_RewritesOnlyConfiguredProtectedApiOrigins(
        string pathOrUrl,
        bool expected)
    {
        Assert.Equal(
            expected,
            MobileBackendMediaUrlPolicy.CanUseConfiguredApiUrl(
                pathOrUrl,
                CreateConfiguration()));
    }

    private static StubConfiguration CreateConfiguration() =>
        new(InternalBackendUri, ExternalBackendUri, "credential-value");

    private static async Task SendSelectedAsync(
        HttpClient client,
        Uri selectedBaseUri,
        Uri requestUri)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        MobileBackendRequestContext.MarkSelectedBackend(request, selectedBaseUri);
        using var response = await client.SendAsync(request);
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

    private static MobileBackendEndpointResolver CreateResolver(
        HttpClient client,
        TimeSpan? probeTimeout = null,
        TestNetworkChangeSource? networkChangeSource = null,
        IMobileBackendConfiguration? configuration = null) =>
        new(
            configuration ?? CreateConfiguration(),
            new SingleClientFactory(client),
            networkChangeSource ?? new TestNetworkChangeSource(),
            probeTimeout ?? TimeSpan.FromSeconds(1),
            TimeSpan.FromMinutes(2));

    private sealed class StubConfiguration : IMobileBackendConfiguration
    {
        public StubConfiguration(Uri? internalBaseUri, Uri? externalBaseUri, string? bearerToken)
        {
            InternalBaseUri = internalBaseUri;
            ExternalBaseUri = externalBaseUri;
            BearerToken = bearerToken;
        }

        public Uri? InternalBaseUri { get; }

        public Uri? ExternalBaseUri { get; }

        public string? BearerToken { get; }

        public bool IsConfigured =>
            ExternalBaseUri is not null
            && !string.IsNullOrWhiteSpace(BearerToken);
    }

    private sealed class FixedEndpointResolver(Uri baseUri) : IMobileBackendEndpointResolver
    {
        public Uri? CurrentBaseUri { get; private set; } = baseUri;

        public int InvalidationCount { get; private set; }

        public Task<Uri> ResolveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentBaseUri ?? baseUri);

        public void Invalidate()
        {
            InvalidationCount++;
            CurrentBaseUri = null;
        }
    }

    private sealed class TestNetworkChangeSource : IMobileNetworkChangeSource
    {
        public event EventHandler? NetworkChanged;

        public void RaiseChanged() => NetworkChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public SingleClientFactory(HttpClient client) => _client = client;

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private int _requestCount;

        public List<Uri> RequestUris { get; } = [];

        public List<AuthenticationHeaderValue?> Authorization { get; } = [];

        public string ResponseBody { get; init; } = "{}";

        public string ContentType { get; init; } = "application/json";

        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        public bool ThrowHttpRequestException { get; init; }

        public Func<Uri, bool>? ThrowSelector { get; init; }

        public TimeSpan Delay { get; init; }

        public Func<Uri, TimeSpan>? DelaySelector { get; init; }

        public Func<Uri, HttpStatusCode>? StatusCodeSelector { get; set; }

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            RequestUris.Add(request.RequestUri!);
            Authorization.Add(request.Headers.Authorization);
            var delay = DelaySelector?.Invoke(request.RequestUri!) ?? Delay;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            if (ThrowHttpRequestException || ThrowSelector?.Invoke(request.RequestUri!) == true)
            {
                throw new HttpRequestException("Synthetic connection failure.");
            }

            return new HttpResponseMessage(StatusCodeSelector?.Invoke(request.RequestUri!) ?? StatusCode)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, ContentType),
            };
        }
    }
}
