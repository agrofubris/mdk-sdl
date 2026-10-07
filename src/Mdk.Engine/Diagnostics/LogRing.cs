namespace Mdk.Engine.Diagnostics;

/// <summary>The last lines printed, oldest first (the console shows them). Any thread may add.</summary>
public sealed class LogRing(int capacity)
{
    private readonly Queue<string> _lines = new();
    private readonly Lock _lock = new();

    public void Add(string line)
    {
        lock (_lock)
        {
            _lines.Enqueue(line);
            if (_lines.Count > capacity)
            {
                _lines.Dequeue();
            }
        }
    }

    /// <summary>The last <paramref name="count"/> lines (all by default), oldest first.</summary>
    public IReadOnlyList<string> Lines(int count = int.MaxValue)
    {
        lock (_lock)
        {
            return _lines.Skip(Math.Max(_lines.Count - count, 0)).ToList();
        }
    }

    /// <summary><see cref="Lines"/> into <paramref name="lines"/> (emptied first): no list per call.</summary>
    public void CopyLast(int count, List<string> lines)
    {
        lines.Clear();
        lock (_lock)
        {
            var skip = Math.Max(_lines.Count - count, 0);
            foreach (var line in _lines)
            {
                if (skip-- <= 0)
                {
                    lines.Add(line);
                }
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _lines.Clear();
        }
    }
}
