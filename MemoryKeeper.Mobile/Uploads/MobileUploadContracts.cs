using System.Net;
using System.Text.Json.Serialization;
using MemoryKeeper.Application.DTOs.Upload;

namespace MemoryKeeper.Mobile.Uploads;

public static class MobileUploadErrorCodes
{
    public const string ServerUpdateRequired = "SERVER_UPDATE_REQUIRED";
    public const string DuplicateContent = "DUPLICATE_CONTENT";
    public const string ContentSha256Mismatch = "CONTENT_SHA256_MISMATCH";
    public const string AuthenticationRequired = "AUTHENTICATION_REQUIRED";
    public const string Conflict = "UPLOAD_CONFLICT";
    public const string Network = "NETWORK_ERROR";
    public const string Server = "SERVER_ERROR";
    public const string InvalidResponse = "INVALID_RESPONSE";
    public const string FileAccess = "FILE_ACCESS_ERROR";
    public const string OriginalMediaPermissionRequired = "ORIGINAL_MEDIA_PERMISSION_REQUIRED";
    public const string OriginalMediaUnavailable = "ORIGINAL_MEDIA_UNAVAILABLE";
}

public sealed class MobileUploadException : Exception
{
    public MobileUploadException(
        string code,
        string message,
        HttpStatusCode? statusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }

    public HttpStatusCode? StatusCode { get; }
}

public sealed class MobileSelectedMediaFile : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<Stream>> _openReadAsync;
    private readonly Func<ValueTask>? _releaseAsync;
    private int _released;

    public MobileSelectedMediaFile(
        string fileName,
        string contentType,
        long fileSize,
        Func<CancellationToken, Task<Stream>> openReadAsync,
        Func<ValueTask>? releaseAsync = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentOutOfRangeException.ThrowIfNegative(fileSize);
        ArgumentNullException.ThrowIfNull(openReadAsync);

        FileName = fileName;
        ContentType = contentType;
        FileSize = fileSize;
        _openReadAsync = openReadAsync;
        _releaseAsync = releaseAsync;
    }

    public string FileName { get; }

    public string ContentType { get; }

    public long FileSize { get; }

    public async Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _released) != 0)
        {
            throw new MobileUploadException(
                MobileUploadErrorCodes.FileAccess,
                "선택한 파일의 임시 원본이 정리되었습니다. 파일을 다시 선택해 주세요.");
        }

        try
        {
            var stream = await _openReadAsync(cancellationToken).ConfigureAwait(false);
            if (stream is null || !stream.CanRead)
            {
                stream?.Dispose();
                throw new IOException("선택한 파일을 읽을 수 없습니다.");
            }

            return stream;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MobileUploadException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new MobileUploadException(
                MobileUploadErrorCodes.FileAccess,
                "선택한 파일을 다시 열 수 없습니다. 파일을 다시 선택해 주세요.",
                innerException: ex);
        }
    }

    public async ValueTask ReleaseAsync()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0 || _releaseAsync is null)
        {
            return;
        }

        await _releaseAsync().ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => ReleaseAsync();
}

public sealed class MobileUploadFile
{
    public MobileUploadFile(MobileSelectedMediaFile source)
        : this(source, Guid.NewGuid().ToString("D"))
    {
    }

    public MobileUploadFile(MobileSelectedMediaFile source, string clientFileId)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!Guid.TryParse(clientFileId, out var parsedClientFileId))
        {
            throw new ArgumentException("client_file_id must be a UUID.", nameof(clientFileId));
        }

        Source = source;
        ClientFileId = parsedClientFileId.ToString("D");
    }

    public MobileSelectedMediaFile Source { get; }

    public string ClientFileId { get; }

    public string? ClientContentSha256 { get; private set; }

    public void SetContentSha256(string value)
    {
        if (!MobileUploadContractValidation.IsLowercaseSha256(value))
        {
            throw new ArgumentException("A lowercase SHA-256 value is required.", nameof(value));
        }

        ClientContentSha256 = value;
    }
}

