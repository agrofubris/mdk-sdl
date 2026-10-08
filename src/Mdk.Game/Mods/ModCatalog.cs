using Mdk.Game.Flow;

namespace Mdk.Game.Mods;

/// <summary>A mod switched on or off in the settings (<c>mod.&lt;folder&gt;=Off</c>).</summary>
public enum ModState { On, Off }

/// <summary>The mods in the user folder's <c>mods/</c>: each subfolder is one (hidden ones aren't),
/// ordered by priority (highest first), then by folder name. The first enabled mod with a file for
/// something wins. New mods are on.
/// <code>
///   mods/
///     hd-textures/   mod.txt  textures/...           (made by "Make HD textures")
///     clean-walls/   mod.txt  textures/LEVEL3/...  images/...  models/...
/// </code></summary>
public sealed class ModCatalog
{
    public const string FolderName = "mods";
    private const char Hidden = '.';

    private readonly string _folder;

    private ModCatalog(string folder, List<ModInfo> mods)
    {
        _folder = folder;
        Mods = mods;
    }

    /// <summary>Every mod found, in order.</summary>
    public IReadOnlyList<ModInfo> Mods { get; }

    /// <summary>The mods folder in a user folder.</summary>
    public static string FolderIn(string userFolder) => Path.Combine(userFolder, FolderName);

    public static ModCatalog Scan(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return new ModCatalog(folder, []);
        }

        var mods = Directory.GetDirectories(folder)
            .Where(d => !Path.GetFileName(d).StartsWith(Hidden))
            .Select(ModInfo.Load)
            .OrderByDescending(m => m.Priority)
            .ThenBy(m => m.Folder, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new ModCatalog(folder, mods);
    }

    /// <summary>The mods the settings leave on, in order.</summary>
    public IReadOnlyList<ModInfo> Enabled(Settings settings) => [.. Mods.Where(m => settings.StateOf(m.Folder) == ModState.On)];

    /// <summary>The enabled mods' folders, in order.</summary>
    public IReadOnlyList<string> Folders(Settings settings) => [.. Enabled(settings).Select(PathOf)];

    /// <summary>The enabled mods' images for a level (null: the menus).</summary>
    public ModImages ModImages(Settings settings, int? level) => Mdk.Game.Mods.ModImages.Open(Folders(settings), level);

    public string PathOf(ModInfo mod) => Path.Combine(_folder, mod.Folder);

    /// <summary>Only these mods on (--mod; tests), the others off.</summary>
    public void Only(Settings settings, IReadOnlyCollection<string> folders)
    {
        foreach (var mod in Mods)
        {
            var on = folders.Contains(mod.Folder, StringComparer.OrdinalIgnoreCase);
            settings.Mods[mod.Folder] = on ? ModState.On : ModState.Off;
        }
    }
}
