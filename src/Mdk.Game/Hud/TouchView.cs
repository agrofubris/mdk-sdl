using System.Drawing;
using System.Numerics;
using System.Text;
using Mdk.Engine.Platform;
using Mdk.Engine.Render;

namespace Mdk.Game.Hud;

/// <summary>Draws the on-screen controls of a touch screen (<see cref="TouchControls"/>) in play:
/// the buttons' outlines and labels, the stick where a finger walks.</summary>
public sealed class TouchView(Renderer renderer, FontView font)
{
    /// <summary>Outline width (canvas units).</summary>
    private const float Line = 1f;
    /// <summary>The knob's half size (canvas units).</summary>
    private const float Knob = 6f;
    private static readonly Vector4 Outline = new(1f, 1f, 1f, 0.35f);
    private static readonly Vector4 Fill = new(1f, 1f, 1f, 0.25f);
    private const float LabelAlpha = 0.7f;

    /// <summary>The labels' bytes, made once (frames don't allocate).</summary>
    private readonly Dictionary<string, byte[]> _labels = [];

    public void Draw(Window window)
    {
        if (window.Touch is not { } touch)
        {
            return;
        }

        var (width, height) = window.Size;
        var screen = new Vector2(width, height);
        foreach (var button in touch.Layout(screen))
        {
            var area = Canvas(button.Area);
            renderer.FrameRect(area, Line, Outline);
            var text = Label(button.Label);
            var x = area.X + (area.Width - font.Width(text)) / 2f;
            font.Draw(text, x, area.Y + area.Height / 2f, 1f, LabelAlpha);
        }

        if (touch.Stick is not { } stick || touch.StickAt is not { } at)
        {
            return;
        }

        // The stick's reach around where the finger landed, and the finger's knob.
        var radius = touch.StickRadius(screen);
        renderer.FrameRect(Canvas(new RectangleF(stick.X - radius, stick.Y - radius, 2f * radius, 2f * radius)), Line, Outline);
        var knob = renderer.CanvasPoint(at.X, at.Y);
        renderer.FillRect(new RectangleF(knob.X - Knob, knob.Y - Knob, 2f * Knob, 2f * Knob), Fill);
    }

    /// <summary>A window rectangle on the canvas.</summary>
    private RectangleF Canvas(RectangleF area)
    {
        var from = renderer.CanvasPoint(area.Left, area.Top);
        var to = renderer.CanvasPoint(area.Right, area.Bottom);
        return RectangleF.FromLTRB(from.X, from.Y, to.X, to.Y);
    }

    private byte[] Label(string label)
    {
        if (!_labels.TryGetValue(label, out var bytes))
        {
            bytes = Encoding.ASCII.GetBytes(label);
            _labels[label] = bytes;
        }

        return bytes;
    }
}
