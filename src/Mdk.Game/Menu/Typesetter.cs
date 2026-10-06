namespace Mdk.Game.Menu;

/// <summary>Lays out the briefings' and debriefings' texts like 0x4335c0. Codes (a decimal number N,
/// possibly negative, may precede the letter):
/// <code>
///   \c, \Nc  end the line; the next is centred on x 300 (or N); y unchanged
///   \n, \Nn  end the line; x 0, left-aligned; y += 36 (+ N)
///   \y, \Ny  end the line; x 0, left-aligned; y += 36 (or N)
///   \x, \Nx  end the line; left-aligned at x N (or 0)
///   \Np      pause: the budget loses N characters
///   \d, \i   characters cost one / appear at once
/// </code></summary>
public static class Typesetter
{
    /// <summary>A line: its characters, where it starts (x is its centre when centred), and the
    /// character count before it.</summary>
    public sealed record Line(byte[] Text, float X, float Y, bool Centred, int Start);

    public const float LineHeight = 36f;
    private const float Centre = 300f;
    private const byte Escape = (byte)'\\';

    /// <summary>The lines of a text whose first baseline is <paramref name="y"/>, and its character count.</summary>
    public static (List<Line> Lines, int Count) Layout(ReadOnlySpan<byte> text, float y)
    {
        var lines = new List<Line>();
        var line = new List<byte>();
        var (x, centred, start, count, cost) = (0f, false, 0, 0, true);
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i++];
            if (c != Escape)
            {
                line.Add(c);
                count += cost ? 1 : 0;
                continue;
            }

            var j = i;
            while (j < text.Length && (char.IsAsciiDigit((char)text[j]) || text[j] == '-'))
            {
                j++;
            }

            var hasNumber = j > i && int.TryParse(System.Text.Encoding.ASCII.GetString(text[i..j]), out _);
            var number = hasNumber ? int.Parse(System.Text.Encoding.ASCII.GetString(text[i..j])) : 0;
            var code = j < text.Length ? (char)text[j] : '\0';
            i = j + 1;
            switch (code)
            {
                case 'c' or 'n' or 'x' or 'y':
                    lines.Add(new Line([.. line], x, y, centred, start));
                    start = count;
                    line.Clear();
                    (x, centred, y) = code switch
                    {
                        'c' => (hasNumber ? number : Centre, true, y),
                        'x' => (hasNumber ? number : 0f, false, y),
                        'n' => (0f, false, y + LineHeight + number),
                        _ => (0f, false, y + (hasNumber ? number : LineHeight)),
                    };
                    break;
                case 'p':
                    count += number;
                    break;
                case 'd':
                    cost = true;
                    break;
                case 'i':
                    cost = false;
                    break;
            }
        }

        lines.Add(new Line([.. line], x, y, centred, start));
        return (lines, count);
    }
}
