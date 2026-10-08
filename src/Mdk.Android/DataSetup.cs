using Android.App;
using Android.Content;
using Mdk.Formats;
using Uri = Android.Net.Uri;

namespace Mdk.Android;

/// <summary>The game's files on first start: the user picks the MDK folder copied to the phone,
/// its game folders are copied into the app's own folder once (Android's shared storage has no
/// paths the game could read). Runs on SDL's thread; dialogs show on the UI thread.
/// <code>
///   imported? ─yes─► MdkData
///      └no─► "pick your MDK folder" ─► folder picker ─► MDK in it? ─no─► "not MDK", again
///                                                          └yes─► copy (progress) ─► MdkData
/// </code></summary>
internal sealed class DataSetup(Activity activity, string dir)
{
    /// <summary>The folder picker's request code ("MD").</summary>
    public const int PickRequest = 0x4d44;
    /// <summary>The progress shows every this many files.</summary>
    private const int ProgressStep = 20;
    private const string Intro = "Copy your MDK installation (GOG, Steam or CD: the folder with TRAVERSE, MISC, FALL3D, STREAM) to this device, then pick that folder. Its game files (about 170 MB) are copied into the app once.";
    private const string NotMdk = "That folder has no MDK game files (TRAVERSE/TRAVSPRT.BNI). Pick the MDK folder.";
    private const string CopyFailed = "Copying failed: ";

    private TaskCompletionSource<Uri?>? _picked;

    /// <summary>The imported installation, after asking for it if needed; null when the user quits.</summary>
    public MdkData? Get()
    {
        if (MdkData.Imported(dir) is { } data)
        {
            return data;
        }

        var message = Intro;
        while (Dialogs.Ask(activity, message, "Pick folder", "Quit") == Answer.Yes)
        {
            if (Pick() is not { } uri)
            {
                message = Intro;
                continue;
            }

            var tree = new DocumentTree(activity.ContentResolver!, uri);
            if (MdkData.FindIn(tree) is not { } root)
            {
                message = NotMdk;
                continue;
            }

            try
            {
                return Copy(tree, root);
            }
            catch (IOException e)
            {
                message = CopyFailed + e.Message;
            }
            catch (Java.Lang.Exception e)
            {
                message = CopyFailed + e.Message;
            }
        }

        return null;
    }

    /// <summary>The folder picker's answer (the activity passes it on). Returns whether it was ours.</summary>
    public bool OnResult(int request, Result result, Intent? data)
    {
        if (request != PickRequest)
        {
            return false;
        }

        _picked?.TrySetResult(result == Result.Ok ? data?.Data : null);
        return true;
    }

    /// <summary>Android's folder picker; null when the user backs out.</summary>
    private Uri? Pick()
    {
        _picked = new TaskCompletionSource<Uri?>();
        activity.RunOnUiThread(() => activity.StartActivityForResult(new Intent(Intent.ActionOpenDocumentTree), PickRequest));
        return _picked.Task.Result;
    }

    /// <summary>Copies the installation, showing the progress.</summary>
    private MdkData Copy(DocumentTree tree, string root)
    {
        var dialog = Dialogs.Show(activity, "Copying the game files...");
        try
        {
            return MdkData.Import(tree, root, dir, (done, total) =>
            {
                if (done % ProgressStep == 0 || done == total)
                {
                    activity.RunOnUiThread(() => dialog.SetMessage($"Copying the game files: {done} of {total}"));
                }
            });
        }
        finally
        {
            activity.RunOnUiThread(() => dialog.Dismiss());
        }
    }
}
