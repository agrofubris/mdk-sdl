using System.Numerics;
using Mdk.Formats.Scripts;

namespace Mdk.Formats.Tests;

/// <summary>The 1996 beta demo's formats (godot-mdk docs/beta96.md, <c>mdk_beta.gd</c>) on synthetic
/// files, and the demo's levels themselves when it's installed (like the Godot port's
/// <c>beta96_test.gd</c>).</summary>
public sealed class BetaDemoTests : IDisposable
{
    private const string BetaVariable = "MDK_BETA_DIR";
    private const uint NotDrawn = BetaDemo.NotDrawn;

    private readonly string _dir = Directory.CreateTempSubdirectory("mdk-beta").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void LevelNumbersAreAboveTheRetailOnes()
    {
        Assert.Equal(961, BetaDemo.NumberOf(1));
        Assert.Equal(6, BetaDemo.LevelOf(966));
        Assert.True(BetaDemo.IsBeta(963));
        Assert.False(BetaDemo.IsBeta(8));
    }

    [Fact]
    public void TheDemoIsFoundByItsVariable()
    {
        var marker = Path.Combine(_dir, "TRAVERSE", "LEVEL1", "LEVEL1.SET");
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        File.WriteAllText(marker, "");
        var before = Environment.GetEnvironmentVariable(BetaVariable);
        try
        {
            Environment.SetEnvironmentVariable(BetaVariable, _dir);
            Assert.Equal(_dir, BetaDemo.Find(null)?.Dir);
        }
        finally
        {
            Environment.SetEnvironmentVariable(BetaVariable, before);
        }
    }

    [Fact]
    public void SettingsGiveTheStartSkyAndGlass()
    {
        const string Set = "2 10.5 -20 30 180 ; start\n\n200 201 120\n1 2 3 40\n5 6 7 80\n9 10 11 120\n13 14 15 160\n";
        var dti = BetaDemo.ParseSettings(Set);
        Assert.Equal(2, dti.StartArena);
        Assert.Equal(new Vector3(10.5f, -20f, 30f), dti.StartPosition);
        Assert.Equal(180f, dti.StartAngle);
        Assert.Equal((200, 201, 120), (dti.SkyTopColor, dti.SkyBottomColor, dti.SkyHorizonRow));
        Assert.Equal(Dti.GlassCount, dti.Glass.Count);
        Assert.Equal([13u, 14u, 15u, 160u], dti.Glass[3]);
    }

    [Fact]
    public void LevelThreeHasNoStartAngle() =>
        Assert.Equal(90f, BetaDemo.ParseSettings("0 1 2 3\n1 2 3\n0 0 0 0\n0 0 0 0\n0 0 0 0\n0 0 0 0\n").StartAngle);

    [Fact]
    public void ArenaNamesSkipThePortals()
    {
        const string Con = "3\narena_1 2\nC1 0 5 1 0 1\nC2 1 5 1 0 1\ncorr_1\t0\r\nhmo_1 1\nC0 0 0 0 0 0\n";
        Assert.Equal(["ARENA_1", "CORR_1", "HMO_1"], BetaDemo.ParseArenaNames(Con));
    }

    [Fact]
    public void HotLinesBecomeDtiRecords()
    {
        const string Hot = """
            ASHOW corr_1 1 2 3 4
            ASHOW NONE 0 0 0 0
            ALIEN xg 3 7 10 20 30 ; a grunt
            PICKUP sw_inter 1 2 3
            HIDEPT 4 5 6 7
            CONNECT 9 1 2 3 4 5 6
            MSWAP 1 2 3 4 5 6
            UNKNOWN 1
            """;
        var records = BetaDemo.ParseRecords(Hot, ["ARENA_1", "CORR_1"]);
        Assert.Equal(7, records.Count);
        Assert.Equal((1u, 1, "CORR_1"), (records[0].Type, records[0].Id, records[0].Name));
        Assert.Equal(new Vector3(3f, 4f, 0f), records[0].BoxEnd);
        Assert.Equal(-1, records[1].Id);
        Assert.Equal((2u, 3, "XG", new Vector3(10f, 20f, 30f)), (records[2].Type, records[2].Id, records[2].Name, records[2].Position));
        Assert.Equal((4u, "SW_INTER"), (records[3].Type, records[3].Name));
        Assert.Equal((5u, 4, new Vector3(5f, 6f, 7f)), (records[4].Type, records[4].Id, records[4].Position));
        Assert.Equal((6u, 9), (records[5].Type, records[5].Id));
        Assert.Equal(new Vector3(1f, 3f, 5f), records[5].Position);
        Assert.Equal(new Vector3(2f, 4f, 6f), records[5].BoxEnd);
        Assert.Equal(103u, records[6].Type);
    }

