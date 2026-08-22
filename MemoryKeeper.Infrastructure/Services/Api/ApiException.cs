using System.Net;
using System.Text.Json;

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
        DetailCode = ExtractDetailCode(serverMessage);
        Category = category == ApiErrorCategory.Unknown
            ? ApiErrorClassifier.FromStatusCode(statusCode)
            : category;
    }

    public HttpStatusCode StatusCode { get; }

    public ApiErrorCategory Category { get; }

    /// <summary>Raw response body or server-provided error text, when available.</summary>
    public string? ServerMessage { get; }

    /// <summary>FastAPI detail.code value, when the response uses the structured error contract.</summary>
    public string? DetailCode { get; }

    private static string? ExtractDetailCode(string? serverMessage)
    {
        if (string.IsNullOrWhiteSpace(serverMessage))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(serverMessage);
            return document.RootElement.TryGetProperty("detail", out var detail)
                && detail.ValueKind == JsonValueKind.Object
                && detail.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String
                    ? code.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
