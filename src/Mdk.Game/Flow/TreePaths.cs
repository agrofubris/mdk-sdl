namespace Mdk.Game.Flow;

/// <summary>Android: the file path of a folder picked with the system's folder picker (a tree URI's
/// authority and document id), for the game to read it in place with All files access.
/// <code>
///   externalstorage  primary:MDK        ─► /storage/emulated/0/MDK
///                    home:MDK           ─► /storage/emulated/0/Documents/MDK
///                    1234-ABCD:MDK      ─► /storage/1234-ABCD/MDK (SD card)
///   downloads        raw:/storage/...   ─► /storage/...
///   anything else (cloud, msf:42)       ─► null
/// </code></summary>
public static class TreePaths
{
    public const string ExternalStorage = "com.android.externalstorage.documents";
    public const string Downloads = "com.android.providers.downloads.documents";
    private const string PrimaryVolume = "primary";
    private const string HomeVolume = "home";
    private const string HomeFolder = "Documents";
    private const string Raw = "raw:";
    private const string Volumes = "/storage";
    /// <summary>The app's external files folder: primary storage/Android/data/package/files.</summary>
    private const string AppData = "/Android/data/";
    private const char VolumeSeparator = ':';
    private const char Separator = '/';

    /// <summary>The picked folder's path; null when it has none (a cloud, a Downloads list entry).</summary>
    public static string? Resolve(string authority, string documentId, string primaryRoot)
    {
        if (authority == Downloads && documentId.StartsWith(Raw, StringComparison.Ordinal))
        {
            return documentId[Raw.Length..];
        }

        var colon = documentId.IndexOf(VolumeSeparator);
        if (authority != ExternalStorage || colon < 0)
        {
            return null;
        }

        var volume = documentId[..colon];
        var root = volume switch
        {
            PrimaryVolume => primaryRoot,
            HomeVolume => primaryRoot + Separator + HomeFolder,
            _ => Volumes + Separator + volume,
        };
        var folder = documentId[(colon + 1)..].Trim(Separator);
        return folder.Length == 0 ? root : root + Separator + folder;
    }

    /// <summary>The primary storage's root, from the app's external files folder; null when that
    /// isn't on it.</summary>
    public static string? PrimaryRoot(string appFiles)
    {
        var at = appFiles.IndexOf(AppData, StringComparison.Ordinal);
        return at < 0 ? null : appFiles[..at];
    }
}
