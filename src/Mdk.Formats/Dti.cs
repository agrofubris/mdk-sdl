using System.Numerics;

namespace Mdk.Formats;

/// <summary><c>LEVELn.DTI</c>: level settings, arena list, base palette and sky. Block offsets are
/// relative to the file without its first 4 bytes.
/// <code>
/// block 0  settings: start, sky fields, GLASS1-4 colours
/// block 2  arenas and corridors with their object records
/// block 3  u32 arena colour count, 256-colour palette
/// block 4  sky panorama(s)
/// </code></summary>
public sealed class Dti
{
    public const int GlassCount = 4;
    private const int BlockTable = 4 + 0x10;
    /// <summary>Panorama rows hold 4 extra pixels for wrapping.</summary>
    private const int SkyWrapExtra = 4;

    public sealed record Record(uint Type, int Id, float Angle, Vector3 Position, Vector3 BoxEnd, string Name);

    /// <summary>An arena or corridor (<c>HMO_n</c>, <c>CHMO_n</c>); <c>Pitch</c> is the camera pitch in
    /// degrees (positive looks down).</summary>
    public sealed record ArenaEntry(string Name, float Pitch, List<Record> Records);

    private byte[] _bytes = [];

    public Palette Palette = new();
    /// <summary>Index in <see cref="Arenas"/> of the starting arena.</summary>
    public int StartArena;
    /// <summary>MDK coordinates; the angle in degrees (90 faces +Y).</summary>
    public Vector3 StartPosition;
    public float StartAngle;

    /// <summary>Sky panorama: the first <see cref="SkyWrapWidth"/> columns cover 360 degrees.</summary>
    public Texture Sky = new();
    public int SkyWrapWidth;
    public int SkyHorizonRow;
    public int SkyOffset;
    public int SkyTopColor;
    public int SkyBottomColor;
    /// <summary>The panorama mirrors show: the second one in levels 5 and 6, else the sky.</summary>
    public Texture MirrorSky = new();
    /// <summary>RGBA8 of <c>GLASS1</c>-<c>GLASS4</c>.</summary>
    public List<uint[]> Glass = [];
    public List<ArenaEntry> Arenas = [];

    public static Dti Load(string path)
    {
        var dti = new Dti { _bytes = File.ReadAllBytes(path) };
        dti.Parse();
        return dti;
    }

    private int Block(int index) => 4 + (int)Bin.U32(_bytes, BlockTable + index * 4);

    private void Parse()
    {
        var paletteOffset = Block(3) + 4;
        Palette = Palette.FromRgb(_bytes.AsSpan(paletteOffset, Palette.Size * 3));

        var r0 = new BinReader(_bytes, Block(0));
        StartArena = (int)r0.U32();
        StartPosition = r0.Vec3();
        StartAngle = r0.F32();
        SkyTopColor = (int)r0.U32();
        SkyBottomColor = (int)r0.U32();
        SkyHorizonRow = (int)r0.U32();
        SkyOffset = (int)r0.U32();
        SkyWrapWidth = (int)r0.U32();
        var skyHeight = (int)r0.U32();
        // Positive when the level has a second panorama, for the mirrors.
        var secondSkyTop = r0.S32();
        r0.S32();
        for (var i = 0; i < GlassCount; i++)
        {
            Glass.Add([r0.U32(), r0.U32(), r0.U32(), r0.U32()]);
        }

        Sky = Panorama(Block(4), skyHeight);
        MirrorSky = secondSkyTop > 0 ? Panorama(Block(4) + Sky.Width * Sky.Height, skyHeight) : Sky;

        var r = new BinReader(_bytes, Block(2));
        var count = r.U32();
        for (var i = 0; i < count; i++)
        {
            var name = r.Name(8);
            var recordsOffset = 4 + (int)r.U32();
            var pitch = r.F32();
            Arenas.Add(new ArenaEntry(name, pitch, ParseRecords(recordsOffset)));
        }
    }

    private Texture Panorama(int offset, int height)
    {
        var width = SkyWrapWidth + SkyWrapExtra;
        return new Texture { Name = "SKY", Width = width, Height = height, Indices = _bytes.AsSpan(offset, width * height).ToArray() };
    }

    private List<Record> ParseRecords(int offset)
    {
        var records = new List<Record>();
        var r = new BinReader(_bytes, offset);
        var count = r.U32();
        for (var i = 0; i < count; i++)
        {
            var type = r.U32();
            var id = r.S32();
            var angle = r.F32();
            var position = r.Vec3();
            // Boxes (fans, wind zones) keep their far corner in the name's bytes.
            var boxEnd = r.Vec3();
            r.Skip(-12);
            records.Add(new Record(type, id, angle, position, boxEnd, r.Name(12)));
        }

        return records;
    }
}
