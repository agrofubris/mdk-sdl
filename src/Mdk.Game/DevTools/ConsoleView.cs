using System.Drawing;
using System.Numerics;
using Mdk.Engine.Diagnostics;
using Mdk.Engine.Render;

namespace Mdk.Game.DevTools;

/// <summary>Draws the console: half the canvas high, slid down from the top by
/// <see cref="DevConsole.Slide"/>; the line typed at the bottom, the log above it, newest last.
/// <code>
///   ┌──────────────────────────────┐ ▲
///   │ Level 3: 12 arenas ...       │ │ half the canvas
///   │ ] pos                        │ │
///   │ ] god_                       │ ▼ × slide
///   └──────────────────────────────┘
/// </code></summary>
public sealed class ConsoleView(DevTextView text)
{
    private const float Margin = 4f;
    private const float EdgeHeight = 1f;
    private const string Cursor = "_";
    private static readonly Vector4 Background = new(0.02f, 0.03f, 0.06f, 0.85f);
    private static readonly Vector4 Edge = new(0.5f, 0.6f, 0.8f, 1f);

    /// <summary>The console's bottom edge on the canvas (0 when closed).</summary>
    public static float Bottom(DevConsole console) => Height * console.Slide;

    private static float Height => Renderer.CanvasHeight / 2f;

    public void Draw(DevConsole console, LogRing log)
    {
        if (console.Slide <= 0f)
        {
            return;
        }

        var width = text.Renderer.CanvasWidth;
        var height = Height;
        var bottom = Bottom(console);
        text.Fill(new RectangleF(0f, bottom - height, width, height), Background);
        text.Fill(new RectangleF(0f, bottom, width, EdgeHeight), Edge);

        // The line typed, then the log upwards from the newest line not scrolled away.
        var baseline = bottom - Margin;
        text.Text(DevConsole.Prompt + console.Line + Cursor, Margin, baseline);
        var rows = (int)((height - Margin) / DevTextView.LineHeight) - 1;
        var lines = log.Lines(rows + console.Scroll);
        var shown = lines.Take(Math.Max(lines.Count - console.Scroll, 0)).TakeLast(rows).Reverse();
        foreach (var line in shown)
        {
            baseline -= DevTextView.LineHeight;
            text.Text(line, Margin, baseline);
        }
    }
}
