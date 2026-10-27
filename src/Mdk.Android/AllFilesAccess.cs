using System.Runtime.Versioning;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Mdk.Game.Flow;
using AndroidSettings = Android.Provider.Settings;
using Uri = Android.Net.Uri;

#if !STORE_BUILD
// Reading the MDK folder in place: All files access from Android 11, the storage permission
// before (Android 10 also needs requestLegacyExternalStorage, in the manifest). A store build
// (-p:StoreBuild=true) leaves them out and always copies.
[assembly: UsesPermission(global::Android.Manifest.Permission.ManageExternalStorage)]
[assembly: UsesPermission(global::Android.Manifest.Permission.ReadExternalStorage, MaxSdkVersion = 29)]
[assembly: UsesPermission(global::Android.Manifest.Permission.WriteExternalStorage, MaxSdkVersion = 29)]
#endif

namespace Mdk.Android;

/// <summary>Access to shared storage by path: checked, and asked for (the system's All files
/// access screen from Android 11, the storage permission dialog before). Asking runs on SDL's
/// thread and waits for the user's answer, which the activity passes on.</summary>
internal sealed class AllFilesAccess(Activity activity)
{
    /// <summary>The All files access screen's request code ("MA").</summary>
    public const int SettingsRequest = 0x4d41;
    /// <summary>The storage permission's request code ("MP").</summary>
    public const int PermissionRequest = 0x4d50;
    private const string PackageScheme = "package:";

    private static readonly string[] Permissions =
        [global::Android.Manifest.Permission.ReadExternalStorage, global::Android.Manifest.Permission.WriteExternalStorage];

    private TaskCompletionSource? _answered;

    /// <summary>All files access: Android 11 (API 30) on.</summary>
    [SupportedOSPlatformGuard("android30.0")]
    private static bool HasAllFiles => OperatingSystem.IsAndroidVersionAtLeast(30);

    public FolderAccess Current
    {
        get
        {
#if STORE_BUILD
            return FolderAccess.Unavailable;
#else
            return Granted() ? FolderAccess.Granted : FolderAccess.Askable;
#endif
        }
    }

    /// <summary>Asks for the access; returns whether it's granted once the user is back.</summary>
    public bool Request()
    {
        if (Current != FolderAccess.Askable)
        {
            return Current == FolderAccess.Granted;
        }

        _answered = new TaskCompletionSource();
        activity.RunOnUiThread(Ask);
        _answered.Task.Wait();
        return Granted();
    }

    /// <summary>The settings screen closed or the permission dialog was answered (the activity
    /// passes them on). Returns whether it was ours.</summary>
    public bool OnAnswer(int request)
    {
        if (request != SettingsRequest && request != PermissionRequest)
        {
            return false;
        }

        _answered?.TrySetResult();
        return true;
    }

    private bool Granted()
    {
        if (HasAllFiles)
        {
            return global::Android.OS.Environment.IsExternalStorageManager;
        }

        return Permissions.All(p => activity.CheckSelfPermission(p) == Permission.Granted);
    }

    /// <summary>All files access's settings screen from Android 11, the storage permission dialog before.</summary>
    private void Ask()
    {
        if (!HasAllFiles)
        {
            activity.RequestPermissions(Permissions, PermissionRequest);
            return;
        }

        OpenSettings();
    }

    /// <summary>The app's All files access switch; some devices only have the list of apps.</summary>
    [SupportedOSPlatform("android30.0")]
    private void OpenSettings()
    {
        try
        {
            var app = Uri.Parse(PackageScheme + activity.PackageName);
            activity.StartActivityForResult(new Intent(AndroidSettings.ActionManageAppAllFilesAccessPermission, app), SettingsRequest);
        }
        catch (ActivityNotFoundException)
        {
            try
            {
                activity.StartActivityForResult(new Intent(AndroidSettings.ActionManageAllFilesAccessPermission), SettingsRequest);
            }
            catch (ActivityNotFoundException)
            {
                _answered?.TrySetResult();
            }
        }
    }
}
