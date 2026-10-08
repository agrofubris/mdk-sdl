using System.Drawing;
using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Formats;
using Mdk.Game.Mods;

namespace Mdk.Game.Hud;

/// <summary>What the HUD shows this frame: Kurt's health and flashes (0-255 like the original's
/// counters), his inventory (item numbers, 1-based icons of <c>PICKUPS</c>) and the health bar of
/// the object he shoots at.</summary>
public readonly record struct HudState(
    int Health,
    float HurtFlash,
    float WhiteFlash,
    bool Dead,
    IReadOnlyList<(int Item, int Count)> Slots,
    int Selected,
    int SuperChainGun,
    int BarHealth,
    int BarMax,
    BomberSight? Bomber = null);

/// <summary>The in-game HUD, drawn like the original (0x41e128) on the 360-high canvas: the health
/// in the <c>SC_STAT</c> panel (bottom right, <c>SNIP_TXT</c> digits, blinking at 20 or less,
/// 0x420830), the inventory outside sniper mode (5 slots at the bottom left, 0x46cce4),
/// the health bar of the object Kurt shoots at (top left, 0x41e3c8), the red and white flashes,
/// the skull when he's dead, and the messages.
/// <code>
///   ┌─bar─────────────────────────┐
///   │          message            │
///   │ [i][i][i][i][i]   [health]  │
///   └─────────────────────────────┘
/// </code></summary>
public sealed class HudView
{
    /// <summary>The super chain gun's item number: its slot shows the ticks left, never selected.</summary>
    public const int SuperChainGun = 6;
    private const int BlinkMask = 31;
    private const int BlinkOn = 16;
    private const int LowHealth = 20;
    private const int MaxNumber = 999;
    private const int DigitWidth = 8;
    private const float PanelRight = 16f;
    private const float PanelBottom = 10f;
    /// <summary>The health bar: 500 pixels for 900 hit points, y 4 to 10, colour 3 framed by 4.</summary>
    private const float BarScale = 500f / 900f;
    private const int BarWidth = 500;
    private const float BarTop = 4f;
    private const float BarHeight = 7f;
    private const int BarFill = 3;
    private const int BarFrame = 4;
    // Inventory slots: 48 apart, icons anchored at (32, 328), the selection box at (8, 304), 47 wide.
    private const float SlotStep = 48f;
    private static readonly Vector2 IconAnchor = new(32f, 328f);
    private static readonly RectangleF SelectionBox = new(8f, 304f, 47f, 47f);
    private const float CountRise = 12f;
    private const float FlashMax = 255f;
    private const float HurtAlpha = 0.4f;
    private static readonly Vector4 SelectionFill = new(0f, 0f, 0f, 0.5f);
    private static readonly Vector4 SelectionFrame = new(0.75f, 0.75f, 0.75f, 1f);

    private sealed record Image(int Texture, Vector2 Size, int HotspotX, int HotspotY);

    private readonly Renderer _renderer;
    private readonly int _palette;
    private readonly Palette _colours;
    private readonly ModImages? _mods;
    private readonly Image _panel;
    private readonly Image _skull;
    private readonly Image _digits;
    private readonly List<Image> _icons = [];
    private readonly Vector4 _barFill;
    private readonly Vector4 _barFrame;
    private int _blink;

    public Messages Messages { get; }
    /// <summary>Sniper mode's screen, which replaces the HUD while it shows.</summary>
    public SniperOverlay Sniper { get; }
    private readonly BomberOverlay _bomber;

