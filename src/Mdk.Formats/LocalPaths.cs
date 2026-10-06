namespace Mdk.Formats;

/// <summary><c>mdk_paths.cfg</c>, a file of your own next to the program (the Godot port's), naming
/// the game's folder (<c>mdk</c>) and the 1996 demo's (<c>beta</c>), so no environment variable is
/// needed:
/// <code>
/// [paths]
/// mdk="C:/GOG Games/MDK"
/// beta="C:/Games/MDK (1996-08-06) (beta demo)"
/// </code></summary>
public static class LocalPaths
{
    public const string FileName = "mdk_paths.cfg";
    public const string Game = "mdk";
    public const string Beta = "beta";
    private const string Section = "paths";
    private const char Quote = '"';

    /// <summary>The folder named by <paramref name="key"/> in the file next to the program, or "".</summary>
    public static string Get(string key)
    {
        var path = Path.Combine(AppContext.BaseDirectory, FileName);
        return File.Exists(path) ? Parse(File.ReadAllText(path), key) : "";
    }

    /// <summary>The value of <paramref name="key"/> in the <c>[paths]</c> section of a config text
    /// (Godot's ConfigFile: quoted strings, <c>;</c> comments), or "".</summary>
    public static string Parse(string text, string key)
    {
        var section = "";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }

            var equals = line.IndexOf('=');
            if (section != Section || equals < 0 || line[..equals].Trim() != key)
            {
                continue;
            }

            return Unquote(line[(equals + 1)..].Trim());
        }

        return "";
    }

    /// <summary>"C:/Games/MDK" → C:/Games/MDK; escaped quotes and backslashes kept as one.</summary>
    private static string Unquote(string value)
    {
        if (value.Length < 2 || value[0] != Quote || value[^1] != Quote)
        {
            return value;
        }

        return value[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
    }
}
