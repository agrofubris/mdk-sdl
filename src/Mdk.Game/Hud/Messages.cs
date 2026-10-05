using Mdk.Formats;

namespace Mdk.Game.Hud;

/// <summary>On-screen messages: texts of <c>MDKFONT.FTI</c> queued by <c>hud_message</c> (opcode
/// 247), pickups and a few events (0x425400), shown one after the other in the big font (0x425474).
/// One or two lines (split at the <c>\n</c> escape), centred on the 600x360 view, baseline at y 120
/// (two lines: 105 and 135). Flag 1 grows the message from nothing in half a second and shrinks it
/// back when its time is up; its time runs twice as fast while others wait. Flag 2 queues it first.
/// A line too wide for the big font is drawn in the small one (0x415b30).</summary>
public sealed class Messages(Fti fti, FontView big, FontView small)
{
    public const int FlagZoom = 1;
    public const int FlagFront = 2;
    /// <summary>The queue has 4 entries (0x57ecf0).</summary>
    private const int QueueSize = 4;
    /// <summary>Seconds to grow or shrink (0.5 × 2 = full size).</summary>
    private const float ZoomTime = 0.5f;
    private const float ViewWidth = 600f;
    private const float Baseline = 120f;
    private const float LineOffset = 15f;
    private const int MaxLines = 2;
    private const int MaxLineLength = 35;
    private static readonly byte[] LineBreak = "\\n"u8.ToArray();

    private sealed record Message(List<byte[]> Lines, int Flags, float Time);

    private readonly LinkedList<Message> _queue = [];
    private Message? _current;
    /// <summary>Time the current message stays (0x57ece0) and its zoom (0x57ece4, 0-0.5).</summary>
    private float _time;
    private float _zoom;

    /// <summary>Queues the text <paramref name="textName"/>; false if it doesn't exist.</summary>
    public bool Push(string textName, int flags, float seconds)
    {
        var text = fti.GetTextBytes(textName);
        if (text.Length == 0)
        {
            return false;
        }

        // At most two lines of 35 characters, split at the two-character \n escape.
        var lines = new List<byte[]>();
        var start = 0;
        while (lines.Count < MaxLines)
        {
            var found = text.AsSpan(start).IndexOf(LineBreak);
            var end = found < 0 ? text.Length : start + found;
            lines.Add(text[start..Math.Min(end, start + MaxLineLength)]);
            if (end >= text.Length)
            {
                break;
            }

            start = end + LineBreak.Length;
        }

        var message = new Message(lines, flags, seconds);
        if ((flags & FlagFront) != 0)
        {
            _queue.AddFirst(message);
        }
        else
        {
            _queue.AddLast(message);
        }

        if (_queue.Count > QueueSize)
        {
            _queue.RemoveLast();
        }

        return true;
    }

    /// <summary>Advances the messages (0x425474, once per frame).</summary>
    public void Update(float delta)
    {
        var step = _queue.Count > 0 ? delta * 2f : delta;
        if (_time == 0f)
        {
            if (_zoom > 0f)
            {
                _zoom = MathF.Max(_zoom - delta, 0f);
                return;
            }

            _current = _queue.First?.Value;
            if (_current != null)
            {
                _queue.RemoveFirst();
                _time = _current.Time;
            }

            return;
        }

        if ((_current!.Flags & FlagZoom) == 0 || _zoom == ZoomTime)
        {
            _time = MathF.Max(_time - step, 0f);
            return;
        }

        _zoom = MathF.Min(_zoom + delta, ZoomTime);
    }

    /// <summary>Draws the current message, centred on a canvas <paramref name="width"/> wide.</summary>
    public void Draw(float width)
    {
        if (_current == null)
        {
            return;
        }

        var full = _time > 0f && ((_current.Flags & FlagZoom) == 0 || _zoom == ZoomTime);
        var scale = full ? 1f : _zoom * 2f;
        if (scale <= 0f)
        {
            return;
        }

        var lines = _current.Lines;
        for (var i = 0; i < lines.Count; i++)
        {
            var y = Baseline;
            if (lines.Count == MaxLines)
            {
                y += (i == 1 ? LineOffset : -LineOffset) * scale;
            }

            var font = full && big.Width(lines[i]) >= ViewWidth ? small : big;
            var x = MathF.Round(width / 2f - font.Width(lines[i]) * scale / 2f);
            font.Draw(lines[i], x, MathF.Round(y), scale);
        }
    }
}
