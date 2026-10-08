namespace Mdk.Formats;

/// <summary>A folder tree an installation is copied from (Android: the folder the user picked,
/// read through its content URIs). Paths are relative, '/'-separated: "" is the root.</summary>
public interface IDataTree
{
    /// <summary>The files and folders of a folder.</summary>
    IReadOnlyList<DataEntry> List(string folder);

    Stream Open(string file);
}

public readonly record struct DataEntry(string Name, EntryKind Kind);

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
    /// <paramref name="dir"/>, reporting (files copied, files) after each file.</summary>
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

        var done = 0;
        foreach (var file in files)
        {
            var relative = root.Length == 0 ? file : file[(root.Length + 1)..];
            var path = Path.Combine(dir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var source = tree.Open(file))
            using (var target = File.Create(path))
            {
                source.CopyTo(target);
            }

            done++;
            progress?.Invoke(done, files.Count);
        }

        File.WriteAllText(stamp, "");
        return new MdkData(dir);
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

    /// <summary>The files under a folder of the tree, at any depth.</summary>
    private static IEnumerable<string> FilesOf(IDataTree tree, string folder)
    {
        foreach (var entry in tree.List(folder))
        {
            var path = Join(folder, entry.Name);
            if (entry.Kind == EntryKind.File)
            {
                yield return path;
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
