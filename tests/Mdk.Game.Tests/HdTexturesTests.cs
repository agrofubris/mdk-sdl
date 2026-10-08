using Mdk.Formats;
using Mdk.Game.HdTextures;
using Mdk.Game.Level;

namespace Mdk.Game.Tests;

/// <summary>HD textures without the upscaler: the export (keys, colours, alpha, padding), the
/// finishing of the upscaler's images, the cache's manifest and the loader's choice.</summary>
public class HdTexturesTests : IDisposable
{
    private const int Rgba = 4;
    private const byte Opaque = 255;
    private const int Scale = 2;

    private static readonly Lazy<MdkData> Data = new(() => MdkData.Find() ?? throw new InvalidOperationException("MDK data not found"));

    private readonly string _folder = Directory.CreateTempSubdirectory("mdk-hd-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>Index 1 red, 2 blue, 3 green, the rest grey.</summary>
    private static Palette Colours()
    {
        var palette = new Palette();
        Array.Fill(palette.Rgba, (byte)128);
        new byte[] { 200, 0, 0, Opaque }.CopyTo(palette.Rgba, 1 * Rgba);
        new byte[] { 0, 0, 100, Opaque }.CopyTo(palette.Rgba, 2 * Rgba);
        new byte[] { 0, 150, 0, Opaque }.CopyTo(palette.Rgba, 3 * Rgba);
        return palette;
    }

    private static Texture Texture(string name, int width, int height, byte[] indices, int frames = 1) =>
        new() { Name = name, Width = width, Height = height, FrameCount = frames, Indices = indices };

    private static Palette With(Palette palette, int index, byte r)
    {
        var copy = new Palette();
        palette.Rgba.CopyTo(copy.Rgba, 0);
        copy.Rgba[index * Rgba] = r;
        return copy;
    }

    // --- Keys --------------------------------------------------------------------------------

    [Fact]
    public void KeyFollowsTheColoursUsed()
    {
        var texture = Texture("WALL", 2, 1, [1, 2]);
        var palette = Colours();
        var key = HdKey.Of(texture, palette);

        Assert.Equal(key, HdKey.Of(Texture("WALL", 2, 1, [1, 2]), Colours()));
        // A colour the texture doesn't use (3), or index 0 (always clear), changes nothing.
        Assert.Equal(key, HdKey.Of(texture, With(palette, 3, 7)));
        Assert.Equal(key, HdKey.Of(texture, With(palette, 0, 7)));
        // A colour it uses, or its indices, make another image.
        Assert.NotEqual(key, HdKey.Of(texture, With(palette, 2, 7)));
        Assert.NotEqual(key, HdKey.Of(Texture("WALL", 2, 1, [2, 1]), palette));
        Assert.NotEqual(key, HdKey.Of(Texture("WALL", 1, 2, [1, 2]), palette));
    }

    // --- Export ------------------------------------------------------------------------------

    [Fact]
    public void FramesTakeThePaletteIndexZeroClear()
    {
        var texture = Texture("ANIM", 2, 1, [1, 0, 2, 3], frames: 2);

        var first = TextureExport.Frame(texture, Colours(), 0);
        var second = TextureExport.Frame(texture, Colours(), 1);

        Assert.Equal(new byte[] { 200, 0, 0, Opaque, 0, 0, 0, 0 }, first);
        Assert.Equal(new byte[] { 0, 0, 100, Opaque, 0, 150, 0, Opaque }, second);
    }

    /// <summary>The upscaler's input: clear texels take their neighbours' colour (no black fringe
    /// on cut-outs), and the image wraps around by the margin (tiling textures meet without seams).</summary>
    [Fact]
    public void InputBleedsAndWraps()
    {
        var rgba = TextureExport.Frame(Texture("CUT", 3, 1, [1, 0, 2]), Colours(), 0);
        const int margin = 2;

        var padded = UpscaleImages.Input(rgba, 3, 1, margin);

        var (width, height) = UpscaleImages.Padded(3, 1, margin);
        Assert.Equal((7, 5), (width, height));
        Assert.Equal(width * height * Rgba, padded.Length);
        // Row 2 is the image's: (wrapped) 0 2 | 1 [between] 2 | 1 0 ...
        var row = padded.AsSpan(2 * width * Rgba, width * Rgba);
        Assert.Equal(new byte[] { 200, 0, 0, Opaque }, row.Slice(2 * Rgba, Rgba).ToArray());
        Assert.Equal(new byte[] { 0, 0, 100, Opaque }, row.Slice(4 * Rgba, Rgba).ToArray());
        Assert.Equal(new byte[] { 0, 0, 100, Opaque }, row.Slice(1 * Rgba, Rgba).ToArray());
        // The clear texel: the mean of red and blue, opaque.
        Assert.Equal(new byte[] { 100, 0, 50, Opaque }, row.Slice(3 * Rgba, Rgba).ToArray());
    }

    /// <summary>The upscaler's output: the margin cut, brought to the cache's scale, the alpha the
    /// source's (hard edges: 0 or 255).</summary>
    [Fact]
    public void OutputCropsScalesAndKeysAlpha()
    {
        var source = TextureExport.Frame(Texture("CUT", 2, 2, [1, 0, 1, 1]), Colours(), 0);
        const int margin = 1;
        const int factor = 4;
        var (paddedWidth, paddedHeight) = UpscaleImages.Padded(2, 2, margin);
        // A uniform grey output from a 4x upscaler.
        var upscaled = new byte[paddedWidth * factor * paddedHeight * factor * Rgba];
        Array.Fill(upscaled, (byte)90);

        var hd = UpscaleImages.Output(upscaled, factor, source, 2, 2, margin, Scale);

        Assert.Equal(4 * 4 * Rgba, hd.Length);
        for (var i = 0; i < 16; i++)
        {
            var (x, y) = (i % 4, i / 4);
            var clear = x >= 2 && y < 2;
            Assert.Equal(clear ? 0 : Opaque, hd[i * Rgba + 3]);
            Assert.Equal(90, hd[i * Rgba]);
        }
    }

    // --- Cache -------------------------------------------------------------------------------

    private HdCache CacheWith(Texture texture, Palette palette, int scale = Scale, int format = HdManifest.Format)
    {
        var width = texture.Width * scale;
        var height = texture.Height * scale * texture.FrameCount;
        var rgba = new byte[width * height * Rgba];
        for (var i = 0; i < width * height; i++)
        {
            rgba[i * Rgba] = 77;
            rgba[i * Rgba + 3] = (byte)(i % 2 == 0 ? Opaque : 0);
        }

        var key = HdKey.Of(texture, palette);
        var file = $"LEVEL3/{texture.Name}_{key}.png";
        Directory.CreateDirectory(Path.Combine(_folder, "LEVEL3"));
        File.WriteAllBytes(Path.Combine(_folder, file), Png.Encode(width, height, rgba, Png.Channels.Rgba));
        var manifest = new HdManifest("model", scale) { Version = format };
        manifest.Entries[key] = new HdManifest.Entry(key, 3, texture.Name, texture.Width, texture.Height, texture.FrameCount, file);
        manifest.Save(_folder);
        return HdCache.Open(_folder);
    }

    [Fact]
    public void ManifestRoundTrips()
    {
        var manifest = new HdManifest("realesrgan-x4plus", Scale);
        manifest.Entries["abc"] = new HdManifest.Entry("abc", 7, "B_WALL", 64, 32, 2, "LEVEL7/B_WALL_abc.png");
        manifest.Save(_folder);

        var loaded = HdManifest.Load(_folder);

        Assert.NotNull(loaded);
        Assert.Equal("realesrgan-x4plus", loaded.Model);
        Assert.Equal(Scale, loaded.Scale);
        Assert.Equal(manifest.Entries["abc"], loaded.Entries["abc"]);
    }

    [Fact]
    public void PresentHdIsUsed()
    {
        var texture = Texture("WALL", 2, 2, [1, 2, 3, 1]);
        var cache = CacheWith(texture, Colours());

        var image = cache.Find(texture, Colours());

        Assert.NotNull(image);
        Assert.Equal((4, 4, 1), (image.Width, image.Height, image.Frames));
        // Premultiplied as the renderer's colour textures: no colour where clear.
        Assert.Equal(new byte[] { 77, 0, 0, Opaque, 0, 0, 0, 0 }, image.Rgba.AsSpan(0, 2 * Rgba).ToArray());
    }

    /// <summary>A level's images are decoded at opening, handed out once, then read again if asked.</summary>
    [Fact]
    public void LevelImagesDecodeAtOpening()
    {
        var texture = Texture("WALL", 2, 2, [1, 2, 3, 1]);
        CacheWith(texture, Colours());

        var cache = HdCache.Open(_folder, 3);

        Assert.Equal(cache.Find(texture, Colours())!.Rgba, cache.Find(texture, Colours())!.Rgba);
    }

    [Fact]
    public void AnimatedFramesStack()
    {
        var texture = Texture("ANIM", 1, 1, [1, 2], frames: 2);

        var image = CacheWith(texture, Colours()).Find(texture, Colours());

        Assert.NotNull(image);
        Assert.Equal((2, 2, 2), (image.Width, image.Height, image.Frames));
        Assert.Equal(2 * 2 * 2 * Rgba, image.Rgba.Length);
    }

    [Fact]
    public void AbsentHdKeepsTheOriginal()
    {
        var cache = CacheWith(Texture("WALL", 2, 2, [1, 2, 3, 1]), Colours());

        Assert.Null(cache.Find(Texture("OTHER", 2, 2, [3, 3, 3, 3]), Colours()));
        Assert.Null(HdCache.Open(Path.Combine(_folder, "missing")).Find(Texture("WALL", 2, 2, [1, 2, 3, 1]), Colours()));
    }

    /// <summary>The game's texture or palette changed since the cache was made: its image is stale.</summary>
    [Fact]
    public void StaleHdKeepsTheOriginal()
    {
        var texture = Texture("WALL", 2, 2, [1, 2, 3, 1]);
        var cache = CacheWith(texture, Colours());

        Assert.Null(cache.Find(Texture("WALL", 2, 2, [1, 2, 3, 2]), Colours()));
        Assert.Null(cache.Find(texture, With(Colours(), 1, 9)));
    }

    [Fact]
    public void OtherFormatOrSizeIsIgnored()
    {
        var texture = Texture("WALL", 2, 2, [1, 2, 3, 1]);

        Assert.Null(CacheWith(texture, Colours(), format: HdManifest.Format + 1).Find(texture, Colours()));

        // Images of another scale than the manifest's.
        CacheWith(texture, Colours(), scale: 3);
        var wrong = new HdManifest("model", Scale);
        foreach (var (key, entry) in HdManifest.Load(_folder)!.Entries)
        {
            wrong.Entries[key] = entry;
        }

        wrong.Save(_folder);
        Assert.Null(HdCache.Open(_folder).Find(texture, Colours()));
    }

    // --- The game's textures --------------------------------------------------------------------

    /// <summary>Level 3's arenas and models: each texture once per distinct look, all in palettes of
    /// the level's arenas.</summary>
    [DataFact]
    public void LevelExportsItsTextures()
    {
        var level = new LevelData(Data.Value, 3);
        var cmi = Cmi.Load(Data.Value.PathOf("TRAVERSE/LEVEL3/LEVEL3.CMI"));

        var sources = TextureExport.Collect(level, cmi);

        Assert.True(sources.Count > 50, $"{sources.Count} textures");
        Assert.Equal(sources.Count, sources.Select(s => s.Key).Distinct().Count());
        Assert.All(sources, s => Assert.Equal(HdKey.Of(s.Texture, s.Palette), s.Key));
    }
}
