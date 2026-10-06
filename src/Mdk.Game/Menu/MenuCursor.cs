using Mdk.Formats;

namespace Mdk.Game.Menu;

/// <summary>The original's mouse cursor in the menus (0x42c010): <c>ARROW</c>, a one-frame sprite of
/// <c>MDKFONT.FTI</c> (after its size) in the <c>SYS_PAL</c> colours, index 0 see-through, scaled by
/// whole steps to the window as the 600 x 360 view.</summary>
public static class MenuCursor
{
    private const string Sprite = "ARROW";
    private const int SpriteOffset = 4;
    private const int Channels = 4;
    private const byte Opaque = 255;

    /// <summary>An RGBA image of the cursor and its hotspot.</summary>
    public sealed record Image(byte[] Rgba, int Width, int Height, int HotX, int HotY);

    /// <summary>Sets the cursor of the window.</summary>
    public static void Apply(Ui ui)
    {
        var (width, height) = ui.Window.Size;
        var scale = Math.Max((int)MathF.Min(width / ScreenView.Size.X, height / ScreenView.Size.Y), 1);
        if (Build(ui.Fti, scale) is { } image)
        {
            ui.Window.SetCursor(image.Rgba, image.Width, image.Height, image.HotX, image.HotY);
        }
    }

    /// <summary>The cursor at <paramref name="scale"/> pixels per pixel, or null without it.</summary>
    public static Image? Build(Fti fti, int scale)
    {
        if (!fti.Has(Sprite))
        {
            return null;
        }

        var animation = SpriteAnimation.Parse(Sprite, fti.GetBytes(Sprite), SpriteOffset);
        if (animation.FrameCount < 1)
        {
            return null;
        }

        var frame = animation.GetFrame(0);
        var palette = Palette.FromRgb(fti.GetBytes("SYS_PAL")).Rgba;
        var (w, h) = (frame.Image.Width * scale, frame.Image.Height * scale);
        var rgba = new byte[w * h * Channels];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var index = frame.Image.Indices[y / scale * frame.Image.Width + x / scale];
                if (index == 0)
                {
                    continue;
                }

                var at = (y * w + x) * Channels;
                palette.AsSpan(index * Channels, Channels - 1).CopyTo(rgba.AsSpan(at));
                rgba[at + Channels - 1] = Opaque;
            }
        }

        return new Image(rgba, w, h, Math.Clamp(frame.HotspotX * scale, 0, w - 1), Math.Clamp(frame.HotspotY * scale, 0, h - 1));
    }
}
