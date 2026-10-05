using Mdk.Formats;

namespace Mdk.Game.Level;

/// <summary>The files of one level: settings and sky (DTI), arenas (MTO), shared textures (MTI) and
/// the corridors between arenas (in <c>LEVELnO.SNI</c>).</summary>
public sealed class LevelData
{
    /// <summary>DTI record type of a link to another arena: arenas without one start hidden.</summary>
    public const uint Connection = 6;

    public int Number { get; }
    public Dti Dti { get; }
    public Mto Mto { get; }
    public TextureArchive LevelTextures { get; }
    /// <summary>Arenas then corridors (a corridor <c>CHMO_1</c> follows arena <c>HMO_1</c>).</summary>
    public List<Arena> Arenas { get; } = [];

    public LevelData(MdkData data, int number)
    {
        Number = number;
        var dir = $"TRAVERSE/LEVEL{number}/LEVEL{number}";
        Dti = Dti.Load(data.PathOf(dir + ".DTI"));
        Mto = Mto.Load(data.PathOf(dir + "O.MTO"));
        LevelTextures = TextureArchive.Load(data.PathOf(dir + "S.MTI"));

        foreach (var name in Mto.ArenaNames)
        {
            Arenas.Add(Mto.GetArena(name));
        }

        var overlays = Sni.Load(data.PathOf(dir + "O.SNI"));
        foreach (var (name, entry) in overlays.Entries)
        {
            if (overlays.IsSound(entry))
            {
                continue;
            }

            // Empty corridors are a placeholder quad of the NONE material.
            var corridor = Arena.ParseWorld(name, overlays.Bytes, entry.Offset);
            if (corridor.Materials is ["NONE"])
            {
                continue;
            }

            Arenas.Add(corridor);
        }
    }

    /// <summary>The palette an arena draws with: the level's, with the arena's colours (corridors
    /// borrow their arena's: <c>CHMO_1</c> from <c>HMO_1</c>).</summary>
    public Palette PaletteOf(Arena arena)
    {
        var source = arena;
        if (source.PaletteRgb.Length == 0)
        {
            var owner = arena.Name[1..];
            source = Mto.Has(owner) ? Mto.GetArena(owner) : Arenas[0];
        }

        return Dti.Palette.WithArenaColors(source.PaletteRgb);
    }

    /// <summary>Texture archives searched for an arena's materials: its own, the level's, then every
    /// other arena's (some borrow, e.g. <c>O3_*</c> in level 6).</summary>
    public List<TextureArchive> ArchivesOf(Arena arena)
    {
        var archives = new List<TextureArchive> { arena.Textures, LevelTextures };
        archives.AddRange(Arenas.Select(a => a.Textures));
        return archives;
    }

    /// <summary>Whether an arena can be entered from the start: the starting one, or one with a connection.</summary>
    public bool IsReachable(string arenaName)
    {
        var index = Dti.Arenas.FindIndex(a => a.Name == arenaName);
        if (index < 0 || index == Dti.StartArena)
        {
            return true;
        }

        return Dti.Arenas[index].Records.Any(r => r.Type == Connection);
    }
}