    /// <summary>The HUD; <paramref name="mods"/>' images replace its own (<see cref="CanvasImages"/>).</summary>
    public HudView(Renderer renderer, Bni sprites, Palette palette, Fti fti, ModImages? mods = null)
    {
        _renderer = renderer;
        _palette = renderer.CreatePalette(palette.Rgba);
        _colours = palette;
        _mods = mods;
        _panel = Load(Panel, sprites.GetImage(Panel), 0, 0);
        _skull = Load(Skull, sprites.GetImage(Skull), 0, 0);
        _digits = Load(Digits, sprites.GetImage(Digits), 0, 0);
        var pickups = sprites.GetAnimation(Pickups);
        for (var i = 0; i < pickups.FrameCount; i++)
        {
            var frame = pickups.GetFrame(i);
            _icons.Add(Load(CanvasImages.FrameName(Pickups, i), frame.Image, frame.HotspotX, frame.HotspotY));
        }

        _barFill = Colour(palette, BarFill);
        _barFrame = Colour(palette, BarFrame);

        // The fonts use the interface's colours (SYS_PAL, the first 64).
        var system = Palette.FromRgb(fti.GetBytes("SYS_PAL"));
        var big = new FontView(renderer, Font.Parse(fti.GetBytes(FontView.Big), FontSpace.Big), system, mods, FontView.Big);
        var small = new FontView(renderer, Font.Parse(fti.GetBytes(FontView.Small), FontSpace.Small), system, mods, FontView.Small);
        Messages = new Messages(fti, big, small);
        Sniper = new SniperOverlay(renderer, sprites, palette, _palette, mods);
        _bomber = new BomberOverlay(renderer, sprites, _palette, big, palette, mods);
    }

    /// <summary>Spaces of the fonts' missing characters.</summary>
    private static class FontSpace
    {
        public const int Big = 6;
        public const int Small = 4;
    }

    private Image Load(string name, Texture texture, int hotspotX, int hotspotY) =>
        new(CanvasImages.Create(_renderer, _mods, name, texture, _colours, _palette), new Vector2(texture.Width, texture.Height), hotspotX, hotspotY);

    /// <summary>The HUD's 2D images by their names in mods (<see cref="CanvasImages"/>; the exports).</summary>
    public static IEnumerable<(string Name, Texture Texture)> Images(Bni sprites)
    {
        foreach (var name in new[] { Panel, Skull, Digits })
        {
            yield return (name, sprites.GetImage(name));
        }

        var pickups = sprites.GetAnimation(Pickups);
        for (var i = 0; i < pickups.FrameCount; i++)
        {
            yield return (CanvasImages.FrameName(Pickups, i), pickups.GetFrame(i).Image);
        }
    }

    private const string Panel = "SC_STAT";
    private const string Skull = "SKULL";
    private const string Digits = "SNIP_TXT";
    private const string Pickups = "PICKUPS";

    private static Vector4 Colour(Palette palette, int index) =>
        new Vector4(palette.Rgba[index * 4], palette.Rgba[index * 4 + 1], palette.Rgba[index * 4 + 2], byte.MaxValue) / byte.MaxValue;

    /// <summary>Once a tick: the blink.</summary>
    public void Tick(HudState state)
    {
        _blink = (_blink + 1) & BlinkMask;
    }

    public void Draw(HudState state)
    {
        var width = _renderer.CanvasWidth;
        var height = Renderer.CanvasHeight;
        DrawFlashes(state, width, height);
        if (Sniper.Visible)
        {
            var blink = state.Health > LowHealth || _blink < BlinkOn ? Blink.Shown : Blink.Hidden;
            var panelAt = new Vector2(SniperScreen.ViewWidth - (_panel.Size.X + PanelRight), SniperScreen.ViewHeight - (_panel.Size.Y + PanelBottom));
            Sniper.Draw(width, state.Health, blink, panelAt, _panel.Size);
            Messages.Draw(width);
            return;
        }

        // The bomber's sight first (0x41e3c8).
        if (state.Bomber is { } sight)
        {
            _bomber.Draw(width, sight);
        }

        // Health panel at the bottom right; the number blinks when health is low.
        var panel = new Vector2(width - (_panel.Size.X + PanelRight), height - (_panel.Size.Y + PanelBottom));
        DrawImage(_panel, panel, Vector4.One);
        if (state.Health > LowHealth || _blink < BlinkOn)
        {
            var centre = panel + new Vector2((int)_panel.Size.X >> 1, ((int)_panel.Size.Y - (int)_digits.Size.Y) >> 1);
            DrawNumber(state.Health, centre);
        }

        // Always shown: 0x46cce4 resets its timer (0x574328) to 60 every frame outside sniper mode,
        // so the super chain gun's ticks count down on screen while Kurt fires.
        DrawInventory(state);

        DrawBar(state.BarHealth, state.BarMax);
        Messages.Draw(width);
    }

