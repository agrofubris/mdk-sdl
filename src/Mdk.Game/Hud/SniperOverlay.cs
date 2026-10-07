using System.Drawing;
using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Formats;
using Mdk.Game.Scripts;

namespace Mdk.Game.Hud;

/// <summary>Whether Bones' air strike has a target in the crosshair (0x4641ac), checked only with
/// the strike selected and the clip ready.</summary>
public enum StrikeAim { NotChecked, None, Target }

/// <summary>Whether the health's digits show (they blink when it's low).</summary>
public enum Blink { Shown, Hidden }

/// <summary>What the sniper screen shows this frame.</summary>
public readonly record struct SniperHud(
    float Zoom,
    IReadOnlyList<int> Ammo,
    int SelectedAmmo,
    IReadOnlyList<SniperRounds.RoundCamera> Cameras,
    StrikeAim Aim);

/// <summary>Sniper mode's screen (HUD 0x41e128, 0x420830; TRAVSPRT.BNI; a port of godot-mdk's
/// sniper_overlay.gd), drawn over the whole 640x480 screen (scaled to the 360-high canvas, centred,
/// black beside it): the SNIPERS1 frame around the 600x360 view at (20, 60), the round cameras'
/// fills, the air strike's iris, the SNIPERS2 mask, the CROSS crosshair, the zoom in percent with its
/// SNIP_RNG gauge, the ammo types and the health. The round cameras' and the loaded rounds' 3D views
/// are drawn by the game as renderer insets (<see cref="RoundViews"/>, <see cref="View"/>).
/// <code>
///   ┌────────── SNIPERS1 ───────────┐
///   │ [cam] [cam] [cam]             │   view (20, 60) 600x360
///   │      ┌─ scope ─┐   zoom %     │
///   │      │    +    │   gauge      │
///   │ ammo └─────────┘     health   │
///   └───────────────────────────────┘
/// </code></summary>
public sealed class SniperOverlay
{
    public const float ScreenWidth = SniperScreen.ScreenWidth;
    public const float ScreenHeight = SniperScreen.ScreenHeight;
    /// <summary>Where the 600x360 view sits on the screen.</summary>
    public static readonly Vector2 View = new(20f, 60f);
    /// <summary>The round cameras' windows in the view (0x461d80).</summary>
    public static readonly RectangleF[] RoundViews = [new(72f, 10f, 140f, 70f), new(228f, 0f, 140f, 70f), new(384f, 10f, 140f, 70f)];

    private static readonly Vector2 Crosshair = new(299f, 219f);
    private static readonly Vector2 ZoomDigits = new(564f, 155f);
    private static readonly Vector2 Gauge = new(552f, 176f);
    private const int GaugeHeight = 88;
    /// <summary>The gauge's shown height follows the zoom by 3 pixels per frame.</summary>
    private const int GaugeSpeed = 3;
    /// <summary>The zoom in percent: (1 - zoom)² x 1.05194 (59% at 4x).</summary>
    private const float ZoomPercentFactor = 1.05194f;
    private const float Percent = 100f;
    private static readonly Vector2 Weapon = new(112f, 304f);
    private static readonly Vector2[] TypeIcons = [new(0, 256), new(0, 280), new(0, 300), new(4, 320), new(16, 336), new(32, 344)];
    private static readonly Vector2[] TypeLabels = [new(12, 268), new(12, 288), new(12, 308), new(20, 320), new(24, 328), new(36, 336)];
    private static readonly Vector2 Count = new(64f, 315f);
    private const int Types = 6;

    /// <summary>Round camera fills: after a hit, after a kill or an explosion; a miss shows SNIPERGA,
    /// a frame every 2 ticks.</summary>
    private const int HitFill = 0x3c;
    private const int KillFill = 0xf4;
    private const int EmptyFill = 0;
    private const float MissFrameTicks = 2f;

    // The air strike's iris (0x41ef90): with a target the scope reddens from its edge in 1 s, leaving
    // a hole of radius 384 x c² (c from 1 to 0); closed, a darker ring contracts every second. View
    // rows 80-359, x 108-492, centred on (300, 220).
    private const int StrikeType = 5;
    private static readonly Vector2 IrisCentre = new(300f, 220f);
    private const int IrisTop = 80;
    private const int IrisBottom = 360;
    private const float IrisHalfWidth = 192f;
    private const float IrisRadius = 384f;
    private const float RingOuter = 392f;
    private const float RingInner = 376f;
    private static readonly Vector4 IrisColour = new(200f / 255f, 0f, 0f, 0x60 / 255f);
    private static readonly Vector4 RingColour = new(200f / 255f, 0f, 0f, 0xC4 / 255f);

