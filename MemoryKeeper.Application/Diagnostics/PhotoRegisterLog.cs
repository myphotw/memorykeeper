using System.Reflection;
using System.Text.RegularExpressions;

namespace MemoryKeeper.Application.Diagnostics;

/// <summary>Bounded, credential-safe diagnostics for photo registration.</summary>
public static partial class PhotoRegisterLog
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private const int BackupCount = 3;
    private static readonly object Gate = new();

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MemoryKeeper",
        "Logs");

    public static string LogFilePath { get; } = Path.Combine(LogDirectory, "photo-register.log");

    public static void WriteFailure(string fileName, string stage, Exception exception, string? jobId = null) =>
        Write(fileName, stage, exception.GetType().Name, GetCategory(exception), exception.Message, jobId);

    public static void WriteWarning(string fileName, string stage, Exception exception, string? jobId = null) =>
        Write(fileName, stage, exception.GetType().Name, GetCategory(exception), exception.Message, jobId);

    public static string GetCategory(Exception exception)
    {
        var property = exception.GetType().GetProperty("Category", BindingFlags.Instance | BindingFlags.Public);
        return property?.GetValue(exception)?.ToString() ?? exception switch
        {
            TimeoutException => "Timeout",
            OperationCanceledException => "Cancelled",
            IOException => "FileIO",
            _ => "Unexpected",
        };
    }

    public static string SanitizeMessage(string? message)
    {
        var singleLine = (message ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        singleLine = CredentialQueryRegex().Replace(singleLine, "$1=<redacted>");
        return singleLine.Length <= 500 ? singleLine : singleLine[..500];
    }

    private static void Write(
        string fileName,
        string stage,
        string exceptionType,
        string category,
        string message,
        string? jobId)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                RotateIfNeeded();
                var safeFileName = Path.GetFileName(fileName).Replace('\r', '_').Replace('\n', '_');
                var line = $"{DateTimeOffset.Now:O} file={safeFileName} stage={stage} exception={exceptionType} category={category} job_id={jobId ?? "-"} message={SanitizeMessage(message)}";
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Diagnostics must never change import state.
        }
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(LogFilePath) || new FileInfo(LogFilePath).Length < MaxBytes)
        {
            return;
        }

        var oldest = $"{LogFilePath}.{BackupCount}";
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var index = BackupCount - 1; index >= 1; index--)
        {
            var source = $"{LogFilePath}.{index}";
            if (File.Exists(source))
            {
                File.Move(source, $"{LogFilePath}.{index + 1}");
            }
        }

        File.Move(LogFilePath, $"{LogFilePath}.1");
    }

    [GeneratedRegex("(?i)([?&](?:token|api[_-]?key|key|credential|password|secret)=)[^&\\s]+")]
    private static partial Regex CredentialQueryRegex();
}
