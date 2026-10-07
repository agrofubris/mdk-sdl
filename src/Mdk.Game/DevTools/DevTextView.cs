using System.Drawing;
using System.Numerics;
using System.Text;
using Mdk.Engine.Render;
using Mdk.Game.Hud;

namespace Mdk.Game.DevTools;

/// <summary>Lines of the developer tools in the game's small font, half size, on the canvas.</summary>
public sealed class DevTextView(Renderer renderer, FontView font)
{
    public const float Scale = 0.5f;
    /// <summary>Baseline to baseline, in canvas units.</summary>
    public const float LineHeight = 9f;

    public Renderer Renderer => renderer;

    /// <summary>A line from x along the baseline y.</summary>
    public void Text(string text, float x, float y) => font.Draw(Bytes(text), x, y, Scale);

    public float Width(string text) => font.Width(Bytes(text)) * Scale;

    /// <summary>The last line's Latin-1 bytes (kept: no array per line drawn).</summary>
    private byte[] _bytes = [];

    public void Fill(RectangleF area, Vector4 colour) => renderer.FillRect(area, colour);

    /// <summary>The font's characters are Latin-1 (the console's line only holds those).</summary>
    private ReadOnlySpan<byte> Bytes(string text)
    {
        if (_bytes.Length < text.Length)
        {
            _bytes = new byte[Math.Max(text.Length, _bytes.Length * 2)];
        }

        var count = Encoding.Latin1.GetBytes(text, _bytes);
        return _bytes.AsSpan(0, count);
    }
}
