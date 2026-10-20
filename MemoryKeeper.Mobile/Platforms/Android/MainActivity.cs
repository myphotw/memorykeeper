using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using MemoryKeeper.Mobile.Diagnostics;
using System.Runtime.Versioning;

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
    private const int UploadMediaPickerRequestCode = 41001;
    private const int MediaLocationPermissionRequestCode = 41002;
    private readonly object _requestLock = new();
    private TaskCompletionSource<Intent?>? _uploadMediaPickerCompletion;
    private TaskCompletionSource<bool>? _mediaLocationPermissionCompletion;

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

    internal async Task<Intent?> PickUploadMediaAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<Intent?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_requestLock)
        {
            if (_uploadMediaPickerCompletion is not null)
            {
                throw new InvalidOperationException("A media picker request is already active.");
            }

            _uploadMediaPickerCompletion = completion;
        }

        using var cancellationRegistration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));
        try
        {
            using var pickerIntent = new Intent(RequireAndroidValue(
                Intent.ActionOpenDocument,
                nameof(Intent.ActionOpenDocument)));
            pickerIntent.AddCategory(RequireAndroidValue(
                Intent.CategoryOpenable,
                nameof(Intent.CategoryOpenable)));
            pickerIntent.SetType("*/*");
            pickerIntent.PutExtra(
                RequireAndroidValue(Intent.ExtraMimeTypes, nameof(Intent.ExtraMimeTypes)),
                new[] { "image/*", "video/*" });
            pickerIntent.PutExtra(
                RequireAndroidValue(Intent.ExtraAllowMultiple, nameof(Intent.ExtraAllowMultiple)),
                true);
            pickerIntent.AddFlags(ActivityFlags.GrantReadUriPermission);

            using var chooser = Intent.CreateChooser(
                pickerIntent,
                "업로드할 사진과 동영상 선택")
                ?? throw new InvalidOperationException("Android media chooser is unavailable.");
            StartActivityForResult(chooser, UploadMediaPickerRequestCode);
            return await completion.Task;
        }
        finally
        {
            lock (_requestLock)
            {
                if (ReferenceEquals(_uploadMediaPickerCompletion, completion))
                {
                    _uploadMediaPickerCompletion = null;
                }
            }
        }
    }

    internal async Task<bool> RequestMediaLocationPermissionAsync(
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            return true;
        }

        var mediaLocationPermission = GetMediaLocationPermission();
        if (CheckSelfPermission(mediaLocationPermission) == Permission.Granted)
        {
            return true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_requestLock)
        {
            if (_mediaLocationPermissionCompletion is not null)
            {
                throw new InvalidOperationException("A media permission request is already active.");
            }

            _mediaLocationPermissionCompletion = completion;
        }

        using var cancellationRegistration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));
        try
        {
            RequestPermissions(
                [mediaLocationPermission],
                MediaLocationPermissionRequestCode);
            return await completion.Task;
        }
        finally
        {
            lock (_requestLock)
            {
                if (ReferenceEquals(_mediaLocationPermissionCompletion, completion))
                {
                    _mediaLocationPermissionCompletion = null;
                }
            }
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode == UploadMediaPickerRequestCode)
        {
            TaskCompletionSource<Intent?>? completion;
            lock (_requestLock)
            {
                completion = _uploadMediaPickerCompletion;
                _uploadMediaPickerCompletion = null;
            }

            completion?.TrySetResult(resultCode == Result.Ok ? data : null);
            return;
        }

        base.OnActivityResult(requestCode, resultCode, data);
    }

    public override void OnRequestPermissionsResult(
        int requestCode,
        string[]? permissions,
        Permission[]? grantResults)
    {
        if (requestCode == MediaLocationPermissionRequestCode)
        {
            TaskCompletionSource<bool>? completion;
            lock (_requestLock)
            {
                completion = _mediaLocationPermissionCompletion;
                _mediaLocationPermissionCompletion = null;
            }

            completion?.TrySetResult(
                grantResults is { Length: > 0 }
                && grantResults.All(result => result == Permission.Granted));
            return;
        }

        base.OnRequestPermissionsResult(
            requestCode,
            permissions ?? [],
            grantResults ?? []);
    }

    [SupportedOSPlatform("android29.0")]
    private static string GetMediaLocationPermission() => RequireAndroidValue(
        global::Android.Manifest.Permission.AccessMediaLocation,
        nameof(global::Android.Manifest.Permission.AccessMediaLocation));

    private static string RequireAndroidValue(string? value, string name) =>
        value ?? throw new InvalidOperationException($"Android value '{name}' is unavailable.");
}