public sealed record MobileContentHashResult(string Sha256, long BytesRead);

public sealed record MobileUploadProgress(long BytesSent, long TotalBytes)
{
    public double Fraction => TotalBytes > 0
        ? Math.Clamp((double)BytesSent / TotalBytes, 0d, 1d)
        : 0d;
}

public sealed class MobileBackendCapabilitiesDto
{
    [JsonPropertyName("api_version")]
    public string ApiVersion { get; init; } = string.Empty;

    [JsonPropertyName("capabilities")]
    public IReadOnlyDictionary<string, bool> Capabilities { get; init; }
        = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("upload_contract")]
    public MobileUploadContractDto? UploadContract { get; init; }

    [JsonPropertyName("supports_upload_source")]
    public bool? SupportsUploadSource { get; init; }

    [JsonPropertyName("supports_client_file_id")]
    public bool? SupportsClientFileId { get; init; }

    [JsonPropertyName("supports_client_content_sha256")]
    public bool? SupportsClientContentSha256 { get; init; }

    [JsonPropertyName("supports_mobile_sha256_preflight")]
    public bool? SupportsMobileSha256Preflight { get; init; }

    [JsonPropertyName("mobile_sha256_preflight_endpoint")]
    public string? MobileSha256PreflightEndpoint { get; init; }

    [JsonPropertyName("upload_failure_codes")]
    public IReadOnlyList<string>? UploadFailureCodes { get; init; }
}

public sealed class MobileUploadContractDto
{
    [JsonPropertyName("supports_upload_source")]
    public bool? SupportsUploadSource { get; init; }

    [JsonPropertyName("upload_sources")]
    public IReadOnlyList<string>? UploadSources { get; init; }

    [JsonPropertyName("supports_client_file_id")]
    public bool? SupportsClientFileId { get; init; }

    [JsonPropertyName("supports_client_content_sha256")]
    public bool? SupportsClientContentSha256 { get; init; }

    [JsonPropertyName("supports_mobile_sha256_preflight")]
    public bool? SupportsMobileSha256Preflight { get; init; }

    [JsonPropertyName("mobile_sha256_preflight_endpoint")]
    public string? MobileSha256PreflightEndpoint { get; init; }

    [JsonPropertyName("upload_failure_codes")]
    public IReadOnlyList<string>? UploadFailureCodes { get; init; }
}

public sealed record MobileUploadBatchCapabilities(
    bool CanUpload,
    bool SupportsPreflight,
    string? PreflightPath,
    bool IsLegacyUnknown,
    string? ResultCode,
    IReadOnlySet<string> FailureCodes)
{
    public static MobileUploadBatchCapabilities LegacyUnknown { get; } = new(
        CanUpload: true,
        SupportsPreflight: false,
        PreflightPath: null,
        IsLegacyUnknown: true,
        ResultCode: null,
        FailureCodes: new HashSet<string>(StringComparer.Ordinal));

    public static MobileUploadBatchCapabilities From(MobileBackendCapabilitiesDto capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        var contract = capabilities.UploadContract;
        var capabilityFlags = capabilities.Capabilities
                              ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var supportsUploadSource = contract?.SupportsUploadSource
                                   ?? capabilities.SupportsUploadSource == true;
        var supportsClientFileId = contract?.SupportsClientFileId
                                   ?? capabilities.SupportsClientFileId == true;
        var supportsContentSha256 = contract?.SupportsClientContentSha256
                                    ?? capabilities.SupportsClientContentSha256 == true;
        var uploadSources = contract?.UploadSources ?? [];
        var supportsMobileSource = uploadSources.Count == 0
                                   || uploadSources.Any(source =>
                                       string.Equals(source, "mobile", StringComparison.OrdinalIgnoreCase));
        var hasRequiredContract = capabilityFlags.TryGetValue("upload", out var uploadSupported)
                                  && uploadSupported
                                  && supportsUploadSource
                                  && supportsMobileSource
                                  && supportsClientFileId
                                  && supportsContentSha256;
        if (!hasRequiredContract)
        {
            return new MobileUploadBatchCapabilities(
                CanUpload: false,
                SupportsPreflight: false,
                PreflightPath: null,
                IsLegacyUnknown: false,
                ResultCode: MobileUploadErrorCodes.ServerUpdateRequired,
                FailureCodes: new HashSet<string>(StringComparer.Ordinal));
        }

        var preflightAdvertised = contract?.SupportsMobileSha256Preflight
                                  ?? capabilities.SupportsMobileSha256Preflight == true;
        var advertisedPreflightPath = contract?.MobileSha256PreflightEndpoint
                                      ?? capabilities.MobileSha256PreflightEndpoint;
        var preflightPath = string.Empty;
        var supportsPreflight = preflightAdvertised
                                && MobileUploadContractValidation.TryNormalizeApiPath(
                                    advertisedPreflightPath,
                                    out preflightPath);
        var failureCodes = contract?.UploadFailureCodes
                           ?? capabilities.UploadFailureCodes
                           ?? [];
        return new MobileUploadBatchCapabilities(
            CanUpload: true,
            SupportsPreflight: supportsPreflight,
            PreflightPath: supportsPreflight ? preflightPath : null,
            IsLegacyUnknown: false,
            ResultCode: null,
            FailureCodes: failureCodes.ToHashSet(StringComparer.Ordinal));
    }
}

