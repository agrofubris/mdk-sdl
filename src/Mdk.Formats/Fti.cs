namespace Mdk.Formats;

/// <summary><c>MISC/MDKFONT.FTI</c>: interface texts, fonts, palette and sounds.
/// <code>
/// u32 size, u32 count, then per entry: char[8] name, u32 offset (from 4)
/// </code>
/// Texts (<c>OPT0</c>-<c>OPT4</c>, <c>OM_*</c>, <c>KM_*</c>) are NUL-terminated with <c>\n</c> escapes;
/// <c>SYS_PAL</c> the interface's 64 colours; <c>FONTBIG</c>, <c>FONTSML</c> fonts; <c>SND_PUSH</c> WAV.</summary>
public sealed class Fti
{
    private readonly ArchiveIndex _index;

    public byte[] Bytes => _index.Bytes;

    private Fti(byte[] bytes) => _index = ArchiveIndex.Read(bytes, 8);

    public static Fti Load(string path) => new(File.ReadAllBytes(path));

    public bool Has(string name) => _index.Entries.ContainsKey(name);

    public byte[] GetBytes(string name) => _index.GetBytes(name);

    /// <summary>A text entry as bytes (the fonts' character set, <c>\n</c> escapes kept), or empty.</summary>
    public byte[] GetTextBytes(string name)
    {
        if (!_index.Entries.TryGetValue(name, out var entry))
        {
            return [];
        }

        var span = Bytes.AsSpan(entry.Offset, entry.Size);
        var end = span.IndexOf((byte)0);
        return (end < 0 ? span : span[..end]).ToArray();
    }

    /// <summary>A text entry, or <paramref name="fallback"/>.</summary>
    public string GetText(string name, string fallback = "")
    {
        if (!_index.Entries.TryGetValue(name, out var entry))
        {
            return fallback;
        }

        return Bin.Ascii(Bytes, entry.Offset, entry.Size).Replace("\\n", "\n");
    }
}

/// <summary>The directory shared by FTI and BNI: <c>u32 size, u32 count</c>, then per entry a
/// fixed-length name and <c>u32 offset</c> (from 4). Sizes run to the next offset.</summary>
public sealed class ArchiveIndex
{
    public sealed record Entry(int Offset, int Size);

    public byte[] Bytes { get; private set; } = [];
    public Dictionary<string, Entry> Entries { get; } = [];

    public static ArchiveIndex Read(byte[] bytes, int nameLength)
    {
        var index = new ArchiveIndex { Bytes = bytes };
        var r = new BinReader(bytes, 4);
        var count = r.U32();
        var offsets = new List<(int Offset, string Name)>();
        for (var i = 0; i < count; i++)
        {
            var name = r.Name(nameLength);
            offsets.Add((4 + (int)r.U32(), name));
        }

        offsets.Sort();
        for (var i = 0; i < offsets.Count; i++)
        {
            var end = i + 1 < offsets.Count ? offsets[i + 1].Offset : bytes.Length;
            index.Entries[offsets[i].Name] = new Entry(offsets[i].Offset, end - offsets[i].Offset);
        }

        return index;
    }

    /// <summary>Adds or replaces an entry, its bytes after the others.</summary>
    internal void Add(string name, byte[] data)
    {
        Entries[name] = new Entry(Bytes.Length, data.Length);
        Bytes = [.. Bytes, .. data];
    }

    public byte[] GetBytes(string name)
    {
        var entry = Entries[name];
        return Bytes.AsSpan(entry.Offset, entry.Size).ToArray();
    }
}
