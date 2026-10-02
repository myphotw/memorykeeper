using System.Diagnostics;
using System.Net;

namespace MemoryKeeper.Mobile.Images;

internal static class MobilePreviewDiagnostics
{
    [Conditional("DEBUG")]
    public static void WriteFailure(
        string? fileId,
        string requestDescription,
        string stage,
        HttpStatusCode? statusCode = null,
        Exception? exception = null)
    {
        var safeFileId = ToSafeToken(fileId);
        var safeRequest = string.IsNullOrWhiteSpace(requestDescription)
            ? "missing"
            : requestDescription;
        var status = statusCode.HasValue ? ((int)statusCode.Value).ToString() : "none";
        var exceptionType = exception?.GetBaseException().GetType().Name ?? "none";
        Debug.WriteLine(
            $"MemoryKeeper Preview failure file_id={safeFileId} request={safeRequest} "
            + $"stage={stage} status={status} exception={exceptionType}");
    }

    private static string ToSafeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "missing";
        }

        var safe = new string(value
            .Trim()
            .Take(128)
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.'
                ? character
                : '_')
            .ToArray());
        return safe.Length == 0 ? "missing" : safe;
    }
}
