namespace Mdk.Formats;

/// <summary>A folder tree an installation is copied from (Android: the folder the user picked,
/// read through its content URIs). Paths are relative, '/'-separated: "" is the root.</summary>
public interface IDataTree
{
    /// <summary>The files and folders of a folder.</summary>
    IReadOnlyList<DataEntry> List(string folder);

    Stream Open(string file);
}

/// <summary>A file or folder of a tree; a file's size in bytes, or <see cref="UnknownSize"/>.</summary>
public readonly record struct DataEntry(string Name, EntryKind Kind, long Size = DataEntry.UnknownSize)
{
    public const long UnknownSize = -1;
}

public enum EntryKind { File, Folder }

/// <summary>Importing: an installation somewhere the game can't read files by path (Android's
/// shared storage) is copied once into a folder of the program's own.
/// <code>
///   picked folder ─FindIn─► "" or "MDK" ─Import─► app folder/TRAVERSE, MISC... + stamp ─Imported─► MdkData
/// </code></summary>
public sealed partial class MdkData
{
    /// <summary>The folders the game reads; the rest (executables, saves) stays behind.</summary>
    private static readonly string[] GameFolders = ["TRAVERSE", "FALL3D", "STREAM", "MISC"];
    /// <summary>Written last: a copy without it was interrupted.</summary>
    private const string ImportStamp = ".imported";
    private const char Separator = '/';

    /// <summary>The installation in a tree: its root (""), a folder of the root (the user picked
    /// the folder above, e.g. "MDK"), or null.</summary>
    public static string? FindIn(IDataTree tree)
    {
        if (HasMarker(tree, ""))
        {
            return "";
        }

        return tree.List("")
            .Where(e => e.Kind == EntryKind.Folder)
            .Select(e => e.Name)
            .FirstOrDefault(name => HasMarker(tree, name));
    }

    /// <summary>Copies the game's folders of <paramref name="root"/> in the tree into
    /// <paramref name="dir"/>, reporting (files done, files) after each file. A file already
    /// there with the same size is kept (an import again copies what changed).</summary>
    public static MdkData Import(IDataTree tree, string root, string dir, Action<int, int>? progress)
    {
        // A copy over an older one: interrupted until the stamp is back.
        var stamp = Path.Combine(dir, ImportStamp);
        Directory.CreateDirectory(dir);
        File.Delete(stamp);

        var files = tree.List(root)
            .Where(e => e.Kind == EntryKind.Folder && GameFolders.Contains(e.Name, StringComparer.OrdinalIgnoreCase))
            .SelectMany(e => FilesOf(tree, Join(root, e.Name)))
            .ToList();

        CopyFiles(tree, files, root, dir, progress);
        File.WriteAllText(stamp, "");
        return new MdkData(dir);
    }

    /// <summary>Copies the folder <paramref name="name"/> (any case) of <paramref name="root"/> in
    /// the tree into <paramref name="target"/>, e.g. HD textures made on a PC; returns the files
    /// copied (0 without that folder). Unchanged files are kept, as in <see cref="Import"/>.</summary>
    public static int CopyFolder(IDataTree tree, string root, string name, string target, Action<int, int>? progress)
    {
        var folder = tree.List(root)
            .FirstOrDefault(e => e.Kind == EntryKind.Folder && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        if (folder.Name == null)
        {
            return 0;
        }

        var from = Join(root, folder.Name);
        return CopyFiles(tree, FilesOf(tree, from).ToList(), from, target, progress);
    }

    /// <summary>Copies files of the tree under <paramref name="from"/> into <paramref name="dir"/>,
    /// skipping those already there with the same size; returns the files copied.</summary>
    private static int CopyFiles(IDataTree tree, List<(string Path, long Size)> files, string from, string dir, Action<int, int>? progress)
    {
        var done = 0;
        var copied = 0;
        foreach (var (file, size) in files)
        {
            var relative = from.Length == 0 ? file : file[(from.Length + 1)..];
            var path = Path.Combine(dir, relative);
            done++;
            if (size != DataEntry.UnknownSize && File.Exists(path) && new FileInfo(path).Length == size)
            {
                progress?.Invoke(done, files.Count);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var source = tree.Open(file))
            using (var target = File.Create(path))
            {
                source.CopyTo(target);
            }

            copied++;
            progress?.Invoke(done, files.Count);
        }

        return copied;
    }

    /// <summary>A finished import in <paramref name="dir"/>, or null.</summary>
    public static MdkData? Imported(string dir)
    {
        if (!File.Exists(Path.Combine(dir, ImportStamp)))
        {
            return null;
        }

        return File.Exists(CaseInsensitivePath.Resolve(dir, Marker)) ? new MdkData(dir) : null;
    }

    /// <summary>The tree has <see cref="Marker"/> under <paramref name="folder"/>, in any case.</summary>
    private static bool HasMarker(IDataTree tree, string folder)
    {
        var path = folder;
        var parts = Marker.Split(Separator);
        for (var i = 0; i < parts.Length; i++)
        {
            var kind = i == parts.Length - 1 ? EntryKind.File : EntryKind.Folder;
            var entry = tree.List(path).FirstOrDefault(e => e.Kind == kind && string.Equals(e.Name, parts[i], StringComparison.OrdinalIgnoreCase));
            if (entry.Name == null)
            {
                return false;
            }

            path = Join(path, entry.Name);
        }

        return true;
    }

    /// <summary>The files under a folder of the tree, at any depth, with their sizes.</summary>
    private static IEnumerable<(string Path, long Size)> FilesOf(IDataTree tree, string folder)
    {
        foreach (var entry in tree.List(folder))
        {
            var path = Join(folder, entry.Name);
            if (entry.Kind == EntryKind.File)
            {
                yield return (path, entry.Size);
                continue;
            }

            foreach (var file in FilesOf(tree, path))
            {
                yield return file;
            }
        }
    }

    private static string Join(string folder, string name) => folder.Length == 0 ? name : folder + Separator + name;
}
