using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Formats;

namespace Mdk.Game.Kurt;

/// <summary>Draws Kurt like the original: a sprite frame at a fixed size on screen (one pixel is
/// 1/360 of the view's height), its hotspot 101 pixels above his projected feet, depth-tested at
/// the feet's depth.
/// <code>
///      ┌──────┐  frame
///      │  ☺   │
///      │ /|\  │  hotspot + (0, 101) ─► the feet
///      └──────┘
/// </code></summary>
public sealed class KurtSprite
{
    /// <summary>Kurt's frames are drawn this many pixels above his feet.</summary>
    private const int FeetOffset = 101;
    private const float ViewHeight = 360f;
    private const int QuadVertices = 6;
    /// <summary>The chute is painted in Kurt's frames (damp_animate 0x4646a4): K_CHUTE 0-4 open it,
    /// then K_CHUTEC sways back and forth.</summary>
    private const string ChuteOpening = "K_CHUTE";
    private const string ChuteSway = "K_CHUTEC";
    private const int ChuteOpenedFrame = 5;

    private static readonly Dictionary<Kurt.State, (string Name, Repeat Repeat)> Animations = new()
    {
        [Kurt.State.Still] = ("K_STILL", Repeat.Once),
        [Kurt.State.Idle] = ("K_IDLE", Repeat.Once),
        [Kurt.State.Run] = ("K_RUN", Repeat.Loop),
        [Kurt.State.Side] = ("K_SIDE", Repeat.Loop),
        [Kurt.State.Turn] = ("K_TRN45", Repeat.Loop),
        [Kurt.State.Jump] = ("K_JUMP", Repeat.Once),
        [Kurt.State.RunJump] = ("K_RJMP", Repeat.Once),
        [Kurt.State.Fall] = ("K_FALL", Repeat.Loop),
        [Kurt.State.Chute] = (ChuteOpening, Repeat.Once),
        [Kurt.State.Land] = ("K_LAND", Repeat.Once),
    };

    private enum Repeat { Once, Loop }

    private readonly Renderer _renderer;
    private readonly Bni _sprites;
    private readonly int _palette;
    private readonly int _mesh;
    private readonly Dictionary<(string, int), int> _textures = [];

    public KurtSprite(Renderer renderer, Bni sprites, Palette palette)
    {
        _renderer = renderer;
        _sprites = sprites;
        _palette = renderer.CreatePalette(palette.Rgba);
        _mesh = renderer.CreateDynamicMesh(QuadVertices);
    }

    public int FrameCount(Kurt.State state) => _sprites.GetAnimation(Animations[state].Name).FrameCount;

    /// <summary>Queues Kurt's frame for the camera at <paramref name="eye"/> looking along
    /// <paramref name="forward"/> with <paramref name="up"/>, the view <paramref name="fieldOfView"/>
    /// degrees high.</summary>
    public void Draw(Kurt kurt, Vector3 eye, Vector3 forward, Vector3 up, float fieldOfView)
    {
        var (name, frame) = Pick(kurt);
        var animation = _sprites.GetAnimation(name);
        var image = animation.GetFrame(frame);

        // World units per sprite pixel at the feet's depth.
        var depth = Vector3.Dot(kurt.Feet - eye, forward);
        if (depth <= 0f)
        {
            return;
        }

        var pixel = 2f * depth * MathF.Tan(float.DegreesToRadians(fieldOfView) / 2f) / ViewHeight;
        var right = Vector3.Normalize(Vector3.Cross(forward, up));
        var flip = kurt.Current == Kurt.State.Side && kurt.StrafeSpeed < 0f ? -1f : 1f;
        var w = image.Image.Width;
        var h = image.Image.Height;
        var ax = image.HotspotX;
        var ay = image.HotspotY + FeetOffset;

        Vector3 Corner(float px, float py) => kurt.Feet + right * ((px - ax) * pixel * flip) + up * ((ay - py) * pixel);
        Span<Vertex> quad =
        [
            new(Corner(0, 0), new Vector2(0, 0)), new(Corner(w, 0), new Vector2(1, 0)), new(Corner(w, h), new Vector2(1, 1)),
            new(Corner(0, 0), new Vector2(0, 0)), new(Corner(w, h), new Vector2(1, 1)), new(Corner(0, h), new Vector2(0, 1)),
        ];
        _renderer.UpdateMesh(_mesh, quad);
        var material = new Material(Texture(name, frame, image.Image), _palette, Vector4.One, 1, Pass.DoubleSided);
        _renderer.Draw(_mesh, 0, QuadVertices, material);
    }

    /// <summary>The animation and frame of Kurt's state; the chute opens, then sways.</summary>
    private (string Name, int Frame) Pick(Kurt kurt)
    {
        var (name, repeat) = Animations[kurt.Current];
        var count = _sprites.GetAnimation(name).FrameCount;
        var frame = (int)MathF.Floor(kurt.AnimationFrame);
        if (kurt.Current == Kurt.State.Chute && frame >= ChuteOpenedFrame)
        {
            var last = _sprites.GetAnimation(ChuteSway).FrameCount - 1;
            var k = (frame - ChuteOpenedFrame) % (last * 2);
            return (ChuteSway, k <= last ? k : last * 2 - k);
        }

        frame = repeat == Repeat.Loop ? ((frame % count) + count) % count : Math.Clamp(frame, 0, count - 1);
        return (name, frame);
    }

    private int Texture(string name, int frame, Texture image)
    {
        if (!_textures.TryGetValue((name, frame), out var id))
        {
            id = _textures[(name, frame)] = _renderer.CreateIndexTexture(image.Width, image.Height, image.Indices);
        }

        return id;
    }
}
