namespace Mdk.Formats;

/// <summary>An SNI archive (<c>TRAVERSE.SNI</c>, <c>LEVELnS.SNI</c>, <c>LEVELnO.SNI</c>).
/// <code>
/// header, u32 count, then per entry:
///   char[12] name, u16 flags, u16 volume, u32 offset (from 4), u32 length
/// </code>
/// Flag 1 loops, flag 2 marks music. Most entries are RIFF WAV; in <c>LEVELnO.SNI</c> the corridors'
/// (<c>CHMO_n</c>...) world geometry.</summary>
public sealed class Sni
{
    public const int FlagLoop = 1;
    private const int DirectoryOffset = 0x14;
    private static readonly byte[] Riff = "RIFF"u8.ToArray();

    public sealed record Entry(int Offset, int Length, int Flags, int Volume);

    public byte[] Bytes { get; }
    /// <summary>In file order.</summary>
    public List<KeyValuePair<string, Entry>> Entries { get; } = [];

    /// <summary>The 1996 demo's archives: <c>u32 size</c>, then the count; names in lower case.</summary>
    private const int BetaDirectoryOffset = 4;

    private Sni(byte[] bytes, int directory = DirectoryOffset)
    {
        Bytes = bytes;
        var r = new BinReader(bytes, directory);
        var count = r.U32();
        for (var i = 0; i < count; i++)
        {
            var name = directory == DirectoryOffset ? r.Name(12) : r.Name(12).ToUpperInvariant();
            int flags = r.U16();
            int volume = r.U16();
            var offset = 4 + (int)r.U32();
            Entries.Add(new(name, new Entry(offset, (int)r.U32(), flags, volume)));
        }
    }

    public static Sni Load(string path) => new(File.ReadAllBytes(path));

    /// <summary>An archive of the 1996 demo (<see cref="BetaDemo"/>).</summary>
    internal static Sni ParseBeta(byte[] bytes) => new(bytes, BetaDirectoryOffset);

    public bool IsSound(Entry entry) => Bytes.AsSpan(entry.Offset, Riff.Length).SequenceEqual(Riff);

    public byte[] GetBytes(Entry entry) => Bytes.AsSpan(entry.Offset, entry.Length).ToArray();

    /// <summary>A sprite animation stored in the archive (<c>LEVEL4S.SNI</c>'s <c>K_SURF</c>), or null.</summary>
    public SpriteAnimation? GetAnimation(string name)
    {
        var entry = Entries.FirstOrDefault(e => e.Key == name).Value;
        return entry == null || IsSound(entry) ? null : SpriteAnimation.Parse(name, Bytes, entry.Offset + 4);
    }
}