    private const int DigitWidth = 8;
    private const int MaxNumber = 999;
    private static readonly Vector4 Black = new(0f, 0f, 0f, 1f);

    private sealed record Image(int Texture, Vector2 Size, Vector2 Hotspot);

    private readonly Renderer _renderer;
    private readonly int _palette;
    private readonly Palette _colours;
    private readonly Image _frame;
    private readonly Image _mask;
    private readonly Image _cross;
    private readonly Image _gauge;
    private readonly Image _weapon;
    private readonly Image _digits;
    private readonly List<Image> _icons = [];
    private readonly List<Image> _labels = [];
    private readonly List<Image> _miss = [];

    private SniperHud? _shown;
    private int _gaugeShown;
    // The iris: its opening (1 open, 0 closed), the pulse, and whether the strike has a target.
    private float _iris = 1f;
    private float _pulse = 1f;
    private bool _target;

    public SniperOverlay(Renderer renderer, Bni sprites, Palette palette, int paletteTexture)
    {
        _renderer = renderer;
        _colours = palette;
        _palette = paletteTexture;
        _frame = Load(CutView(SniperScreen.Frame(sprites)), Vector2.Zero);
        _mask = Load(SniperScreen.Mask(sprites), Vector2.Zero);
        var cross = sprites.GetAnimation("CROSS").GetFrame(0);
        _cross = Load(cross.Image, new Vector2(cross.HotspotX, cross.HotspotY));
        _gauge = Load(sprites.GetImage("SNIP_RNG"), Vector2.Zero);
        _weapon = Load(sprites.GetImage("SNIP_WEP"), Vector2.Zero);
        _digits = Load(sprites.GetImage("SNIP_TXT"), Vector2.Zero);
        for (var i = 1; i <= Types; i++)
        {
            _icons.Add(Load(sprites.GetImage($"SNIP_W{i}"), Vector2.Zero));
            _labels.Add(Load(sprites.GetImage($"SNIP_L{i}"), Vector2.Zero));
        }

        var miss = sprites.GetAnimation("SNIPERGA");
        for (var i = 0; i < miss.FrameCount; i++)
        {
            _miss.Add(Load(miss.GetFrame(i).Image, Vector2.Zero));
        }
    }

    /// <summary>Whether sniper mode's screen replaces the HUD.</summary>
    public bool Visible => _shown != null;

    private Image Load(Texture texture, Vector2 hotspot) =>
        new(_renderer.CreateIndexTexture(texture.Width, texture.Height, texture.Indices), new Vector2(texture.Width, texture.Height), hotspot);

    /// <summary>The frame without the view's rectangle.</summary>
    private static Texture CutView(Texture frame)
    {
        var indices = (byte[])frame.Indices.Clone();
        for (var y = (int)View.Y; y < View.Y + SniperScreen.ViewHeight; y++)
        {
            Array.Clear(indices, y * frame.Width + (int)View.X, SniperScreen.ViewWidth);
        }

        return new Texture { Name = frame.Name, Width = frame.Width, Height = frame.Height, Indices = indices };
    }

    /// <summary>Each frame: what to show (null out of sniper mode), the iris and the gauge.</summary>
    public void Update(float delta, SniperHud? hud)
    {
        _shown = hud;
        if (hud is not { } shown)
        {
            _iris = 1f;
            _pulse = 1f;
            return;
        }

        UpdateIris(delta, shown);
        var goal = (int)MathF.Round(GaugeHeight * ZoomFraction(shown.Zoom));
        _gaugeShown = Math.Clamp(goal, _gaugeShown - GaugeSpeed, _gaugeShown + GaugeSpeed);
    }

    private static float ZoomFraction(float zoom) => Math.Clamp((1f - zoom) * (1f - zoom) * ZoomPercentFactor, 0f, 1f);

