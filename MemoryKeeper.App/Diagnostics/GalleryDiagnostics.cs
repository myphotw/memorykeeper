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

    public static void WriteOperationFailure(
        string operation,
        string stage,
        int selectedCount,
        Guid? targetPlaceId,
        string exceptionType,
        string safeMessage,
        int? httpStatus = null,
        string? detailCode = null,
        int? returnedCount = null,
        int? revisionMapCount = null,
        int? verifiedCount = null,
        int? mismatchCount = null,
        string? safeStackTrace = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} HANDLED OPERATION FAILURE");
        builder.AppendLine($"Operation: {NormalizeSingleLine(operation, 128)}");
        builder.AppendLine($"Stage: {NormalizeSingleLine(stage, 128)}");
        builder.AppendLine($"SelectedCount: {selectedCount}");
        builder.AppendLine($"TargetPlaceId: {targetPlaceId?.ToString() ?? "(null)"}");
        AppendOptionalCount(builder, "ReturnedCount", returnedCount);
        AppendOptionalCount(builder, "RevisionMapCount", revisionMapCount);
        AppendOptionalCount(builder, "VerifiedCount", verifiedCount);
        AppendOptionalCount(builder, "MismatchCount", mismatchCount);
        if (httpStatus.HasValue)
        {
            builder.AppendLine($"HttpStatus: {httpStatus.Value}");
        }
        if (!string.IsNullOrWhiteSpace(detailCode))
        {
            builder.AppendLine($"DetailCode: {NormalizeSingleLine(detailCode, 128)}");
        }
        builder.AppendLine($"ExceptionType: {NormalizeSingleLine(exceptionType, 256)}");
        builder.AppendLine($"Message: {NormalizeSingleLine(safeMessage, 512)}");
        if (!string.IsNullOrWhiteSpace(safeStackTrace))
        {
            builder.AppendLine("StackTrace:");
            builder.AppendLine(NormalizeMultiline(safeStackTrace, 4096));
        }

        WriteLine(builder.ToString().TrimEnd());
        StartupDiagnostics.WriteStep(
            $"[Gallery] Handled operation failure. Operation={NormalizeSingleLine(operation, 128)} "
            + $"Stage={NormalizeSingleLine(stage, 128)} SelectedCount={selectedCount} "
            + $"TargetPlaceId={targetPlaceId?.ToString() ?? "(null)"} "
            + $"ExceptionType={NormalizeSingleLine(exceptionType, 256)} "
            + $"HttpStatus={httpStatus?.ToString() ?? "(null)"} "
            + $"DetailCode={NormalizeSingleLine(detailCode, 128)}");
    }

    private static void AppendOptionalCount(StringBuilder builder, string name, int? value)
    {
        if (value.HasValue)
        {
            builder.AppendLine($"{name}: {value.Value}");
        }
    }

    private static string NormalizeSingleLine(string? value, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? "(null)"
            : value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength] + "…";
    }

    private static string NormalizeMultiline(string value, int maximumLength)
    {
        var normalized = value.Replace("\0", string.Empty).Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength] + "…";
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
