using System.Globalization;
using Android.App;
using Android.Content;
using Android.Provider;
using Mdk.Formats;
using Mdk.Game.Flow;
using Mdk.Game.HdTextures;
using Mdk.Game.Mods;
using Uri = Android.Net.Uri;

namespace Mdk.Android;

/// <summary>The game's files and the user folder (settings, saves, mods, mdk.log).</summary>
internal sealed record GameFiles(MdkData Data, string UserFolder);

/// <summary>The game's files on first start. With All files access the user picks the MDK folder
/// copied to the phone and the game plays from it in place, keeping settings, saves and mods
/// there (<see cref="StorageChoice"/> remembers it, each start checks it). Without (declined, a
/// store build) its game folders are copied into the app's own folder once, and its <c>mods</c>
/// (HD textures made on a PC among them) into the user folder. An install that copied before
/// keeps its copy. Options, "Import from folder" picks it again: in place, another folder (or,
/// from the copy, the folder in place); copied, what's new or changed. Runs on SDL's thread;
/// dialogs show on the UI thread.
/// <code>
///   chosen folder readable? ─yes─► MdkData in place
///      └gone─► "can't read it" ─► forget ─┐
///   copied before? ─yes─► MdkData in the app ◄┘
///      └no─► "play in place?" ─allow─► All files access ─► folder picker ─► path, MDK, writable? ─► remember
///              └copy instead, refused, store build ─► folder picker ─► MDK in it? ─► copy (progress)
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
    private const string Again = "Pick your MDK folder again: new or changed game files are copied, and mods (HD textures made on a PC among them) if it has them.";
    private const string AccessIntro = "MDK can play straight from your MDK installation (GOG, Steam or CD: the folder with TRAVERSE, MISC, FALL3D, STREAM) copied to this device, keeping its settings, saves and mods there. That needs All files access: switch it on for MDK on the next screen, then come back. Or copy the game files (about 170 MB) into the app instead.";
    private const string PickIntro = "Pick your MDK folder on this device's storage (e.g. MDK on the internal storage). The game reads it in place and keeps its settings, saves and mods there.";
    private const string PickAgain = "Pick the MDK folder to play from: settings and saves are copied there, the game reads it from the next start.";
    private const string Switch = "Play from your MDK folder in place (All files access; settings and saves are copied there, the game reads it from the next start), or copy new or changed game files into the app again?";
    private const string NoAccess = "MDK has no All files access: it can't read the folder in place.";
    private const string NoPath = "That folder isn't on this device's storage (a cloud, the Downloads list?). Pick it from the device's storage: in the picker's menu, show the internal storage, then pick your MDK folder.";
    private const string NotWritable = "MDK can't write to that folder (settings and saves go there). Pick a folder on the internal storage.";
    private const string Lost = "MDK can't read {0} any more (moved, deleted, or All files access switched off). Saves there stay there; pick the folder again to play from it.";
    private const string NextStart = "MDK plays from {0} from the next start; your settings and saves were copied there.";
    /// <summary>The primary storage's usual root, when the app's folder doesn't tell.</summary>
    private const string DefaultPrimary = "/storage/emulated/0";

    private readonly AllFilesAccess _access = new(activity);
    private readonly StorageChoice _choice = new(userFolder);
    private TaskCompletionSource<Uri?>? _picked;
    /// <summary>The folder played from in place this session, or null (the copy).</summary>
    private string? _inPlace;

    /// <summary>The installation: the chosen folder, the copy, or asked for; null when the user quits.</summary>
    public GameFiles? Get()
    {
        var plan = _choice.Plan(_access.Current, MdkData.Imported(dir));
        if (plan.Kind == StorageKind.Lost)
        {
            Dialogs.Tell(activity, string.Format(CultureInfo.InvariantCulture, Lost, _choice.Folder));
            _choice.Forget();
            plan = _choice.Plan(_access.Current, MdkData.Imported(dir));
        }

        if (plan.Data is { } data)
        {
            _inPlace = plan.Kind == StorageKind.InPlace ? data.Dir : null;
            return new GameFiles(data, _inPlace ?? userFolder);
        }

        // Declined, refused or backed out of: the copy, as before.
        if (plan.Kind == StorageKind.AskAccess
            && Dialogs.Ask(activity, AccessIntro, "Allow", "Copy instead") == Answer.Yes
            && ChooseInPlace(PickIntro, "Copy instead") is { } chosen)
        {
            _inPlace = chosen.Dir;
            return new GameFiles(chosen, chosen.Dir);
        }

        return PickAndCopy(Intro, "Quit") is { } copy ? new GameFiles(copy, userFolder) : null;
    }

    /// <summary>Options' "Import from folder". In place: another folder, from the next start.
    /// Copied: the folder in place from the next start (not on a store build), or picked and
    /// copied again. The game goes on with the files it has when the user cancels.</summary>
    public void Reimport()
    {
        if (_inPlace is { } current)
        {
            MoveTo(current, PickAgain);
            return;
        }

        if (_access.Current != FolderAccess.Unavailable && Dialogs.Ask(activity, Switch, "Play in place", "Copy again") == Answer.Yes)
        {
            MoveTo(userFolder, PickAgain);
            return;
        }

        if (PickAndCopy(Again, "Cancel") != null)
        {
            Dialogs.Tell(activity, "Import done. HD textures apply from the next level.");
        }
    }

    /// <summary>A folder to play from in place from the next start; settings and saves follow.</summary>
    private void MoveTo(string current, string intro)
    {
        if (ChooseInPlace(intro, "Cancel") is not { } chosen || chosen.Dir == current)
        {
            return;
        }

        StorageChoice.CopyUserFiles(current, chosen.Dir);
        Dialogs.Tell(activity, string.Format(CultureInfo.InvariantCulture, NextStart, chosen.Dir));
    }

    /// <summary>All files access, then the MDK folder until one has the game and can be written
    /// (remembered: its data) or the user refuses (null).</summary>
    private MdkData? ChooseInPlace(string intro, string no)
    {
        if (!_access.Request())
        {
            Dialogs.Tell(activity, NoAccess);
            return null;
        }

        var primary = TreePaths.PrimaryRoot(userFolder) ?? DefaultPrimary;
        var message = intro;
        while (Dialogs.Ask(activity, message, "Pick folder", no) == Answer.Yes)
        {
            if (Pick() is not { } uri)
            {
                message = intro;
                continue;
            }

            if (PathOf(uri, primary) is not { } path)
            {
                message = NoPath;
                continue;
            }

            if (MdkData.At(path) is not { } data)
            {
                message = NotMdk;
                continue;
            }

            if (!StorageChoice.IsWritable(data.Dir))
            {
                message = NotWritable;
                continue;
            }

            // An older build's textures-hd there moves into its mod, as a copy's does.
            HdGenerator.Migrate(data.Dir);
            _choice.Choose(data.Dir);
            return data;
        }

        return null;
    }

    /// <summary>The picked tree's file path (<see cref="TreePaths"/>), or null.</summary>
    private static string? PathOf(Uri tree, string primary)
    {
        if (tree.Authority is not { } authority || DocumentsContract.GetTreeDocumentId(tree) is not { } document)
        {
            return null;
        }

        return TreePaths.Resolve(authority, document, primary);
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

    /// <summary>The folder picker's answer, the All files access screen's or the storage
    /// permission's (the activity passes them on). Returns whether it was ours.</summary>
    public bool OnResult(int request, Result result, Intent? data)
    {
        if (_access.OnAnswer(request))
        {
            return true;
        }

        if (request != PickRequest)
        {
            return false;
        }

        _picked?.TrySetResult(result == Result.Ok ? data?.Data : null);
        return true;
    }

    /// <summary>The storage permission's answer (the activity passes it on). Returns whether it was ours.</summary>
    public bool OnPermissions(int request) => _access.OnAnswer(request);

    /// <summary>Android's folder picker; null when the user backs out.</summary>
    private Uri? Pick()
    {
        _picked = new TaskCompletionSource<Uri?>();
        activity.RunOnUiThread(() => activity.StartActivityForResult(new Intent(Intent.ActionOpenDocumentTree), PickRequest));
        return _picked.Task.Result;
    }

    /// <summary>Copies the installation and its mods (HD textures made on a PC among them; an older
    /// build's textures-hd moved into its mod), showing the progress.</summary>
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
            MdkData.CopyFolder(tree, root, ModCatalog.FolderName, ModCatalog.FolderIn(userFolder), Progress("the mods"));
            MdkData.CopyFolder(tree, root, HdGenerator.OldFolder, Path.Combine(userFolder, HdGenerator.OldFolder), Progress("the HD textures"));
            HdGenerator.Migrate(userFolder);
            return data;
        }
        finally
        {
            activity.RunOnUiThread(() => dialog.Dismiss());
        }
    }
}
