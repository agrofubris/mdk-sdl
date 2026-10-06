using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using Mdk.Engine.Render;
using Mdk.Formats;
using Mdk.Game.Kurt;
using Mdk.Game.Level;

namespace Mdk.Game.Stream;

/// <summary>Draws the stream (0x436b00, 0x438bfc): the scrolling background far behind, the models,
/// the translucent tube far to near, the lights and the planet, the health box and the fade.
/// <code>
///   background quad (far) ─► Kurt, Bones, bonus / Gunter ─► tube (blended, far to near) ─► lights
///   ─► canvas: health box, fade (red added, white, black)
/// </code>
/// The tube's vertices carry their RGBA (ramp colour, level alpha), Gouraud-blended as the
/// original's direct-colour triangles.</summary>
public sealed class StreamView
{
    private const float ViewHeight = 360f;
    private const float Focal = 250f;
    private const float Near = 0.05f;
    private const float Far = 1000f;
    /// <summary>The background quad stays this far ahead, behind everything in the tube.</summary>
    private const float BackgroundDistance = 900f;
    /// <summary>Sprites are drawn a quarter of their size (lights), the planet half.</summary>
    private const float LightScale = 0.25f;
    private const float PlanetScale = 0.5f;
    /// <summary>The lights glow over what's behind (❓ the original's blend isn't known).</summary>
    private const float LightAlpha = 0.75f;
    private const int SpriteCapacity = 256;
    private const int QuadVertices = 6;
    private const int TubeCapacity = StreamTube.Rings * StreamTube.Points * 2 * 3;
    // The health box (0x420830): SC_STAT at the bottom right, SNIP_TXT digits, blinking at 20 or less.
    private const float PanelRight = 16f;
    private const float PanelBottom = 10f;
    private const int DigitWidth = 8;
    private const int MaxNumber = 999;
    private const int LowHealth = 20;
    private const int BlinkOn = 16;

    private sealed record PoseMesh(int Mesh, List<(int First, int Count, Material Material)> Batches);

    private sealed record Image(int Texture, Vector2 Size);

    private readonly Renderer _renderer;
    private readonly Bni _bni;
    private readonly Palette _palette;
    private readonly IReadOnlyList<TextureArchive> _archives;
    private readonly MaterialResolver _resolver;
    private readonly Dictionary<(string Model, string? Animation, int Frame), PoseMesh?> _meshes = [];
    private readonly Dictionary<string, Model> _models = [];
    private readonly Dictionary<string, ModelAnimation> _animations = [];
    private readonly Dictionary<(string, string), Vector3[][][]> _baked = [];
    private readonly int _paletteId;
    private readonly int _tubeMesh;
    private readonly int _spriteMesh;
    private readonly int _backMesh;
    private readonly Image _background;
    private readonly Image _light;
    private readonly Image _planet;
    private readonly Image _panel;
    private readonly Image _digits;
    private readonly List<TubeVertex> _tubeVertices = [];
    private readonly List<Vertex> _vertices = [];

    public StreamView(Renderer renderer, Bni bni, Palette palette, TextureArchive archive)
    {
        _renderer = renderer;
        _bni = bni;
        _palette = palette;
        _archives = [archive];
        _resolver = new MaterialResolver(renderer, new Dti());
        _paletteId = renderer.CreatePalette(palette.Rgba);
        _background = Load("BG");
        _light = Load("LIGHT");
        _planet = Load("PLANET");
        _panel = Load("SC_STAT");
        _digits = Load("SNIP_TXT");
        _tubeMesh = renderer.CreateDynamicMesh(TubeCapacity);
        _spriteMesh = renderer.CreateDynamicMesh(SpriteCapacity * QuadVertices);
        _backMesh = renderer.CreateDynamicMesh(QuadVertices);
    }

    private Image Load(string name)
    {
        var texture = _bni.GetImage(name);
        return new Image(_renderer.CreateIndexTexture(texture.Width, texture.Height, texture.Indices), new Vector2(texture.Width, texture.Height));
    }

    /// <summary>The camera's view for the window's aspect: 250-pixel focal length on the 360-high view.</summary>
    public View ViewOf(Flight flight)
    {
        var fieldOfView = float.RadiansToDegrees(2f * MathF.Atan(ViewHeight / 2f / Focal));
        return CameraMath.View(flight.Eye, flight.Forward, flight.Up, fieldOfView, _renderer.AspectRatio, Near, Far);
    }

    /// <summary>Queues the whole frame.</summary>
    public void Draw(Flight flight, string kurtAnimation, string otherModel, string otherAnimation)
    {
        DrawBackground(flight);
        DrawModel("KURT", kurtAnimation, flight.KurtFrame(), flight.KurtTransform());
        if (flight.Bones != null)
        {
            DrawModel("BONES", "BONESANIM", flight.BonesFrame(), flight.KurtTransform());
        }

        if (flight.Other is { } other)
        {
            DrawModel(otherModel, otherAnimation, flight.OtherFrame(other), flight.Transform(other));
        }

        DrawTube(flight);
        DrawSprites(flight);
        DrawHealth(flight.Health, flight.Blink);
        DrawFade(flight.Fade);
    }