    /// <summary>Hits flash the screen red; once Kurt is dead the skull fades in instead. The nuke
    /// flashes it white.</summary>
    private void DrawFlashes(HudState state, float width, float height)
    {
        if (state.Dead)
        {
            var alpha = Math.Clamp(state.HurtFlash / FlashMax, 0f, 1f);
            DrawImage(_skull, new Vector2(width, height) / 2f - _skull.Size / 2f, new Vector4(1f, 1f, 1f, alpha));
        }
        else if (state.HurtFlash > 0f)
        {
            _renderer.FillRect(new RectangleF(0f, 0f, width, height), new Vector4(1f, 0f, 0f, state.HurtFlash / FlashMax * HurtAlpha));
        }

        if (state.WhiteFlash > 0f)
        {
            _renderer.FillRect(new RectangleF(0f, 0f, width, height), new Vector4(1f, 1f, 1f, MathF.Min(state.WhiteFlash / FlashMax, 1f)));
        }
    }

    private void DrawImage(Image image, Vector2 at, Vector4 tint) =>
        _renderer.DrawImage(image.Texture, _palette, image.Size, new RectangleF(0f, 0f, image.Size.X, image.Size.Y),
            new RectangleF(at.X, at.Y, image.Size.X, image.Size.Y), tint);

    /// <summary>A number (at most 999) centred on <paramref name="at"/>.X, 8-pixel digits (0x420bd0).</summary>
    private void DrawNumber(int value, Vector2 at)
    {
        var text = Math.Min(value, MaxNumber).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var x = at.X - DigitWidth / 2f * text.Length;
        foreach (var c in text)
        {
            var digit = c - '0';
            var source = new RectangleF(digit * DigitWidth, 0f, DigitWidth, _digits.Size.Y);
            _renderer.DrawImage(_digits.Texture, _palette, _digits.Size, source, new RectangleF(x, at.Y, DigitWidth, _digits.Size.Y), Vector4.One);
            x += DigitWidth;
        }
    }

    private void DrawInventory(HudState state)
    {
        if (state.Selected < state.Slots.Count && state.Slots[state.Selected].Item != SuperChainGun)
        {
            var box = SelectionBox;
            box.Offset(state.Selected * SlotStep, 0f);
            _renderer.FillRect(RectangleF.Inflate(box, -1f, -1f), SelectionFill);
            _renderer.FrameRect(box, 1f, SelectionFrame);
        }

        for (var i = 0; i < state.Slots.Count; i++)
        {
            var (item, count) = state.Slots[i];
            var frame = item - 1;
            if (frame < 0 || frame >= _icons.Count)
            {
                continue;
            }

            var icon = _icons[frame];
            var anchor = IconAnchor + new Vector2(i * SlotStep, 0f);
            DrawImage(icon, anchor - new Vector2(icon.HotspotX, icon.HotspotY), Vector4.One);
            var shown = item == SuperChainGun ? state.SuperChainGun : count;
            if (shown > 1)
            {
                DrawNumber(shown, anchor - new Vector2(0f, CountRise));
            }
        }
    }

    private void DrawBar(int health, int max)
    {
        if (max <= 0)
        {
            return;
        }

        var fill = Math.Clamp(MathF.Round(health * BarScale), 0f, BarWidth);
        var frame = Math.Clamp(MathF.Round(max * BarScale), 0f, BarWidth);
        _renderer.FillRect(new RectangleF(0f, BarTop, fill + 1f, BarHeight), _barFill);
        _renderer.FrameRect(new RectangleF(0f, BarTop, frame + 1f, BarHeight), 1f, _barFrame);
    }
}
