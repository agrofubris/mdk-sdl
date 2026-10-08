using System.Drawing;
using System.Globalization;
using System.Numerics;
using System.Text;
using Mdk.Engine.Render;
using Mdk.Formats;
using Mdk.Game.Mods;

namespace Mdk.Game.Hud;

/// <summary>What the bomber's HUD shows: the cursor (pixels of the 600×360 view) and the bombs left.</summary>
public readonly record struct BomberSight(Vector2 Cursor, int Bombs);

/// <summary>The XE bomber's HUD (0x46be98; bomber_overlay.gd), while the controls are unlocked:
/// <c>BOMBTARG</c> at the view's centre, <c>CROSS</c> at the cursor and the bombs left
/// right-aligned at x 472 on the baseline y 56, in the big font. The 600×360 view is centred on the
/// canvas.</summary>
public sealed class BomberOverlay
{
    private const string Target = "BOMBTARG";
    private const string Cross = "CROSS";
    private static readonly Vector2 ViewCentre = new(300f, 180f);
    private const float CountRight = 472f;
    private const float CountBaseline = 56f;

    private sealed record Image(int Texture, Vector2 Size, Vector2 Hotspot);

    private readonly Renderer _renderer;
    private readonly int _palette;
    private readonly FontView _font;
    private readonly Image _target;
    private readonly Image _cross;

    /// <summary>The bomber's HUD; <paramref name="mods"/>' images (through <paramref name="colours"/>) replace its own.</summary>
    public BomberOverlay(Renderer renderer, Bni sprites, int palette, FontView font, Palette? colours = null, ModImages? mods = null)
    {
        _renderer = renderer;
        _palette = palette;
        _font = font;
        _target = Load(Target, sprites.GetAnimation(Target).GetFrame(0), colours, mods);
        _cross = Load(Cross, sprites.GetAnimation(Cross).GetFrame(0), colours, mods);
    }

    /// <summary>The HUD's 2D images by their names in mods (<see cref="CanvasImages"/>; the exports).</summary>
    public static IEnumerable<(string Name, Texture Texture)> Images(Bni sprites) =>
        [.. new[] { Target, Cross }.Select(a => (CanvasImages.FrameName(a, 0), sprites.GetAnimation(a).GetFrame(0).Image))];

    private Image Load(string animation, SpriteAnimation.Frame frame, Palette? colours, ModImages? mods)
    {
        var texture = _renderer.CreateIndexTexture(frame.Image.Width, frame.Image.Height, frame.Image.Indices);
        if (colours != null)
        {
            CanvasImages.Replace(_renderer, mods, CanvasImages.FrameName(animation, 0), frame.Image, colours, texture, _palette);
        }

        return new(texture, new Vector2(frame.Image.Width, frame.Image.Height), new Vector2(frame.HotspotX, frame.HotspotY));
    }

    public void Draw(float canvasWidth, BomberSight sight)
    {
        var origin = new Vector2(canvasWidth / 2f - ViewCentre.X, 0f);
        DrawImage(_target, origin + ViewCentre);
        DrawImage(_cross, origin + new Vector2(MathF.Round(sight.Cursor.X), MathF.Round(sight.Cursor.Y)));
        var text = Encoding.ASCII.GetBytes(sight.Bombs.ToString(CultureInfo.InvariantCulture));
        _font.Draw(text, origin.X + CountRight - _font.Width(text), origin.Y + CountBaseline);
    }

    /// <summary>An image with its hotspot at <paramref name="at"/>.</summary>
    private void DrawImage(Image image, Vector2 at)
    {
        var corner = at - image.Hotspot;
        _renderer.DrawImage(image.Texture, _palette, image.Size, new RectangleF(0f, 0f, image.Size.X, image.Size.Y),
            new RectangleF(corner.X, corner.Y, image.Size.X, image.Size.Y), Vector4.One);
    }
}
