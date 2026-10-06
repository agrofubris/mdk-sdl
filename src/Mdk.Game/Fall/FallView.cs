using System.Drawing;
using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Formats;

namespace Mdk.Game.Fall;

/// <summary>Draws the fall (<see cref="FallSim"/>): a camera looking straight down with a focal
/// length of 250 pixels on the 360-high view (wider windows see more on the sides). The original's
/// 2D layers behind the models (the ground, the minecrawler, the haze; the intro's space, moon and
/// earth) are screen-aligned quads far below the camera, so the models stay in front of them.
/// <code>
///   camera ──►  models (Kurt, Bones, missiles, pickups, chutes, explosions), radar beam, trails
///      │
///      ▼ 6970  haze (white streaks, by the wind level)
///        6980  earth                   (intro)
///        6990  minecrawler │ moon      (fall │ intro)
///        7000  ground      │ space
/// </code>
/// The palette effects go over everything as canvas fills (white, then black); the red of death
/// changes the palette, as the original does.</summary>
public sealed class FallView
{
    private const float Focal = 250f;
    private const float ViewHeight = 360f;
    private const float ViewCentreY = ViewHeight / 2f;
    private const float Near = 0.5f;
    private const float Far = 8000f;
    private const float BackDepth = 7000f;
    private const float LayerStep = 10f;
    private const int BackQuads = 16;
    private const int QuadVertices = 6;
    /// <summary>Blend of each haze table towards white (0x490d1c, / 256), tables 1-24.</summary>
    private static readonly int[] HazeBlend = [0, 0, 0, 0, 0, 0, 0, 0, 3, 6, 12, 18, 24, 48, 72, 96, 120, 144, 168, 192, 215, 230, 245, 255];
    private const int HazeLevels = 8;
    private const float BlendUnit = 256f;
    // The radar: rings 1-4 of 6 points, translucent green by these / 256 (approximated colours).
    private static readonly int[] RadarAlpha = [48, 64, 80, 96, 128];
    private const int RadarRings = 4;
    private const int RadarSides = 6;
    private const float RadarBaseFactor = -4f;
    private const float RadarFirstAngle = 60f;
    private const int RadarVertices = 138;
    private static readonly Vector4 TrailColour = new(0.8f, 0.8f, 0.8f, 0.35f);
    private const float TrailWidth = 0.5f;
    private const float TrailWiden = 1.5f;
    private const int TrailMissiles = 24;
    // The intro as f goes 0 to 1 (0x41106c): space (600 x 360) behind, the moon (128²) centred at
    // (300, 270 − 90 f) scaled (64 + 256 f) / 256, the earth (512²) centred at (300, 488 − 224 f)
    // scaled (300 + 128 f) / 256 by (100 + 42 f) / 256: a flattened disc rising from below.
    private static readonly Vector2 ViewSize = new(600f, ViewHeight);
    private const float SpriteUnit = 256f;
    private static readonly Vector2 MoonStart = new(300f, 270f);
    private const float MoonRise = 90f;
    private static readonly Vector2 MoonScale = new(64f, 256f);
    private static readonly Vector2 EarthStart = new(300f, 488f);
    private const float EarthRise = 224f;
    private static readonly Vector2 EarthScaleX = new(300f, 128f);
    private static readonly Vector2 EarthScaleY = new(100f, 42f);
    /// <summary>Kurt lies face down (+0x4c yaw 90°, +0x13c pitch −90°).</summary>
    private static readonly Matrix4x4 KurtTurn = Matrix4x4.CreateRotationY(MathF.PI / 2f) * Matrix4x4.CreateRotationZ(MathF.PI / 2f);
    private const float ChuteAbove = 2f;
    /// <summary>An explosion grows to twice its size over its frames.</summary>
    private const float ExplosionGrowth = 2f;
    private const float ExplosionMinScale = 0.01f;

    private readonly Renderer _renderer;
    private readonly int _index;
    private readonly byte[] _rgba;
    private readonly int _palette;
    private readonly int _spacePalette;
    private readonly int _whitePalette;
    private readonly FallGround _ground;
    private readonly int _groundTexture;
    private readonly List<(int Texture, Vector2 Size)> _crawler = [];
    private readonly (int Texture, Vector2 Size) _space;
    private readonly (int Texture, Vector2 Size) _moon;
    private readonly (int Texture, Vector2 Size) _earth;
    private readonly List<int> _haze = [];
    private readonly int _backMesh;
    private readonly int _radarMesh;
    private readonly int _trailMesh;
    private readonly List<Vertex> _back = [];
    private readonly List<(int First, Material Material, int Frame)> _backDraws = [];
    private float _red;

