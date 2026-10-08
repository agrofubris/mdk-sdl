using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Game.DevTools;
using Mdk.Game.Mods;
using Mdk.Game.Flow;
using Mdk.Game.Kurt;

namespace Mdk.Game.Tests;

/// <summary>The console's commands: their arguments reach the game and the level (fakes here).</summary>
public class ConsoleCommandsTests
{
    private readonly FakeLevel _level = new();
    private readonly FakeGame _game;
    private readonly CommandRegistry _registry = new();

    public ConsoleCommandsTests()
    {
        _game = new FakeGame(_level);
        ConsoleCommands.Register(_registry, _game);
    }

    private string Run(string line) => string.Join('\n', _registry.Run(line));

    [Fact]
    public void PosPrintsCommandLineOptions()
    {
        var text = Run("pos");
        Assert.Contains("--level=3 --at=-4.00,0.50,195.00,90", text);
        Assert.Contains("--teleport=ARENA_1,-4.00,0.50,195.00", text);
        Assert.Contains("--console=\"tp ARENA_1 -4.00 0.50 195.00\"", text);
    }

    [Theory]
    [InlineData("tp ARENA_2", "ARENA_2", null)]
    [InlineData("teleport 1 2 3", "", "1 2 3")]
    [InlineData("tp 1,2,3", "", "1 2 3")]
    [InlineData("tp ARENA_2,1,2,3", "ARENA_2", "1 2 3")]
    public void TeleportTakesAnArenaOrAPoint(string line, string arena, string? point)
    {
        Run(line);
        Assert.Equal(arena, _level.Teleported?.Arena);
        Assert.Equal(point, _level.Teleported?.Point is { } p ? $"{p.X} {p.Y} {p.Z}" : null);
    }

    [Fact]
    public void BadArgumentsShowTheUsage()
    {
        Assert.StartsWith("Usage: health", Run("health lots"));
        Assert.StartsWith("Usage: tp", Run("tp 1 2"));
        Assert.StartsWith("Usage: map", Run("map"));
        Assert.StartsWith("Usage: difficulty", Run("difficulty insane"));
        Assert.Null(_level.Teleported);
    }

    [Fact]
    public void MapTakesALevelAndAnArena()
    {
        Run("map 6 ARENA_3");
        Assert.Equal((6, "ARENA_3"), _game.Mapped);
        Run("map 4");
        Assert.Equal((4, ""), _game.Mapped);
        Assert.StartsWith("No level 9", Run("map 9"));
    }

    [Fact]
    public void TogglesAndSettings()
    {
        Assert.Equal("god on", Run("god"));
        Assert.Equal(Switch.On, _level.God);
        Assert.Equal("god off", Run("god"));
        Assert.Equal("noclip on", Run("noclip"));
        Assert.Equal(Switch.On, _level.Noclip);
        Run("health 50");
        Assert.Equal(50, _level.Health);
        Run("difficulty hard");
        Assert.Equal(Difficulty.Hard, _game.Difficulty);
        Assert.Equal(Difficulty.Hard, _level.Difficulty);
        Assert.Equal("look enhanced", Run("look enhanced"));
        Assert.Equal(Graphics.Enhanced, _game.Look);
        Run("aa 4x");
        Assert.Equal(AntiAliasing.X4, _game.AntiAliasing);
        Assert.Equal("stereo intr", Run("stereo intr"));
        Assert.Equal(Stereo.InterlacedReversed, _game.Stereo);
        Assert.Equal("stereo tabr", Run("stereo tabr"));
        Assert.Equal(Stereo.TabReversed, _game.Stereo);
        Assert.Equal("stereo tab", Run("stereo tab"));
        Assert.Equal(Stereo.Tab, _game.Stereo);
        Run("stereo sbs 0.5 6");
        Assert.Equal((Stereo.Sbs, 0.5f, 6f), (_game.Stereo, _game.StereoSeparation, _game.StereoConvergence));
        Assert.StartsWith("Usage", Run("stereo sideways"));
        Run("timescale 0.5");
        Assert.Equal(0.5f, _game.TimeScale);
        Assert.StartsWith("Usage", Run("timescale -1"));
        Run("fps");
        Assert.Equal(1, _game.Overlays);
        Run("quit");
        Assert.True(_game.Quitted);
    }

    [Fact]
    public void GiveTakesOneOrAll()
    {
        Run("give sw_hbomb");
        Assert.Equal(["SW_HBOMB"], _level.Given);
        Run("give all");
        Assert.Contains("SW_GATT", _level.Given);
        Assert.Contains("SW_H150", _level.Given);
        Assert.StartsWith("Not a pickup", Run("give apple"));
    }

    [Fact]
    public void KillSaveLoad()
    {
        Assert.Equal("Killed 2 enemies", Run("kill"));
        Run("save 7");
        Assert.Equal("7", _level.Saved);
        Assert.StartsWith("No saved game", Run("load nothing"));
    }

    /// <summary>Menus, the fall, the stream: the level's commands answer, the game's work.</summary>
    [Theory]
    [InlineData("pos")]
    [InlineData("tp ARENA_2")]
    [InlineData("give all")]
    [InlineData("health 50")]
    [InlineData("kill")]
    [InlineData("save 7")]
    public void LevelCommandsNeedALevel(string line)
    {
        _game.Level = null;
        Assert.Equal("Not in a level", Run(line));
        Assert.Null(_level.Teleported);
        Assert.Empty(_level.Given);
    }

