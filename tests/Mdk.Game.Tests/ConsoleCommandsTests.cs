using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Game.DevTools;
using Mdk.Game.Flow;
using Mdk.Game.Kurt;

namespace Mdk.Game.Tests;

/// <summary>The console's commands: their arguments reach the level (a fake one here).</summary>
public class ConsoleCommandsTests
{
    private readonly FakeLevel _level = new();
    private readonly CommandRegistry _registry = new();

    public ConsoleCommandsTests() => ConsoleCommands.Register(_registry, _level);

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
        Assert.Equal((6, "ARENA_3"), _level.Mapped);
        Run("map 4");
        Assert.Equal((4, ""), _level.Mapped);
        Assert.StartsWith("No level 9", Run("map 9"));
    }

    [Fact]
    public void TogglesAndSettings()
    {
        Assert.Equal("god on", Run("god"));
        Assert.Equal("god off", Run("god"));
        Assert.Equal("noclip on", Run("noclip"));
        Run("health 50");
        Assert.Equal(50, _level.Health);
        Run("difficulty hard");
        Assert.Equal(Difficulty.Hard, _level.Difficulty);
        Run("look enhanced");
        Assert.Equal(Graphics.Enhanced, _level.Look);
        Run("aa 4x");
        Assert.Equal(AntiAliasing.X4, _level.AntiAliasing);
        Run("timescale 0.5");
        Assert.Equal(0.5f, _level.TimeScale);
        Assert.StartsWith("Usage", Run("timescale -1"));
        Run("fps");
        Assert.Equal(1, _level.Overlays);
        Run("quit");
        Assert.True(_level.Quitted);
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

    private sealed class FakeLevel : ICommandTarget
    {
        public (string Arena, Vector3? Point)? Teleported;
        public (int, string)? Mapped;
        public int Health;
        public Difficulty Difficulty;
        public Graphics Look;
        public AntiAliasing AntiAliasing;
        public float TimeScale = 1f;
        public int Overlays;
        public bool Quitted;
        public string? Saved;
        public readonly List<string> Given = [];
        private Switch _god;
        private Switch _noclip;

        public Placement Where() => new(3, "ARENA_1", new Vector3(-4f, 0.5f, 195f), 90f);

        public bool Teleport(string arena, Vector3? point)
        {
            Teleported = (arena, point);
            return true;
        }

        public bool Map(int level, string arena)
        {
            if (level > 8)
            {
                return false;
            }

            Mapped = (level, arena);
            return true;
        }

        public Switch ToggleGod() => _god = _god == Switch.On ? Switch.Off : Switch.On;

        public Switch ToggleNoclip() => _noclip = _noclip == Switch.On ? Switch.Off : Switch.On;

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

        public bool Load(string slot) => false;

        public void SetDifficulty(Difficulty difficulty) => Difficulty = difficulty;

        public bool SetLook(Graphics look)
        {
            Look = look;
            return true;
        }

        public void SetAntiAliasing(AntiAliasing antiAliasing) => AntiAliasing = antiAliasing;

        public void SetTimeScale(float scale) => TimeScale = scale;

        public Switch ToggleOverlay()
        {
            Overlays++;
            return Switch.On;
        }

        public void Quit() => Quitted = true;
    }
}
