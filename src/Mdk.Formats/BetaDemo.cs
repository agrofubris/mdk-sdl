using System.Globalization;
using System.Numerics;
using System.Text;

namespace Mdk.Formats;

/// <summary>The beta demo of 6 August 1996 (<c>MDKDEMO.EXE</c>, godot-mdk docs/beta96.md, its
/// <c>mdk_beta.gd</c>): <c>TRAVERSE/LEVEL1</c> (the city), <c>LEVEL3</c> (<c>HMO_1</c>) and
/// <c>LEVEL6</c> (<c>OLYM_1</c>), in loose files and earlier versions of the retail formats, read
/// into the retail loaders' objects (<see cref="Dti"/>, <see cref="Mto"/>, <see cref="Cmi"/>...), so
/// the game runs them like its own levels, numbered 961, 963 and 966.
/// <code>
///   LEVELn.SET, LEVELn.CON, ARENAS/*.HOT ──► Dti      ARENAS/*.BSP, LEVELnO.MTO ──► Mto
///   LEVELnS.MTI, *.LBA ──► TextureArchive            LEVELn.CMI ──► Cmi (Beta1996 scripts)
///   *.SNI ──► Sni                                    SPRITES/*.ABB, *.LBB ──► TRAVSPRT.BNI entries
/// </code></summary>
public sealed class BetaDemo
{
    /// <summary>The demo's <c>TRAVERSE/LEVELn</c> folders.</summary>
    public static readonly int[] Levels = [1, 3, 6];
    /// <summary>Added to a demo level's number to tell it from the retail levels.</summary>
    public const int NumberBase = 960;
    /// <summary>The folders the demo is usually unpacked to, looked for in and next to the game's
    /// folder and the program's.</summary>
    private static readonly string[] Folders = ["BETA96", "MDK (1996-08-06) (beta demo)"];
    private const string Marker = "TRAVERSE/LEVEL1/LEVEL1.SET";
    private const string EnvironmentVariable = "MDK_BETA_DIR";
    private const int SkyWidth = 1800;
    private const int SkyHeight = 360;
    private const int SkyWrapExtra = 4;
    private const int PaletteBytes = Palette.Size * 3;
    /// <summary>The camera pitch the retail levels usually give their arenas (degrees).</summary>
    private const float CameraPitch = 4f;
    /// <summary><c>LEVEL3.SET</c> has no start angle; 90 faces +Y, into the arena.</summary>
    private const float DefaultStartAngle = 90f;
    /// <summary>A triangle flag of the <c>.BSP</c> files: simple shapes around the detailed ones,
    /// solid but not drawn (0x15d90) and left out of the triangle tests of rays.</summary>
    public const uint NotDrawn = 2;
    /// <summary>Black in every palette of the demo (the first colour after the 16 system colours).</summary>
    private const byte OpaqueBlack = 16;
    /// <summary>Textures of the level archives whose index 0 is see-through (projectiles).</summary>
    private static readonly string[] SeeThrough = ["BULLET", "BOLT"];
    /// <summary><c>BACK_3.LBB</c> is a copy of the city's sky, in level 1's colours: drawn in the
    /// nearest colours of the level's own palette.</summary>
    private static readonly Dictionary<int, int> SkyPalettes = new() { [3] = 1 };
    /// <summary>The effect textures: loose animated textures (<c>u32 frames, u16 width, u16 height</c>).</summary>
    private static readonly string[] EffectTextures =
    [
        "TRAVERSE/LEVEL1/TEXTURES/TRAIL.LBA", "TRAVERSE/SPRITES/SLIME/SL_BIG.LBA", "TRAVERSE/SPRITES/SLIME/SL_MED.LBA",
        "TRAVERSE/SPRITES/SLIME/SL_SMA.LBA", "TRAVERSE/SPRITES/SLIME/SB_MED.LBA", "TRAVERSE/SPRITES/SLIME/SB_SMA.LBA",
    ];
    /// <summary>Kurt's sprites and the HUD's images the demo has its own versions of: file to
    /// <c>TRAVSPRT.BNI</c> entry (<c>*.ABB</c> RLE animations, <c>*.LBB</c> plain images).</summary>
    private static readonly string[] SpriteFiles =
    [
        "SPRITES/K_STILL.ABB", "SPRITES/K_IDLE.ABB", "SPRITES/K_RUN.ABB", "SPRITES/K_SIDE.ABB", "SPRITES/K_JUMP.ABB",
        "SPRITES/K_RJMP.ABB", "SPRITES/K_CHUTE.ABB", "SPRITES/K_SHOT.ABB", "SPRITES/K_RUNFIR.ABB", "SPRITES/K_HANG.ABB",
        "SPRITES/K_LOOKU.ABB", "SPRITES/CROSS.ABB", "SPRITES/K_ROLLL.ABB", "SPRITES/K_ROLLR.ABB", "SPRITES/K_HELM.ABB",
        "SPRITES/K_BCKUP.ABB", "SPRITES/K_LOOKD.ABB", "SPRITES/K_FIRE_M.ABB", "SPRITES/HUD/SC_STAT.LBB", "SPRITES/HUD/SC_BSTAT.LBB",
    ];

