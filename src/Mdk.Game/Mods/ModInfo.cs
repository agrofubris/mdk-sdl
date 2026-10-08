using System.Globalization;

namespace Mdk.Game.Mods;

/// <summary>A mod's <c>mod.txt</c>: <c>name=value</c> lines (<c>#</c> comments); all optional.
/// The name defaults to the folder's; a higher priority wins over lower ones (default 0).
/// <code>
///   name=Clean walls
///   author=Me
///   version=1.0
///   description=Cleaner walls for LEVEL3
///   priority=10
/// </code></summary>
public sealed record ModInfo(string Folder, string Name, string Author, string Version, string Description, int Priority)
{
    public const string FileName = "mod.txt";
    private const char Comment = '#';

    /// <summary>The mod of a folder (its <c>mod.txt</c>, if any).</summary>
    public static ModInfo Load(string folder)
    {
        var path = Path.Combine(folder, FileName);
        var name = Path.GetFileName(folder);
        try
        {
            return Parse(name, File.Exists(path) ? File.ReadAllText(path) : "");
        }
        catch (IOException e)
        {
            Console.Error.WriteLine($"Mod {name}: {e.Message}");
            return Parse(name, "");
        }
    }

    public static ModInfo Parse(string folder, string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n'))
        {
            var parts = line.Trim().Split('=', 2);
            if (parts.Length != 2 || parts[0].StartsWith(Comment))
            {
                continue;
            }

            values[parts[0].Trim()] = parts[1].Trim();
        }

        var priority = int.TryParse(values.GetValueOrDefault("priority"), CultureInfo.InvariantCulture, out var p) ? p : 0;
        var name = values.GetValueOrDefault("name", "");
        return new ModInfo(folder, name.Length != 0 ? name : folder, values.GetValueOrDefault("author", ""),
            values.GetValueOrDefault("version", ""), values.GetValueOrDefault("description", ""), priority);
    }

    /// <summary>The <c>mod.txt</c> text (a generated mod's).</summary>
    public string Format() => string.Create(CultureInfo.InvariantCulture,
        $"name={Name}\nauthor={Author}\nversion={Version}\ndescription={Description}\npriority={Priority}\n");
}