    /// <summary>The background, one BG pixel per canvas unit, as a quad far ahead filling the view.</summary>
    private void DrawBackground(Flight flight)
    {
        var width = _renderer.CanvasWidth;
        var unit = BackgroundDistance / Focal;
        var centre = flight.Eye + flight.Forward * BackgroundDistance;
        var right = flight.Right * (width / 2f * unit);
        var up = flight.Up * (ViewHeight / 2f * unit);
        var uv0 = flight.BackgroundOffset / _background.Size;
        var uv1 = (flight.BackgroundOffset + new Vector2(width, ViewHeight)) / _background.Size;

        _vertices.Clear();
        AddQuad(centre - right + up, centre + right + up, centre + right - up, centre - right - up, uv0, uv1);
        _renderer.UpdateMesh(_backMesh, CollectionsMarshal.AsSpan(_vertices));
        _renderer.Draw(_backMesh, 0, QuadVertices, new Material(_background.Texture, _paletteId, Vector4.One, 1, Pass.DoubleSided));
    }

    /// <summary>A quad from its top-left corner clockwise, textured from <paramref name="uv0"/> (top left) to <paramref name="uv1"/>.</summary>
    private void AddQuad(Vector3 topLeft, Vector3 topRight, Vector3 bottomRight, Vector3 bottomLeft, Vector2 uv0, Vector2 uv1)
    {
        _vertices.Add(new Vertex(topLeft, uv0));
        _vertices.Add(new Vertex(topRight, new Vector2(uv1.X, uv0.Y)));
        _vertices.Add(new Vertex(bottomRight, uv1));
        _vertices.Add(new Vertex(topLeft, uv0));
        _vertices.Add(new Vertex(bottomRight, uv1));
        _vertices.Add(new Vertex(bottomLeft, new Vector2(uv0.X, uv1.Y)));
    }

    /// <summary>The tube's segments far to near in one draw, each vertex with its RGBA.</summary>
    private void DrawTube(Flight flight)
    {
        var tube = flight.Tube;
        _vertices.Clear();
        foreach (var n in tube.DrawnSegments())
        {
            _tubeVertices.Clear();
            tube.AddTriangles(n, flight.Eye, _tubeVertices);
            foreach (var v in _tubeVertices)
            {
                var (r, g, b, a) = tube.Rgba(v);
                _vertices.Add(new Vertex(v.Position, Vector2.Zero, Vertex.Rgba(r, g, b, a)));
            }
        }

        if (_vertices.Count == 0)
        {
            return;
        }

        _renderer.UpdateMesh(_tubeMesh, CollectionsMarshal.AsSpan(_vertices));
        _renderer.Draw(_tubeMesh, 0, _vertices.Count, Material.Flat(Vector4.One, Pass.Blended));
    }

    /// <summary>The planet and the lights: billboards facing the camera.</summary>
    private void DrawSprites(Flight flight)
    {
        _vertices.Clear();
        var planets = AddSprites(flight, StreamTube.SpawnKind.Planet, PlanetScale);
        var lights = AddSprites(flight, StreamTube.SpawnKind.Light, LightScale);
        if (_vertices.Count == 0)
        {
            return;
        }

        _renderer.UpdateMesh(_spriteMesh, CollectionsMarshal.AsSpan(_vertices));
        if (planets > 0)
        {
            _renderer.Draw(_spriteMesh, 0, planets, new Material(_planet.Texture, _paletteId, Vector4.One, 1, Pass.DoubleSided));
        }

        if (lights > 0)
        {
            var glow = new Vector4(1f, 1f, 1f, LightAlpha);
            _renderer.Draw(_spriteMesh, planets, lights, new Material(_light.Texture, _paletteId, glow, 1, Pass.Blended));
        }
    }

    /// <summary>Adds the sprites of a kind; returns their vertex count.</summary>
    private int AddSprites(Flight flight, StreamTube.SpawnKind kind, float scale)
    {
        var first = _vertices.Count;
        foreach (var (spawn, thing) in flight.Sprites)
        {
            if (spawn.Kind != kind || _vertices.Count >= SpriteCapacity * QuadVertices)
            {
                continue;
            }

            var centre = flight.Tube.Place(thing.T, thing.X, thing.Z);
            var half = spawn.Size * scale / 2f;
            var right = flight.Right * half;
            var up = flight.Up * half;
            AddQuad(centre - right + up, centre + right + up, centre + right - up, centre - right - up, Vector2.Zero, Vector2.One);
        }

        return _vertices.Count - first;
    }

