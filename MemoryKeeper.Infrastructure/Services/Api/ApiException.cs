using System.Net;

namespace MemoryKeeper.Infrastructure.Services.Api;

public enum ApiErrorCategory
{
    Unknown,
    Unauthorized,
    Forbidden,
    Timeout,
    Tls,
    Dns,
    Network,
    BackendUnavailable,
    MalformedResponse,
}

/// <summary>
/// Thrown when a TC-Backend HTTP call fails.
/// </summary>
public sealed class ApiException : Exception
{
    public ApiException(
        HttpStatusCode statusCode,
        string message,
        string? serverMessage = null,
        Exception? innerException = null,
        ApiErrorCategory category = ApiErrorCategory.Unknown)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ServerMessage = serverMessage;
        Category = category == ApiErrorCategory.Unknown
            ? ApiErrorClassifier.FromStatusCode(statusCode)
            : category;
    }

    public HttpStatusCode StatusCode { get; }

    public ApiErrorCategory Category { get; }

    /// <summary>Raw response body or server-provided error text, when available.</summary>
    public string? ServerMessage { get; }
}
