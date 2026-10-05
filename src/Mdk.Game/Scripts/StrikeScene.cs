using System.Numerics;
using Mdk.Formats;
using Mdk.Game.Objects;

namespace Mdk.Game.Scripts;

/// <summary>The full-screen strike (0x4398f0; a port of godot-mdk's strike_scene.gd): before Bones'
/// air strike (X_STRIKB) and Kurt's strike at the end of the game (X_STRIKD) the game stops and only
/// the sky and that model are drawn. The model plays its animation once while it turns at 45°/s; the
/// camera sits at its reference point 1 looking at its reference point 0 (both moved by the
/// animation), with a long lens (focal length 600 / 0.35265 pixels of the 360-high view). Esc skips
/// it; the music goes on.
/// <code>
///   game paused ──► [ sky + model, camera on the model's points ] ──► Finished ──► game resumes
/// </code></summary>
public sealed class StrikeScene
{
    /// <summary>Which strike: Bones' plane, or Kurt's (event 51).</summary>
    public enum Kind { Bones, Kurt }

    /// <summary>The plane with Bones in it, or the plane only (the dive strike).</summary>
    public enum Plane { WithPilot, Only }

    private const float TurnRate = 45f;
    private const float Focal = 600f / 0.35265395f;
    private const float ViewHeight = 360f;
    private const int CameraPoint = 1;
    private const int LookPoint = 0;
    private const int NeededPoints = 2;
    /// <summary>When Bones dives alone, only the plane's parts show (0x491ddc).</summary>
    private static readonly string[] PlaneParts = ["AWING", "CANOPY", "LEVER", "LEVER01", "LEVER02", "LEVER03", "OBJECT"];

    /// <summary>Vertical field of view in degrees.</summary>
    public static readonly float FieldOfView = float.RadiansToDegrees(2f * MathF.Atan(ViewHeight / 2f / Focal));

    private ModelAnimation? _animation;
    private float _frame;
    private float _yaw;

    public event Action? Finished;

    /// <summary>The model shown, posed and turned (drawn like an object).</summary>
    public MdkObject Subject { get; } = new();

    public bool Active { get; private set; }

    /// <summary>The camera: its place and the point it looks at.</summary>
    public (Vector3 Eye, Vector3 Target) Camera
    {
        get
        {
            var turn = Matrix4x4.CreateRotationZ(float.DegreesToRadians(_yaw));
            return (Vector3.Transform(ReferencePoint(CameraPoint), turn), Vector3.Transform(ReferencePoint(LookPoint), turn));
        }
    }

    /// <summary>Plays the strike of <paramref name="kind"/> in Kurt's arena; it ends at once without its model.</summary>
    public void Play(ScriptRuntime runtime, Kind kind, Plane plane)
    {
        var arena = runtime.CurrentArena;
        var name = kind == Kind.Bones ? "X_STRIKB" : "X_STRIKD";
        var model = runtime.FindModel(arena, name);
        _animation = kind == Kind.Bones ? runtime.Items.GetAnimation(name) : runtime.FindArenaAnimation(arena, name);
        if (model == null || _animation == null || _animation.FrameCount == 0 || model.ReferencePoints.Count < NeededPoints)
        {
            Finished?.Invoke();
            return;
        }

        Subject.TypeName = name;
        Subject.Model = model;
        Subject.Arena = arena;
        Subject.Animation = _animation;
        Subject.HiddenParts = plane == Plane.Only ? HiddenParts(model) : 0;
        Active = true;
        Show();
    }

    private static int HiddenParts(Model model)
    {
        var hidden = 0;
        for (var i = 0; i < model.PartList.Count; i++)
        {
            if (!PlaneParts.Contains(model.PartList[i].Name.ToUpperInvariant()))
            {
                hidden |= 1 << i;
            }
        }

        return hidden;
    }

    /// <summary>Advances by <paramref name="delta"/> seconds; the animation's end finishes it.</summary>
    public void Update(float delta)
    {
        if (!Active)
        {
            return;
        }

        _frame += delta * Kurt.Kurt.Ticks * _animation!.Speed;
        _yaw += TurnRate * delta;
        if (_frame >= _animation.FrameCount)
        {
            Skip();
            return;
        }

        Show();
    }

    /// <summary>Ends the scene (Esc).</summary>
    public void Skip()
    {
        if (!Active)
        {
            return;
        }

        Active = false;
        Finished?.Invoke();
    }

    private void Show()
    {
        Subject.AnimationFrame = (int)_frame;
        Subject.Yaw = _yaw;
    }

    /// <summary>A reference point in the current frame: the animation moves them.</summary>
    private Vector3 ReferencePoint(int index)
    {
        if (_animation != null && index < _animation.ReferencePoints.Count)
        {
            return _animation.ReferencePoints[index][Math.Min((int)_frame, _animation.FrameCount - 1)];
        }

        return Subject.Model?.ReferencePoints[index] ?? Vector3.Zero;
    }
}
