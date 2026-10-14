using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MemoryKeeper.Application.DTOs.Upload;
using MemoryKeeper.Mobile.Configuration;
using MemoryKeeper.Mobile.Http;

namespace MemoryKeeper.Mobile.Uploads;

public sealed class MobileUploadApiClient : IMobileUploadApiClient
{
    internal static readonly TimeSpan PreflightTimeout = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMobileBackendEndpointResolver _endpointResolver;

    public MobileUploadApiClient(
        IHttpClientFactory httpClientFactory,
        IMobileBackendEndpointResolver endpointResolver)
    {
        _httpClientFactory = httpClientFactory;
        _endpointResolver = endpointResolver;
    }

    public async Task<MobileUploadBatchCapabilities> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default)
    {
        var baseUri = await _endpointResolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        using var request = CreateRequest(
            HttpMethod.Get,
            baseUri,
            "/api/common/capabilities");
        try
        {
            var client = _httpClientFactory.CreateClient(MobileHttpClientNames.Upload);
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
            {
                return MobileUploadBatchCapabilities.LegacyUnknown;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiExceptionAsync(response, cancellationToken).ConfigureAwait(false);
            }

            var capabilities = await DeserializeAsync<MobileBackendCapabilitiesDto>(
                    response,
                    cancellationToken)
                .ConfigureAwait(false);
            return MobileUploadBatchCapabilities.From(capabilities);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MobileUploadException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            throw NetworkFailure(ex);
        }
    }

    public async Task<MobileUploadPreflightResult> PreflightAsync(
        MobileUploadBatchCapabilities capabilities,
        string contentSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ValidateSha256(contentSha256);
        if (!capabilities.CanUpload)
        {
            return new MobileUploadPreflightResult(
                MobileUploadPreflightDisposition.BlockedServerUpdate,
                FailureCode: MobileUploadErrorCodes.ServerUpdateRequired);
        }

        if (!capabilities.SupportsPreflight
            || !MobileUploadContractValidation.TryNormalizeApiPath(
                capabilities.PreflightPath,
                out var preflightPath))
        {
            return new MobileUploadPreflightResult(
                MobileUploadPreflightDisposition.SkippedUnsupported);
        }

        var baseUri = await _endpointResolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        using var request = CreateRequest(HttpMethod.Post, baseUri, preflightPath);
        request.Content = JsonContent.Create(
            new MobileUploadPreflightRequest { ClientContentSha256 = contentSha256 },
            options: JsonOptions);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PreflightTimeout);
        try
        {
            var client = _httpClientFactory.CreateClient(MobileHttpClientNames.Upload);
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
            {
                return new MobileUploadPreflightResult(
                    MobileUploadPreflightDisposition.SkippedUnsupported);
            }

            if ((int)response.StatusCode >= 500)
            {
                return new MobileUploadPreflightResult(
                    MobileUploadPreflightDisposition.SkippedUnavailable,
                    FailureCode: MobileUploadErrorCodes.Server);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiExceptionAsync(response, timeout.Token).ConfigureAwait(false);
            }

            var result = await DeserializeAsync<MobileUploadPreflightResponse>(
                    response,
                    timeout.Token)
                .ConfigureAwait(false);
            if (result.Duplicate)
            {
                return new MobileUploadPreflightResult(
                    MobileUploadPreflightDisposition.Duplicate,
                    result.Reason,
                    MobileUploadErrorCodes.DuplicateContent);
            }

            if (result.UploadAllowed)
            {
                return new MobileUploadPreflightResult(
                    MobileUploadPreflightDisposition.UploadAllowed);
            }

            throw InvalidResponse("Preflight response did not allow or reject the upload.");
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new MobileUploadPreflightResult(
                MobileUploadPreflightDisposition.SkippedUnavailable,
                FailureCode: MobileUploadErrorCodes.Network);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MobileUploadException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new MobileUploadPreflightResult(
                MobileUploadPreflightDisposition.SkippedUnavailable,
                FailureCode: MobileUploadErrorCodes.Network);
        }
    }

    public async Task<UploadResponseDto> UploadAsync(
        MobileUploadFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ValidateSha256(file.ClientContentSha256);

        var baseUri = await _endpointResolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        await using var source = await file.Source.OpenReadAsync(cancellationToken).ConfigureAwait(false);
        using var form = new MultipartFormDataContent();
        using var fileContent = new StreamContent(source);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.TryParse(
            file.Source.ContentType,
            out var contentType)
            ? contentType
            : new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "file", file.Source.FileName);
        form.Add(new StringContent("MemoryKeeper"), "service_name");
        form.Add(new StringContent("mobile"), "upload_source");
        form.Add(new StringContent(file.ClientFileId), "client_file_id");
        form.Add(new StringContent(file.ClientContentSha256!), "client_content_sha256");

        using var request = CreateRequest(HttpMethod.Post, baseUri, "/api/common/upload");
        request.Content = form;
        try
        {
            var client = _httpClientFactory.CreateClient(MobileHttpClientNames.Upload);
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiExceptionAsync(response, cancellationToken).ConfigureAwait(false);
            }

            var result = await DeserializeAsync<UploadResponseDto>(response, cancellationToken)
                .ConfigureAwait(false);
            if (!Guid.TryParse(result.JobId, out _))
            {
                throw InvalidResponse("Upload response did not contain a valid job_id.");
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MobileUploadException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            throw NetworkFailure(ex);
        }
    }

    public async Task<MobileUploadJobResult> GetJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var baseUri = await _endpointResolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        var path = $"/api/common/upload/jobs/{Uri.EscapeDataString(jobId.ToString("D"))}";
        using var request = CreateRequest(HttpMethod.Get, baseUri, path);
        try
        {
            var client = _httpClientFactory.CreateClient(MobileHttpClientNames.Upload);
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiExceptionAsync(response, cancellationToken).ConfigureAwait(false);
            }

            var status = await DeserializeAsync<UploadJobStatusDto>(response, cancellationToken)
                .ConfigureAwait(false);
            return MobileUploadJobResult.From(status);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MobileUploadException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            throw NetworkFailure(ex);
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, Uri baseUri, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(baseUri, path));
        MobileBackendRequestContext.MarkSelectedBackend(request, baseUri);
        return request;
    }

    private static async Task<T> DeserializeAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
                       .ConfigureAwait(false)
                   ?? throw InvalidResponse("Backend returned an empty response.");
        }
        catch (JsonException ex)
        {
            throw InvalidResponse("Backend returned an invalid JSON response.", ex);
        }
    }

    private static async Task<MobileUploadException> CreateApiExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var backendCode = await ReadBackendErrorCodeAsync(response, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new MobileUploadException(
                MobileUploadErrorCodes.AuthenticationRequired,
                "Backend authentication failed.",
                response.StatusCode);
        }

        if (string.Equals(
                backendCode,
                MobileUploadErrorCodes.DuplicateContent,
                StringComparison.Ordinal))
        {
            return new MobileUploadException(
                MobileUploadErrorCodes.DuplicateContent,
                "The selected file is already registered.",
                response.StatusCode);
        }

        if (string.Equals(
                backendCode,
                MobileUploadErrorCodes.ContentSha256Mismatch,
                StringComparison.Ordinal))
        {
            return new MobileUploadException(
                MobileUploadErrorCodes.ContentSha256Mismatch,
                "The uploaded content did not match its SHA-256 value.",
                response.StatusCode);
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return new MobileUploadException(
                MobileUploadErrorCodes.Conflict,
                "The upload conflicts with an existing request.",
                response.StatusCode);
        }

        return new MobileUploadException(
            (int)response.StatusCode >= 500
                ? MobileUploadErrorCodes.Server
                : MobileUploadErrorCodes.InvalidResponse,
            "The Backend rejected the upload request.",
            response.StatusCode);
    }

    private static async Task<string?> ReadBackendErrorCodeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (document.RootElement.TryGetProperty("detail", out var detail)
                && detail.ValueKind == JsonValueKind.Object
                && detail.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String)
            {
                return code.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static void ValidateSha256(string? value)
    {
        if (!MobileUploadContractValidation.IsLowercaseSha256(value))
        {
            throw new ArgumentException("A lowercase SHA-256 value is required.", nameof(value));
        }
    }

    private static MobileUploadException NetworkFailure(Exception exception) => new(
        MobileUploadErrorCodes.Network,
        "The Backend could not be reached.",
        innerException: exception);

    private static MobileUploadException InvalidResponse(
        string message,
        Exception? exception = null) => new(
        MobileUploadErrorCodes.InvalidResponse,
        message,
        innerException: exception);
}
