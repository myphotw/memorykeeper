using System.Text;

namespace MemoryKeeper.App.Diagnostics;

/// <summary>
/// Gallery entry / query diagnostics.
/// Log path: %LocalAppData%\MemoryKeeper\Logs\gallery.log (also mirrored to startup.log)
/// </summary>
public static class GalleryDiagnostics
{
    private static readonly object Sync = new();

    public static string LogFilePath { get; } = Path.Combine(StartupDiagnostics.LogDirectory, "gallery.log");

    public static void WriteStep(string step)
    {
        WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {step}");
        StartupDiagnostics.WriteStep($"[Gallery] {step}");
    }

    public static void WriteException(string stage, Exception ex, string? queryContext = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} EXCEPTION at {stage}");
        if (!string.IsNullOrWhiteSpace(queryContext))
        {
            builder.AppendLine($"Query: {queryContext}");
        }

        builder.AppendLine($"Exception Type: {ex.GetType().FullName}");
        builder.AppendLine($"Message: {ex.Message}");
        builder.AppendLine("StackTrace:");
        builder.AppendLine(ex.StackTrace ?? "(null)");
        if (ex.InnerException is not null)
        {
            builder.AppendLine($"InnerException Type: {ex.InnerException.GetType().FullName}");
            builder.AppendLine($"InnerException Message: {ex.InnerException.Message}");
            builder.AppendLine("InnerException StackTrace:");
            builder.AppendLine(ex.InnerException.StackTrace ?? "(null)");
        }

        var text = builder.ToString().TrimEnd();
        WriteLine(text);
        StartupDiagnostics.WriteException($"[Gallery] {stage}", ex);
        if (!string.IsNullOrWhiteSpace(queryContext))
        {
            StartupDiagnostics.WriteStep($"[Gallery] Query context: {queryContext}");
        }
    }

    private static void WriteLine(string line)
    {
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(StartupDiagnostics.LogDirectory);
                File.AppendAllText(LogFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Avoid recursive failure during diagnostics.
            }
        }
    }
}
