using Android.App;
using Android.Content.PM;
using Android.OS;
using MemoryKeeper.Mobile.Diagnostics;

namespace MemoryKeeper.Mobile;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize
                           | ConfigChanges.Orientation
                           | ConfigChanges.UiMode
                           | ConfigChanges.ScreenLayout
                           | ConfigChanges.SmallestScreenSize
                           | ConfigChanges.Density)]
public sealed class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
#if DEBUG
        MobileStartupCheckpoint.Record("ANDROID-ACTIVITY");
#endif

        try
        {
            base.OnCreate(savedInstanceState);
#if DEBUG
            MobileStartupCheckpoint.Record("ANDROID-ACTIVITY-DONE");
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

    protected override void OnPostResume()
    {
        base.OnPostResume();
#if DEBUG
        MobileStartupCheckpoint.Complete("STARTUP-COMPLETE");
#endif
    }
}