public enum MobileUploadPreflightDisposition
{
    UploadAllowed,
    Duplicate,
    SkippedUnsupported,
    SkippedUnavailable,
    BlockedServerUpdate,
}

public sealed record MobileUploadPreflightResult(
    MobileUploadPreflightDisposition Disposition,
    string? Reason = null,
    string? FailureCode = null)
{
    public bool ShouldUpload => Disposition is MobileUploadPreflightDisposition.UploadAllowed
        or MobileUploadPreflightDisposition.SkippedUnsupported
        or MobileUploadPreflightDisposition.SkippedUnavailable;
}

public sealed class MobileUploadPreflightRequest
{
    [JsonPropertyName("service_name")]
    public string ServiceName { get; init; } = "MemoryKeeper";

    [JsonPropertyName("upload_source")]
    public string UploadSource { get; init; } = "mobile";

    [JsonPropertyName("client_content_sha256")]
    public required string ClientContentSha256 { get; init; }
}

public sealed class MobileUploadPreflightResponse
{
    [JsonPropertyName("duplicate")]
    public bool Duplicate { get; init; }

    [JsonPropertyName("upload_allowed")]
    public bool UploadAllowed { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

public static class MobileUploadContractValidation
{
    public static bool IsLowercaseSha256(string? value) =>
        value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static bool TryNormalizeApiPath(string? value, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.Contains('?')
            || trimmed.Contains('#')
            || Uri.TryCreate(trimmed, UriKind.Absolute, out _))
        {
            return false;
        }

        path = trimmed;
        return true;
    }
}

public enum MobileUploadJobFailureKind
{
    None,
    DuplicateContent,
    ContentSha256Mismatch,
    ProcessingFailed,
}

public sealed record MobileUploadJobResult(
    UploadJobStatusDto Status,
    MobileUploadJobFailureKind FailureKind)
{
    public static MobileUploadJobResult From(UploadJobStatusDto status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (!status.IsFailed)
        {
            return new MobileUploadJobResult(status, MobileUploadJobFailureKind.None);
        }

        var failureKind = status.FailureCode switch
        {
            MobileUploadErrorCodes.DuplicateContent => MobileUploadJobFailureKind.DuplicateContent,
            MobileUploadErrorCodes.ContentSha256Mismatch =>
                MobileUploadJobFailureKind.ContentSha256Mismatch,
            _ => MobileUploadJobFailureKind.ProcessingFailed,
        };
        return new MobileUploadJobResult(status, failureKind);
    }
}
