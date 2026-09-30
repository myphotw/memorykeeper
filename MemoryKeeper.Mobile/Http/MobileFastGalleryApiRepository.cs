using System.Text.Json;
using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Mobile.Configuration;

namespace MemoryKeeper.Mobile.Http;

public sealed class MobileFastGalleryApiRepository : IFastGalleryApiRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMobileBackendConfiguration _configuration;

    public MobileFastGalleryApiRepository(
        IHttpClientFactory httpClientFactory,
        IMobileBackendConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public Task<FastGalleryPhotoPageDto> GetPhotosAsync(
        FastGalleryPhotoQuery query,
        CancellationToken cancellationToken = default) =>
        GetAsync<FastGalleryPhotoPageDto>(
            FastGalleryRequestPathBuilder.Photos(query),
            cancellationToken);

    public Task<FastGallerySummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default) =>
        GetAsync<FastGallerySummaryDto>(
            $"{FastGalleryRequestPathBuilder.Root}/summary",
            cancellationToken);

    public Task<FastGalleryHierarchyDto> GetHierarchyAsync(CancellationToken cancellationToken = default) =>
        GetAsync<FastGalleryHierarchyDto>(
            $"{FastGalleryRequestPathBuilder.Root}/hierarchy",
            cancellationToken);

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
        where T : class, new()
    {
        var baseUri = _configuration.BaseUri;
        if (!_configuration.IsConfigured || baseUri is null)
        {
            throw new MobileBackendConfigurationException();
        }

        var requestUri = new Uri(baseUri, path.TrimStart('/'));
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        var client = _httpClientFactory.CreateClient(MobileHttpClientNames.Backend);
        using var response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new MobileBackendApiException(response.StatusCode);
        }

        try
        {
            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
                       .ConfigureAwait(false)
                   ?? new T();
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException("Mobile Backend returned an invalid response.", ex);
        }
    }
}
