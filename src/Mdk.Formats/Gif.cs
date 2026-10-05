namespace Mdk.Formats;

/// <summary>A still GIF (<c>MISC/MDKS_00n.GIF</c>, the menu's slideshow): the first image's palette
/// indices and its palette (256 RGB).
/// <code>
///   header ─► global palette ─► extensions (skipped) ─► image descriptor ─► LZW data
/// </code></summary>
public sealed class Gif
{
    private const int HeaderSize = 13;
    private const byte Image = 0x2C;
    private const byte Extension = 0x21;
    /// <summary>Packed fields: a colour table follows (2 &lt;&lt; low 3 bits entries); interlaced rows.</summary>
    private const int HasTable = 0x80;
    private const int TableSize = 0x07;
    private const int Interlaced = 0x40;
    private const int MaxCodeSize = 12;
    private const int PaletteBytes = 768;
    /// <summary>Interlaced rows come in four passes: start row and step.</summary>
    private static readonly (int Start, int Step)[] Passes = [(0, 8), (4, 8), (2, 4), (1, 2)];

    private readonly byte[] _bytes;

    public int Width { get; }
    public int Height { get; }
    public byte[] Indices { get; private set; } = [];
    public byte[] Palette { get; } = new byte[PaletteBytes];

    private Gif(byte[] bytes)
    {
        _bytes = bytes;
        Width = Bin.U16(bytes, 6);
        Height = Bin.U16(bytes, 8);
    }

    public static Gif? Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < HeaderSize || Bin.Ascii(bytes, 0, 3) != "GIF")
        {
            return null;
        }

        var gif = new Gif(bytes);
        return gif.Parse() ? gif : null;
    }

    private bool Parse()
    {
        var p = HeaderSize;
        int fields = _bytes[10];
        if ((fields & HasTable) != 0)
        {
            p = ReadPalette(p, fields);
        }

        while (p < _bytes.Length)
        {
            var block = _bytes[p++];
            if (block == Extension)
            {
                p = SkipSubBlocks(p + 1);
                continue;
            }

            return block == Image && ReadImage(p);
        }

        return false;
    }

    private int ReadPalette(int p, int fields)
    {
        var count = 2 << (fields & TableSize);
        _bytes.AsSpan(p, count * 3).CopyTo(Palette);
        return p + count * 3;
    }

    private int SkipSubBlocks(int p)
    {
        while (p < _bytes.Length && _bytes[p] != 0)
        {
            p += _bytes[p] + 1;
        }

        return p + 1;
    }

    private bool ReadImage(int p)
    {
        int left = Bin.U16(_bytes, p);
        int top = Bin.U16(_bytes, p + 2);
        int imageWidth = Bin.U16(_bytes, p + 4);
        int imageHeight = Bin.U16(_bytes, p + 6);
        int fields = _bytes[p + 8];
        p += 9;
        if ((fields & HasTable) != 0)
        {
            p = ReadPalette(p, fields);
        }

        int codeSize = _bytes[p++];
        var data = new List<byte>();
        while (p < _bytes.Length && _bytes[p] != 0)
        {
            data.AddRange(_bytes.AsSpan(p + 1, _bytes[p]).ToArray());
            p += _bytes[p] + 1;
        }

        var pixels = Lzw([.. data], codeSize, imageWidth * imageHeight);
        Indices = new byte[Width * Height];
        var rows = RowOrder(imageHeight, (fields & Interlaced) != 0 ? Interlace.On : Interlace.Off);
        for (var i = 0; i < imageHeight; i++)
        {
            var y = rows[i] + top;
            if (y >= Height)
            {
                continue;
            }

            var count = Math.Min(imageWidth, Width - left);
            pixels.AsSpan(i * imageWidth, count).CopyTo(Indices.AsSpan(y * Width + left));
        }

        return true;
    }

    private enum Interlace { Off, On }

    /// <summary>The image's rows in the order they're stored.</summary>
    private static List<int> RowOrder(int rows, Interlace interlace)
    {
        if (interlace == Interlace.Off)
        {
            return Enumerable.Range(0, rows).ToList();
        }

        var order = new List<int>();
        foreach (var (start, step) in Passes)
        {
            for (var y = start; y < rows; y += step)
            {
                order.Add(y);
            }
        }

        return order;
    }

    /// <summary>Variable-width LZW: codes grow from min + 1 bits up to 12; a clear code starts over.</summary>
    private static byte[] Lzw(byte[] data, int minSize, int count)
    {
        var output = new byte[count];
        var clear = 1 << minSize;
        var end = clear + 1;
        const int Codes = 1 << MaxCodeSize;

        // Each code is a prefix code and its last byte; strings are rebuilt backwards.
        var prefixes = new int[Codes];
        var suffixes = new byte[Codes];
        var firsts = new byte[Codes];
        for (var i = 0; i < clear; i++)
        {
            prefixes[i] = -1;
            suffixes[i] = (byte)i;
            firsts[i] = (byte)i;
        }

        var stack = new byte[Codes];
        var size = minSize + 1;
        var next = end + 1;
        var previous = -1;
        var bits = 0;
        var bitCount = 0;
        var written = 0;
        var p = 0;
        while (written < count)
        {
            while (bitCount < size && p < data.Length)
            {
                bits |= data[p++] << bitCount;
                bitCount += 8;
            }

            if (bitCount < size)
            {
                break;
            }

            var code = bits & ((1 << size) - 1);
            bits >>= size;
            bitCount -= size;
            if (code == clear)
            {
                size = minSize + 1;
                next = end + 1;
                previous = -1;
                continue;
            }

            if (code == end)
            {
                break;
            }

            // A code not defined yet is the previous string plus its own first byte.
            var current = code;
            var depth = 0;
            if (code >= next && previous >= 0)
            {
                stack[depth++] = firsts[previous];
                current = previous;
            }

            while (current >= 0)
            {
                stack[depth++] = suffixes[current];
                current = prefixes[current];
            }

            for (var k = depth - 1; k >= 0 && written < count; k--)
            {
                output[written++] = stack[k];
            }

            if (previous >= 0 && next < Codes)
            {
                prefixes[next] = previous;
                suffixes[next] = stack[depth - 1];
                firsts[next] = firsts[previous];
                next++;
                if (next == 1 << size && size < MaxCodeSize)
                {
                    size++;
                }
            }

            previous = code;
        }

        return output;
    }
}
