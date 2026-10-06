namespace Mdk.Formats;

/// <summary>The fall's own entries of <c>FALL3D.BNI</c> (godot-mdk docs/formats.md, "Fall files").
/// <code>
/// ZOOMnnnn  u32 size, then 180 records (one per pair of screen rows):
///           u32 nL, u8 left[4 nL], u32 nM, u32 nR, u8 right[4 nR]    (nL + nM + nR = 150)
/// FALLPU_n  char[12] pickup model names, ended by a name starting with 0
/// </code></summary>
public static class Fall3d
{
    public const int HazeWidth = 600;
    /// <summary>One record covers two screen rows of the 360-high view.</summary>
    public const int HazeRows = 180;
    /// <summary>Pixels per group of the records.</summary>
    private const int HazeGroup = 4;
    private const int PickupNameLength = 12;

    /// <summary>A haze frame as one byte per pixel (0 plain, 1-8 the blend table offset), 600 x 180.</summary>
    public static byte[] Haze(Bni bni, string name)
    {
        var pixels = new byte[HazeWidth * HazeRows];
        var r = new BinReader(bni.Bytes, bni.Entries[name].Offset + sizeof(uint));
        for (var row = 0; row < HazeRows; row++)
        {
            var left = (int)r.U32() * HazeGroup;
            r.Buffer(left).CopyTo(pixels, row * HazeWidth);
            var middle = (int)r.U32() * HazeGroup;
            var right = (int)r.U32() * HazeGroup;
            r.Buffer(right).CopyTo(pixels, row * HazeWidth + left + middle);
        }

        return pixels;
    }

    /// <summary>The pickups dropped in the fall of <c>FALLPU_n</c> (none if there is no entry).</summary>
    public static List<string> Pickups(Bni bni, string name)
    {
        var names = new List<string>();
        if (!bni.Has(name))
        {
            return names;
        }

        var entry = bni.Entries[name];
        for (var offset = entry.Offset; offset + PickupNameLength <= entry.Offset + entry.Size && bni.Bytes[offset] != 0; offset += PickupNameLength)
        {
            names.Add(Bin.Ascii(bni.Bytes, offset, PickupNameLength));
        }

        return names;
    }
}
