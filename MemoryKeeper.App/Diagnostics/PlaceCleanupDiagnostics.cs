using System.Text;

namespace MemoryKeeper.App.Diagnostics;

/// <summary>
/// Best-effort diagnostics for place-cleanup assignment verification.
/// Log path: %LocalAppData%\MemoryKeeper\Logs\place-cleanup-diag.log
/// </summary>
public static class PlaceCleanupDiagnostics
{
    private static readonly object Sync = new();

    public static string LogFilePath { get; } = Path.Combine(
        StartupDiagnostics.LogDirectory,
        "place-cleanup-diag.log");

    public static void WriteAssignment(
        int selectedCount,
        int assignedCount,
        int updatedIdCount,
        int reclassAssignedCount,
        int reclassUnassignedCount,
        int postReloadCleanupSelectedCount,
        int postReloadWithPlaceIdCount)
    {
        try
        {
            var line =
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} PLACE_CLEANUP_ASSIGN_DIAG " +
                $"selected_count={selectedCount} " +
                $"assigned_count={assignedCount} " +
                $"updated_id_count={updatedIdCount} " +
                $"reclass_assigned_count={reclassAssignedCount} " +
                $"reclass_unassigned_count={reclassUnassignedCount} " +
                $"post_reload_cleanup_selected_count={postReloadCleanupSelectedCount} " +
                $"post_reload_with_place_id_count={postReloadWithPlaceIdCount}";

            lock (Sync)
            {
                Directory.CreateDirectory(StartupDiagnostics.LogDirectory);
                File.AppendAllText(LogFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostics must never affect place assignment.
        }
    }
}
