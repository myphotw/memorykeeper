using System.Text;

namespace MemoryKeeper.Application.Diagnostics;

public static class ErrorReportFormatter
{
    public static ErrorLogTarget GetLogTarget(ErrorReportSource source)
        => source == ErrorReportSource.Gallery ? ErrorLogTarget.Gallery : ErrorLogTarget.Startup;

    public static string BuildDisplayText(Exception exception, string? stage = null)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(stage))
        {
            builder.AppendLine($"Stage: {stage}");
            builder.AppendLine();
        }

        AppendException(builder, exception, header: "Exception");
        if (exception.InnerException is not null)
        {
            builder.AppendLine();
            AppendException(builder, exception.InnerException, header: "Inner Exception");
        }

        builder.AppendLine();
        builder.AppendLine("StackTrace:");
        builder.AppendLine(string.IsNullOrWhiteSpace(exception.StackTrace) ? "(null)" : exception.StackTrace);
        return builder.ToString().TrimEnd();
    }

    public static string BuildClipboardText(
        Exception exception,
        string? stage,
        DateTimeOffset time,
        string version,
        ErrorReportSource source)
    {
        var builder = new StringBuilder();
        builder.AppendLine("MemoryKeeper Error Report");
        builder.AppendLine($"Time: {time:yyyy-MM-dd HH:mm:ss.fff zzz}");
        builder.AppendLine($"Version: {version}");
        builder.AppendLine($"Source: {source}");
        if (!string.IsNullOrWhiteSpace(stage))
        {
            builder.AppendLine($"Stage: {stage}");
        }

        builder.AppendLine();
        AppendException(builder, exception, header: "Exception");
        builder.AppendLine();
        if (exception.InnerException is not null)
        {
            AppendException(builder, exception.InnerException, header: "Inner Exception");
        }
        else
        {
            builder.AppendLine("Inner Exception: (null)");
        }

        builder.AppendLine();
        builder.AppendLine("StackTrace:");
        builder.AppendLine(string.IsNullOrWhiteSpace(exception.StackTrace) ? "(null)" : exception.StackTrace);
        return builder.ToString().TrimEnd();
    }

    private static void AppendException(StringBuilder builder, Exception exception, string header)
    {
        builder.AppendLine($"{header}:");
        builder.AppendLine($"Type: {exception.GetType().FullName}");
        builder.AppendLine($"Message: {exception.Message}");
    }
}
