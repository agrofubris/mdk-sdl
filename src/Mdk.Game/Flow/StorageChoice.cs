using Mdk.Formats;

namespace Mdk.Game.Flow;

/// <summary>Whether the game may read and write shared storage by path (Android's All files
/// access; storage permission before Android 11).</summary>
public enum FolderAccess
{
    Granted,
    /// <summary>Not granted, but the user can be asked.</summary>
    Askable,
    /// <summary>A store build: never asked, the files are copied.</summary>
    Unavailable,
}

/// <summary>What a start does with the game's files (<see cref="StorageChoice.Plan"/>).</summary>
public enum StorageKind
{
    /// <summary>The chosen folder, read in place; it's the user folder too.</summary>
    InPlace,
    /// <summary>The copy in the app's folder (a store build, or an install from before).</summary>
    Copied,
    /// <summary>The chosen folder is gone or no longer readable: say so, forget it, plan again.</summary>
    Lost,
    /// <summary>Nothing yet: ask for access to play in place (declined: copy).</summary>
    AskAccess,
    /// <summary>Nothing yet and no access to ask for: copy.</summary>
    CopyFlow,
}

/// <summary>A start's plan; <see cref="Data"/> the installation for InPlace and Copied.</summary>
public readonly record struct StoragePlan(StorageKind Kind, MdkData? Data = null);

/// <summary>Android: the MDK folder the user chose to play from in place, remembered in a file of
/// the app's own folder and checked each start.
/// <code>
///   chosen? ─yes─► access and MDK there? ─yes─► InPlace
///      │                  └no─► Lost (forget, plan again)
///      └no─► copied before? ─yes─► Copied
///               └no─► store build? ─yes─► CopyFlow
///                        └no─► AskAccess
/// </code></summary>
public sealed class StorageChoice(string appFolder)
{
    private const string FileName = "data-folder.txt";
    private const string Probe = ".write-test";

    private string ChoiceFile => Path.Combine(appFolder, FileName);

    /// <summary>The chosen folder (its installation's), or null.</summary>
    public string? Folder
    {
        get
        {
            if (!File.Exists(ChoiceFile))
            {
                return null;
            }

            var folder = File.ReadAllText(ChoiceFile).Trim();
            return folder.Length == 0 ? null : folder;
        }
    }

    public void Choose(string folder)
    {
        Directory.CreateDirectory(appFolder);
        File.WriteAllText(ChoiceFile, folder);
    }

    public void Forget() => File.Delete(ChoiceFile);

    /// <summary>What this start does, given the access and the copy made before (or null).</summary>
    public StoragePlan Plan(FolderAccess access, MdkData? copy)
    {
        if (Folder is { } folder)
        {
            var data = access == FolderAccess.Granted ? MdkData.At(folder) : null;
            return data != null && IsWritable(data.Dir) ? new(StorageKind.InPlace, data) : new(StorageKind.Lost);
        }

        if (copy != null)
        {
            return new(StorageKind.Copied, copy);
        }

        return new(access == FolderAccess.Unavailable ? StorageKind.CopyFlow : StorageKind.AskAccess);
    }

    /// <summary>Settings and saves follow the user to a new folder; files already there are kept.</summary>
    public static void CopyUserFiles(string from, string to)
    {
        if (File.Exists(Settings.PathIn(from)) && !File.Exists(Settings.PathIn(to)))
        {
            File.Copy(Settings.PathIn(from), Settings.PathIn(to));
        }

        var saves = SaveGames.In(from).Directory;
        if (!Directory.Exists(saves))
        {
            return;
        }

        var target = Directory.CreateDirectory(SaveGames.In(to).Directory).FullName;
        foreach (var file in Directory.GetFiles(saves))
        {
            var copy = Path.Combine(target, Path.GetFileName(file));
            if (!File.Exists(copy))
            {
                File.Copy(file, copy);
            }
        }
    }

    /// <summary>Writes and deletes a probe file: settings, saves and the log go there.</summary>
    public static bool IsWritable(string folder)
    {
        var probe = Path.Combine(folder, Probe);
        try
        {
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