    /// <summary>A model in a pose: a mesh per (model, animation, frame), built once.</summary>
    private void DrawModel(string name, string animation, int frame, Matrix4x4 transform)
    {
        var key = (name, (string?)animation, frame);
        if (!_meshes.TryGetValue(key, out var mesh))
        {
            mesh = _meshes[key] = Build(ModelOf(name), Pose(name, animation, frame));
        }

        if (mesh == null)
        {
            return;
        }

        foreach (var (first, count, material) in mesh.Batches)
        {
            _renderer.Draw(mesh.Mesh, first, count, material, 0, transform);
        }
    }

    private Model ModelOf(string name)
    {
        if (!_models.TryGetValue(name, out var model))
        {
            model = _models[name] = Model.Parse(name, _bni.Bytes, _bni.Entries[name].Offset, Model.Header.NoFlags, Model.Parts.Named);
        }

        return model;
    }

    /// <summary>An animation of STREAM.BNI (KURTANIM, BONESANIM, SWHANM, GUNTANIM).</summary>
    public ModelAnimation AnimationOf(string name)
    {
        if (!_animations.TryGetValue(name, out var animation))
        {
            animation = _animations[name] = ModelAnimation.Parse(name, _bni.Bytes, _bni.Entries[name].Offset);
        }

        return animation;
    }

    private Vector3[][] Pose(string model, string animation, int frame)
    {
        if (!_baked.TryGetValue((model, animation), out var frames))
        {
            frames = _baked[(model, animation)] = AnimationOf(animation).Bake(ModelOf(model));
        }

        return frames[Math.Clamp(frame, 0, frames.Length - 1)];
    }

    private PoseMesh? Build(Model model, Vector3[][] pose)
    {
        var vertices = new List<Vertex>();
        var batches = new List<(int First, int Count, Material Material)>();
        for (var p = 0; p < model.PartList.Count; p++)
        {
            var part = model.PartList[p];
            for (var t = 0; t < part.TriangleMaterials.Length; t++)
            {
                if (_resolver.Resolve(part.TriangleMaterials[t], model.Materials, _palette, _archives, Pass.DoubleSided) is not { } surface)
                {
                    continue;
                }

                var texture = surface.Texture;
                var scale = texture == null ? Vector2.Zero : new Vector2(1f / texture.Width, 1f / texture.Height);
                var first = vertices.Count;
                for (var k = 0; k < 3; k++)
                {
                    var index = part.TriangleIndices[t * 3 + k];
                    vertices.Add(new Vertex(pose[p][index], part.TriangleUvs[t * 3 + k] * scale));
                }

                // Neighbouring triangles of one surface draw together.
                if (batches.Count > 0 && batches[^1].Material == surface.Material)
                {
                    batches[^1] = (batches[^1].First, batches[^1].Count + 3, surface.Material);
                    continue;
                }

                batches.Add((first, 3, surface.Material));
            }
        }

        return vertices.Count == 0 ? null : new PoseMesh(_renderer.CreateMesh([.. vertices]), batches);
    }

    /// <summary>The health box at the bottom right; the number blinks at 20 or less.</summary>
    private void DrawHealth(int health, int blink)
    {
        var width = _renderer.CanvasWidth;
        var at = new Vector2(width - (_panel.Size.X + PanelRight), Renderer.CanvasHeight - (_panel.Size.Y + PanelBottom));
        DrawImage(_panel, new RectangleF(0f, 0f, _panel.Size.X, _panel.Size.Y), new RectangleF(at.X, at.Y, _panel.Size.X, _panel.Size.Y));
        if (health <= LowHealth && blink >= BlinkOn)
        {
            return;
        }

        // A number centred on the panel, 8-pixel digits (0x420bd0).
        var text = Math.Min(health, MaxNumber).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var x = at.X + ((int)_panel.Size.X >> 1) - DigitWidth / 2f * text.Length;
        var y = at.Y + (((int)_panel.Size.Y - (int)_digits.Size.Y) >> 1);
        foreach (var c in text)
        {
            var source = new RectangleF((c - '0') * DigitWidth, 0f, DigitWidth, _digits.Size.Y);
            DrawImage(_digits, source, new RectangleF(x, y, DigitWidth, _digits.Size.Y));
            x += DigitWidth;
        }
    }

    private void DrawImage(Image image, RectangleF source, RectangleF target) =>
        _renderer.DrawImage(image.Texture, _paletteId, image.Size, source, target, Vector4.One);

    /// <summary>The fade over everything: red added (0x4352ac: R + red, green and blue kept), then
    /// towards white, then darkened.</summary>
    private void DrawFade(Fade fade)
    {
        var screen = new RectangleF(0f, 0f, _renderer.CanvasWidth, Renderer.CanvasHeight);
        if (fade.Red > 0f)
        {
            _renderer.AddRect(screen, new Vector4(1f, 0f, 0f, fade.Red));
        }

        if (fade.Whiten < 1f)
        {
            _renderer.FillRect(screen, new Vector4(1f, 1f, 1f, 1f - fade.Whiten));
        }

        if (fade.Dark < 1f)
        {
            _renderer.FillRect(screen, new Vector4(0f, 0f, 0f, 1f - fade.Dark));
        }
    }
}
