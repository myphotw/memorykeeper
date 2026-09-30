namespace MemoryKeeper.Mobile.Diagnostics;

/// <summary>
/// Carries fixed, credential-free startup checkpoints to the platform diagnostic sink.
/// It is intentionally best-effort so diagnostics can never become a startup dependency.
/// </summary>
public static class MobileStartupCheckpoint
{
    private static Action<string>? _record;
    private static Action<string, string>? _fail;
    private static Action<string>? _complete;
    private static string _lastCheckpoint = "PROCESS-START";

    public static void Configure(
        Action<string> record,
        Action<string, string> fail,
        Action<string> complete)
    {
        _record = record;
        _fail = fail;
        _complete = complete;
    }

    public static void Record(string checkpoint)
    {
        _lastCheckpoint = checkpoint;
        try
        {
            _record?.Invoke(checkpoint);
        }
        catch
        {
            // Startup diagnostics must never affect application startup.
        }
    }

    public static void Fail(Exception exception)
    {
        var exceptionType = exception.GetBaseException().GetType().Name;
        try
        {
            _fail?.Invoke(_lastCheckpoint, exceptionType);
        }
        catch
        {
            // Preserve Android's normal unhandled-exception behavior.
        }
    }

    public static void Complete(string checkpoint)
    {
        _lastCheckpoint = checkpoint;
        try
        {
            _complete?.Invoke(checkpoint);
        }
        catch
        {
            // Startup diagnostics must never affect application startup.
        }
    }
}
