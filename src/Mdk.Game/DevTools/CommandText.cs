namespace Mdk.Game.DevTools;

/// <summary>A console line: commands split by ';', words by spaces.
/// <code>
///   "tp ARENA_1; god"  ──►  [tp, ARENA_1], [god]
/// </code></summary>
public static class CommandText
{
    private const char CommandSeparator = ';';

    public static IReadOnlyList<string[]> Parse(string line) =>
        line.Split(CommandSeparator)
            .Select(command => command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(words => words.Length != 0)
            .ToList();
}
