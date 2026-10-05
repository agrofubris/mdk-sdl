namespace Mdk.Formats;

/// <summary><c>LEVELn.CMI</c>: level scripts and global models. After the header, 4 directories,
/// each <c>u32 count</c> then <c>u8 length, char name[length], u32 offset</c> (from 4, 0 = none):
/// <code>
/// 0  alien instance scripts   HMO_1$XG_0
/// 1  global models            (0 = look it up in the arena)
/// 2  object type scripts      HMO_1$XH1_DOOR
/// 3  arenas                   record: pascal NONE?, pascal music, u32 script
/// </code></summary>
public sealed class Cmi
{
    private const int DirectoryOffset = 0x14;
    private const int FileBase = 4;

    private readonly Dictionary<string, Model> _models = [];
    private readonly List<int> _entries = [];

    public byte[] Bytes { get; }
    /// <summary>Name to absolute file offset (0 = none), per directory.</summary>
    public Dictionary<string, int> AlienScripts { get; } = [];
    public Dictionary<string, int> ModelOffsets { get; } = [];
    public Dictionary<string, int> ObjectScripts { get; } = [];
    public Dictionary<string, int> ArenaScripts { get; } = [];
    /// <summary>Arena to its music (a sound of <c>LEVELnO.SNI</c>, or <c>NONE</c>).</summary>
    public Dictionary<string, string> ArenaMusic { get; } = [];

    private Cmi(byte[] bytes)
    {
        Bytes = bytes;
        var r = new BinReader(bytes, DirectoryOffset);
        foreach (var directory in new[] { AlienScripts, ModelOffsets, ObjectScripts, ArenaScripts })
        {
            var count = r.U32();
            for (var i = 0; i < count; i++)
            {
                var name = r.Name(r.U8());
                var offset = Absolute(r.U32());
                directory[name] = offset;
                if (offset != 0 && (directory == AlienScripts || directory == ObjectScripts))
                {
                    _entries.Add(offset);
                }
            }
        }

        foreach (var arena in ArenaScripts.Keys.ToList())
        {
            var record = new BinReader(bytes, ArenaScripts[arena]);
            record.Name(record.U8());
            ArenaMusic[arena] = record.Name(record.U8());
            ArenaScripts[arena] = Absolute(record.U32());
            if (ArenaScripts[arena] != 0)
            {
                _entries.Add(ArenaScripts[arena]);
            }
        }
    }

    private static int Absolute(uint offset) => offset == 0 ? 0 : FileBase + (int)offset;

    /// <summary>Where scripts start: every alien, object and arena script, in directory order (a
    /// name repeated in a directory counts once per entry).</summary>
    public IEnumerable<int> EntryPoints() => _entries;

    public static Cmi Load(string path) => new(File.ReadAllBytes(path));

    /// <summary>A global model, or null when it is an arena's (or unknown).</summary>
    public Model? GetModel(string name)
    {
        if (!ModelOffsets.TryGetValue(name, out var offset) || offset == 0)
        {
            return null;
        }

        if (!_models.TryGetValue(name, out var model))
        {
            model = _models[name] = Model.Parse(name, Bytes, offset);
        }

        return model;
    }
}
