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

    /// <summary>Maps transport/API failures to UI-safe text without exposing routes or revision internals.</summary>
    public static string ToUserMessage(ApiException exception, string? notFoundMessage = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                => "NAS 연결 인증 정보를 확인하세요.",
            HttpStatusCode.NotFound
                => notFoundMessage ?? "요청한 정보를 찾을 수 없습니다.",
            HttpStatusCode.Conflict
                => "다른 곳에서 정보가 변경되었습니다. 최신 정보를 다시 불러온 뒤 다시 시도하세요.",
            HttpStatusCode.UnprocessableEntity
                => "입력한 정보를 확인하세요.",
            HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout
                => "NAS 서비스에 연결할 수 없습니다. 잠시 후 다시 시도하세요.",
            _ => "요청을 처리하지 못했습니다. 잠시 후 다시 시도하세요.",
        };
    }

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