    [Fact]
    public void GameCommandsWorkOutsideALevel()
    {
        _game.Level = null;
        Run("map 6");
        Assert.Equal((6, ""), _game.Mapped);
        Run("difficulty easy");
        Assert.Equal(Difficulty.Easy, _game.Difficulty);
        Assert.Equal("look enhanced from the next level", Run("look enhanced"));
        Run("timescale 2");
        Assert.Equal(2f, _game.TimeScale);
        Run("fps");
        Assert.Equal(1, _game.Overlays);

        // God and noclip are the session's: they hold from the next level.
        Assert.Equal("god on from the next level", Run("god"));
        Assert.Equal("noclip on from the next level", Run("noclip"));
        Assert.Equal("onehit on from the next level", Run("onehit"));
        Assert.Equal(Switch.Off, _level.God);
    }

    [Fact]
    public void OneHitTogglesInTheLevel()
    {
        Assert.Equal("onehit on", Run("onehit"));
        Assert.Equal(Switch.On, _level.OneHit);
        Assert.Equal("onehit off", Run("onehit"));
        Assert.Equal(Switch.Off, _level.OneHit);
    }

    /// <summary>The mods found, then what the level replaced (outside a level: the list only).</summary>
    [Fact]
    public void ModsListsWhatTheLevelTook()
    {
        Assert.Equal("Clean walls (walls) on, priority 0\nmods 1: 1 images, 1 models\nimages: WALL\nmodels: GRUNT", Run("mods"));

        _game.Level = null;
        Assert.Equal("Clean walls (walls) on, priority 0", Run("mods"));
    }

    /// <summary>The game: the session's switches reach the level, when there's one.</summary>
    private sealed class FakeGame(FakeLevel level) : ICommandTarget
    {
        public ILevelTarget? Level { get; set; } = level;
        public (int, string)? Mapped;
        public Difficulty Difficulty;
        public Graphics Look;
        public AntiAliasing AntiAliasing;
        public float TimeScale = 1f;
        public int Overlays;
        public bool Quitted;
        private Switch _god;
        private Switch _noclip;
        private Switch _oneHit;

        public bool Map(int number, string arena)
        {
            if (number > 8)
            {
                return false;
            }

            Mapped = (number, arena);
            return true;
        }

        public bool Load(string slot) => false;

        public Switch ToggleGod()
        {
            _god = _god == Switch.On ? Switch.Off : Switch.On;
            Level?.SetGod(_god);
            return _god;
        }

        public Switch ToggleNoclip()
        {
            _noclip = _noclip == Switch.On ? Switch.Off : Switch.On;
            Level?.SetNoclip(_noclip);
            return _noclip;
        }

        public Switch ToggleOneHit()
        {
            _oneHit = _oneHit == Switch.On ? Switch.Off : Switch.On;
            Level?.SetOneHit(_oneHit);
            return _oneHit;
        }

        public void SetDifficulty(Difficulty difficulty)
        {
            Difficulty = difficulty;
            Level?.SetDifficulty(difficulty);
        }

        public bool SetLook(Graphics look)
        {
            Look = look;
            return Level != null;
        }

        public void SetAntiAliasing(AntiAliasing antiAliasing) => AntiAliasing = antiAliasing;

        public Stereo Stereo;
        public float? StereoSeparation;
        public float? StereoConvergence;

        public void SetStereo(Stereo stereo, float? separation, float? convergence)
        {
            Stereo = stereo;
            StereoSeparation = separation;
            StereoConvergence = convergence;
        }

        public void SetTimeScale(float scale) => TimeScale = scale;

        public Switch ToggleOverlay()
        {
            Overlays++;
            return Switch.On;
        }

        public void Quit() => Quitted = true;

        public IReadOnlyList<string> ModList() => ["Clean walls (walls) on, priority 0"];
    }

    private sealed class FakeLevel : ILevelTarget
    {
        public (string Arena, Vector3? Point)? Teleported;
        public int Health;
        public Difficulty Difficulty;
        public Switch God;
        public Switch Noclip;
        public Switch OneHit;
        public string? Saved;
        public readonly List<string> Given = [];

        public ModReport Mods { get; } = new(1, ["WALL"], ["GRUNT"]);

        public Placement Where() => new(3, "ARENA_1", new Vector3(-4f, 0.5f, 195f), 90f);

        public bool Teleport(string arena, Vector3? point)
        {
            Teleported = (arena, point);
            return true;
        }

        public void SetGod(Switch god) => God = god;

        public void SetNoclip(Switch noclip) => Noclip = noclip;

        public void SetOneHit(Switch oneHit) => OneHit = oneHit;

        public bool Give(string pickup)
        {
            Given.Add(pickup);
            return true;
        }

        public void SetHealth(int health) => Health = health;

        public int KillEnemies() => 2;

        public bool Save(string slot)
        {
            Saved = slot;
            return true;
        }

        public void SetDifficulty(Difficulty difficulty) => Difficulty = difficulty;

        public string? Snapshot() => "{}";
    }
}
