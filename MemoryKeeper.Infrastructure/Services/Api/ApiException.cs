using System.Net;

namespace MemoryKeeper.Infrastructure.Services.Api;

/// <summary>
/// Thrown when a TC-Backend HTTP call fails.
/// </summary>
public sealed class ApiException : Exception
{
    public ApiException(
        HttpStatusCode statusCode,
        string message,
        string? serverMessage = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ServerMessage = serverMessage;
    }

    public HttpStatusCode StatusCode { get; }

    /// <summary>Raw response body or server-provided error text, when available.</summary>
    public string? ServerMessage { get; }
}