    [Fact]
    public void TeleportsAreLinesOfArenaAndPosition()
    {
        var teleports = BetaDemo.ParseTeleports("arena_4 1 2 -29\nbad line\nolym_1 0 0 5 ; tel0\n");
        Assert.Equal([("ARENA_4", new Vector3(1f, 2f, -29f)), ("OLYM_1", new Vector3(0f, 0f, 5f))], teleports);
    }

    /// <summary>A square floor at z 0: one node (z = 0) holding two up-facing triangles.</summary>
    internal static byte[] FloorWorld(float z = 0f)
    {
        var b = new Bytes().U32(1).Name("wall", 16);
        b.U32(1).F32(0f, 0f, 1f, 0f).S16(-1, -1, 2, 0, 0, 0).S16(0, 0, 0, 0);
        b.U32(2);
        b.S16(0, 1, 2, 0).F32(0, 0, 1, 0, 1, 1).U32(NotDrawn);
        b.S16(0, 2, 3, 0).F32(0, 0, 1, 1, 0, 1).U32(0);
        b.U32(4).Vec(new(-100f, -100f, z)).Vec(new(100f, -100f, 0f)).Vec(new(100f, 100f, 0f)).Vec(new(-100f, 100f, 0f));
        return b.ToArray();
    }

    [Fact]
    public void WorldsHaveLongNamesAndShortNodes()
    {
        var arena = Arena.ParseBetaWorld("HMO_1", FloorWorld());
        Assert.Equal(["WALL"], arena.Materials);
        var node = Assert.Single(arena.Nodes);
        Assert.Equal((Vector3.UnitZ, -1, -1, 0, 2, 0), (node.Normal, node.Negative, node.Positive, node.FrontFirst, node.FrontCount, node.BackCount));
        Assert.Equal(2, arena.TriangleCount);
        Assert.Equal([0, 2, 3], arena.TriangleIndices[3..]);
        Assert.Equal(NotDrawn, arena.TriangleFlags[0]);
        Assert.Equal(new Vector3(-100f, 100f, 0f), arena.Vertices[3]);
        Assert.Equal(0, BetaDemo.CheckNodes(arena));
    }

    [Fact]
    public void NodesWhoseTrianglesAreOffTheirPlaneAreReported() =>
        Assert.Equal(1, BetaDemo.CheckNodes(Arena.ParseBetaWorld("HMO_1", FloorWorld(z: 5f))));

    /// <summary>A texture archive without the retail header: a 2 x 2 texture and a palette colour.</summary>
    private static byte[] Archive(string texture)
    {
        const int Base = 4;
        var b = new Bytes().U32(0).U32(2);
        var offset = b.Position;
        b.Name(texture, 8).U32(0, 0).F32(0f).U32(0);
        b.Name("pen", 8).U32(0xFFFFFFFF, 5).F32(0f).U32(0);
        b.Patch(offset + 20, b.Position - Base);
        b.U16(2).U16(2).U8(0, 1, 0, 2);
        return b.ToArray();
    }

    [Fact]
    public void ArchivesHaveNoHeaderAndLowerCaseNames()
    {
        var archive = TextureArchive.Parse(Archive("brick"), sizeof(uint), 0, TextureArchive.Names.Upper);
        Assert.Equal(5, archive.Colors["PEN"]);
        Assert.Equal([0, 1, 0, 2], archive.Textures["BRICK"].Indices);
    }

    [Theory]
    [InlineData("brick", new byte[] { 16, 1, 16, 2 })]
    [InlineData("bolt", new byte[] { 0, 1, 0, 2 })]
    public void IndexZeroIsBlackButInProjectiles(string name, byte[] expected)
    {
        var archive = TextureArchive.Parse(Archive(name), sizeof(uint), 0, TextureArchive.Names.Upper);
        BetaDemo.MakeOpaque(archive);
        Assert.Equal(expected, archive.Textures[name.ToUpperInvariant()].Indices);
    }

