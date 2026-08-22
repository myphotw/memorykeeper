using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MemoryKeeper.Infrastructure.Services.Api.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryKeeper.Infrastructure.Services.Api;

/// <summary>
/// Shared HTTP client for TC-Backend V1.0. Uses a named <see cref="HttpClient"/> from
/// <see cref="IHttpClientFactory"/> and resolves <see cref="TcBackendOptions.ApiBaseUrl"/>
/// per request so configuration changes take effect.
/// </summary>
public sealed class BaseApiClient
{
    public const string HttpClientName = "TcBackend";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<TcBackendOptions> _options;
    private readonly ILogger<BaseApiClient> _logger;

    public BaseApiClient(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<TcBackendOptions> options,
        ILogger<BaseApiClient> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ApiBaseUrl => _options.CurrentValue.ApiBaseUrl;

    public string ServiceName => _options.CurrentValue.ServiceName;

    public Task<ApiResponse<T>> GetAsync<T>(string path, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Get, path, content: null, ownsContent: false, cancellationToken);

    public Task<ApiResponse<T>> PostAsync<T>(
        string path,
        object? body,
        CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Post, path, CreateJsonContent(body), ownsContent: true, cancellationToken);

    public Task<ApiResponse<T>> PutAsync<T>(
        string path,
        object? body,
        CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Put, path, CreateJsonContent(body), ownsContent: true, cancellationToken);

    public Task<ApiResponse<T>> PatchAsync<T>(
        string path,
        object? body,
        CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Patch, path, CreateJsonContent(body), ownsContent: true, cancellationToken);

    public Task<ApiResponse<T>> DeleteAsync<T>(string path, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Delete, path, content: null, ownsContent: false, cancellationToken);

    public async Task<ApiResponse<T>> UploadAsync<T>(
        string path,
        Stream fileStream,
        string fileName,
        string fieldName = "file",
        string? contentType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        using var form = new MultipartFormDataContent();
        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        form.Add(streamContent, fieldName, fileName);

        // Multipart is not safely retried; ownsContent=false because form is disposed by using.
        return await SendAsync<T>(
                HttpMethod.Post,
                path,
                form,
                ownsContent: false,
                cancellationToken,
                allowRetry: false)
            .ConfigureAwait(false);
    }

    private async Task<ApiResponse<T>> SendAsync<T>(
        HttpMethod method,
        string path,
        HttpContent? content,
        bool ownsContent,
        CancellationToken cancellationToken,
        bool allowRetry = true)
    {
        var options = _options.CurrentValue;
        var retryCount = allowRetry ? Math.Max(0, options.RetryCount) : 0;
        Exception? lastException = null;

        try
        {
            for (var attempt = 1; attempt <= retryCount + 1; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(method, BuildUri(path, options.ApiBaseUrl));
                    if (content is not null)
                    {
                        request.Content = content;
                    }

                    var client = _httpClientFactory.CreateClient(HttpClientName);
                    using var response = await client
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);

                    // Detach content so disposing the request does not dispose shared retry content.
                    request.Content = null;

                    var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        var canRetry = allowRetry
                            && IsTransient(response.StatusCode)
                            && IsIdempotent(method)
                            && attempt <= retryCount;

                        if (canRetry)
                        {
                            _logger.LogWarning(
                                "TC-Backend {Method} {Path} returned {StatusCode}; retry {Attempt}/{RetryCount}",
                                method,
                                ApiErrorClassifier.SafePath(path),
                                (int)response.StatusCode,
                                attempt,
                                retryCount);
                            await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        throw new ApiException(
                            response.StatusCode,
                            $"TC-Backend request failed: {(int)response.StatusCode} {response.ReasonPhrase} ({method} {ApiErrorClassifier.SafePath(path)})",
                            serverMessage: string.IsNullOrWhiteSpace(body) ? null : body,
                            category: ApiErrorClassifier.FromStatusCode(response.StatusCode));
                    }

                    if (string.IsNullOrWhiteSpace(body))
                    {
                        return ApiResponse<T>.Ok(default!);
                    }

                    T? data;
                    try
                    {
                        data = JsonSerializer.Deserialize<T>(body, JsonOptions);
                    }
                    catch (JsonException ex)
                    {
                        throw new ApiException(
                            response.StatusCode,
                            $"Failed to deserialize TC-Backend response ({method} {ApiErrorClassifier.SafePath(path)})",
                            serverMessage: body,
                            innerException: ex,
                            category: ApiErrorCategory.MalformedResponse);
                    }

                    return ApiResponse<T>.Ok(data!);
                }
                catch (ApiException)
                {
                    throw;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (allowRetry && IsIdempotent(method) && attempt <= retryCount)
                    {
                        lastException = ex;
                        _logger.LogWarning(
                            "TC-Backend {Method} {Path} transport failure; retry {Attempt}/{RetryCount}",
                            method,
                            ApiErrorClassifier.SafePath(path),
                            attempt,
                            retryCount);
                        await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw ApiErrorClassifier.FromTransport(ex, method, path);
                }
            }
        }
        finally
        {
            if (ownsContent)
            {
                content?.Dispose();
            }
        }

        throw new ApiException(
            HttpStatusCode.ServiceUnavailable,
            $"TC-Backend request failed after {retryCount} retries ({method} {ApiErrorClassifier.SafePath(path)})",
            innerException: lastException);
    }

    private static HttpContent? CreateJsonContent(object? body)
    {
        if (body is null)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(body, JsonOptions);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static Uri BuildUri(string path, string apiBaseUrl) =>
        TcBackendRequestPolicy.ResolveUri(path, apiBaseUrl);

    private static bool IsIdempotent(HttpMethod method) =>
        method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Delete;

    private static bool IsTransient(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code == 408 || code == 429 || code >= 500;
    }

    private static Task DelayBeforeRetryAsync(int attempt, CancellationToken cancellationToken)
    {
        var delayMs = Math.Min(2000, 200 * (int)Math.Pow(2, Math.Max(0, attempt - 1)));
        return Task.Delay(delayMs, cancellationToken);
    }
}
