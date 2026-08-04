namespace MemoryKeeper.Application.Diagnostics;

/// <summary>
/// Photo import / place-assignment pipeline log (MK-042M).
/// Path: %LocalAppData%\MemoryKeeper\Logs\ImportPipeline.log
/// </summary>
public static class ImportPipelineLog
{
    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MemoryKeeper",
        "Logs");

    public static string LogFilePath { get; } = Path.Combine(LogDirectory, "ImportPipeline.log");

    public static void Write(string step)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            File.AppendAllText(
                LogFilePath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {step}{Environment.NewLine}");
        }
        catch
        {
            // Best-effort diagnostics only.
        }
    }
}
