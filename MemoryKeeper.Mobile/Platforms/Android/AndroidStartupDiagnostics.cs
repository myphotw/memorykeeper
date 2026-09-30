using Android.Content;
using Android.Widget;
using MemoryKeeper.Mobile.Diagnostics;

namespace MemoryKeeper.Mobile;

internal static class AndroidStartupDiagnostics
{
    private const string PreferencesName = "memorykeeper.startup.diagnostics";
    private const string CheckpointKey = "checkpoint";
    private const string ExceptionTypeKey = "exception_type";
    private const string CompletedKey = "completed";

    public static void Initialize(Context context)
    {
#if DEBUG
        try
        {
            var applicationContext = context.ApplicationContext ?? context;
            ShowPreviousIncompleteRun(applicationContext);
            MobileStartupCheckpoint.Configure(
                checkpoint => SaveCheckpoint(applicationContext, checkpoint),
                (checkpoint, exceptionType) => SaveFailure(applicationContext, checkpoint, exceptionType),
                checkpoint => SaveCompleted(applicationContext, checkpoint));
        }
        catch
        {
            // Diagnostics must not introduce a new startup failure.
        }
#endif
    }

#if DEBUG
    private static void ShowPreviousIncompleteRun(Context context)
    {
        var preferences = GetPreferences(context);
        if (preferences is null || preferences.GetBoolean(CompletedKey, true))
        {
            return;
        }

        var checkpoint = preferences.GetString(CheckpointKey, "UNKNOWN") ?? "UNKNOWN";
        var exceptionType = preferences.GetString(ExceptionTypeKey, null);
        var diagnostic = string.IsNullOrWhiteSpace(exceptionType)
            ? $"PREVIOUS-STARTUP-STOP / {checkpoint}"
            : $"PREVIOUS-STARTUP-FAIL / {checkpoint} / {exceptionType}";
        ShowToast(context, diagnostic);
    }

    private static void SaveCheckpoint(Context context, string checkpoint)
    {
        var editor = GetPreferences(context)?.Edit();
        editor?.PutString(CheckpointKey, checkpoint);
        editor?.Remove(ExceptionTypeKey);
        editor?.PutBoolean(CompletedKey, false);
        _ = editor?.Commit();
    }

    private static void SaveFailure(Context context, string checkpoint, string exceptionType)
    {
        var editor = GetPreferences(context)?.Edit();
        editor?.PutString(CheckpointKey, checkpoint);
        editor?.PutString(ExceptionTypeKey, exceptionType);
        editor?.PutBoolean(CompletedKey, false);
        _ = editor?.Commit();
        ShowToast(context, $"STARTUP-FAIL / {checkpoint} / {exceptionType}");
    }

    private static void SaveCompleted(Context context, string checkpoint)
    {
        var editor = GetPreferences(context)?.Edit();
        editor?.PutString(CheckpointKey, checkpoint);
        editor?.Remove(ExceptionTypeKey);
        editor?.PutBoolean(CompletedKey, true);
        _ = editor?.Commit();
    }

    private static ISharedPreferences? GetPreferences(Context context) =>
        context.GetSharedPreferences(PreferencesName, FileCreationMode.Private);

    private static void ShowToast(Context context, string diagnostic)
    {
        try
        {
            Toast.MakeText(context, diagnostic, ToastLength.Long)?.Show();
        }
        catch
        {
            // A Toast failure must not replace the original startup failure.
        }
    }
#endif
}
