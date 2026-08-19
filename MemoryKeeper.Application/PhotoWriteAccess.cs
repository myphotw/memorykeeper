namespace MemoryKeeper.Application;

/// <summary>
/// Prevents backend-only photos from reaching legacy SQLite mutation paths.
/// </summary>
public static class PhotoWriteAccess
{
    public static bool CanWriteLocal(bool isBackendOnly) => !isBackendOnly;

    public static async Task<bool> TryExecuteLocalAsync(
        bool isBackendOnly,
        Func<Task> localWrite)
    {
        ArgumentNullException.ThrowIfNull(localWrite);
        if (!CanWriteLocal(isBackendOnly))
        {
            return false;
        }

        await localWrite().ConfigureAwait(false);
        return true;
    }
}