    /// <summary>The iris closes over 1 s while the strike is selected and has a target, and opens
    /// again otherwise; closed, it pulses once a second.</summary>
    private void UpdateIris(float delta, SniperHud hud)
    {
        var strike = hud.SelectedAmmo == StrikeType;
        if (strike && hud.Aim != StrikeAim.NotChecked)
        {
            _target = hud.Aim == StrikeAim.Target;
            if (_target)
            {
                _iris = MathF.Max(_iris - delta, 0f);
            }
        }

        if (!strike || !_target)
        {
            _iris += delta;
            if (_iris >= 1f)
            {
                _iris = 1f;
                _pulse = 1f;
                return;
            }
        }

        if (_iris == 0f || _pulse != 1f)
        {
            _pulse -= delta;
            if (_pulse < 0f)
            {
                _pulse = 1f;
            }
        }
    }

    /// <summary>The screen's scale and left edge on the canvas.</summary>
    public static (float Scale, float Left) Placement(float canvasWidth)
    {
        var scale = Renderer.CanvasHeight / ScreenHeight;
        return (scale, (canvasWidth - ScreenWidth * scale) / 2f);
    }

    /// <summary>Draws the sniper screen, and the health centred in the panel at <paramref name="panel"/>
    /// (pixels of the view) of <paramref name="panelSize"/>.</summary>
    public void Draw(float canvasWidth, int health, Blink blink, Vector2 panel, Vector2 panelSize)
    {
        if (_shown is not { } hud)
        {
            return;
        }

        var (scale, left) = Placement(canvasWidth);
        if (left > 0f)
        {
            _renderer.FillRect(new RectangleF(0f, 0f, MathF.Ceiling(left), Renderer.CanvasHeight), Black);
            _renderer.FillRect(new RectangleF(MathF.Floor(canvasWidth - left), 0f, MathF.Ceiling(left), Renderer.CanvasHeight), Black);
        }

        var screen = new Screen(_renderer, _palette, scale, left);
        DrawFrame(screen);
        DrawCameras(screen, hud);
        DrawIris(screen);
        screen.Image(_mask, View);
        screen.Image(_cross, View + Crosshair - _cross.Hotspot);

        var fraction = ZoomFraction(hud.Zoom);
        DrawNumber(screen, (int)MathF.Round(Percent * fraction), View + ZoomDigits);
        if (_gaugeShown > 0)
        {
            var top = GaugeHeight - _gaugeShown;
            screen.Part(_gauge, new RectangleF(0f, top, _gauge.Size.X, _gaugeShown), View + Gauge + new Vector2(0f, top));
        }

        DrawAmmo(screen, hud);

        // Health in the frame's panel, where the normal view draws SC_STAT.
        if (blink == Blink.Shown)
        {
            DrawNumber(screen, health, View + panel + new Vector2((int)panelSize.X >> 1, ((int)panelSize.Y - (int)_digits.Size.Y) >> 1));
        }
    }

    /// <summary>Black behind the frame (index 0 is transparent), then the frame.</summary>
    private void DrawFrame(Screen screen)
    {
        var viewBottom = View.Y + SniperScreen.ViewHeight;
        screen.Fill(new RectangleF(0f, 0f, ScreenWidth, View.Y), Black);
        screen.Fill(new RectangleF(0f, viewBottom, ScreenWidth, ScreenHeight - viewBottom), Black);
        screen.Fill(new RectangleF(0f, View.Y, View.X, SniperScreen.ViewHeight), Black);
        screen.Fill(new RectangleF(View.X + SniperScreen.ViewWidth, View.Y, ScreenWidth - View.X - SniperScreen.ViewWidth, SniperScreen.ViewHeight), Black);
        screen.Image(_frame, Vector2.Zero);
    }

    /// <summary>Round cameras that don't watch a round: a colour, or the miss animation.</summary>
    private void DrawCameras(Screen screen, SniperHud hud)
    {
        for (var i = 0; i < RoundViews.Length && i < hud.Cameras.Count; i++)
        {
            var camera = hud.Cameras[i];
            var rect = RoundViews[i];
            rect.Offset(View.X, View.Y);
            switch (camera.Shot)
            {
                case SniperRounds.Shot.Watching:
                    continue;
                case SniperRounds.Shot.Miss:
                    screen.Fill(rect, Black);
                    var frame = _miss[(int)(camera.Time / MissFrameTicks) % _miss.Count];
                    screen.Image(frame, new Vector2(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f) - frame.Size / 2f);
                    continue;
            }

            var fill = camera.Shot switch
            {
                SniperRounds.Shot.Hit => HitFill,
                SniperRounds.Shot.Kill => KillFill,
                _ => EmptyFill,
            };
            screen.Fill(rect, Colour(fill));
        }
    }

