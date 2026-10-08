using Mdk.Formats;

namespace Mdk.Game.HdTextures;

/// <summary>An HD image for the renderer: <see cref="Frames"/> frames of <see cref="Width"/> x
/// <see cref="Height"/> stacked downwards, RGBA8 premultiplied (as its colour textures).</summary>
public sealed record HdImage(int Width, int Height, int Frames, byte[] Rgba);

/// <summary>The HD textures made by <see cref="HdGenerator"/> (<c>textures-hd/</c> in the user's
/// folder): the image of a texture seen through a palette, when the cache has one for exactly that
/// (<see cref="HdKey"/>) and it's whole; otherwise null, and the game expands the original.
/// A level's images are decoded together on every core when it opens; others when first asked.
/// <code>
///   texture × palette ─► key ─► manifest entry? same size? file? decodes to scale × size? ─► HdImage
/// </code></summary>
public sealed class HdCache
{
    public const string FolderName = "textures-hd";
    private const int Channels = 4;
    private const int Alpha = 3;

    private readonly string _folder;
    private readonly HdManifest? _manifest;
    /// <summary>The level's files decoded at opening (each handed out once: no copy is kept).</summary>
    private readonly Dictionary<string, Png.Image> _decoded = [];

    private HdCache(string folder, HdManifest? manifest)
    {
        _folder = folder;
        _manifest = manifest;
    }

    /// <summary>The cache folder in a user folder.</summary>
    public static string FolderIn(string userFolder) => Path.Combine(userFolder, FolderName);

    /// <summary>The cache of a folder (empty when it has no manifest of this <see cref="HdManifest.Format"/>),
    /// with the images of <paramref name="level"/> decoded.</summary>
    public static HdCache Open(string folder, int? level = null)
    {
        var manifest = HdManifest.Load(folder);
        var cache = new HdCache(folder, manifest?.Version == HdManifest.Format ? manifest : null);
        if (cache._manifest != null && level != null)
        {
            cache.Decode(cache._manifest.Entries.Values.Where(e => e.Level == level).ToList());
        }

        return cache;
    }

    private void Decode(List<HdManifest.Entry> entries)
    {
        var images = new Png.Image?[entries.Count];
        Parallel.For(0, entries.Count, i => images[i] = Load(entries[i]));
        for (var i = 0; i < entries.Count; i++)
        {
            if (images[i] is { } image)
            {
                _decoded[entries[i].Key] = image;
            }
        }
    }

    /// <summary>An entry's file decoded, or null (missing, broken).</summary>
    private Png.Image? Load(HdManifest.Entry entry)
    {
        var path = Path.Combine(_folder, entry.File);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return Png.Decode(File.ReadAllBytes(path));
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            Console.Error.WriteLine($"HD texture {entry.File}: {e.Message}");
            return null;
        }
    }

    /// <summary>How many images the cache has.</summary>
    public int Count => _manifest?.Entries.Count ?? 0;

    /// <summary>The HD image of a texture through a palette, or null.</summary>
    public HdImage? Find(Texture texture, Palette palette)
    {
        if (_manifest == null || !_manifest.Entries.TryGetValue(HdKey.Of(texture, palette), out var entry))
        {
            return null;
        }

        if (entry.Width != texture.Width || entry.Height != texture.Height || entry.Frames != texture.FrameCount)
        {
            return null;
        }

        var image = _decoded.Remove(entry.Key, out var decoded) ? decoded : Load(entry);
        var scale = _manifest.Scale;
        if (image == null || image.Width != texture.Width * scale || image.Height != texture.Height * scale * texture.FrameCount)
        {
            return null;
        }

        Premultiply(image.Rgba);
        return new HdImage(image.Width, texture.Height * scale, texture.FrameCount, image.Rgba);
    }

    private static void Premultiply(byte[] rgba)
    {
        for (var i = 0; i < rgba.Length; i += Channels)
        {
            var alpha = rgba[i + Alpha];
            for (var c = 0; c < Alpha; c++)
            {
                rgba[i + c] = (byte)((rgba[i + c] * alpha + byte.MaxValue / 2) / byte.MaxValue);
            }
        }
    }
}
