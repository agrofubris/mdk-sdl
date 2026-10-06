using Mdk.Engine.Diagnostics;
using Mdk.Engine.Platform;
using Mdk.Game.DevTools;
using SDL;

namespace Mdk.Game.Tests;

/// <summary>The console's parts: parsing, the registry, completion, history, the input line, its keys.</summary>
public class DevConsoleTests
{
    [Fact]
    public void LinesSplitIntoCommandsAndWords()
    {
        var commands = CommandText.Parse("  tp  ARENA_1 ; god;; health 50 ");
        Assert.Equal(3, commands.Count);
        Assert.Equal(["tp", "ARENA_1"], commands[0]);
        Assert.Equal(["god"], commands[1]);
        Assert.Equal(["health", "50"], commands[2]);
    }

    [Fact]
    public void RegistryRunsCommandsByNameOrAlias()
    {
        var registry = new CommandRegistry();
        var seen = new List<string>();
        registry.Add(new Command("teleport", "teleport <arena>", args => { seen.AddRange(args); return "done"; }, ["tp"]));

        Assert.Equal(["done"], registry.Run("TP ARENA_1"));
        Assert.Equal(["ARENA_1"], seen);
        Assert.Equal(["Unknown command: fly"], registry.Run("fly"));
    }

    [Fact]
    public void CompletionFindsCommandNames()
    {
        var registry = new CommandRegistry();
        foreach (var name in new[] { "god", "give", "health" })
        {
            registry.Add(new Command(name, name, _ => ""));
        }

        Assert.Equal("health ", registry.Complete("he").Line);
        var (line, matches) = registry.Complete("g");
        Assert.Equal("g", line);
        Assert.Equal(["give", "god"], matches);
        Assert.Equal("gi", registry.Complete("gi").Line[..2]);
        Assert.Equal("give ", registry.Complete("gi").Line);

        // Only the command's name completes.
        Assert.Equal("give al", registry.Complete("give al").Line);
    }

    [Fact]
    public void HistoryWalksBackAndForth()
    {
        var history = new CommandHistory();
        history.Add("god");
        history.Add("pos");
        history.Add("pos");

        Assert.Equal("pos", history.Previous());
        Assert.Equal("god", history.Previous());
        Assert.Equal("god", history.Previous());
        Assert.Equal("pos", history.Next());
        Assert.Equal("", history.Next());
    }

    [Fact]
    public void ConsoleEditsRunsAndRemembers()
    {
        var log = new LogRing(10);
        var console = NewConsole(log, out var ran);
        console.Toggle();
        Assert.Equal(ConsoleState.Open, console.State);

        console.Type("hea");
        console.Press(EditKey.Complete);
        Assert.Equal("health ", console.Line);
        console.Type("51");
        console.Press(EditKey.Erase);
        console.Type("0");
        console.Press(EditKey.Submit);

        Assert.Equal(["health 50"], ran);
        Assert.Equal("", console.Line);
        Assert.Contains("] health 50", log.Lines());

        console.Press(EditKey.Previous);
        Assert.Equal("health 50", console.Line);
    }

    [Fact]
    public void ConsoleClearsTheLogAndListsCommands()
    {
        var log = new LogRing(10);
        var console = NewConsole(log, out _);
        console.Run("help");
        Assert.Contains(log.Lines(), l => l.StartsWith("health", StringComparison.Ordinal));

        console.Run("clear");
        Assert.Empty(log.Lines());
    }

    [Fact]
    public void ConsoleKeysAreFixedByPlace()
    {
        var input = new Input();
        input.BeginFrame();

        // The key left of 1, whatever the layout types there (";" on a Slovak keyboard).
        input.SetKey(SDL_Scancode.SDL_SCANCODE_GRAVE, Input.State.Down, Repeat.No);
        input.SetKey(SDL_Scancode.SDL_SCANCODE_F3, Input.State.Down, Repeat.No);
        input.SetKey(SDL_Scancode.SDL_SCANCODE_BACKSPACE, Input.State.Down, Repeat.Yes);
        Assert.True(input.WasPressed(RawKey.Grave));
        Assert.True(input.WasPressed(RawKey.F3));
        Assert.True(input.WasPressed(RawKey.Backspace));
        Assert.False(input.WasPressed(RawKey.Tab));

        input.BeginFrame();
        Assert.False(input.WasPressed(RawKey.Grave));
    }

    /// <summary>A console with a "health" command that records its lines; its output goes to <paramref name="log"/>.</summary>
    private static DevConsole NewConsole(LogRing log, out List<string> ran)
    {
        var lines = new List<string>();
        ran = lines;
        var registry = new CommandRegistry();
        registry.Add(new Command("health", "health <n>", args => { lines.Add("health " + string.Join(' ', args)); return ""; }));
        return new DevConsole(registry, new CommandHistory(), log, log.Add);
    }
}