    [Fact]
    public void SoundArchivesHaveTheirCountAfterTheSize()
    {
        var b = new Bytes().U32(0).U32(1).Name("shot", 12).U16(1).U16(100);
        var offset = b.Position;
        b.U32(0).U32(4);
        b.Patch(offset, b.Position - sizeof(uint));
        var sni = Sni.ParseBeta(b.Raw("RIFF"u8.ToArray()).ToArray());
        var (name, entry) = Assert.Single(sni.Entries);
        Assert.Equal(("SHOT", 1, 100), (name, entry.Flags, entry.Volume));
        Assert.True(sni.IsSound(entry));
    }

    [Fact]
    public void AnimationsCarryADeltaForEveryFrame()
    {
        // 1 track, 2 frames: offsets, root motion, boxes, no reference points, then the track.
        const int TrackOffset = 8 + 4 + 2 * 12 + 2 * 24 + 4;
        var b = new Bytes().U32(1, 2, TrackOffset).Vec(Vector3.Zero).Vec(Vector3.UnitX).Raw(new byte[2 * 24]).U32(0);
        b.Name("body", 12).U32(1).F32(0.5f).Vec(new(1f, 2f, 3f)).U8(2, 0xFE, 4);
        var animation = ModelAnimation.ParseBeta("@0", b.ToArray(), 0);
        Assert.Equal(2, animation.FrameCount);
        Assert.Equal(Vector3.UnitX, animation.RootMotion[1]);
        var model = new Model { PartList = [new Model.Part { Name = "BODY", Vertices = [Vector3.Zero] }] };
        var frames = animation.Bake(model);
        Assert.Equal(new Vector3(1f, 2f, 3f), frames[0][0][0]);
        Assert.Equal(new Vector3(2f, 1f, 5f), frames[1][0][0]);
    }

    [Fact]
    public void SpritesAddedOverTheRetailOnesReplaceThem()
    {
        var b = new Bytes().U32(0, 1).Name("SC_STAT", 12);
        var offset = b.Position;
        b.U32(0);
        b.Patch(offset, b.Position - sizeof(uint));
        b.U16(1).U16(1).U8(7);
        var index = ArchiveIndex.Read(b.ToArray(), 12);
        index.Add("SC_STAT", [1, 0, 1, 0, 9]);
        Assert.Equal([1, 0, 1, 0, 9], index.GetBytes("SC_STAT"));
    }

    [Fact]
    public void SkiesAreRemappedToTheNearestColours()
    {
        var from = new byte[Palette.Size * 3];
        var to = new byte[Palette.Size * 3];
        from[1 * 3] = 250;
        to[7 * 3] = 255;
        to[8 * 3 + 1] = 255;
        Assert.Equal([0, 7], BetaDemo.Remap([0, 1], Palette.FromRgb(from), Palette.FromRgb(to)));
    }

    /// <summary>A CMI file: one alien script following a path, one arena with its music and loop.</summary>
    internal static (byte[] Bytes, int Script, int Path) Cmi()
    {
        const int FileBase = 4;
        var b = new Bytes().U32(0);
        b.U32(1).Pascal("arena_1$xg_0");
        var scriptField = b.Position;
        b.U32(0).U32(0).U32(0).U32(1).Pascal("arena_1");
        var arenaField = b.Position;
        b.U32(0);
        b.Patch(arenaField, b.Position - FileBase).Pascal("song_active").Pascal("amb1");
        var script = b.Position;
        b.Patch(scriptField, script - FileBase).U8(2);
        var pathField = b.Position;
        b.U32(0).U8(ScriptDecoder.End);
        var path = b.Position;
        b.Patch(pathField, path - FileBase).U32(3).Vec(new(1f, 2f, 3f)).Vec(Vector3.UnitX).Vec(Vector3.UnitY);
        return (b.ToArray(), script, path);
    }

    [Fact]
    public void CmiArenasHaveMusicAndAmbienceButNoScript()
    {
        var cmi = Formats.Cmi.ParseBeta(Cmi().Bytes);
        Assert.Equal(ScriptDialect.Beta1996, cmi.Dialect);
        Assert.Equal("SONG_ACTIVE", cmi.ArenaMusic["ARENA_1"]);
        Assert.Equal("AMB1", cmi.ArenaAmbience["ARENA_1"]);
        Assert.Equal(0, cmi.ArenaScripts["ARENA_1"]);
        Assert.Equal(new[] { Cmi().Script }, cmi.EntryPoints());
    }

