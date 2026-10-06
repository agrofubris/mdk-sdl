using System.Drawing;
using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Formats;
using Mdk.Game.Kurt;

namespace Mdk.Game.Menu;

/// <summary>The Score-O-matic's heads (0x433268; stats_screen.gd): the <c>XGHEAD</c> model of
/// <c>STATS.BNI</c> (textures of <c>STATS.MTI</c>), at most 16, spinning at 157°/s, seen from the
/// origin along +Y with a focal length of 250 pixels; one row below 5, else two rows of
/// (count + 1) / 2 (8 at most). Drawn under the page's text and fades.
/// <code>
///   row 0 (z −3):     ☻   ☻   ☻   ☻        x = 20 (k + 1) / (m + 1) − 10, y 8
///   row 1 (z −4.5):     ☻   ☻   ☻
/// </code></summary>
public sealed class HeadsView
{
    private const string ModelName = "XGHEAD";
    private const string PaletteName = "PAL";
    private const string Textures = "MISC/STATS.MTI";
    public const int MaxHeads = 16;
    private const float Spin = 157f;
    private const float Focal = 250f;
    private const float Scale = 0.8f;
    private const float Near = 0.5f;
    private const float Far = 1000f;
    private const float Distance = 8f;
    private const float RowWidth = 20f;
    private const float FirstRowZ = -3f;
    private const float SecondRowZ = -4.5f;
    /// <summary>Up to 4 heads make one row.</summary>
    private const int OneRow = 5;
    private const int MaxPerRow = 8;
    private const int PaletteBytes = 768;

    private readonly record struct Batch(int First, int Count, Material Material);

    private readonly Renderer _renderer;
    private readonly int _mesh = -1;
    private readonly List<Batch> _batches = [];
    private readonly float[] _angles = new float[MaxHeads];

    /// <summary>The model through the page's palette (<paramref name="pageRgb"/> turns the BNI's
    /// palette into the page's: the system colours and the model's).</summary>
    public HeadsView(Renderer renderer, Bni bni, Func<byte[], byte[]> pageRgb, MdkData data)
    {
        _renderer = renderer;
        if (!bni.Has(ModelName) || !bni.Has(PaletteName))
        {
            return;
        }

        var model = Model.Parse(ModelName, bni.Bytes, bni.Entries[ModelName].Offset, Model.Header.NoFlags);
        var rgb = bni.Bytes.AsSpan(bni.Entries[PaletteName].Offset, PaletteBytes).ToArray();
        var palette = Palette.FromRgb(pageRgb(rgb));
        var archive = TextureArchive.Load(data.PathOf(Textures));
        _mesh = Build(model, palette, archive);
    }

    /// <summary>Each frame: the shown heads turn.</summary>
    public void Update(int shown, float delta)
    {
        for (var i = 0; i < Math.Min(shown, MaxHeads); i++)
        {
            _angles[i] += Spin * delta;
        }
    }

    /// <summary>Draws <paramref name="shown"/> of <paramref name="count"/> heads in the page's view
    /// (<paramref name="area"/>, canvas units).</summary>
    public void Draw(int shown, int count, RectangleF area)
    {
        if (_mesh < 0 || shown <= 0)
        {
            return;
        }

        var fieldOfView = float.RadiansToDegrees(2f * MathF.Atan(ScreenView.Size.Y / 2f / Focal));
        var view = CameraMath.View(Vector3.Zero, Vector3.UnitY, Vector3.UnitZ, fieldOfView, area.Width / area.Height, Near, Far);
        _renderer.BeginInset(view, area, InsetContent.Own, InsetLayer.UnderCanvas);
        foreach (var (position, i) in Places(shown, count).Select((p, i) => (p, i)))
        {
            var world = Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateRotationZ(float.DegreesToRadians(_angles[i])) * Matrix4x4.CreateTranslation(position);
            foreach (var batch in _batches)
            {
                _renderer.Draw(_mesh, batch.First, batch.Count, batch.Material, 0, world);
            }
        }

        _renderer.EndInset();
    }

    /// <summary>Where the shown heads are: one row below 5 heads, else two rows.</summary>
    public static IEnumerable<Vector3> Places(int shown, int count)
    {
        var perRow = count < OneRow ? count : count <= MaxHeads ? (count + 1) / 2 : MaxPerRow;
        shown = Math.Min(shown, perRow * 2);
        for (var i = 0; i < shown; i++)
        {
            var row = i / perRow;
            var k = i % perRow;
            var m = Math.Min(perRow, shown - row * perRow);
            var x = RowWidth * (k + 1) / (m + 1) - RowWidth / 2f;
            yield return new Vector3(x, Distance, row == 0 ? FirstRowZ : SecondRowZ);
        }
    }

    /// <summary>The model's rest pose, both faces, in batches by surface (textures or palette colours).</summary>
    private int Build(Model model, Palette palette, TextureArchive archive)
    {
        var paletteId = _renderer.CreatePalette(palette.Rgba);
        var textures = new Dictionary<Texture, int>();
        var bySurface = new Dictionary<Material, List<Vertex>>();
        foreach (var part in model.PartList)
        {
            for (var t = 0; t < part.TriangleMaterials.Length; t++)
            {
                if (Surface(part.TriangleMaterials[t], model, palette, archive, paletteId, textures) is not var (material, texture))
                {
                    continue;
                }

                var scale = texture == null ? Vector2.Zero : new Vector2(1f / texture.Width, 1f / texture.Height);
                if (!bySurface.TryGetValue(material, out var vertices))
                {
                    bySurface[material] = vertices = [];
                }

                for (var k = 0; k < 3; k++)
                {
                    vertices.Add(new Vertex(part.Vertices[part.TriangleIndices[t * 3 + k]], part.TriangleUvs[t * 3 + k] * scale));
                }
            }
        }

        var all = new List<Vertex>();
        foreach (var (material, vertices) in bySurface)
        {
            _batches.Add(new Batch(all.Count, vertices.Count, material));
            all.AddRange(vertices);
        }

        return all.Count == 0 ? -1 : _renderer.CreateMesh([.. all]);
    }

    private (Material, Texture?)? Surface(int value, Model model, Palette palette, TextureArchive archive, int paletteId, Dictionary<Texture, int> textures)
    {
        if (value >= 0 && value < model.Materials.Count && archive.Textures.TryGetValue(model.Materials[value], out var texture))
        {
            if (!textures.TryGetValue(texture, out var id))
            {
                id = textures[texture] = _renderer.CreateIndexTexture(texture.Width, texture.Height * texture.FrameCount, texture.Indices);
            }

            return (new Material(id, paletteId, Vector4.One, texture.FrameCount, Pass.DoubleSided), texture);
        }

        var index = value < 0 ? -value
            : value < model.Materials.Count && archive.Colors.TryGetValue(model.Materials[value], out var colour) ? colour : -1;
        if (index is < 0 or >= Palette.Size)
        {
            return null;
        }

        var rgba = palette.Rgba;
        var flat = new Vector4(rgba[index * 4], rgba[index * 4 + 1], rgba[index * 4 + 2], byte.MaxValue) / byte.MaxValue;
        return (Material.Flat(flat, Pass.DoubleSided), null);
    }
}
