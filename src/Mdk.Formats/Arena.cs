using System.Numerics;

namespace Mdk.Formats;

/// <summary>A BSP node: triangles lie on its plane (<c>n·p + d</c>), in a front list (facing the
/// positive side) and a back list; children -1 are empty leaves. See godot-mdk docs/bsp.md.</summary>
public readonly record struct BspNode(Vector3 Normal, float D, int Negative, int Positive,
    int FrontFirst, int FrontCount, int BackFirst, int BackCount)
{
    public float Distance(Vector3 p) => Vector3.Dot(Normal, p) + D;
}

/// <summary>One arena of a level (<c>HMO_n</c> in <c>LEVELnO.MTO</c>): textures, palette colours,
/// models and world geometry, or a corridor's world alone.</summary>
public sealed class Arena
{
    /// <summary>Colours an arena brings to the palette (indices 64-175).</summary>
    public const int PaletteColors = 112;
    private const int BspNodeSize = 44;
    private const int MaterialNameLength = 10;
    /// <summary>The 1996 demo's world section (<see cref="BetaDemo"/>): 16-character material names,
    /// 36-byte BSP nodes (<c>f32 plane[4]</c>, ten <c>s16</c>; the first six taken as the retail ones).</summary>
    private const int BetaBspNodeSize = 36;
    private const int BetaMaterialNameLength = 16;
    private const int Alignment = 4;

    public string Name = "";
    /// <summary>The arena's palette colours (RGB); empty for corridors.</summary>
    public byte[] PaletteRgb = [];
    public TextureArchive Textures = new();
    public Dictionary<string, Model> Models = [];
    public Dictionary<string, ModelAnimation> Animations = [];
    /// <summary>RIFF WAV bytes.</summary>
    public Dictionary<string, byte[]> Sounds = [];

    /// <summary>Texture names, or <c>PEN_n</c> for flat palette colours.</summary>
    public List<string> Materials = [];
    /// <summary>MDK coordinates (Z up).</summary>
    public Vector3[] Vertices = [];
    /// <summary>3 vertex indices per triangle.</summary>
    public int[] TriangleIndices = [];
    /// <summary>Per triangle: index into <see cref="Materials"/>, or <c>-n</c> for palette colour n.</summary>
    public int[] TriangleMaterials = [];
    /// <summary>3 UVs per triangle, in texels.</summary>
    public Vector2[] TriangleUvs = [];
    /// <summary>Per triangle: group in bits 24-31, outline bits 20-23.</summary>
    public uint[] TriangleFlags = [];
    /// <summary>Node 0 is the root; children have higher indices.</summary>
    public BspNode[] Nodes = [];
    /// <summary>The flag of triangles that only stop Kurt: not drawn, passed by the scripts' rays
    /// (the 1996 demo's 2); 0 for none.</summary>
    public uint ClipFlag;

    public int TriangleCount => TriangleMaterials.Length;

    /// <summary>An arena of the MTO; <paramref name="offset"/> points to its size field.</summary>
    public static Arena Parse(string name, byte[] bytes, int offset)
    {
        var arena = new Arena { Name = name };

        // The game loads the bytes after the size field; offsets are relative to that buffer.
        var buf = offset + 4;
        var models = buf + (int)Bin.U32(bytes, buf);
        var palette = buf + (int)Bin.U32(bytes, buf + 4);
        var world = buf + (int)Bin.U32(bytes, buf + 8);
        arena.PaletteRgb = bytes.AsSpan(palette, PaletteColors * 3).ToArray();
        arena.Textures = TextureArchive.Parse(bytes, buf + 0x10);
        arena.ParseWorld(bytes, world);
        arena.ParseModels(bytes, models + 4);
        return arena;
    }

    /// <summary>A world section alone (corridors in <c>LEVELnO.SNI</c>).</summary>
    public static Arena ParseWorld(string name, byte[] bytes, int offset)
    {
        var arena = new Arena { Name = name };
        arena.ParseWorld(bytes, offset);
        return arena;
    }

    /// <summary>A world section of the 1996 demo (<c>ARENAS/name.BSP</c>); its material names, in
    /// lower case, are made upper case.</summary>
    public static Arena ParseBetaWorld(string name, byte[] bytes)
    {
        var arena = new Arena { Name = name };
        arena.ParseWorld(bytes, 0, BetaMaterialNameLength, BetaBspNodeSize);
        arena.Materials = arena.Materials.Select(m => m.ToUpperInvariant()).ToList();
        return arena;
    }

    private void ParseModels(byte[] bytes, int baseOffset)
    {
        var r = new BinReader(bytes, baseOffset);
        var animationCount = r.U32();
        var modelCount = r.U32();
        var soundCount = r.U32();
        for (var i = 0; i < animationCount; i++)
        {
            var name = r.Name(8);
            Animations[name] = ModelAnimation.Parse(name, bytes, baseOffset + (int)r.U32());
        }

        for (var i = 0; i < modelCount; i++)
        {
            var name = r.Name(8);
            Models[name] = Model.Parse(name, bytes, baseOffset + (int)r.U32());
        }

        for (var i = 0; i < soundCount; i++)
        {
            var name = r.Name(12);
            r.Skip(4);
            var offset = baseOffset + (int)r.U32();
            Sounds[name] = bytes.AsSpan(offset, (int)r.U32()).ToArray();
        }
    }

    private void ParseWorld(byte[] bytes, int baseOffset, int nameLength = MaterialNameLength, int nodeSize = BspNodeSize)
    {
        var r = new BinReader(bytes, baseOffset);
        var materialCount = r.U32();
        for (var i = 0; i < materialCount; i++)
        {
            Materials.Add(r.Name(nameLength));
        }

        // Names of 10 bytes: an odd count pads to 4.
        if (materialCount * nameLength % Alignment != 0)
        {
            r.Skip(2);
        }

        Nodes = new BspNode[r.U32()];
        for (var i = 0; i < Nodes.Length; i++)
        {
            var next = r.Pos + nodeSize;
            var normal = r.Vec3();
            var d = r.F32();
            int negative = r.S16();
            int positive = r.S16();
            int frontCount = r.U16();
            int frontFirst = r.S16();
            int backCount = r.U16();
            int backFirst = r.S16();
            Nodes[i] = new BspNode(normal, d, negative, positive, frontFirst, frontCount, backFirst, backCount);

            // Then two bit set offsets and two run-time object lists.
            r.Pos = next;
        }

        var triangleCount = (int)r.U32();
        TriangleIndices = new int[triangleCount * 3];
        TriangleMaterials = new int[triangleCount];
        TriangleUvs = new Vector2[triangleCount * 3];
        TriangleFlags = new uint[triangleCount];
        for (var i = 0; i < triangleCount; i++)
        {
            TriangleIndices[i * 3] = r.S16();
            TriangleIndices[i * 3 + 1] = r.S16();
            TriangleIndices[i * 3 + 2] = r.S16();
            TriangleMaterials[i] = r.S16();
            for (var k = 0; k < 3; k++)
            {
                TriangleUvs[i * 3 + k] = new Vector2(r.F32(), r.F32());
            }

            TriangleFlags[i] = r.U32();
        }

        Vertices = new Vector3[r.U32()];
        for (var i = 0; i < Vertices.Length; i++)
        {
            Vertices[i] = r.Vec3();
        }
    }
}