    private Vector4 Colour(int index) =>
        new Vector4(_colours.Rgba[index * 4], _colours.Rgba[index * 4 + 1], _colours.Rgba[index * 4 + 2], byte.MaxValue) / byte.MaxValue;

    /// <summary>The iris, row by row from the scope's edges inwards: red, the darker pulse ring, red
    /// again, then the clear hole.</summary>
    private void DrawIris(Screen screen)
    {
        if (_iris >= 1f)
        {
            return;
        }

        var circles = new List<(float Radius, Vector4 Colour)>();
        var hole = IrisRadius * _iris * _iris;
        var outer = RingOuter * _pulse * _pulse;
        var inner = RingInner * _pulse * _pulse;
        if (outer > hole && _pulse != 1f)
        {
            circles.Add((outer, RingColour));
            if (inner > hole)
            {
                circles.Add((inner, IrisColour));
            }
        }

        circles.Add((hole, Vector4.Zero));
        for (var y = IrisTop; y < IrisBottom; y++)
        {
            var dy = y - IrisCentre.Y;
            var colour = IrisColour;
            var outside = IrisHalfWidth;
            foreach (var (r, next) in circles)
            {
                var edge = MathF.Min(MathF.Abs(dy) < r ? MathF.Sqrt(r * r - dy * dy) : 0f, outside);
                DrawSpan(screen, y, edge, outside, colour);
                outside = edge;
                colour = next;
            }

            DrawSpan(screen, y, 0f, outside, colour);
        }
    }

    /// <summary>A row of the iris between two distances from its centre, on both sides.</summary>
    private static void DrawSpan(Screen screen, int y, float near, float far, Vector4 colour)
    {
        if (colour.W == 0f || far <= near)
        {
            return;
        }

        var width = far - near;
        screen.Fill(new RectangleF(View.X + IrisCentre.X - far, View.Y + y, width, 1f), colour);
        screen.Fill(new RectangleF(View.X + IrisCentre.X + near, View.Y + y, width, 1f), colour);
    }

    /// <summary>SNIP_WEP, the icons of the types with rounds, the selected type's label and count.</summary>
    private void DrawAmmo(Screen screen, SniperHud hud)
    {
        screen.Image(_weapon, View + Weapon);
        for (var i = 0; i < Types; i++)
        {
            if (i == 0 || hud.Ammo[i - 1] > 0)
            {
                screen.Image(_icons[i], View + TypeIcons[i]);
            }
        }

        var selected = hud.SelectedAmmo;
        screen.Image(_labels[selected], View + TypeLabels[selected]);
        if (selected > 0)
        {
            DrawNumber(screen, hud.Ammo[selected - 1], View + Count);
        }
    }

    /// <summary>A number (at most 999) centred on <paramref name="at"/>.X, 8-pixel digits.</summary>
    private void DrawNumber(Screen screen, int value, Vector2 at)
    {
        var text = Math.Clamp(value, 0, MaxNumber).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var x = at.X - DigitWidth / 2f * text.Length;
        foreach (var c in text)
        {
            var source = new RectangleF((c - '0') * DigitWidth, 0f, DigitWidth, _digits.Size.Y);
            screen.Part(_digits, source, new Vector2(x, at.Y));
            x += DigitWidth;
        }
    }

    /// <summary>Draws in 640x480 screen pixels on the canvas.</summary>
    private readonly record struct Screen(Renderer Renderer, int Palette, float Scale, float Left)
    {
        private RectangleF ToCanvas(RectangleF r) => new(Left + r.X * Scale, r.Y * Scale, r.Width * Scale, r.Height * Scale);

        public void Fill(RectangleF r, Vector4 colour) => Renderer.FillRect(ToCanvas(r), colour);

        public void Image(Image image, Vector2 at) => Part(image, new RectangleF(0f, 0f, image.Size.X, image.Size.Y), at);

        public void Part(Image image, RectangleF source, Vector2 at) =>
            Renderer.DrawImage(image.Texture, Palette, image.Size, source, ToCanvas(new RectangleF(at.X, at.Y, source.Width, source.Height)), Vector4.One);
    }
}
