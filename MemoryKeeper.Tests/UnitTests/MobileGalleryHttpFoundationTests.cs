using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Mobile.Configuration;
using MemoryKeeper.Mobile.Http;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class MobileGalleryHttpFoundationTests
{
    private static readonly Uri BackendUri = new("https://backend.example:8443/");

    [Fact]
    public void BuildConfiguration_RequiresHttpsUrlAndCredential()
    {
        Assert.False(new MobileBackendConfiguration(null, null).IsConfigured);
        Assert.False(new MobileBackendConfiguration("http://backend.example", "token").IsConfigured);
        Assert.False(new MobileBackendConfiguration("https://backend.example", null).IsConfigured);
        Assert.True(new MobileBackendConfiguration("https://backend.example", "token").IsConfigured);
    }

    [Fact]
    public async Task Authentication_AddsBearerOnlyToBackendApiOrigin()
    {
        var terminal = new RecordingHandler();
        using var handler = new MobileBackendAuthenticationHandler(
            new StubConfiguration(BackendUri, "credential-value"))
        {
            InnerHandler = terminal,
        };
        using var client = new HttpClient(handler);

        await client.GetAsync(new Uri(BackendUri, "api/memorykeeper/gallery/photos"));
        await client.GetAsync(new Uri(BackendUri, "health"));
        await client.GetAsync("https://cdn.example/image.jpg");

        Assert.Equal("credential-value", terminal.Authorization[0]?.Parameter);
        Assert.Null(terminal.Authorization[1]);
        Assert.Null(terminal.Authorization[2]);
    }

    [Fact]
    public async Task Repository_RequestsFirstFiftyWithCursorAndParsesContract()
    {
        var terminal = new RecordingHandler
        {
            ResponseBody = """{"items":[{"file_id":"a","filename":"a.jpg","thumbnail_url":"/api/common/gallery/a/thumbnail","preview_url":"/api/common/gallery/a/preview"}],"next_cursor":"opaque+/=","has_more":true}""",
        };
        var configuration = new StubConfiguration(BackendUri, "credential-value");
        using var authentication = new MobileBackendAuthenticationHandler(configuration)
        {
            InnerHandler = terminal,
        };
        using var client = new HttpClient(authentication);
        var repository = new MobileFastGalleryApiRepository(
            new SingleClientFactory(client),
            configuration);

        var page = await repository.GetPhotosAsync(new FastGalleryPhotoQuery
        {
            Limit = 50,
            Cursor = "cursor+/=",
        });

        Assert.Contains("limit=50", terminal.RequestUris.Single().Query, StringComparison.Ordinal);
        Assert.Contains("cursor=cursor%2B%2F%3D", terminal.RequestUris.Single().Query, StringComparison.Ordinal);
        Assert.Equal("a", Assert.Single(page.Items).FileId);
        Assert.Equal("/api/common/gallery/a/thumbnail", page.Items[0].ThumbnailUrl);
        Assert.Equal("opaque+/=", page.NextCursor);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task Repository_RequestsHierarchyAndParsesYearNodes()
    {
        var terminal = new RecordingHandler
        {
            ResponseBody = """{"items":[{"year":2026,"count":12},{"year":2025,"count":8}]}""",
        };
        var configuration = new StubConfiguration(BackendUri, "credential-value");
        using var authentication = new MobileBackendAuthenticationHandler(configuration)
        {
            InnerHandler = terminal,
        };
        using var client = new HttpClient(authentication);
        var repository = new MobileFastGalleryApiRepository(
            new SingleClientFactory(client),
            configuration);

        var hierarchy = await repository.GetHierarchyAsync();

        Assert.EndsWith(
            "/api/memorykeeper/gallery/hierarchy",
            terminal.RequestUris.Single().AbsolutePath,
            StringComparison.Ordinal);
        Assert.Equal(
            new[] { 2026, 2025 },
            hierarchy.Roots.Select(node => node.Year.GetValueOrDefault()));
    }

    [Fact]
    public async Task Repository_MissingConfigurationFailsWithoutNetworkCall()
    {
        var terminal = new RecordingHandler();
        using var client = new HttpClient(terminal);
        var repository = new MobileFastGalleryApiRepository(
            new SingleClientFactory(client),
            new StubConfiguration(null, null));

        await Assert.ThrowsAsync<MobileBackendConfigurationException>(() =>
            repository.GetPhotosAsync(new FastGalleryPhotoQuery()));

        Assert.Empty(terminal.RequestUris);
    }

    [Fact]
    public async Task ApiFailure_DoesNotExposeCredentialInException()
    {
        const string credential = "credential-must-not-leak";
        var terminal = new RecordingHandler { StatusCode = HttpStatusCode.Unauthorized };
        var configuration = new StubConfiguration(BackendUri, credential);
        using var authentication = new MobileBackendAuthenticationHandler(configuration)
        {
            InnerHandler = terminal,
        };
        using var client = new HttpClient(authentication);
        var repository = new MobileFastGalleryApiRepository(
            new SingleClientFactory(client),
            configuration);

        var exception = await Assert.ThrowsAsync<MobileBackendApiException>(() =>
            repository.GetPhotosAsync(new FastGalleryPhotoQuery()));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.DoesNotContain(credential, exception.ToString(), StringComparison.Ordinal);
    }

    private sealed class StubConfiguration : IMobileBackendConfiguration
    {
        public StubConfiguration(Uri? baseUri, string? bearerToken)
        {
            BaseUri = baseUri;
            BearerToken = bearerToken;
        }

        public Uri? BaseUri { get; }

        public string? BearerToken { get; }

        public bool IsConfigured => BaseUri is not null && !string.IsNullOrWhiteSpace(BearerToken);
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public SingleClientFactory(HttpClient client) => _client = client;

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];

        public List<AuthenticationHeaderValue?> Authorization { get; } = [];

        public string ResponseBody { get; init; } = "{}";

        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            Authorization.Add(request.Headers.Authorization);
            return Task.FromResult(new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json"),
            });
        }
    }
}
