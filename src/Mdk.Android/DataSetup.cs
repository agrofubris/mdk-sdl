using Android.App;
using Android.Content;
using Mdk.Formats;
using Mdk.Game.HdTextures;
using Uri = Android.Net.Uri;

namespace Mdk.Android;

/// <summary>The game's files on first start: the user picks the MDK folder copied to the phone,
/// its game folders are copied into the app's own folder once (Android's shared storage has no
/// paths the game could read), and its <c>textures-hd</c> (made on a PC) into the user folder.
/// Options, "Import from folder" picks it again and copies what's new or changed. Runs on SDL's
/// thread; dialogs show on the UI thread.
/// <code>
///   imported? ─yes─► MdkData
///      └no─► "pick your MDK folder" ─► folder picker ─► MDK in it? ─no─► "not MDK", again
///                                                          └yes─► copy (progress) ─► MdkData
/// </code></summary>
internal sealed class DataSetup(Activity activity, string dir, string userFolder)
{
    /// <summary>The folder picker's request code ("MD").</summary>
    public const int PickRequest = 0x4d44;
    /// <summary>The progress shows every this many files.</summary>
    private const int ProgressStep = 20;
    private const string Intro = "Copy your MDK installation (GOG, Steam or CD: the folder with TRAVERSE, MISC, FALL3D, STREAM) to this device, then pick that folder. Its game files (about 170 MB) are copied into the app once.";
    private const string NotMdk = "That folder has no MDK game files (TRAVERSE/TRAVSPRT.BNI). Pick the MDK folder.";
    private const string CopyFailed = "Copying failed: ";
    private const string Again = "Pick your MDK folder again: new or changed game files are copied, and textures-hd (HD textures made on a PC) if it's in it.";

    private TaskCompletionSource<Uri?>? _picked;

    /// <summary>The imported installation, after asking for it if needed; null when the user quits.</summary>
    public MdkData? Get()
    {
        if (MdkData.Imported(dir) is { } data)
        {
            return data;
        }

        return PickAndCopy(Intro, "Quit");
    }

    /// <summary>Options' "Import from folder": the folder picked and copied again (the game goes on
    /// with the files it has when the user cancels).</summary>
    public void Reimport()
    {
        if (PickAndCopy(Again, "Cancel") != null)
        {
            Dialogs.Tell(activity, "Import done. HD textures apply from the next level.");
        }
    }

    /// <summary>Asks for the MDK folder until one is copied (its data) or the user refuses (null).</summary>
    private MdkData? PickAndCopy(string intro, string no)
    {
        var message = intro;
        while (Dialogs.Ask(activity, message, "Pick folder", no) == Answer.Yes)
        {
            if (Pick() is not { } uri)
            {
                message = intro;
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

    /// <summary>Copies the installation and its HD textures, showing the progress.</summary>
    private MdkData Copy(DocumentTree tree, string root)
    {
        var dialog = Dialogs.Show(activity, "Copying the game files...");
        Action<int, int> Progress(string what) => (done, total) =>
        {
            if (done % ProgressStep == 0 || done == total)
            {
                activity.RunOnUiThread(() => dialog.SetMessage($"Copying {what}: {done} of {total}"));
            }
        };

        try
        {
            var data = MdkData.Import(tree, root, dir, Progress("the game files"));
            MdkData.CopyFolder(tree, root, HdCache.FolderName, HdCache.FolderIn(userFolder), Progress("the HD textures"));
            return data;
        }
        finally
        {
            activity.RunOnUiThread(() => dialog.Dismiss());
        }
    }
}
