using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Formats;
using Mdk.Game.HdTextures;
using Mdk.Game.Mods;

namespace Mdk.Game.Kurt;

/// <summary>Draws Kurt like the original: a sprite frame at a fixed size on screen (one pixel is
/// 1/360 of the view's height), its hotspot 101 pixels above his projected feet, depth-tested at
/// the feet's depth. The enhanced look takes mods' images of its frames (<see cref="ModImages"/>).
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
    /// <summary>K_CHUTE's frame shown once when the chute closes in the air.</summary>
    private const int ChuteClosedFrame = 7;
    /// <summary>The chain gun's muzzle flash, drawn behind Kurt (a little farther from the camera).</summary>
    private const string MuzzleFlash = "K_MUZZF";
    private const float MuzzleBehind = 0.05f;

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
        [Kurt.State.Shot] = ("K_SHOT", Repeat.Loop),
        [Kurt.State.RunFire] = ("K_RUNFIR", Repeat.Loop),
        [Kurt.State.Throw] = ("K_SPWEP", Repeat.Once),
        [Kurt.State.Knocked] = ("K_BANG", Repeat.Once),
        [Kurt.State.GetUp] = ("K_BFLIP", Repeat.Once),
        [Kurt.State.Dead] = ("K_BANG", Repeat.Once),
        [Kurt.State.Slip] = ("K_SLIP", Repeat.Once),
        [Kurt.State.Slide] = ("K_SLIDE", Repeat.Loop),
        [Kurt.State.SlideFast] = ("K_FSLIDE", Repeat.Loop),
        [Kurt.State.SlideBrake] = ("K_BSLIDE", Repeat.Loop),
        [Kurt.State.Hang] = ("K_HANG", Repeat.Once),
        [Kurt.State.HardLand] = ("K_CRASHL", Repeat.Once),
        [Kurt.State.RollLeft] = ("K_ROLLL", Repeat.Once),
        [Kurt.State.RollRight] = ("K_ROLLR", Repeat.Once),
        [Kurt.State.HelmetOn] = (Helmet, Repeat.Once),
        [Kurt.State.HelmetOff] = (Helmet, Repeat.Once),
    };

    /// <summary>The 1996 demo's frames: the helmet (played backwards when it comes off), backing up.</summary>
    private const string Helmet = "K_HELM";
    private const string BackUp = "K_BCKUP";

    private enum Repeat { Once, Loop }

    /// <summary>Kurt's frames kept in the level's archives: the snowboard's (LEVEL4S.SNI), the
    /// slide's (LEVEL6S.SNI).</summary>
    public static readonly string[] LevelAnimations = ["K_SURF", "K_SURFJ", "K_SLIP", "K_SLIDE", "K_FSLIDE", "K_BSLIDE"];

    /// <summary>The animations Kurt is drawn with: TRAVSPRT.BNI's, then the levels' (the HD textures' export).</summary>
    public static IEnumerable<string> Drawn =>
        Animations.Values.Select(a => a.Name).Append(ChuteSway).Append(MuzzleFlash).Append(BackUp).Concat(LevelAnimations).Distinct();

    private readonly Renderer _renderer;
    private readonly Bni _sprites;
    private readonly int _palette;
    private readonly Shading _shading;
    private readonly Palette _colours;
    private readonly ModImages? _mods;
    private readonly int _mesh;
    private readonly int _muzzleMesh;
    private readonly Dictionary<(string, int), int> _textures = [];
    /// <summary>Frames from the level's archives (the board's K_SURF and K_SURFJ).</summary>
    private readonly Dictionary<string, SpriteAnimation> _extra = [];

    public KurtSprite(Renderer renderer, Bni sprites, Palette palette, Shading shading, ModImages? mods = null)
    {
        _renderer = renderer;
        _sprites = sprites;
        _shading = shading;
        _colours = palette;
        _mods = mods;
        _palette = renderer.CreatePalette(palette.Rgba);
        _mesh = renderer.CreateDynamicMesh(QuadVertices);
        _muzzleMesh = renderer.CreateDynamicMesh(QuadVertices);
        foreach (var name in Animations.Values.Select(a => a.Name).Append(ChuteSway).Append(MuzzleFlash).Append(BackUp).Distinct())
        {
            if (sprites.Has(name))
            {
                Preload(sprites.GetAnimation(name));
            }
        }
    }

    /// <summary>Decodes an animation's frames and puts them on the GPU, HD when made (a level's load).</summary>
    private void Preload(SpriteAnimation animation)
    {
        for (var frame = 0; frame < animation.FrameCount; frame++)
        {
            var image = animation.GetFrame(frame).Image;
            var material = MaterialOf(animation.Name, frame, image);
            if (Upscaled(_mods, _shading, FrameName(animation.Name, frame), image, _colours) is { } hd)
            {
                _renderer.Replace(material, hd.Width, hd.Height, hd.Rgba);
            }
        }
    }

    /// <summary>A frame's mod image (HD textures too): the enhanced look's sprites only.</summary>
    public static HdImage? Upscaled(ModImages? mods, Shading shading, string name, Texture image, Palette palette) =>
        shading == Shading.Sprite ? mods?.Texture(name, image, palette) : null;

    /// <summary>A frame's name in mods: the animation's, then its number (K_RUN_0).</summary>
    public static string FrameName(string animation, int frame) => $"{animation}_{frame}";

    private Material MaterialOf(string name, int frame, Texture image)
    {
        var material = new Material(Texture(name, frame, image), _palette, Vector4.One, 1, Pass.DoubleSided, Shading: _shading);
        _renderer.Prepare(material);
        return material;
    }

    public int FrameCount(Kurt.State state) => FrameCount(Animations[state].Name);

    /// <summary>Adds an animation of the level's (null: none there).</summary>
    public void Add(SpriteAnimation? animation)
    {
        if (animation != null)
        {
            _extra[animation.Name] = animation;
            Preload(animation);
        }
    }

    /// <summary>Frames of an animation of the level's or of <c>TRAVSPRT.BNI</c> (0 when missing).</summary>
    public int FrameCount(string name) =>
        _extra.TryGetValue(name, out var animation) ? animation.FrameCount : _sprites.Has(name) ? _sprites.GetAnimation(name).FrameCount : 0;

    private SpriteAnimation Animation(string name) => _extra.TryGetValue(name, out var animation) ? animation : _sprites.GetAnimation(name);

    /// <summary>Queues Kurt's frame for the camera at <paramref name="eye"/> looking along
    /// <paramref name="forward"/> with <paramref name="up"/>, the view <paramref name="fieldOfView"/>
    /// degrees high.</summary>
    public void Draw(Kurt kurt, Vector3 eye, Vector3 forward, Vector3 up, float fieldOfView)
    {
        // World units per sprite pixel at the feet's depth.
        var depth = Vector3.Dot(kurt.Feet - eye, forward);
        if (!kurt.Visible || kurt.Sniping || depth <= 0f)
        {
            return;
        }

        var pixel = 2f * depth * MathF.Tan(float.DegreesToRadians(fieldOfView) / 2f) / ViewHeight;
        var right = Vector3.Normalize(Vector3.Cross(forward, up));
        // K_ROLLL and K_ROLLR are the same file, a roll to the right: mirrored for the left.
        var flip = (kurt.Current == Kurt.State.Side && kurt.StrafeSpeed < 0f) || kurt.Current == Kurt.State.RollLeft ? -1f : 1f;
        var (name, frame) = Pick(kurt);
        var quad = new Quad(kurt.Feet, right * (pixel * flip), up * pixel);
        DrawFrame(_mesh, name, frame, quad, 0, 0);

        // The flash's hotspot is at Kurt's hotspot plus its offset.
        if (kurt.Muzzle is { } muzzle)
        {
            var behind = quad with { Feet = kurt.Feet + forward * MuzzleBehind };
            DrawFrame(_muzzleMesh, MuzzleFlash, muzzle.Frame, behind, muzzle.X, muzzle.Y);
        }
    }

    /// <summary>Where a frame goes: the feet, and the world vectors of one sprite pixel right and up.</summary>
    private readonly record struct Quad(Vector3 Feet, Vector3 Right, Vector3 Up);

    /// <summary>Draws a frame with its hotspot 101 pixels above the feet, moved by (dx, dy) pixels (y down).</summary>
    private void DrawFrame(int mesh, string name, int frame, Quad at, int dx, int dy)
    {
        var image = Animation(name).GetFrame(frame);
        var w = image.Image.Width;
        var h = image.Image.Height;
        var ax = image.HotspotX - dx;
        var ay = image.HotspotY + FeetOffset - dy;

        Vector3 Corner(float px, float py) => at.Feet + at.Right * (px - ax) + at.Up * (ay - py);
        Span<Vertex> quad =
        [
            new(Corner(0, 0), new Vector2(0, 0)), new(Corner(w, 0), new Vector2(1, 0)), new(Corner(w, h), new Vector2(1, 1)),
            new(Corner(0, 0), new Vector2(0, 0)), new(Corner(w, h), new Vector2(1, 1)), new(Corner(0, h), new Vector2(0, 1)),
        ];
        _renderer.UpdateMesh(mesh, quad);
        _renderer.Draw(mesh, 0, QuadVertices, MaterialOf(name, frame, image.Image));
    }

    /// <summary>The animation and frame of Kurt's state; the chute opens, then sways.</summary>
    private (string Name, int Frame) Pick(Kurt kurt)
    {
        // A ride's or the end's frame; past the last it loops (K_FLOATC).
        if (kurt.Pose is { } pose && FrameCount(pose.Name) is > 0 and var poseFrames)
        {
            return (pose.Name, ((pose.Frame % poseFrames) + poseFrames) % poseFrames);
        }

        if (kurt.ChuteClosing)
        {
            return (ChuteOpening, ChuteClosedFrame);
        }

        var (name, repeat) = Animations[kurt.Current];
        if (kurt.BackingUp && FrameCount(BackUp) > 0)
        {
            name = BackUp;
        }

        var count = Animation(name).FrameCount;
        var frame = (int)MathF.Floor(kurt.AnimationFrame);
        if (kurt.Current == Kurt.State.Chute && frame >= ChuteOpenedFrame)
        {
            var last = _sprites.GetAnimation(ChuteSway).FrameCount - 1;
            var k = (frame - ChuteOpenedFrame) % (last * 2);
            return (ChuteSway, k <= last ? k : last * 2 - k);
        }

        frame = repeat == Repeat.Loop ? ((frame % count) + count) % count : Math.Clamp(frame, 0, count - 1);
        return (name, kurt.Current == Kurt.State.HelmetOff ? count - 1 - frame : frame);
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