    public FallModels Models { get; }
    public FallHud Hud { get; }

    public FallView(Renderer renderer, Bni bni, TextureArchive mti, int index, Fti fti)
    {
        _renderer = renderer;
        _index = index;
        var n = index + 1;
        var palette = PaletteOf(bni, $"FALLP{n}");
        _rgba = palette.Rgba;
        _palette = renderer.CreatePalette(palette.Rgba);
        _spacePalette = renderer.CreatePalette(PaletteOf(bni, "SPACEPAL").Rgba);
        var white = new byte[Palette.Size * 4];
        Array.Fill(white, byte.MaxValue);
        _whitePalette = renderer.CreatePalette(white);
        Models = new FallModels(renderer, bni, mti, palette, _palette);
        Hud = new FallHud(renderer, bni, _palette, fti);

        _ground = new FallGround(mti.Textures[$"LEVEL{n}"], mti.Textures[$"POD{n}"]);
        _groundTexture = renderer.CreateIndexTexture(FallGround.Size, FallGround.Size, _ground.Indices);
        for (var i = 1; i <= FallSim.CrawlerFrames; i++)
        {
            if (mti.Textures.TryGetValue($"L{n}_C{i:0000}", out var frame))
            {
                _crawler.Add(Image(frame));
            }
        }

        _space = Image(bni.GetImage("SPACE"));
        _moon = Image(bni.GetImage("MOON"));
        _earth = Image(bni.GetImage("EARTH"));
        for (var i = 0; i < FallSim.HazeFrames; i++)
        {
            _haze.Add(HazeTexture(Fall3d.Haze(bni, $"ZOOM{i:0000}")));
        }

        _backMesh = renderer.CreateDynamicMesh(BackQuads * QuadVertices);
        _radarMesh = renderer.CreateDynamicMesh(RadarVertices);
        _trailMesh = renderer.CreateDynamicMesh(TrailMissiles * FallMissile.TrailPoints * QuadVertices);
    }

    /// <summary>A palette entry (768 RGB bytes); colour 0 is forced to black.</summary>
    private static Palette PaletteOf(Bni bni, string name) =>
        Palette.FromRgb(bni.Bytes.AsSpan(bni.Entries[name].Offset, Palette.Size * 3));

    private (int, Vector2) Image(Texture texture) =>
        (_renderer.CreateIndexTexture(texture.Width, texture.Height, texture.Indices), new Vector2(texture.Width, texture.Height));

    /// <summary>A haze frame as 8 stacked masks, one per blend offset b (1-8): index 1 where the
    /// pixel's offset is b, so each level draws with its own alpha.</summary>
    private int HazeTexture(byte[] haze)
    {
        var size = Fall3d.HazeWidth * Fall3d.HazeRows;
        var masks = new byte[size * HazeLevels];
        for (var i = 0; i < size; i++)
        {
            var b = haze[i];
            if (b is > 0 and <= HazeLevels)
            {
                masks[(b - 1) * size + i] = 1;
            }
        }

        return _renderer.CreateIndexTexture(Fall3d.HazeWidth, Fall3d.HazeRows * HazeLevels, masks);
    }

    /// <summary>Queues the frame's 3D scene and returns its camera.</summary>
    public View Draw(FallSim sim)
    {
        SetRed(sim.Red);
        var camera = sim.CameraPosition;
        var width = _renderer.CanvasWidth;
        _back.Clear();
        _backDraws.Clear();
        if (sim.InIntro)
        {
            QueueIntro(sim, camera, width);
        }
        else
        {
            QueueGround(sim, camera, width);
        }

        _renderer.UpdateMesh(_backMesh, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_back));
        foreach (var (first, material, frame) in _backDraws)
        {
            _renderer.Draw(_backMesh, first, QuadVertices, material, frame);
        }

        QueueModels(sim);
        QueueRadar(sim);
        QueueTrails(sim);

