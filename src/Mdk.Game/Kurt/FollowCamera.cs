using System.Numerics;
using Mdk.Engine.Platform;
using Mdk.Engine.Render;

namespace Mdk.Game.Kurt;

/// <summary>The third-person camera behind Kurt (<c>camera_update</c>). Its pitch (positive looks
/// down) is the arena's (DTI), eased in, plus the mouse's look offset and a tilt in the air.
/// <code>
///        camera ●
///                 \  pitch
///   pivot 4.5 ─────● Kurt's feet + up
/// </code></summary>
public sealed class FollowCamera
{
    /// <summary>Vertical field of view in degrees (the Godot port's, matching the original's view).</summary>
    public const float FieldOfView = 71.5f;
    private const float Distance = 8f;
    private const float Pivot = 4.5f;
    private const float MouseDegrees = 0.15f;
    private const float MinPitch = -60f;
    private const float MaxPitch = 90f;
    private const float AirPitchRate = 0.667f * Kurt.Ticks;
    private const float AirPitchMax = 40f;
    private const float AirPitchDecay = 40f;
    /// <summary>The arena pitch eases in by 0.85·old + 0.15·new per tick.</summary>
    private const float PitchEase = 0.85f;
    private const float DefaultPitch = 4f;
    /// <summary>Looking far up brings the camera closer: its distance shrinks below −20°.</summary>
    private const float CloseUpPitch = -20f;
    private const float CloseUpRange = 80f;
    private const float CloseUpBias = 100f;
    /// <summary>Looking down lifts the camera back towards Kurt by up to 5.</summary>
    private const float DownBack = 5f;
    private const float Near = 0.5f;
    private const float Far = 20000f;

    private float _arenaPitch = DefaultPitch;
    private float _lookOffset;
    private float _airPitch;
    private float _airTime;

    public Vector3 Position { get; private set; }
    public Vector3 Forward { get; private set; } = Vector3.UnitY;
    public Vector3 Up { get; private set; } = Vector3.UnitZ;

    public void Update(Kurt kurt, float arenaPitch, Input input, float delta)
    {
        _arenaPitch = float.Lerp(arenaPitch, _arenaPitch, MathF.Pow(PitchEase, delta * Kurt.Ticks));
        _lookOffset += input.MouseY * MouseDegrees;
        if (kurt.OnFloor)
        {
            _airTime = 0f;
            _airPitch = MathF.Max(_airPitch - AirPitchDecay * delta, 0f);
        }
        else
        {
            _airTime += delta;
            _airPitch = MathF.Max(_airPitch, MathF.Min(_airTime * AirPitchRate, AirPitchMax));
        }

        _lookOffset = Math.Clamp(_lookOffset, MinPitch - _arenaPitch - _airPitch, MaxPitch - _arenaPitch - _airPitch);
        var pitch = float.DegreesToRadians(_arenaPitch + _lookOffset + _airPitch);
        var facing = kurt.Facing;
        Position = Point(kurt.Feet, facing, pitch);
        Forward = Vector3.Normalize(facing * MathF.Cos(pitch) - Vector3.UnitZ * MathF.Sin(pitch));
        Up = Vector3.Normalize(Vector3.Cross(Vector3.Cross(Forward, Vector3.UnitZ), Forward));
    }

    /// <summary>The camera's place for the feet and pitch (radians).</summary>
    private static Vector3 Point(Vector3 feet, Vector3 facing, float pitch)
    {
        var distance = Distance;
        var back = -distance * MathF.Cos(pitch);
        if (pitch > 0f)
        {
            back += DownBack * (1f - MathF.Cos(pitch));
        }
        else if (pitch < float.DegreesToRadians(CloseUpPitch))
        {
            distance = Distance * (float.RadiansToDegrees(pitch) + CloseUpBias) / CloseUpRange;
            back = -distance * MathF.Cos(pitch);
        }

        return feet + facing * back + Vector3.UnitZ * (Pivot + distance * MathF.Sin(pitch));
    }

    public View View(float aspect) => CameraMath.View(Position, Forward, Up, FieldOfView, aspect, Near, Far);
}

/// <summary>View matrices for a camera (MDK coordinates, Z up).</summary>
public static class CameraMath
{
    public static View View(Vector3 position, Vector3 forward, Vector3 up, float fieldOfView, float aspect, float near, float far)
    {
        var view = Matrix4x4.CreateLookAt(position, position + forward, up);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(float.DegreesToRadians(fieldOfView), aspect, near, far);

        // The sky only turns with the camera: its rotation alone, inverted.
        var rotation = Matrix4x4.CreateLookAt(Vector3.Zero, forward, up);
        Matrix4x4.Invert(rotation * projection, out var clipToDirection);
        return new View(view * projection, clipToDirection, position);
    }
}
