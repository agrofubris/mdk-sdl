using System.Numerics;
using Mdk.Formats.Scripts;
using Mdk.Game.Level;
using Mdk.Game.Objects;
using static Mdk.Game.Scripts.ScriptMath;

namespace Mdk.Game.Scripts;

/// <summary>What the 1996 demo's levels do differently (godot-mdk docs/beta96.md): no town timer,
/// Kurt's arena from the arenas' boxes (its connections have no direction), the alarm-ended
/// condition (opcode 28), and the part the chain gun hits.</summary>
public sealed partial class ScriptRuntime
{
    /// <summary>Ticks "an object that sounded the alarm is gone" lasts (the demo's <c>0xe21de</c>).</summary>
    private const int AlarmEndedDuration = 10;
    /// <summary>Parts this much farther from the line of fire than the nearest may be hit too.</summary>
    private const float BetaPartSpread = 1.5f;
    /// <summary>The movement command of an object sounding the alarm.</summary>
    private const int AlarmCommand = 15;

    /// <summary>Ticks left of "an object that sounded the alarm is gone" (opcode 28 of the demo).</summary>
    public int AlarmEndedTicks;

    /// <summary>The level is one of the 1996 demo's.</summary>
    private bool IsBeta => Cmi.Dialect == ScriptDialect.Beta1996;

    /// <summary>Kurt's arena in the demo's levels: the smallest arena box around him, when his arena
    /// connects to it.</summary>
    private string BetaArena()
    {
        var arena = _space.ArenaAt(KurtPosition) ?? "";
        return CurrentArena.Length == 0 || Connects(CurrentArena, arena) ? arena : CurrentArena;
    }

    private bool Connects(string from, string to) =>
        ArenaRecords(from).Any(r => r.Type == LevelData.Connection && _connections.GetValueOrDefault((from, r.Id)) == to);

    /// <summary>An object killed: one sounding the alarm starts the demo's alarm-ended ticks.</summary>
    private void NoteAlarmEnded(MdkObject obj)
    {
        if (obj.MoveCommand == AlarmCommand)
        {
            AlarmEndedTicks = AlarmEndedDuration;
        }
    }

    /// <summary>The part of an object the chain gun hits in the demo's levels, or -1. Its scripts ask
    /// which part was hit for every object (<c>XW3</c>'s guns, <c>XB2</c>'s eyes and nose, the grunts'
    /// heads). How the demo picks it wasn't read: one of the shown parts nearest to the line Kurt fires
    /// along, at random within <see cref="BetaPartSpread"/>, so that parts above each other all get hit.</summary>
    private int BetaHitPart(MdkObject obj, Vector3 origin)
    {
        if (obj.Model == null)
        {
            return -1;
        }

        var aim = FromAngle(KurtYaw);
        var bounds = obj.PartBounds();
        var distances = new float[bounds.Length];
        for (var i = 0; i < bounds.Length; i++)
        {
            distances[i] = float.PositiveInfinity;
            if ((obj.HiddenParts & (1 << i)) != 0 || bounds[i] is not { } part)
            {
                continue;
            }

            var center = GetWorldBounds(obj, part).Center();
            var across = new Vector2(center.X - origin.X, center.Y - origin.Y);
            distances[i] = MathF.Abs(across.X * aim.Y - across.Y * aim.X);
        }

        var nearest = distances.DefaultIfEmpty(float.PositiveInfinity).Min();
        if (float.IsPositiveInfinity(nearest))
        {
            return -1;
        }

        var candidates = Enumerable.Range(0, distances.Length).Where(i => distances[i] <= nearest + BetaPartSpread).ToList();
        return candidates[Rng.Next(candidates.Count)];
    }
}