        var view = Matrix4x4.CreateLookAt(camera, camera - Vector3.UnitZ, Vector3.UnitY);
        var fieldOfView = 2f * MathF.Atan(ViewCentreY / Focal);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(fieldOfView, _renderer.AspectRatio, Near, Far);
        return new View(view * projection, Matrix4x4.Identity, camera);
    }

    /// <summary>The palette effects over everything: towards white, then darkened.</summary>
    public void DrawEffects(FallSim sim)
    {
        var canvas = new RectangleF(0f, 0f, _renderer.CanvasWidth, Renderer.CanvasHeight);
        if (sim.Whiten < 1f)
        {
            _renderer.FillRect(canvas, new Vector4(1f, 1f, 1f, 1f - sim.Whiten));
        }

        if (sim.Dark < 1f)
        {
            _renderer.FillRect(canvas, new Vector4(0f, 0f, 0f, 1f - sim.Dark));
        }
    }

    /// <summary>The red of death: added to the palette's red.</summary>
    private void SetRed(float red)
    {
        if (red == _red)
        {
            return;
        }

        _red = red;
        var rgba = (byte[])_rgba.Clone();
        for (var i = 0; i < Palette.Size; i++)
        {
            rgba[i * 4] = (byte)Math.Min(rgba[i * 4] + red * byte.MaxValue, byte.MaxValue);
        }

        _renderer.UpdateTexture(_palette, Palette.Size, 1, rgba);
    }

    /// <summary>Space as the background, the moon and the flattened earth rising and growing.</summary>
    private void QueueIntro(FallSim sim, Vector3 camera, float width)
    {
        var f = 1f - sim.IntroLeft / (float)FallSim.IntroTicks;
        var origin = new Vector2((width - ViewSize.X) / 2f, 0f);
        Quad(camera, width, new RectangleF(origin.X, origin.Y, ViewSize.X, ViewSize.Y), BackDepth, Plain(_space.Texture, _spacePalette));
        float Grow(Vector2 scale) => (scale.X + scale.Y * f) / SpriteUnit;
        var moon = origin + MoonStart - new Vector2(0f, MoonRise * f);
        Sprite(camera, width, _moon, moon, new Vector2(Grow(MoonScale)), BackDepth - LayerStep, _spacePalette);
        var earth = origin + EarthStart - new Vector2(0f, EarthRise * f);
        Sprite(camera, width, _earth, earth, new Vector2(Grow(EarthScaleX), Grow(EarthScaleY)), BackDepth - 2f * LayerStep, _spacePalette);
    }

    /// <summary>The ground with the track, the minecrawler on it and the haze over both.</summary>
    private void QueueGround(FallSim sim, Vector3 camera, float width)
    {
        var centreRow = FallGround.CentreRow(sim.Time);
        if (_ground.UpdateTrack(centreRow))
        {
            _renderer.UpdateTexture(_groundTexture, FallGround.Size, FallGround.Size, _ground.Indices);
        }

        var (screen, texels) = FallGround.Visible(camera, centreRow, width);
        if (screen.Width > 0f)
        {
            var uv = new RectangleF(texels.X / FallGround.Size, texels.Y / FallGround.Size, texels.Width / FallGround.Size, texels.Height / FallGround.Size);
            Quad(camera, width, screen, BackDepth, Plain(_groundTexture, _palette), uv);
        }

        if (_crawler.Count != 0)
        {
            var (centre, scale) = FallGround.Crawler(camera, width);
            Sprite(camera, width, _crawler[(int)sim.CrawlerFrame % _crawler.Count], centre, new Vector2(scale), BackDepth - LayerStep, _palette);
        }

        // Each blend offset b uses table (wind level + b): white by its alpha.
        var level = sim.Wind >> FallSim.WindShift;
        for (var b = 1; b <= HazeLevels; b++)
        {
            var alpha = HazeBlend[Math.Clamp(level + b, 1, HazeBlend.Length) - 1] / BlendUnit;
            if (alpha <= 0f)
            {
                continue;
            }

            var material = new Material(_haze[sim.HazeFrame], _whitePalette, new Vector4(1f, 1f, 1f, alpha), HazeLevels, Pass.Blended);
            Quad(camera, width, new RectangleF(0f, 0f, width, ViewHeight), BackDepth - 3f * LayerStep, material, frame: b - 1);
        }
    }

    private static Material Plain(int texture, int palette) => new(texture, palette, Vector4.One, 1, Pass.DoubleSided);

    /// <summary>An image centred on <paramref name="centre"/> (view pixels), scaled.</summary>
    private void Sprite(Vector3 camera, float width, (int Texture, Vector2 Size) image, Vector2 centre, Vector2 scale, float depth, int palette)
    {
        var size = image.Size * scale;
        Quad(camera, width, new RectangleF(centre.X - size.X / 2f, centre.Y - size.Y / 2f, size.X, size.Y), depth, Plain(image.Texture, palette));
    }

    /// <summary>A quad covering <paramref name="screen"/> (view pixels) at <paramref name="depth"/>
    /// below the camera.</summary>
    private void Quad(Vector3 camera, float width, RectangleF screen, float depth, Material material, RectangleF? uv = null, int frame = 0)
    {
        if (_back.Count + QuadVertices > BackQuads * QuadVertices)
        {
            return;
        }

        var t = uv ?? new RectangleF(0f, 0f, 1f, 1f);
        Vertex Corner(float x, float y, float u, float v) => new(
            new Vector3(camera.X + (x - width / 2f) * depth / Focal, camera.Y - (y - ViewCentreY) * depth / Focal, camera.Z - depth),
            new Vector2(u, v));
        var first = _back.Count;
        _back.Add(Corner(screen.Left, screen.Top, t.Left, t.Top));
        _back.Add(Corner(screen.Right, screen.Top, t.Right, t.Top));
        _back.Add(Corner(screen.Right, screen.Bottom, t.Right, t.Bottom));
        _back.Add(Corner(screen.Left, screen.Top, t.Left, t.Top));
        _back.Add(Corner(screen.Right, screen.Bottom, t.Right, t.Bottom));
        _back.Add(Corner(screen.Left, screen.Bottom, t.Left, t.Bottom));
        _backDraws.Add((first, material, frame));
    }

    private void QueueModels(FallSim sim)
    {
        if (sim.KurtVisible)
        {
            var animation = sim.KurtPose == FallSim.KurtAnimation.Hit ? "KURT_HIT" : "KURTANIM";
            Models.Draw("KURT", KurtTurn * Matrix4x4.CreateTranslation(sim.KurtPosition), animation, (int)sim.KurtFrame);
        }

        if (sim.BonesPosition is { } bones)
        {
            Models.Draw("BONES", KurtTurn * Matrix4x4.CreateTranslation(bones), "BONESANM", (int)sim.BonesFrame);
        }

        foreach (var missile in sim.Missiles)
        {
            Models.Draw("MISSILE", Orient(missile.Velocity) * Matrix4x4.CreateTranslation(missile.Position));
        }

        foreach (var pickup in sim.Pickups)
        {
            var turn = Matrix4x4.CreateRotationZ(float.DegreesToRadians(pickup.Yaw));
            Models.Draw(pickup.Name, turn * Matrix4x4.CreateTranslation(pickup.Position));
            if (pickup.Chute)
            {
                Models.Draw("CHUTE", turn * Matrix4x4.CreateTranslation(pickup.Position + new Vector3(0f, 0f, ChuteAbove)));
            }
        }

        // Explosions grow and play their texture's frames, over everything (sorted at camera z + 5).
        foreach (var explosion in sim.Explosions)
        {
            var frame = (int)explosion.Ticks;
            var scale = MathF.Max(ExplosionGrowth * frame / FallSim.ExplosionTicks, ExplosionMinScale);
            var world = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateRotationZ(explosion.Yaw) * Matrix4x4.CreateTranslation(explosion.Position);
            Models.Draw("EXPLODE", world, textureFrame: frame, pass: Pass.OnTop);
        }
    }

    /// <summary>Points the model's +y along a direction, z up (0x4123e8).</summary>
    private static Matrix4x4 Orient(Vector3 direction)
    {
        var y = Vector3.Normalize(direction);
        var x = Vector3.Cross(y, Vector3.UnitZ);
        x = x.LengthSquared() < 1e-12f ? Vector3.UnitX : Vector3.Normalize(x);
        var z = Vector3.Cross(x, y);
        return new Matrix4x4(x.X, x.Y, x.Z, 0f, y.X, y.Y, y.Z, 0f, z.X, z.Y, z.Z, 0f, 0f, 0f, 0f, 1f);
    }

    /// <summary>The radar's beam (0x412f94): a fan from its base to ring 1, rings 1-4 at
    /// <c>1 − 2^−k</c> of the way to the spot with radius <c>r (1 − 2^−k)</c>, and a cap on ring 4.
    /// <code>
    ///   base ◄── fan ── ring 1 ══ ring 2 ══ ring 3 ══ ring 4 (cap) ── spot
    /// </code></summary>
    private void QueueRadar(FallSim sim)
    {
        if (!sim.RadarActive)
        {
            return;
        }

        var camera = sim.CameraPosition;
        var spot = sim.RadarSpot;
        var basePoint = new Vector3(RadarBaseFactor * camera.X, RadarBaseFactor * camera.Y, 0f);
        var rings = new Vector3[RadarRings][];
        for (var k = 0; k < RadarRings; k++)
        {
            var f = 1f - MathF.Pow(2f, -(k + 1));
            var centre = Vector3.Lerp(basePoint, spot, f);
            rings[k] = new Vector3[RadarSides];
            for (var m = 0; m < RadarSides; m++)
            {
                var angle = float.DegreesToRadians(RadarFirstAngle * (m + 1));
                rings[k][m] = centre + new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0f) * sim.RadarSpotRadius * f;
            }
        }

        var vertices = new List<Vertex>(RadarVertices);
        void Add(params Vector3[] points) => vertices.AddRange(points.Select(p => new Vertex(p, Vector2.Zero)));
        var bands = new List<int> { 0 };
        for (var m = 0; m < RadarSides; m++)
        {
            Add(basePoint, rings[0][m], rings[0][(m + 1) % RadarSides]);
        }

        for (var k = 0; k < RadarRings - 1; k++)
        {
            bands.Add(vertices.Count);
            for (var m = 0; m < RadarSides; m++)
            {
                var (a, b) = (rings[k][m], rings[k][(m + 1) % RadarSides]);
                var (c, d) = (rings[k + 1][m], rings[k + 1][(m + 1) % RadarSides]);
                Add(a, b, c, b, d, c);
            }
        }

        bands.Add(vertices.Count);
        for (var m = 1; m < RadarSides - 1; m++)
        {
            Add(rings[^1][0], rings[^1][m], rings[^1][m + 1]);
        }

        bands.Add(vertices.Count);
        _renderer.UpdateMesh(_radarMesh, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(vertices));
        for (var c = 0; c < RadarAlpha.Length; c++)
        {
            var colour = Material.Flat(new Vector4(0f, 1f, 0f, RadarAlpha[c] / BlendUnit), Pass.Blended);
            _renderer.Draw(_radarMesh, bands[c], bands[c + 1] - bands[c], colour);
        }
    }

    /// <summary>The smoke trails (0x439454, approximated): a band along the last 32 points of each
    /// missile, narrowing towards its tail.</summary>
    private void QueueTrails(FallSim sim)
    {
        var vertices = new List<Vertex>();
        foreach (var missile in sim.Missiles.Take(TrailMissiles))
        {
            var trail = missile.Trail;
            var count = trail.Count;
            for (var i = 0; i < count - 1; i++)
            {
                var (a, b) = (trail[i], trail[i + 1]);
                var side = new Vector3(b.Y - a.Y, a.X - b.X, 0f);
                side = side == Vector3.Zero ? side : Vector3.Normalize(side);
                var wa = TrailWidth + TrailWiden * (count - i) / FallMissile.TrailPoints;
                var wb = TrailWidth + TrailWiden * (count - i - 1) / FallMissile.TrailPoints;
                Vector3[] quad = [a - side * wa, a + side * wa, b + side * wb, b - side * wb];
                foreach (var k in (int[])[0, 1, 2, 0, 2, 3])
                {
                    vertices.Add(new Vertex(quad[k], Vector2.Zero));
                }
            }
        }

        if (vertices.Count == 0)
        {
            return;
        }

        _renderer.UpdateMesh(_trailMesh, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(vertices));
        _renderer.Draw(_trailMesh, 0, vertices.Count, Material.Flat(TrailColour, Pass.Blended));
    }
}
