using Mdk.Formats;

namespace Mdk.Game.Mods;

/// <summary>The enabled mods' model replacements for a level (<see cref="ModelSwap"/>): per model,
/// the first mod's <c>models/LEVELn/NAME.glb</c>, else its <c>models/NAME.glb</c> (names ignore case).
/// Read at the level's load; a broken file keeps the original (and says why).</summary>
public sealed class ModModels
{
    public const string FolderName = "models";
    private const string Extension = ".glb";

    private readonly Dictionary<Model, ModelSwap> _swaps = [];
    private readonly List<string> _found = [];

    /// <summary>The models replaced, by name.</summary>
    public IReadOnlyList<string> Found => _found;

    public static ModModels Load(IReadOnlyList<string> mods, int level, IEnumerable<Model> models)
    {
        var loaded = new ModModels();
        var files = mods.Select(m => Files(Path.Combine(m, FolderName), level)).ToList();
        foreach (var model in models.Distinct())
        {
            var path = files.Select(f => f.GetValueOrDefault(model.Name)).FirstOrDefault(p => p != null);
            if (path == null || Read(path, model) is not { } swap)
            {
                continue;
            }

            loaded._swaps[model] = swap;
            if (!loaded._found.Contains(model.Name))
            {
                loaded._found.Add(model.Name);
            }
        }

        return loaded;
    }

    public ModelSwap? Of(Model model) => _swaps.GetValueOrDefault(model);

    /// <summary>A mod's models by name: the level's folder first.</summary>
    private static Dictionary<string, string> Files(string folder, int level)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in new[] { Path.Combine(folder, ModImages.LevelFolder(level)), folder })
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*" + Extension))
            {
                files.TryAdd(Path.GetFileNameWithoutExtension(path), path);
            }
        }

        return files;
    }

    private static ModelSwap? Read(string path, Model model)
    {
        try
        {
            var unknown = new List<string>();
            var swap = ModelSwap.Of(Glb.Read(File.ReadAllBytes(path)), model, unknown);
            if (unknown.Count > 0)
            {
                Console.Error.WriteLine($"Mod model {path}: no part {string.Join(", ", unknown)} in {model.Name}");
            }

            return swap;
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            Console.Error.WriteLine($"Mod model {path}: {e.Message}");
            return null;
        }
    }
}
