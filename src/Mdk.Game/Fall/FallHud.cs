using System.Drawing;
using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Formats;
using Mdk.Game.Hud;
using Mdk.Game.Kurt;
using Mdk.Game.Mods;

namespace Mdk.Game.Fall;

/// <summary>The HUD over the fall, as in a level (0x420830, 0x46cce4) but from <c>FALL3D.BNI</c>'s
/// copies of the images: the messages, the health panel (blinking at 20 or less), the inventory
/// (always: 0x46cce4 resets its timer every frame outside sniper mode), and the skull growing while Kurt dies.
/// <code>
///   ┌─────────────────────────────┐
///   │          message            │
///   │          (skull)            │
///   │ [i][i][i][i][i]   [health]  │
///   └─────────────────────────────┘
/// </code></summary>
public sealed class FallHud
{
    private const int BlinkMask = 31;
    private const int BlinkOn = 16;
    private const int LowHealth = 20;
    private const int MaxNumber = 999;
    private const int DigitWidth = 8;
    private const float PanelRight = 16f;
    private const float PanelBottom = 10f;
    private const float SlotStep = 48f;
    private static readonly Vector2 IconAnchor = new(32f, 328f);
    private const float CountRise = 12f;
    private const float ViewCentreY = 180f;
    /// <summary>The skull grows with the red: full size at 256/255.</summary>
    private const float SkullGrowth = 255f / 256f;
    private const int FontSpaceBig = 6;
    private const int FontSpaceSmall = 4;

    public enum Life { Alive, Dying }

    private sealed record Image(int Texture, Vector2 Size, int HotspotX, int HotspotY);

    private readonly Renderer _renderer;
    private readonly int _palette;
    private readonly Palette? _colours;
    private readonly ModImages? _mods;
    private readonly Image _panel;
    private readonly Image _skull;
    private readonly Image _digits;
    private readonly List<Image> _icons = [];
    private int _blink;

    public Messages Messages { get; }

    /// <summary>The fall's HUD; <paramref name="mods"/>' images (through <paramref name="colours"/>,
    /// the palette's colours before its effects) replace its own, named as the level's HUD's.</summary>
    public FallHud(Renderer renderer, Bni bni, int palette, Fti fti, Palette? colours = null, ModImages? mods = null)
    {
        _renderer = renderer;
        _palette = palette;
        _colours = colours;
        _mods = mods;
        var images = HudView.Images(bni).ToList();
        _panel = Load(images[0], 0, 0);
        _skull = Load(images[1], 0, 0);
        _digits = Load(images[2], 0, 0);
        var pickups = bni.GetAnimation("PICKUPS");
        for (var i = 0; i < pickups.FrameCount; i++)
        {
            var frame = pickups.GetFrame(i);
            _icons.Add(Load(images[HudFirstIcon + i], frame.HotspotX, frame.HotspotY));
        }

        // The fonts use the fall's palette and its effects (fall.gd).
        var big = new FontView(renderer, Font.Parse(fti.GetBytes("FONTBIG"), FontSpaceBig), palette);
        var small = new FontView(renderer, Font.Parse(fti.GetBytes("FONTSML"), FontSpaceSmall), palette);
        Messages = new Messages(fti, big, small);
    }

    /// <summary>The panel, the skull and the digits come before the icons in <see cref="HudView.Images"/>.</summary>
    private const int HudFirstIcon = 3;

    private Image Load((string Name, Texture Texture) image, int hotspotX, int hotspotY)
    {
        var texture = image.Texture;
        var id = _renderer.CreateIndexTexture(texture.Width, texture.Height, texture.Indices);
        if (_colours != null)
        {
            CanvasImages.Replace(_renderer, _mods, image.Name, texture, _colours, id, _palette);
        }

        return new(id, new Vector2(texture.Width, texture.Height), hotspotX, hotspotY);
    }

    /// <summary>Some ticks passed: the blink.</summary>
    public void Tick(Inventory inventory, int ticks)
    {
        _blink = (_blink + ticks) & BlinkMask;
    }

    /// <summary>Draws it; <paramref name="red"/> (0-1) sizes the skull while Kurt dies.</summary>
    public void Draw(int health, Inventory inventory, Life life, float red)
    {
        var width = _renderer.CanvasWidth;
        if (life == Life.Dying)
        {
            var size = _skull.Size * MathF.Min(red * SkullGrowth, 1f);
            DrawImage(_skull, new Vector2(width / 2f, ViewCentreY) - size / 2f, size);
        }

        Messages.Draw(width);

        // The health panel at the bottom right; the number blinks when health is low.
        var panel = new Vector2(width - (_panel.Size.X + PanelRight), Renderer.CanvasHeight - (_panel.Size.Y + PanelBottom));
        DrawImage(_panel, panel, _panel.Size);
        if (health > LowHealth || _blink < BlinkOn)
        {
            DrawNumber(health, panel + new Vector2((int)_panel.Size.X >> 1, ((int)_panel.Size.Y - (int)_digits.Size.Y) >> 1));
        }

        DrawInventory(inventory);
    }

    private void DrawInventory(Inventory inventory)
    {
        for (var i = 0; i < inventory.Slots.Count; i++)
        {
            var (item, count) = inventory.Slots[i];
            var frame = (int)item - 1;
            if (frame < 0 || frame >= _icons.Count)
            {
                continue;
            }

            var icon = _icons[frame];
            var anchor = IconAnchor + new Vector2(i * SlotStep, 0f);
            DrawImage(icon, anchor - new Vector2(icon.HotspotX, icon.HotspotY), icon.Size);
            var shown = item == Inventory.Item.SuperChainGun ? inventory.SuperChainGun : count;
            if (shown > 1)
            {
                DrawNumber(shown, anchor - new Vector2(0f, CountRise));
            }
        }
    }

    private void DrawImage(Image image, Vector2 at, Vector2 size) =>
        _renderer.DrawImage(image.Texture, _palette, image.Size, new RectangleF(0f, 0f, image.Size.X, image.Size.Y),
            new RectangleF(at.X, at.Y, size.X, size.Y), Vector4.One);

    /// <summary>A number (at most 999) centred on <paramref name="at"/>.X, 8-pixel digits (0x420bd0).</summary>
    private void DrawNumber(int value, Vector2 at)
    {
        var text = Math.Min(value, MaxNumber).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var x = at.X - DigitWidth / 2f * text.Length;
        foreach (var c in text)
        {
            var source = new RectangleF((c - '0') * DigitWidth, 0f, DigitWidth, _digits.Size.Y);
            _renderer.DrawImage(_digits.Texture, _palette, _digits.Size, source, new RectangleF(x, at.Y, DigitWidth, _digits.Size.Y), Vector4.One);
            x += DigitWidth;
        }
    }
}
