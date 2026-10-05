namespace Mdk.Formats;

/// <summary><c>LEVELnO.MTO</c>: the arenas of a level, parsed on demand.</summary>
public sealed class Mto
{
    private const int DirectoryOffset = 0x14;

    private readonly byte[] _bytes;
    private readonly Dictionary<string, Arena> _arenas = [];

    /// <summary>Arena name (<c>HMO_1</c>...) to the file offset of its block, in file order.</summary>
    public List<KeyValuePair<string, int>> ArenaOffsets { get; } = [];

    private Mto(byte[] bytes)
    {
        _bytes = bytes;
        var r = new BinReader(bytes, DirectoryOffset);
        var count = r.U32();
        for (var i = 0; i < count; i++)
        {
            var name = r.Name(8);
            ArenaOffsets.Add(new(name, (int)r.U32()));
        }
    }

    public static Mto Load(string path) => new(File.ReadAllBytes(path));

    public IEnumerable<string> ArenaNames => ArenaOffsets.Select(e => e.Key);

    public bool Has(string name) => ArenaOffsets.Any(e => e.Key == name);

    public Arena GetArena(string name)
    {
        if (_arenas.TryGetValue(name, out var arena))
        {
            return arena;
        }

        var offset = ArenaOffsets.First(e => e.Key == name).Value;
        return _arenas[name] = Arena.Parse(name, _bytes, offset);
    }
}
