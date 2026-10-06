using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Game.Kurt;
using Mdk.Game.Scripts;

namespace Mdk.Game;

/// <summary>The camera of the scripts' cutscenes (0x477d94; godot-mdk script_runtime.gd
/// <c>get_cutscene_camera</c>): the runtime moves its point and turns it each tick, this looks
/// from there. Its yaw is 90° − the heading, its pitch positive down.
/// <code>
///        yaw 0 ──► +Y (heading 90°)
///   point ●──────────► forward = (sin yaw · cos pitch, cos yaw · cos pitch, −sin pitch)
/// </code></summary>
public sealed class CutsceneCamera
{
    private const float Near = 0.5f;
    private const float Far = 20000f;

    public Vector3 Position { get; private set; }
    public Vector3 Forward { get; private set; } = Vector3.UnitY;
    public Vector3 Up { get; private set; } = Vector3.UnitZ;
    public Vector3 Right { get; private set; } = Vector3.UnitX;

    /// <summary>Whether a cutscene holds the camera: from the runtime's first shot (the tick after
    /// special_event), so it never looks from the origin.</summary>
    public static bool Active(ScriptRuntime scripts) => scripts.Cutscene != 0 && scripts.CameraPoint != Vector3.Zero;

    /// <summary>Takes the runtime's shot of this tick.</summary>
    public void Update(ScriptRuntime scripts)
    {
        Position = scripts.CameraPoint;
        Forward = ForwardOf(scripts.CameraYaw, scripts.CameraPitch);
        Up = FollowCamera.UpOf(Forward);
        Right = Vector3.Normalize(Vector3.Cross(Forward, Vector3.UnitZ));
    }

    /// <summary>The line of sight for a yaw (90° − heading) and a pitch (positive down), in degrees.</summary>
    public static Vector3 ForwardOf(float yaw, float pitch)
    {
        var y = float.DegreesToRadians(yaw);
        var p = float.DegreesToRadians(pitch);
        return new Vector3(MathF.Sin(y) * MathF.Cos(p), MathF.Cos(y) * MathF.Cos(p), -MathF.Sin(p));
    }

    public View View(float aspect) => CameraMath.View(Position, Forward, Up, FollowCamera.FieldOfView, aspect, Near, Far);
}
