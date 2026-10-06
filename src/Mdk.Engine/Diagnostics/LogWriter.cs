using System.Text;

namespace Mdk.Engine.Diagnostics;

/// <summary>A writer that passes everything on unchanged and copies whole lines to a ring.
/// <code>
///   Console.WriteLine ──► LogWriter ──► stdout / stderr (as before)
///                                 └──► LogRing (the console's log)
/// </code></summary>
public sealed class LogWriter(TextWriter inner, LogRing ring) : TextWriter
{
    private readonly StringBuilder _line = new();
    private readonly Lock _lock = new();

    public override Encoding Encoding => inner.Encoding;

    public override void Write(char value)
    {
        lock (_lock)
        {
            inner.Write(value);
            Collect(value);
        }
    }

    public override void Write(string? value)
    {
        if (value == null)
        {
            return;
        }

        lock (_lock)
        {
            inner.Write(value);
            foreach (var c in value)
            {
                Collect(c);
            }
        }
    }

    public override void Write(char[] buffer, int index, int count) => Write(new string(buffer, index, count));

    public override void WriteLine(string? value) => Write(value + NewLine);

    public override void Flush() => inner.Flush();

    /// <summary>A line ends at \n; \r is dropped.</summary>
    private void Collect(char c)
    {
        if (c == '\r')
        {
            return;
        }

        if (c != '\n')
        {
            _line.Append(c);
            return;
        }

        ring.Add(_line.ToString());
        _line.Clear();
    }
}

/// <summary>Captures the program's output (stdout and stderr) into one ring, still printing it.</summary>
public static class Log
{
    private static LogRing? _ring;

    /// <summary>The ring of the output's last lines; the first call installs it.</summary>
    public static LogRing Capture(int capacity)
    {
        if (_ring != null)
        {
            return _ring;
        }

        _ring = new LogRing(capacity);
        Console.SetOut(new LogWriter(Console.Out, _ring));
        Console.SetError(new LogWriter(Console.Error, _ring));
        return _ring;
    }
}