    /// <summary>Record types of the <c>.HOT</c> files, the retail DTI numbers.</summary>
    private const uint RecordShow = 1;
    private const uint RecordAlien = 2;
    private const uint RecordPickup = 4;
    private const uint RecordHidePoint = 5;
    private const uint RecordConnection = 6;
    /// <summary><c>MSWAP</c> (3 in the demo) isn't a retail type 3 record.</summary>
    private const uint RecordMaterialSwap = 103;

    /// <summary>The folder with <c>TRAVERSE</c> and <c>MDKDEMO.EXE</c>.</summary>
    public string Dir { get; }

    private BetaDemo(string dir) => Dir = dir;

    public static bool IsBeta(int number) => number > NumberBase;

    /// <summary>The demo's level (1, 3, 6) of a port's number (961, 963, 966).</summary>
    public static int LevelOf(int number) => number - NumberBase;

    public static int NumberOf(int level) => NumberBase + level;

    /// <summary>The demo, or null: <c>MDK_BETA_DIR</c>, <c>beta</c> in <see cref="LocalPaths"/>, or a
    /// <c>BETA96</c> or <c>MDK (1996-08-06) (beta demo)</c> folder in or next to the game's folder or
    /// the program's.</summary>
    public static BetaDemo? Find(MdkData? data)
    {
        var candidates = new List<string>();
        var env = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrEmpty(env))
        {
            candidates.Add(env);
        }

        var local = LocalPaths.Get(LocalPaths.Beta);
        if (local.Length != 0)
        {
            candidates.Add(local);
        }

        foreach (var root in new[] { data?.Dir, AppContext.BaseDirectory })
        {
            if (string.IsNullOrEmpty(root))
            {
                continue;
            }

            foreach (var folder in Folders)
            {
                candidates.Add(Path.Combine(root, folder));
                candidates.Add(Path.GetFullPath(Path.Combine(root, "..", folder)));
            }
        }