    [Fact]
    public void PathsBecomeSplinesThroughEveryFrame()
    {
        const int KeySize = 40;
        var (bytes, script, path) = Cmi();
        var cmi = Formats.Cmi.ParseBeta(bytes);
        var spline = Assert.Single(cmi.BetaPaths);
        Assert.Equal((path, bytes.Length), (spline.Key, spline.Value));

        // The positions, then back to the first.
        var s = spline.Value;
        Assert.Equal(4u, Bin.U32(cmi.Bytes, s));
        Vector3 Key(int key, int field) => new(Bin.F32(cmi.Bytes, s + 8 + key * KeySize + field * 12),
            Bin.F32(cmi.Bytes, s + 12 + key * KeySize + field * 12), Bin.F32(cmi.Bytes, s + 16 + key * KeySize + field * 12));
        Assert.Equal([new(1f, 2f, 3f), new(2f, 2f, 3f), new(2f, 3f, 3f), new(1f, 2f, 3f)], Enumerable.Range(0, 4).Select(k => Key(k, 0)));
        Assert.Equal(2, Bin.S32(cmi.Bytes, s + 4 + 2 * KeySize));
        Assert.Equal(Vector3.UnitX, Key(1, 1));
        Assert.Equal(Vector3.UnitY, Key(1, 2));

        var ins = ScriptDecoder.For(cmi).Decode(script)!;
        Assert.Equal((BetaOpcodes.FollowPath, s), (ins.Opcode, (int)ins.Operands[0]!));
    }

    /// <summary>Level: arenas and corridors, aliens placed, instructions, paths (beta96_test.gd).</summary>
    private static readonly (int Level, int Arenas, int Aliens, int Instructions, int Paths)[] Levels =
        [(1, 19, 35, 640, 22), (3, 2, 2, 143, 0), (6, 1, 10, 337, 0)];

    [BetaFact]
    public void TheDemoLevelsLoadAndTheirScriptsDecode()
    {
        foreach (var (level, arenas, aliens, instructions, paths) in Levels)
        {
            CheckLevel(level, arenas, aliens, instructions, paths);
        }
    }

    private static void CheckLevel(int level, int arenas, int aliens, int instructions, int paths)
    {
        var demo = BetaData.Demo!;
        var dti = demo.LoadDti(level);
        Assert.Equal(arenas, dti.Arenas.Count);
        Assert.Equal(aliens, dti.Arenas.Sum(a => a.Records.Count(r => r.Type == 2)));
        Assert.Equal(dti.Sky.Width * dti.Sky.Height, dti.Sky.Indices.Length);

        var mto = demo.LoadMto(level);
        foreach (var arena in mto.ArenaNames.Select(mto.GetArena))
        {
            Assert.NotEmpty(arena.Vertices);
            Assert.Equal(Arena.PaletteColors * 3, arena.PaletteRgb.Length);
            Assert.Equal(0, BetaDemo.CheckNodes(arena));
        }

        var cmi = demo.LoadCmi(level);
        Assert.Equal(paths, cmi.BetaPaths.Count);
        var decoder = ScriptDecoder.For(cmi);
        var seen = new HashSet<int>();
        var todo = new Stack<int>(cmi.EntryPoints());
        while (todo.Count != 0)
        {
            var pc = todo.Pop();
            while (pc > 0 && !seen.Contains(pc))
            {
                var ins = decoder.Decode(pc);
                Assert.True(ins != null, $"LEVEL{level}: unknown opcode {cmi.Bytes[pc]} at {pc:x}");
                seen.Add(pc);
                ScriptDecoder.Targets(ins).ForEach(todo.Push);
                if (ins.Opcode is 3 or 59)
                {
                    Assert.True(cmi.GetBetaAnimation((int)ins.Operands[0]!).FrameCount > 0);
                }

                if (ins.Opcode is ScriptDecoder.End or 9 or 12 or 253)
                {
                    break;
                }

                pc = ins.Next;
            }
        }

        Assert.Equal(instructions, seen.Count);
        Assert.All(cmi.ModelOffsets.Keys, name => Assert.NotNull(cmi.GetModel(name)));
    }
}
