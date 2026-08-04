using System.Text;

namespace MemoryKeeper.App.Diagnostics;

/// <summary>
/// File-based startup diagnostics for publish/exe silent-exit investigation.
/// Log path: %LocalAppData%\MemoryKeeper\Logs\startup.log
/// </summary>
public static class StartupDiagnostics
{
    private static readonly object Sync = new();

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MemoryKeeper",
        "Logs");

    public static string LogFilePath { get; } = Path.Combine(LogDirectory, "startup.log");

    public static void WriteStep(string step)
    {
        WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {step}");
    }

    public static void WriteException(string stage, Exception ex)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} EXCEPTION at {stage}");
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

        WriteLine(builder.ToString().TrimEnd());
    }

    /// <summary>
    /// Shows the shared Error Dialog (copyable TextBox). Prefer <see cref="ErrorDialog.Show"/>.
    /// </summary>
    public static void ShowErrorDialog(string title, Exception exception, string? stage = null)
    {
        ErrorDialog.Show(
            MemoryKeeper.Application.Diagnostics.ErrorReportSource.Startup,
            title,
            exception,
            stage);
    }

    private static void WriteLine(string line)
    {
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(LogFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Avoid recursive failure during diagnostics.
            }
        }
    }
}