        var found = candidates.FirstOrDefault(c => File.Exists(CaseInsensitivePath.Resolve(c, Marker)));
        return found == null ? null : new BetaDemo(found);
    }

    private string PathOf(string relative) => CaseInsensitivePath.Resolve(Dir, relative);

    /// <summary>A file's bytes, or none when it's missing.</summary>
    private byte[] Read(string relative)
    {
        var path = PathOf(relative);
        return File.Exists(path) ? File.ReadAllBytes(path) : [];
    }

    private static string LevelDir(int level) => $"TRAVERSE/LEVEL{level}/";

    /// <summary>The level's loading screen (<c>DEMO/SCREENn.LBB</c>, in the demo's order; the retail
    /// <c>LOAD_n.LBB</c> layout), or none.</summary>
    public byte[] LoadingScreen(int level) => Read($"DEMO/SCREEN{Array.IndexOf(Levels, level) + 1}.LBB");

    // Level settings: LEVELn.SET, LEVELn.CON, the .HOT files, palettes and the sky.

    /// <summary>The level's settings as the retail <c>LEVELn.DTI</c> has them.</summary>
    public Dti LoadDti(int level)
    {
        var dir = LevelDir(level);
        var dti = ParseSettings(Text(dir + $"LEVEL{level}.SET"));
        dti.Palette = Palette.FromRgb(Read(dir + $"L{level}_PAL.LBP"));
        var sky = new byte[(SkyWidth + SkyWrapExtra) * SkyHeight];
        var file = Read($"TRAVERSE/SCREENS/BACK_{level}.LBB");
        file.AsSpan(0, Math.Min(file.Length, sky.Length)).CopyTo(sky);
        if (SkyPalettes.TryGetValue(level, out var skyLevel))
        {
            sky = Remap(sky, Palette.FromRgb(Read(LevelDir(skyLevel) + $"L{skyLevel}_PAL.LBP")), dti.Palette);
        }

        dti.SkyWrapWidth = SkyWidth;
        dti.Sky = new Texture { Name = "SKY", Width = SkyWidth + SkyWrapExtra, Height = SkyHeight, Indices = sky };
        dti.MirrorSky = dti.Sky;
        var names = ArenaNames(level);
        foreach (var name in names)
        {
            dti.Arenas.Add(new Dti.ArenaEntry(name, CameraPitch, ParseRecords(Text(dir + $"ARENAS/{name}.HOT"), names)));
        }

        return dti;
    }

    /// <summary><c>LEVELn.SET</c>: the start arena's index and Kurt's position (and angle), the sky's
    /// fill colours (top, bottom) and horizon row, then the four glass colours <c>r g b opacity</c>.</summary>
    internal static Dti ParseSettings(string text)
    {
        const int StartWords = 5;
        const int FirstGlassLine = 2;
        var lines = Lines(text);
        var start = lines[0];
        var dti = new Dti
        {
            StartArena = (int)Number(start[0]),
            StartPosition = new Vector3(Number(start[1]), Number(start[2]), Number(start[3])),
            StartAngle = start.Length >= StartWords ? Number(start[4]) : DefaultStartAngle,
            SkyTopColor = (int)Number(lines[1][0]),
            SkyBottomColor = (int)Number(lines[1][1]),
            SkyHorizonRow = (int)Number(lines[1][2]),
        };
        for (var i = 0; i < Dti.GlassCount; i++)
        {
            dti.Glass.Add(lines[FirstGlassLine + i].Take(4).Select(w => (uint)Number(w)).ToArray());
        }

        return dti;
    }

    /// <summary>The level's arenas and corridors in the order of <c>LEVELn.CON</c> (the start arena
    /// is an index in it), in upper case.</summary>
    private List<string> ArenaNames(int level) => ParseArenaNames(Text(LevelDir(level) + $"LEVEL{level}.CON"));

    /// <summary><c>LEVELn.CON</c>: the number of arenas, then per arena its name, a number of portals
    /// and the portals (<c>C&lt;arena index&gt; side z plane from to</c>, not used).</summary>
    internal static List<string> ParseArenaNames(string text)
    {
        var lines = Lines(text);
        var names = new List<string>();
        var line = 1;
        for (var i = 0; i < (int)Number(lines[0][0]) && line < lines.Count; i++)
        {
            names.Add(lines[line][0].ToUpperInvariant());
            line += 1 + (int)Number(lines[line][1]);
        }

        return names;
    }

    /// <summary>An arena's <c>.HOT</c> file as DTI records, one per line:
    /// <code>
    ///   ASHOW arena x1 y1 x2 y2          walking into the rectangle shows that arena (NONE: none)
    ///   ALIEN type id n x y z            an alien, run by the script ARENA$TYPE_id
    ///   PICKUP type x y z                a pickup
    ///   HIDEPT id x y z                  a cover spot
    ///   CONNECT id x1 x2 y1 y2 z1 z2     a doorway between an arena and a corridor, in pairs
    ///   MSWAP six numbers                a material swap (in no file)
    /// </code></summary>
    internal static List<Dti.Record> ParseRecords(string text, List<string> arenaNames)
    {
        var records = new List<Dti.Record>();
        foreach (var words in Lines(text))
        {
            var keyword = words[0].ToUpperInvariant();
            var named = keyword is not ("HIDEPT" or "CONNECT" or "MSWAP");
            var name = named && words.Length > 1 ? words[1].ToUpperInvariant() : "";
            var n = words.Skip(named ? 2 : 1).Select(Number).ToArray();
            Dti.Record? record = keyword switch
            {
                "ASHOW" => new(RecordShow, arenaNames.IndexOf(name), 0f, new Vector3(n[0], n[1], 0f), new Vector3(n[2], n[3], 0f), name),
                "ALIEN" => new(RecordAlien, (int)n[0], 0f, new Vector3(n[2], n[3], n[4]), Vector3.Zero, name),
                "PICKUP" => new(RecordPickup, -1, 0f, new Vector3(n[0], n[1], n[2]), Vector3.Zero, name),
                "HIDEPT" => new(RecordHidePoint, (int)n[0], 0f, new Vector3(n[1], n[2], n[3]), Vector3.Zero, ""),
                "CONNECT" => new(RecordConnection, (int)n[0], 0f, new Vector3(n[1], n[3], n[5]), new Vector3(n[2], n[4], n[6]), ""),
                "MSWAP" => new(RecordMaterialSwap, -1, 0f, Vector3.Zero, Vector3.Zero, ""),
                _ => null,
            };
            if (record != null)
            {
                records.Add(record);
            }
        }

        return records;
    }

    /// <summary>The demo's teleports, <c>TRAVERSE/TELEPORT.TXT</c>: a line <c>arena x y z</c> per digit
    /// (the first is 0; 0x440c4).</summary>
    public List<(string Arena, Vector3 Position)> LoadTeleports() => ParseTeleports(Text("TRAVERSE/TELEPORT.TXT"));

    internal static List<(string Arena, Vector3 Position)> ParseTeleports(string text)
    {
        const int Words = 4;
        return Lines(text).Where(w => w.Length >= Words)
            .Select(w => (w[0].ToUpperInvariant(), new Vector3(Number(w[1]), Number(w[2]), Number(w[3]))))
            .ToList();
    }

    // Arenas: LEVELnO.MTO (textures), ARENAS/name.BSP (geometry) and name.LBP (palette).

    /// <summary>The level's arenas. <c>LEVELnO.MTO</c> only has their textures: <c>u32 count</c>, per
    /// arena <c>char[8] name, u32 offset</c>; at the offset <c>u32 size</c>, then a texture archive
    /// without the retail header (offsets from its count).</summary>
    public Mto LoadMto(int level)
    {
        const int NameLength = 8;
        var dir = LevelDir(level);
        var bytes = Read(dir + $"LEVEL{level}O.MTO");
        var textures = new Dictionary<string, int>();
        var r = new BinReader(bytes);
        var count = bytes.Length == 0 ? 0 : r.U32();
        for (var i = 0; i < count; i++)
        {
            var name = r.Name(NameLength).ToUpperInvariant();
            textures[name] = (int)r.U32() + sizeof(uint);
        }

        var mto = new Mto();
        foreach (var name in ArenaNames(level).Where(n => !IsCorridor(n)))
        {
            if (LoadWorld(level, name) is not { } arena)
            {
                continue;
            }

            if (textures.TryGetValue(name, out var offset))
            {
                arena.Textures = TextureArchive.Parse(bytes, offset, 0, TextureArchive.Names.Upper);
                MakeOpaque(arena.Textures);
            }

            var palette = Read(dir + $"ARENAS/{name}.LBP");
            if (palette.Length >= PaletteBytes)
            {
                arena.PaletteRgb = palette.AsSpan(Palette.ArenaFirstIndex * 3, Arena.PaletteColors * 3).ToArray();
            }

            mto.Add(arena);
        }

        return mto;
    }

    /// <summary>Corridors (<c>CORR_n</c>; level 3's <c>CHMO_1</c> is a copy of the city's <c>CORR_1</c>)
    /// have no textures or palette of their own, like the retail ones.</summary>
    private static bool IsCorridor(string name) => name.StartsWith('C');

    public List<Arena> LoadCorridors(int level) =>
        ArenaNames(level).Where(IsCorridor).Select(n => LoadWorld(level, n)).OfType<Arena>().ToList();

    /// <summary>An arena's geometry, <c>ARENAS/name.BSP</c>, or null; its <see cref="NotDrawn"/>
    /// triangles only stop Kurt.</summary>
    private Arena? LoadWorld(int level, string name)
    {
        var bytes = Read(LevelDir(level) + $"ARENAS/{name}.BSP");
        if (bytes.Length == 0)
        {
            return null;
        }

        var arena = Arena.ParseBetaWorld(name, bytes);
        arena.ClipFlag = NotDrawn;
        return arena;
    }

    /// <summary>The nodes whose listed triangles don't lie on their plane: none when the demo's node
    /// layout is read right (it's taken as the retail one; not checked against the demo).</summary>
    public static int CheckNodes(Arena arena)
    {
        const float Tolerance = 0.1f;
        const int EmptyLeaf = -1;
        var bad = 0;
        foreach (var node in arena.Nodes)
        {
            var lists = new[] { (node.FrontFirst, node.FrontCount), (node.BackFirst, node.BackCount) };
            var off = lists.Any(l => l.Item2 > 0 && (l.Item1 < 0 || l.Item1 + l.Item2 > arena.TriangleCount
                || Enumerable.Range(l.Item1, l.Item2).Any(t => Enumerable.Range(0, 3)
                    .Any(k => MathF.Abs(node.Distance(arena.Vertices[arena.TriangleIndices[t * 3 + k]])) > Tolerance))));
            var children = new[] { node.Negative, node.Positive }.Any(c => c < EmptyLeaf || c >= arena.Nodes.Length);
            if (off || children)
            {
                bad++;
            }
        }

        return bad;
    }

    // Textures, sounds, sprites.

    /// <summary><c>LEVELnS.MTI</c> (<c>u32 size</c>, then an archive without the retail header) and the
    /// loose effect textures.</summary>
    public TextureArchive LoadTextures(int level)
    {
        var archive = TextureArchive.Parse(Read(LevelDir(level) + $"LEVEL{level}S.MTI"), sizeof(uint), 0, TextureArchive.Names.Upper);
        MakeOpaque(archive);
        foreach (var file in EffectTextures)
        {
            var bytes = Read(file);
            var name = Path.GetFileNameWithoutExtension(file);
            if (bytes.Length != 0 && !archive.Textures.ContainsKey(name))
            {
                archive.Textures[name] = Texture.ParseAnimated(name, bytes, 0, 0);
            }
        }

        return archive;
    }

    /// <summary>The demo's walls and models use palette index 0 as black, where the retail game keeps
    /// it for the see-through parts of effects: it becomes <see cref="OpaqueBlack"/>, except in
    /// animated textures and <see cref="SeeThrough"/>.</summary>
    internal static void MakeOpaque(TextureArchive archive)
    {
        foreach (var texture in archive.Textures.Values.Where(t => t.FrameCount == 1 && !SeeThrough.Contains(t.Name)))
        {
            texture.Indices.AsSpan().Replace((byte)0, OpaqueBlack);
        }
    }

    /// <summary>The sound archives, later ones winning: <c>TRAVERSE.SNI</c>, <c>LEVELnS.SNI</c>,
    /// <c>LEVELnO.SNI</c> (<c>u32 size, u32 count</c>, then the retail entries); missing ones are left out.</summary>
    public List<Sni> LoadSounds(int level)
    {
        var dir = LevelDir(level);
        return new[] { "TRAVERSE/TRAVERSE.SNI", dir + $"LEVEL{level}S.SNI", dir + $"LEVEL{level}O.SNI" }
            .Select(Read).Where(b => b.Length != 0).Select(Sni.ParseBeta).ToList();
    }

    /// <summary>Puts the demo's versions of Kurt's sprites and the HUD's images over the retail ones.</summary>
    public void AddSprites(Bni sprites)
    {
        foreach (var file in SpriteFiles)
        {
            var bytes = Read("TRAVERSE/" + file);
            if (bytes.Length != 0)
            {
                sprites.Add(Path.GetFileNameWithoutExtension(file), bytes);
            }
        }
    }

    /// <summary><c>LEVELn.CMI</c>, its paths converted (<see cref="Cmi.ParseBeta"/>).</summary>
    public Cmi LoadCmi(int level) => Cmi.ParseBeta(Read(LevelDir(level) + $"LEVEL{level}.CMI"));

    /// <summary>Palette indices drawn with <paramref name="from"/> as the nearest colours of <paramref name="to"/>.</summary>
    internal static byte[] Remap(byte[] indices, Palette from, Palette to)
    {
        var table = new byte[Palette.Size];
        for (var i = 0; i < Palette.Size; i++)
        {
            var best = 0;
            var bestDistance = int.MaxValue;
            for (var k = 0; k < Palette.Size; k++)
            {
                var distance = 0;
                for (var c = 0; c < 3; c++)
                {
                    var d = from.Rgba[i * 4 + c] - to.Rgba[k * 4 + c];
                    distance += d * d;
                }

                if (distance < bestDistance)
                {
                    best = k;
                    bestDistance = distance;
                }
            }

            table[i] = (byte)best;
        }

        return indices.Select(i => table[i]).ToArray();
    }

    private string Text(string relative) => Encoding.ASCII.GetString(Read(relative));

    /// <summary>The words of a text file's lines, without comments (<c>;</c>) and empty lines.</summary>
    private static List<string[]> Lines(string text) => text.Split('\n')
        .Select(l => l.Split(';')[0].Split([' ', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries))
        .Where(w => w.Length != 0)
        .ToList();

    private static float Number(string word) =>
        float.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0f;
}
