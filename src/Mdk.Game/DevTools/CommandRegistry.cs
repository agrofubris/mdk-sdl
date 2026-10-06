namespace Mdk.Game.DevTools;

/// <summary>A console command: its name, how it's used, what it does (given its arguments, it
/// returns its output, "" for none) and its other names.</summary>
public sealed record Command(string Name, string Usage, Func<string[], string> Run, IReadOnlyList<string>? Aliases = null)
{
    public IEnumerable<string> Names => [Name, .. Aliases ?? []];
}

/// <summary>The console's commands by name (any case), and completion of their names.</summary>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, Command> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Command> _commands = [];

    public IReadOnlyList<Command> Commands => _commands;

    public void Add(Command command)
    {
        _commands.Add(command);
        foreach (var name in command.Names)
        {
            _byName[name] = command;
        }
    }

    /// <summary>Runs a line's commands in turn; returns their output lines.</summary>
    public IReadOnlyList<string> Run(string line)
    {
        var output = new List<string>();
        foreach (var words in CommandText.Parse(line))
        {
            var text = _byName.TryGetValue(words[0], out var command)
                ? command.Run(words[1..])
                : $"Unknown command: {words[0]}";
            output.AddRange(text.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        }

        return output;
    }

    /// <summary>Completes a command's name: one match is completed (and a space added), several
    /// are completed to their common start and listed. Arguments aren't completed.
    /// <code>
    ///   "he" ──► "health "        "g" ──► "g" [give, god]
    /// </code></summary>
    public (string Line, IReadOnlyList<string> Matches) Complete(string line)
    {
        if (line.Contains(' ') || line.Length == 0)
        {
            return (line, []);
        }

        var matches = _byName.Keys.Where(n => n.StartsWith(line, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToList();
        return matches.Count switch
        {
            0 => (line, matches),
            1 => (matches[0] + " ", matches),
            _ => (CommonStart(matches), matches),
        };
    }

    private static string CommonStart(IReadOnlyList<string> names)
    {
        var length = names.Min(n => n.Length);
        var common = 0;
        while (common < length && names.All(n => char.ToLowerInvariant(n[common]) == char.ToLowerInvariant(names[0][common])))
        {
            common++;
        }

        return names[0][..common];
    }
}
