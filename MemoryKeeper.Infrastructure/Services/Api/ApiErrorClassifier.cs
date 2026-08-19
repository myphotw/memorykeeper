using System.Net;

namespace MemoryKeeper.Infrastructure.Services.Api;

public static class ApiErrorClassifier
{
    public static ApiErrorCategory FromStatusCode(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => ApiErrorCategory.Unauthorized,
        HttpStatusCode.Forbidden => ApiErrorCategory.Forbidden,
        HttpStatusCode.RequestTimeout => ApiErrorCategory.Timeout,
        HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout
            => ApiErrorCategory.BackendUnavailable,
        _ when (int)statusCode >= 500 => ApiErrorCategory.BackendUnavailable,
        _ => ApiErrorCategory.Unknown,
    };

    public static ApiException FromTransport(
        Exception exception,
        HttpMethod method,
        string path)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var category = exception switch
        {
            OperationCanceledException => ApiErrorCategory.Timeout,
            HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError }
                => ApiErrorCategory.Dns,
            HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError }
                => ApiErrorCategory.Tls,
            HttpRequestException => ApiErrorCategory.Network,
            _ => ApiErrorCategory.BackendUnavailable,
        };

        var statusCode = category == ApiErrorCategory.Timeout
            ? HttpStatusCode.RequestTimeout
            : HttpStatusCode.ServiceUnavailable;

        return new ApiException(
            statusCode,
            $"TC-Backend request failed ({method} {SafePath(path)}). Category={category}",
            innerException: exception,
            category: category);
    }

    public static string SafePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
        {
            return absolute.AbsolutePath;
        }

        var queryIndex = path.IndexOf('?');
        return queryIndex >= 0 ? path[..queryIndex] : path;
    }
}
