using Mdk.Formats;

namespace Mdk.Formats.Tests;

/// <summary>Parses the real game data (MDK must be installed above this folder or in MDK_DATA_DIR).</summary>
public class FormatTests
{
    /// <summary>The game's levels are numbered 3 to 8.</summary>
    private const int FirstLevel = 3;
    private const int LastLevel = 8;
    /// <summary>Level 7's starting arena.</summary>
    private const string FirstArena = "DANT_1";

    private static readonly MdkData Data = MdkData.Find() ?? throw new InvalidOperationException("MDK data not found");

    private static string LevelPath(int level, string suffix) => Data.PathOf($"TRAVERSE/LEVEL{level}/LEVEL{level}{suffix}");

    [DataFact]
    public void EveryLevelParses()
    {
        for (var level = FirstLevel; level <= LastLevel; level++)
        {
            var dti = Dti.Load(LevelPath(level, ".DTI"));
            var mto = Mto.Load(LevelPath(level, "O.MTO"));
            Assert.NotEmpty(dti.Arenas);
            foreach (var name in mto.ArenaNames)
            {
                var arena = mto.GetArena(name);
                Assert.NotEmpty(arena.Vertices);
                Assert.Equal(arena.TriangleCount * 3, arena.TriangleIndices.Length);
                Assert.All(arena.TriangleIndices, i => Assert.InRange(i, 0, arena.Vertices.Length - 1));
            }
        }
    }

    [DataFact]
    public void Level7StartsInDant1()
    {
        var dti = Dti.Load(LevelPath(7, ".DTI"));
        var mto = Mto.Load(LevelPath(7, "O.MTO"));
        Assert.Equal(FirstArena, dti.Arenas[dti.StartArena].Name);
        Assert.True(mto.Has(FirstArena));
        Assert.Equal(Arena.PaletteColors * 3, mto.GetArena(FirstArena).PaletteRgb.Length);
    }

    [DataFact]
    public void CorridorsParseFromSni()
    {
        var sni = Sni.Load(LevelPath(7, "O.SNI"));
        var corridors = sni.Entries.Where(e => !sni.IsSound(e.Value)).ToList();
        Assert.NotEmpty(corridors);
        foreach (var (name, entry) in corridors)
        {
            var corridor = Arena.ParseWorld(name, sni.Bytes, entry.Offset);
            Assert.NotEmpty(corridor.Materials);
        }
    }

    [DataFact]
    public void ModelAnimationsBake()
    {
        var mto = Mto.Load(LevelPath(7, "O.MTO"));
        var arena = mto.GetArena(FirstArena);
        Assert.NotEmpty(arena.Models);
        foreach (var animation in arena.Animations.Values)
        {
            var model = arena.Models.Values.FirstOrDefault(m => m.PartList.Count > 1);
            if (model == null)
            {
                return;
            }

            Assert.Equal(animation.FrameCount, animation.Bake(model).Length);
        }
    }

    [DataFact]
    public void KurtRunFramesDecode()
    {
        var bni = Bni.Load(Data.PathOf("TRAVERSE/TRAVSPRT.BNI"));
        var run = bni.GetAnimation("K_RUN");
        Assert.True(run.FrameCount > 1);
        for (var i = 0; i < run.FrameCount; i++)
        {
            var frame = run.GetFrame(i);
            Assert.Contains(frame.Image.Indices, index => index != 0);
        }
    }

    [DataFact]
    public void ScriptsAndSoundsParse()
    {
        for (var level = FirstLevel; level <= LastLevel; level++)
        {
            var cmi = Cmi.Load(LevelPath(level, ".CMI"));
            Assert.NotEmpty(cmi.ArenaScripts);
            Assert.NotEmpty(cmi.ArenaMusic);

            var sni = Sni.Load(LevelPath(level, "S.SNI"));
            foreach (var (name, entry) in sni.Entries.Where(e => sni.IsSound(e.Value)))
            {
                var wav = Wav.Parse(sni.GetBytes(entry));
                Assert.True(wav is { Data.Length: > 0 }, name);
            }
        }
    }

    [DataFact]
    public void FontsParse()
    {
        var fti = Fti.Load(Data.PathOf("MISC/MDKFONT.FTI"));
        var big = Font.Parse(fti.GetBytes("FONTBIG"), 6);
        Assert.True(big.Width("MDK"u8) > big.SpaceWidth * 3);
    }

    [DataFact]
    public void FontArchiveHasMenuTexts()
    {
        var fti = Fti.Load(Data.PathOf("MISC/MDKFONT.FTI"));
        Assert.True(fti.Has("FONTBIG"));
        Assert.False(string.IsNullOrEmpty(fti.GetText("OPT0")));
    }
}
