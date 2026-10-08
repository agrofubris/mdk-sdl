using System.Globalization;

namespace Mdk.Game.HdTextures;

/// <summary>The HD cache's index (<c>manifest.txt</c>): how it was made, then one line per image.
/// <code>
///   format=1
///   model=realesrgan-x4plus
///   scale=2
///   key level name width height frames file      (the source's size; the image is scale × it,
///   9f3a... 7 B_WALL 64 64 1 LEVEL7/B_WALL_9f3a....png   frames stacked downwards)
/// </code></summary>
public sealed class HdManifest(string model, int scale)
{
    /// <summary>The layout of the cache; another one is ignored (made again).</summary>
    public const int Format = 1;
    public const string FileName = "manifest.txt";
    private const int Fields = 7;

    public sealed record Entry(string Key, int Level, string Name, int Width, int Height, int Frames, string File);

    public int Version { get; init; } = Format;
    /// <summary>The upscaler's model and the images' scale.</summary>
    public string Model => model;
    public int Scale => scale;
    public Dictionary<string, Entry> Entries { get; } = [];

    /// <summary>The manifest of a cache folder, or null (none, unreadable).</summary>
    public static HdManifest? Load(string folder)
    {
        var path = Path.Combine(folder, FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        var settings = new Dictionary<string, string>();
        var entries = new List<Entry>();
        foreach (var line in File.ReadAllLines(path))
        {
            var setting = line.Split('=', 2);
            if (setting.Length == 2)
            {
                settings[setting[0]] = setting[1];
                continue;
            }

            if (Parse(line) is { } entry)
            {
                entries.Add(entry);
            }
        }

        if (!int.TryParse(settings.GetValueOrDefault("format"), CultureInfo.InvariantCulture, out var version)
            || !int.TryParse(settings.GetValueOrDefault("scale"), CultureInfo.InvariantCulture, out var scale))
        {
            return null;
        }

        var manifest = new HdManifest(settings.GetValueOrDefault("model", ""), scale) { Version = version };
        foreach (var entry in entries)
        {
            manifest.Entries[entry.Key] = entry;
        }

        return manifest;
    }

    private static Entry? Parse(string line)
    {
        var f = line.Split(' ');
        if (f.Length != Fields)
        {
            return null;
        }

        var numbers = new[] { f[1], f[3], f[4], f[5] }.Select(n => int.TryParse(n, CultureInfo.InvariantCulture, out var v) ? v : -1).ToArray();
        return numbers.Any(n => n < 0) ? null : new Entry(f[0], numbers[0], f[2], numbers[1], numbers[2], numbers[3], f[6]);
    }

    /// <summary>Writes the manifest (through a temporary file: an interrupted run leaves the last one).</summary>
    public void Save(string folder)
    {
        var lines = new List<string> { $"format={Version}", $"model={Model}", $"scale={Scale}" };
        lines.AddRange(Entries.Values.OrderBy(e => e.File, StringComparer.Ordinal)
            .Select(e => string.Create(CultureInfo.InvariantCulture, $"{e.Key} {e.Level} {e.Name} {e.Width} {e.Height} {e.Frames} {e.File}")));
        var path = Path.Combine(folder, FileName);
        File.WriteAllLines(path + ".tmp", lines);
        File.Move(path + ".tmp", path, overwrite: true);
    }
}
