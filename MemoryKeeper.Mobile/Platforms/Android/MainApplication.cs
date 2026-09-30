using Android.App;
using Android.Runtime;
using MemoryKeeper.Mobile.Diagnostics;

namespace MemoryKeeper.Mobile;

[Application]
public sealed class MainApplication : MauiApplication
{
    public MainApplication(nint handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    public override void OnCreate()
    {
#if DEBUG
        AndroidStartupDiagnostics.Initialize(this);
        MobileStartupCheckpoint.Record("ANDROID-APPLICATION");
#endif

        try
        {
            base.OnCreate();
#if DEBUG
            MobileStartupCheckpoint.Record("ANDROID-APPLICATION-DONE");
#endif
        }
        catch (Exception exception)
        {
#if DEBUG
            MobileStartupCheckpoint.Fail(exception);
#endif
            throw;
        }
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
