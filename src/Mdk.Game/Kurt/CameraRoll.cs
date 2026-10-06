using System.Numerics;

namespace Mdk.Game.Kurt;

/// <summary>The camera's roll (0x573910, degrees; positive banks the view right), set by Kurt's
/// moves each step (camera_roll.gd; godot-mdk docs/gameplay.md "Horizontal").
/// <code>
///   running + turning (keys) ──► ±0.25°/tick up to ±10° (damp_move 0x467fa4)
///   sliding                  ──► 0.9·roll + 0.1·(90° − slope angle) per tick (0x468db8)
///   snowboard                ──► towards the board's bank at 45°/s (0x46ac4c)
///   none of these            ──► back to 0 by clamp(0.35·|roll|, 0.05, 2.5)°/tick (damp_control)
/// </code></summary>
public sealed class CameraRoll
{
    private const float WalkRate = 0.25f * Kurt.Ticks;
    private const float WalkLimit = 10f;
    /// <summary>Turning the other way first jumps 2° back towards level.</summary>
    private const float WalkReverse = 2f;
    private const float DecayFactor = 0.35f;
    private const float DecayMin = 0.05f;
    private const float DecayMax = 2.5f;
    private const float SlideKeep = 0.9f;
    private const float SlideLevel = 90f;
    private const float BoardRate = 45f;

    public float Roll { get; private set; }

    /// <summary>Something set the roll this step, so it doesn't level out (0x57ff74).</summary>
    private bool _held;

    /// <summary>Running and turning with the keys (positive: right, forward). Without both it doesn't change.</summary>
    public void Walk(float right, float forward, float delta)
    {
        if (forward == 0f || right == 0f)
        {
            return;
        }

        _held = true;
        var step = WalkRate * delta;
        if (right * forward > 0f)
        {
            Roll += step;
            Roll = MathF.Min(Roll < 0f ? Roll + WalkReverse : Roll, WalkLimit);
            return;
        }

        Roll -= step;
        Roll = MathF.Max(Roll > 0f ? Roll - WalkReverse : Roll, -WalkLimit);
    }

    /// <summary>Sliding: eases towards <c>90° − atan2(n.z, n.x·f.y − n.y·f.x)</c> for the floor normal
    /// and the facing, a tenth per tick.</summary>
    public void Slide(Vector3 normal, Vector2 facing, float delta)
    {
        _held = true;
        var angle = float.RadiansToDegrees(MathF.Atan2(normal.Z, normal.X * facing.Y - normal.Y * facing.X));
        Roll = float.Lerp(SlideLevel - angle, Roll, MathF.Pow(SlideKeep, Kurt.Ticks * delta));
    }

    /// <summary>On the board: towards its bank at 45°/s.</summary>
    public void Follow(float bank, float delta)
    {
        _held = true;
        var step = BoardRate * delta;
        Roll = MathF.Abs(bank - Roll) <= step ? bank : Roll + MathF.Sign(bank - Roll) * step;
    }

    /// <summary>At the start of a step: levels out unless the last step set it.</summary>
    public void Settle(float delta)
    {
        if (_held)
        {
            _held = false;
            return;
        }

        var step = Math.Clamp(DecayFactor * MathF.Abs(Roll), DecayMin, DecayMax) * Kurt.Ticks * delta;
        Roll = MathF.Abs(Roll) <= step ? 0f : Roll - MathF.Sign(Roll) * step;
    }
}
