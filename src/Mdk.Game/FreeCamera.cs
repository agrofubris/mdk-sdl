using System.Numerics;
using Mdk.Engine.Platform;
using Mdk.Engine.Render;

namespace Mdk.Game;

/// <summary>A flying camera for looking around a level (MDK coordinates, Z up; yaw 90 faces +Y).</summary>
public sealed class FreeCamera
{
    private const float Speed = 60f;
    private const float TurboFactor = 4f;
    private const float MouseDegrees = 0.15f;
    private const float PitchLimit = 89f;
    public const float FieldOfView = 60f;
    private const float Near = 0.5f;
    private const float Far = 20000f;

    public Vector3 Position;
    /// <summary>Degrees.</summary>
    public float Yaw;
    public float Pitch;

    public Vector3 Forward
    {
        get
        {
            var yaw = float.DegreesToRadians(Yaw);
            var pitch = float.DegreesToRadians(Pitch);
            return new Vector3(MathF.Cos(yaw) * MathF.Cos(pitch), MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch));
        }
    }

    public Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, Vector3.UnitZ));

    public void Update(Input input, float delta)
    {
        Yaw -= input.MouseX * MouseDegrees;
        Pitch = Math.Clamp(Pitch - input.MouseY * MouseDegrees, -PitchLimit, PitchLimit);

        var forward = Forward;
        var right = Right;
        var move = Vector3.Zero;
        move += Axis(input, Key.Forward, Key.Back) * forward;
        move += Axis(input, Key.StrafeRight, Key.StrafeLeft) * right;
        move += Axis(input, Key.Up, Key.Down) * Vector3.UnitZ;
        var speed = input.IsDown(Key.Turbo) ? Speed * TurboFactor : Speed;
        Position += move * speed * delta;
    }

    private static float Axis(Input input, Key positive, Key negative) =>
        (input.IsDown(positive) ? 1f : 0f) - (input.IsDown(negative) ? 1f : 0f);

    public View View(float aspect) =>
        Kurt.CameraMath.View(Position, Forward, Vector3.UnitZ, FieldOfView, aspect, Near, Far);
}
