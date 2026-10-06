using System.Drawing;
using System.Numerics;

namespace Mdk.Game.DevTools;

/// <summary>Draws the debug overlay's lines (<see cref="OverlayText"/>) at the top left (under the
/// console when it's open), on a dark box.</summary>
public sealed class OverlayView(DevTextView text)
{
    private const float Margin = 3f;
    /// <summary>The font's ascent at the view's scale: the first baseline is this far down.</summary>
    private const float Ascent = 7f;
    private static readonly Vector4 Background = new(0f, 0f, 0f, 0.6f);

    public void Draw(IReadOnlyList<string> lines, float top)
    {
        var width = lines.Max(text.Width) + 2f * Margin;
        var height = lines.Count * DevTextView.LineHeight + 2f * Margin;
        text.Fill(new RectangleF(0f, top, width, height), Background);
        var baseline = top + Margin + Ascent;
        foreach (var line in lines)
        {
            text.Text(line, Margin, baseline);
            baseline += DevTextView.LineHeight;
        }
    }
}
