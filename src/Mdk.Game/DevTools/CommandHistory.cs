namespace Mdk.Game.DevTools;

/// <summary>The lines run in the console, oldest first; Up and Down walk them. It lasts the session.
/// <code>
///   [god, pos] ◄─Previous── (new line) ──Next─► ""
/// </code></summary>
public sealed class CommandHistory
{
    private const int MaxLines = 100;

    private readonly List<string> _lines = [];
    /// <summary>The line shown; <c>_lines.Count</c> is the new line.</summary>
    private int _cursor;

    /// <summary>A line was run (a repeat of the last one isn't kept twice).</summary>
    public void Add(string line)
    {
        if (_lines.Count == 0 || _lines[^1] != line)
        {
            _lines.Add(line);
        }

        if (_lines.Count > MaxLines)
        {
            _lines.RemoveAt(0);
        }

        _cursor = _lines.Count;
    }

    public string Previous()
    {
        _cursor = Math.Max(_cursor - 1, 0);
        return Current();
    }

    public string Next()
    {
        _cursor = Math.Min(_cursor + 1, _lines.Count);
        return Current();
    }

    private string Current() => _cursor < _lines.Count ? _lines[_cursor] : "";
}
