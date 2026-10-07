using System.Numerics;
using Mdk.Formats;
using Mdk.Game.Collision;
using Mdk.Game.Objects;
using static Mdk.Game.Scripts.ScriptMath;

namespace Mdk.Game.Scripts;

/// <summary>Moves scripted objects each tick like the original object update (0x43c7dc): spline
/// paths (0x43c258), movement commands (0x45b6c8), gravity (0x45e74c), then friction and velocity
/// with arena collisions (0x45e810, a BSP box sweep 0x45fec4). A port of godot-mdk's
/// <c>object_motion.gd</c> (see its docs/engine.md).
/// <code>
///   path ─► command (walk, fly, chase...) ─► swing ─► gravity ─► velocity + push ─► BSP sweep
///        ─► item / pickup / cow ─► animation (+ frame sound) ─► roll, bank
/// </code></summary>
public sealed class ObjectMotion(ScriptRuntime runtime)
{
    /// <summary>Tick time in seconds and in ticks.</summary>
    public const float Dt = ScriptRuntime.Tick;
    public const float Ticks = 1f;
    /// <summary>Turn speed (degrees per second) walking towards a waypoint or chasing.</summary>
    public const float TurnSpeed = 180f;
    private const float TerminalVelocity = -220f;
    /// <summary>Walkers slow down to half speed when the waypoint is more than this angle away.</summary>
    private const float SlowTurnAngle = 80f;
    /// <summary>Objects falling this far below their arena's floor are killed.</summary>
    private const float FallOutDepth = 200f;
    /// <summary>Lines of sight for detours start this high above the object and the destination.</summary>
    private const float PlanHeight = 8f;
    /// <summary>A path key: s32 frame, position, in and out tangents (3 floats each).</summary>
    private const int PathKeySize = 40;
    private const int PathVectorSize = 12;
    private const int PathCountSize = 4;
    private const int PathFrameSize = 4;
    /// <summary>The BSP sweep of objects (0x45fec4): passes after the first, and the slide factor ❓
    /// (the caller's; Kurt walks with 0.75).</summary>
    private const int SweepIterations = 2;
    private const float SweepSlide = 0.75f;
    /// <summary>Bouncing objects reflect their velocity along the normal (0x45e810).</summary>
    private const float Bounce = -1.8f;
    /// <summary>Contact flag cleared with the collision flags each step.</summary>
    private const int ContactCleared = 0x10;
    private const int LevelSix = 6;
    private const int MortarItem = 4;
    /// <summary>Kurt's thrown items (but not active, collected or on gravity flags 0x44000) lose most
    /// of their horizontal speed in an updraft.</summary>
    private const int ThrownFlagsMask = 0x45000;
    private const float UpdraftBrake = 0.1f;

    /// <summary>One tick of an object's motion.</summary>
    public void Update(MdkObject obj)
    {
        if (obj.EffectFrames > 0)
        {
            // Effects only play their texture animation, one frame per tick (object_anim_update).
            obj.EffectTime += Ticks;
            if (obj.EffectTime >= obj.EffectFrames)
            {
                runtime.Remove(obj);
            }
            else
            {
                obj.TextureFrame = (int)obj.EffectTime;
            }

            return;
        }

        if (obj.Path != 0)
        {
            if ((obj.Flags & MdkObject.FlagPathSpeedByKurt) != 0)
            {
                UpdatePathSpeed(obj);
            }

            UpdatePath(obj);
        }

        UpdateCommand(obj);
        if (obj.Dead)
        {
            return;
        }

        if ((obj.Flags & MdkObject.FlagSwinging) != 0)
        {
            Swing(obj);
        }

        if ((obj.Flags & MdkObject.FlagGravity) != 0)
        {
            obj.Velocity.Z -= obj.Gravity * Dt;
            Updraft(obj);
            obj.Velocity.Z = MathF.Max(obj.Velocity.Z, TerminalVelocity);
        }

        ApplyVelocity(obj);
        if (obj.Dead)
        {
            return;
        }

        if ((obj.Flags & MdkObject.FlagChangesArena) != 0)
        {
            runtime.FollowArenas(obj);
        }

        // Kurt's items and effects (0x1000), or else pickups (0x200000).
        if (obj.ThrownKind > 0)
        {
            runtime.Items.UpdateThrown(obj);
            if (obj.Dead)
            {
                return;
            }
        }
        else if ((obj.Flags & MdkObject.FlagPickup) != 0)
        {
            runtime.Behaviors.UpdatePickup(obj);
        }
        else if ((obj.Flags & MdkObject.FlagCow) != 0)
        {
            runtime.Behaviors.UpdateCow(obj);
            if (obj.Dead)
            {
                return;
            }
        }

        var time = obj.AnimationTime;
        obj.AdvanceAnimation(Dt);
        if (obj.FrameSound.Length != 0 && obj.Animation != null && time < obj.FrameSoundFrame
            && obj.FrameSoundFrame <= time + Dt * obj.AnimationRate * obj.Animation.Speed)
        {
            runtime.PlaySound(obj, obj.FrameSound, 0, null);
            obj.FrameSound = "";
        }

        if ((obj.Flags & MdkObject.FlagRolling) != 0)
        {
            Roll(obj);
        }

        Bank(obj);
        obj.PreviousPosition = obj.Position;
    }

    // Spline paths: u32 key count, then keys of 40 bytes: s32 frame, position, in tangent and out
    // tangent. Segments are cubic Hermite curves.

    public int PathKeyCount(int path) => (int)Bin.U32(runtime.Cmi.Bytes, path);

    public int PathKeyFrame(int path, int key) => Bin.S32(runtime.Cmi.Bytes, path + PathCountSize + key * PathKeySize);

    private Vector3 PathVector(int path, int key, int field)
    {
        var bytes = runtime.Cmi.Bytes;
        var offset = path + PathCountSize + key * PathKeySize + PathFrameSize + field * PathVectorSize;
        return new Vector3(Bin.F32(bytes, offset), Bin.F32(bytes, offset + 4), Bin.F32(bytes, offset + 8));
    }

    /// <summary>Fans lift objects with gravity (0x45e74c; not rolling objects in LEVEL6); Kurt's thrown
    /// items (but the mortar) also lose most of their horizontal speed in them.</summary>
    private void Updraft(MdkObject obj)
    {
        if (runtime.Level.Number == LevelSix && (obj.Flags & MdkObject.FlagRolling) != 0)
        {
            return;
        }

        var vz = runtime.Fans.Query(obj.Arena, obj.Position, obj.Velocity.Z, Fans.MaskObjects, Dt);
        if (float.IsNaN(vz))
        {
            return;
        }

        obj.Velocity.Z = vz;
        if ((obj.Flags & ThrownFlagsMask) == Items.FlagThrown && obj.ThrownKind != MortarItem)
        {
            obj.Velocity.X *= UpdraftBrake;
            obj.Velocity.Y *= UpdraftBrake;
        }
    }

    /// <summary>path_speed_by_kurt (opcode 164, 0x43c258): the path speed goes towards the speed for
    /// Kurt being ahead of, around, or behind the set distance (along the object's heading), by
    /// (near - far) × 0.5 per second.</summary>
    private void UpdatePathSpeed(MdkObject obj)
    {
        const float Around = 5f;
        var heading = FromAngle(obj.Yaw);
        var ahead = (obj.Position.X - runtime.KurtPosition.X) * heading.X + (obj.Position.Y - runtime.KurtPosition.Y) * heading.Y;
        var speeds = obj.PathSpeeds;
        var distance = speeds[0];
        var target = speeds[1];
        if (ahead <= distance + Around)
        {
            target = ahead >= distance - Around ? speeds[2] : speeds[3];
        }

        var step = (speeds[3] - speeds[1]) * Dt * 0.5f;
        obj.PathSpeed = obj.PathSpeed <= target ? MathF.Min(obj.PathSpeed + step, target) : MathF.Max(obj.PathSpeed - step, target);
    }

    /// <summary>Position on a path at a time (in frames), relative to its origin (spline_eval 0x43c0f8).</summary>
    public Vector3 PathPosition(int path, float time)
    {
        var key = PathKeyCount(path) - 2;
        while (key > 0 && PathKeyFrame(path, key) >= time)
        {
            key--;
        }

        key = Math.Max(key, 0);
        var start = PathKeyFrame(path, key);
        var length = PathKeyFrame(path, key + 1) - start;
        var u = length != 0 ? (time - start) / length : 0f;
        var p0 = PathVector(path, key, 0);
        var p1 = PathVector(path, key + 1, 0);
        var m0 = PathVector(path, key, 2);
        var m1 = PathVector(path, key + 1, 1);
        var d = p1 - p0;
        var a = m0 + m1 - d * 2f;
        var b = d * 3f - m0 * 2f - m1;
        return ((a * u + b) * u + m0) * u + p0;
    }

    private void UpdatePath(MdkObject obj)
    {
        var path = obj.Path;
        if (obj.PathStop >= 0 && Round(obj.PathTime) == obj.PathStop)
        {
            return;
        }

        var step = Ticks * obj.PathSpeed;
        var last = PathKeyFrame(path, PathKeyCount(path) - 1);
        float stop = obj.PathStop;
        var once = (obj.Flags & MdkObject.FlagPathOnce) != 0;
        if (step < 0f)
        {
            if (obj.PathStop < 0 || obj.PathTime < stop || stop < obj.PathTime + step)
            {
                obj.PathTime += step;
            }
            else
            {
                obj.PathTime = stop;
            }

            if (obj.PathTime < 0f)
            {
                if (once)
                {
                    obj.Path = 0;
                    obj.PathTime = 0f;
                }
                else
                {
                    obj.PathTime += last - 1;
                }
            }
        }
        else
        {
            if (obj.PathStop < 0 || stop < obj.PathTime || obj.PathTime + step < stop)
            {
                obj.PathTime += step;
            }
            else
            {
                obj.PathTime = stop;
            }

            if (once)
            {
                if (obj.PathTime >= last - 2)
                {
                    obj.Path = 0;
                    obj.PathTime = last - 2;
                }
            }
            else if (obj.PathTime >= last - 1)
            {
                obj.PathTime -= last - 1;
            }
        }

        var old = obj.Position;
        var p = PathPosition(path, obj.PathTime) + obj.PathOrigin;
        var pushes = (obj.Flags & MdkObject.FlagPathPushes) != 0;
        if (pushes)
        {
            p.Z = old.Z;
        }

        if ((obj.Flags & MdkObject.FlagNoTurning) == 0 && MathF.Abs(p.X - old.X) + MathF.Abs(p.Y - old.Y) > Dt * 0.5f)
        {
            obj.Yaw = Wrap360(Heading(old, p) + obj.PathYawOffset);
        }

        if (!pushes)
        {
            obj.Position = p;
            return;
        }

        // The path drives the horizontal velocity instead of the position.
        obj.Push.X += (p.X - old.X) / Dt;
        obj.Push.Y += (p.Y - old.Y) / Dt;
    }

    /// <summary>Starts following a path (follow_path): time in frames, origin = the path's offset.</summary>
    public void StartPath(MdkObject obj, int path, float time, Vector3 origin)
    {
        obj.Path = path;
        obj.PathTime = time;
        obj.PathOrigin = origin;
        obj.PathStop = -1;
        obj.Position = PathPosition(path, time) + origin;
    }

    // Movement commands.

    private void UpdateCommand(MdkObject obj)
    {
        const float ChaseAbove = 8f;
        const int AlarmEvery = 31;
        const int AlarmTicks = 10;
        switch (obj.MoveCommand)
        {
            case 1:
                FollowFormation(obj);
                break;
            case 6:
                var target = runtime.TargetPosition;
                Chase(obj, target with { Z = target.Z + ChaseAbove });
                break;
            case 43:
            case 78:
            case 197:
                var yaw = obj.Yaw;
                var walking = (obj.Flags & MdkObject.FlagGravity) != 0;
                if (walking)
                {
                    Walk(obj);
                }
                else
                {
                    Fly(obj);
                }

                if (obj.MoveCommand != 0)
                {
                    DetectStuck(obj, walking ? Moving.Walking : Moving.Flying);
                    HandleStuck(obj, MathF.Abs(WrapAngle(obj.Yaw - yaw)));
                }

                break;
            case 61:
                FlyProjectile(obj);
                break;
            case 74:
                FollowAttachment(obj);
                break;
            case 88:
                MoveForward(obj);
                break;
            case 30:
                UpdateChain(obj);
                break;
            case 15:
                // The alarm: ALERT every 32 ticks, and if_alarm holds for 10 ticks.
                if (obj.Arena != runtime.CurrentArena)
                {
                    break;
                }

                if ((runtime.TickCount & AlarmEvery) == 0)
                {
                    runtime.PlaySoundAt("ALERT", obj.Position);
                }

                runtime.AlarmTicks = AlarmTicks;
                break;
            case 229:
                obj.ParameterTimer += Dt;
                if (obj.ParameterTimer >= obj.Parameter * 0.5f && (obj.ContactFlags & MdkObject.ContactFloor) != 0)
                {
                    obj.MoveCommand = 0;
                    obj.Velocity = Vector3.Zero;
                }

                break;
        }
    }

    /// <summary>Rolling (0x4602c8): the object turns by distance / (2π × radius) turns about the
    /// horizontal axis across its motion.</summary>
    private static void Roll(MdkObject obj)
    {
        var moved = new Vector2(obj.Position.X - obj.PreviousPosition.X, obj.Position.Y - obj.PreviousPosition.Y);
        var distance = moved.Length();
        if (distance <= 0f)
        {
            return;
        }

        var radius = obj.RollRadius > 0f ? obj.RollRadius : 1f;
        var axis = new Vector3(-moved.Y, moved.X, 0f) / distance;
        obj.RollingBasis = Orthonormal(obj.RollingBasis * Matrix4x4.CreateFromAxisAngle(axis, distance / radius));
    }

    /// <summary>A rotation's axes made unit and perpendicular again (Gram-Schmidt): float products
    /// drift, skewing and scaling a long-rolling model.</summary>
    private static Matrix4x4 Orthonormal(Matrix4x4 m)
    {
        var x = Vector3.Normalize(new Vector3(m.M11, m.M12, m.M13));
        var y = new Vector3(m.M21, m.M22, m.M23);
        y = Vector3.Normalize(y - Vector3.Dot(y, x) * x);
        var z = Vector3.Cross(x, y);
        return new Matrix4x4(x.X, x.Y, x.Z, 0f, y.X, y.Y, y.Z, 0f, z.X, z.Y, z.Z, 0f, 0f, 0f, 0f, 1f);
    }

    /// <summary>turn_and_jump_to_dest (opcode 229, 0x460d24): turns towards the destination by at most
    /// the rate (degrees per second); once facing it, jumps there (movement command 229). The top of
    /// the jump is 10 above the higher end and at least 30 above the lower one; the flight time allows
    /// for the friction slowing the object down.</summary>
    public void TurnAndJump(MdkObject obj, float rate)
    {
        const int JumpCommand = 229;
        const float AboveHigher = 10f;
        const float AboveLower = 30f;
        var heading = obj.YawTo(obj.MoveDestination);
        var diff = WrapAngle(heading - obj.Yaw);
        if (MathF.Abs(diff) > rate * Dt)
        {
            obj.Yaw = Wrap360(obj.Yaw + MathF.Sign(diff) * rate * Dt);
            return;
        }

        obj.Yaw = heading;
        obj.MoveCommand = JumpCommand;
        var z = obj.Position.Z;
        var goal = obj.MoveDestination.Z;
        var top = goal < z ? MathF.Max(z + AboveHigher, goal + AboveLower) : MathF.Max(goal + AboveHigher, z + AboveLower);
        var g = obj.Gravity;
        var up = MathF.Sqrt((top - z) * 2f * g);
        var time = (up + MathF.Sqrt(MathF.Max(up * up + (z - goal) * g * 2f, 0f))) / g;
        obj.Parameter = time;
        obj.ParameterTimer = 0f;
        var offset = new Vector2(obj.MoveDestination.X - obj.Position.X, obj.MoveDestination.Y - obj.Position.Y);
        var distance = offset.Length();
        var speed = (obj.Friction * 0.5f * time * time + distance) / time;
        var direction = distance > 0f ? offset / distance : Vector2.Zero;
        obj.Velocity = new Vector3(direction.X * speed, direction.Y * speed, up);
    }

    /// <summary>A chain link (movement command 30, spawn_chain): the links hang off the head object,
    /// each turned 90° more than the one before, with its first reference point on the previous one's
    /// second. Only the first link (id 0) places the chain; a link whose leader or any link before it
    /// is gone detaches and stops.</summary>
    private void UpdateChain(MdkObject link)
    {
        const float LinkTurn = 90f;
        var leader = link.Leader;
        if (leader is null or { Dead: true })
        {
            link.Leader = null;
            link.MoveCommand = 0;
            return;
        }

        var links = runtime.Objects.Where(other => other.Leader == leader && !other.Dead).OrderBy(other => other.InstanceId).ToList();
        for (var i = 0; i < link.InstanceId; i++)
        {
            if (i >= links.Count || links[i].InstanceId != i)
            {
                link.Leader = null;
                link.MoveCommand = 0;
                return;
            }
        }

        if (link.InstanceId != 0)
        {
            return;
        }

        var previous = leader;
        for (var i = 0; i < links.Count && links[i].InstanceId == i; i++)
        {
            var current = links[i];
            current.Yaw = Wrap360(leader.Yaw + LinkTurn * i);
            current.Position += previous.ReferencePoint(1) - current.ReferencePoint(0);
            previous = current;
        }
    }

    /// <summary>A pendulum (0x43cfe8, flag 0x400000 set by jump_to): the pitch swings by
    /// ω -= sin θ·k·t, θ += ω·k·t (t in ticks) and the object hangs on its rope below the pivot, in the
    /// vertical plane of its yaw. At each turning point the plane turns towards Kurt by at most 15°
    /// (folded to ±90°: either side will do), at 10° per second.
    /// <code>
    ///        pivot
    ///          │ \  rope
    ///          │  \ θ = pitch
    ///          │   ● object
    /// </code></summary>
    private void Swing(MdkObject obj)
    {
        const float MaxTurn = 15f;
        const float QuarterTurn = 90f;
        const float PlaneTurnSpeed = 10f;
        var oldSpeed = obj.SwingSpeed;
        obj.SwingSpeed -= MathF.Sin(float.DegreesToRadians(obj.Pitch)) * obj.SwingGain * Ticks;
        obj.Pitch += obj.SwingSpeed * obj.SwingGain * Ticks;
        var (sin, cos) = MathF.SinCos(float.DegreesToRadians(obj.Pitch));
        var heading = FromAngle(obj.Yaw);
        obj.Position = obj.SwingPivot + new Vector3(heading.X * sin, heading.Y * sin, -cos) * obj.SwingLength;
        if (obj.SwingSpeed * oldSpeed <= 0f)
        {
            obj.Yaw = Wrap360(obj.Yaw);
            var diff = WrapAngle(obj.YawTo(runtime.TargetPosition) - obj.Yaw);
            if (diff < -QuarterTurn)
            {
                diff += HalfTurn;
            }
            else if (diff > QuarterTurn)
            {
                diff -= HalfTurn;
            }

            obj.SwingYaw = obj.Yaw + Math.Clamp(diff, -MaxTurn, MaxTurn);
        }

        obj.Yaw = MoveToward(obj.Yaw, obj.SwingYaw, PlaneTurnSpeed * Dt);
        obj.RopeColor = 1;
        obj.RopeMask = 1;
        obj.RopePoints[0] = obj.Position;
        obj.RopePoints[1] = obj.SwingPivot;
    }

    /// <summary>plan_move (0x45a1dc), when a walk or flight starts (move_to, move_near_target,
    /// move_to_bomb, command_objects 43): if the line from the object to the destination (both 8 units
    /// up) is blocked, tries detour points on either side of its middle, 0.1 to 1.1 times its length
    /// away, and takes the first one seen from both ends. The original offsets them along (sin a, cos a)
    /// for a heading a, which is only sideways when the line runs along an axis.</summary>
    public void PlanMove(MdkObject obj)
    {
        const int Detours = 11;
        obj.Waypoint = obj.MoveDestination;
        obj.StuckCount = 0;
        obj.StuckTicks = 0f;
        obj.StuckMoved = Vector3.Zero;
        obj.ContactFlags &= ~MdkObject.ContactStuck;
        var up = new Vector3(0f, 0f, PlanHeight);
        var a = obj.Position + up;
        var b = obj.MoveDestination + up;
        if (runtime.Raycast(a, b) == null)
        {
            return;
        }

        var middle = (a + b) * 0.5f;
        for (var i = 0; i < Detours; i++)
        {
            foreach (var side in Sides)
            {
                var point = Detour(a, b, middle, 0.1f * (i + 1) * side);
                if (Clear(a, point, b))
                {
                    obj.Waypoint = point;
                    return;
                }
            }
        }
    }

    private static readonly float[] Sides = [-1f, 1f];

    /// <summary>The stuck replan (0x45a434): an object heading to a detour gives it up and heads for the
    /// destination again; one heading for the destination tries detours around the point 3/4 of the
    /// way there, then around itself (0.1 to 1.9 times the distance, both sides).</summary>
    private void Replan(MdkObject obj)
    {
        const int Detours = 7;
        if (obj.Waypoint != obj.MoveDestination)
        {
            obj.Waypoint = obj.MoveDestination;
            obj.StuckTicks = 0f;
            obj.StuckMoved = Vector3.Zero;
            obj.ContactFlags &= ~MdkObject.ContactStuck;
            return;
        }

        var up = new Vector3(0f, 0f, PlanHeight);
        var a = obj.Position + up;
        var b = obj.MoveDestination + up;
        foreach (var origin in new[] { b * 0.75f + a * 0.25f, a })
        {
            for (var i = 0; i < Detours; i++)
            {
                foreach (var side in Sides)
                {
                    var point = Detour(a, b, origin, (0.1f + 0.3f * i) * side);
                    if (Clear(a, point, b))
                    {
                        obj.Waypoint = point;
                        return;
                    }
                }
            }
        }
    }

    private static Vector3 Detour(Vector3 a, Vector3 b, Vector3 origin, float amount)
    {
        var (sin, cos) = MathF.SinCos(MathF.Atan2(b.Y - a.Y, b.X - a.X));
        return origin + new Vector3(sin, cos, 0f) * amount * Vector3.Distance(a, b);
    }

    private bool Clear(Vector3 a, Vector3 point, Vector3 b) => runtime.Raycast(a, point) == null && runtime.Raycast(point, b) == null;

    private enum Moving { Walking, Flying }

    /// <summary>Stuck detection after walking or flying (0x45a7d4, 0x45ae74). Walking with command 43,
    /// any collision counts as stuck. Otherwise an object that collides while faster than 2 is stuck
    /// when it has moved less than half its speed over a window of 16 ticks (9 walking with 197).</summary>
    private static void DetectStuck(MdkObject obj, Moving moving)
    {
        const float StuckSpeed = 2f;
        const float Window = 16f;
        const float AdvanceWindow = 9f;
        const float CoverStuckTicks = 30f;
        var collided = (obj.ContactFlags & MdkObject.ContactCollided) != 0;
        var walking = moving == Moving.Walking;
        if (walking && obj.MoveCommand == 43)
        {
            if (collided)
            {
                obj.ContactFlags |= MdkObject.ContactStuck;
                obj.StuckTicks = CoverStuckTicks;
                return;
            }

            ResetStuck(obj);
            return;
        }

        if (!collided || obj.Speed <= StuckSpeed)
        {
            ResetStuck(obj);
            return;
        }

        var window = walking && obj.MoveCommand == 197 ? AdvanceWindow : Window;
        if (obj.StuckTicks < window)
        {
            obj.StuckMoved += obj.Position - obj.PreviousPosition;
            obj.StuckTicks += Ticks;
            return;
        }

        var moved = obj.StuckMoved;
        if (MathF.Abs(moved.X) + MathF.Abs(moved.Y) + MathF.Abs(moved.Z) >= obj.Speed * 0.5f)
        {
            obj.StuckTicks = 0f;
            obj.StuckMoved = Vector3.Zero;
            return;
        }

        obj.ContactFlags |= MdkObject.ContactStuck;
    }

    private static void ResetStuck(MdkObject obj)
    {
        obj.StuckCount = 0;
        obj.StuckTicks = 0f;
        obj.StuckMoved = Vector3.Zero;
    }

    /// <summary>A stuck object that isn't turning (less than 3° this update, 0x45b6c8) replans once;
    /// after that move_to (78) slides straight to the waypoint through everything and the others give
    /// up (if_move_idle_flag sees StuckCount). Command 197 gives up at once.</summary>
    private void HandleStuck(MdkObject obj, float turned)
    {
        const float Turning = 3f;
        const int Replans = 3;
        if ((obj.ContactFlags & MdkObject.ContactStuck) == 0 || turned >= Turning)
        {
            return;
        }

        obj.StuckCount++;
        if (obj.MoveCommand == 197)
        {
            Stop(obj);
        }
        else if (obj.StuckCount < Replans)
        {
            Replan(obj);
            obj.StuckCount++;
        }
        else if (obj.MoveCommand == 78)
        {
            var toWaypoint = obj.Waypoint - obj.Position;
            var step = obj.Speed * Dt;
            obj.Position += Vector3.Clamp(toWaypoint, new Vector3(-step), new Vector3(step));
        }
        else
        {
            obj.MoveCommand = 0;
            obj.Speed = 0f;
        }
    }

    /// <summary>Automatic banking (0x43b65c): objects lean into their turns, roll = (roll - turn per
    /// tick) × 0.95 within ±10° (the turn counted at most 2° per tick), unless flag 0x80 (set_banking
    /// 0) or flag 1 is set.</summary>
    private static void Bank(MdkObject obj)
    {
        const float MaxTurn = 2f;
        const float MaxRoll = 10f;
        const float Damping = 0.95f;
        const int NoRoll = 1;
        var turn = Math.Clamp(WrapAngle(obj.Yaw - obj.BankingYaw) / Ticks, -MaxTurn, MaxTurn);
        obj.BankingYaw = obj.Yaw;
        if ((obj.Flags & (MdkObject.FlagNoBanking | NoRoll | MdkObject.FlagRolling)) != 0 || obj.ThrownKind > 0)
        {
            return;
        }

        obj.Roll = Math.Clamp((WrapAngle(obj.Roll) - turn) * Damping, -MaxRoll, MaxRoll);
    }

    /// <summary>Turns the object towards a point by at most TurnSpeed × dt. Returns the angle that was
    /// left to turn (before turning).</summary>
    public static float TurnTowards(MdkObject obj, Vector3 point)
    {
        var targetYaw = obj.Yaw;
        if (point.X != obj.Position.X || point.Y != obj.Position.Y)
        {
            targetYaw = obj.YawTo(point);
        }

        var diff = WrapAngle(targetYaw - obj.Yaw);
        var maxStep = TurnSpeed * Dt;
        obj.Yaw = Wrap360(obj.Yaw + Math.Clamp(diff, -maxStep, maxStep));
        return diff;
    }

    private static void Accelerate(MdkObject obj, float targetSpeed)
    {
        if (obj.Speed > targetSpeed)
        {
            obj.Speed = MathF.Max(obj.Speed - obj.Deceleration * Dt, targetSpeed);
        }
        else if (obj.Speed < targetSpeed)
        {
            obj.Speed = MathF.Min(obj.Speed + obj.Acceleration * Dt, targetSpeed);
        }
    }

    private static void Stop(MdkObject obj)
    {
        obj.MoveCommand = 0;
        obj.Speed = 0f;
        obj.ContactFlags &= ~MdkObject.ContactStuck;
    }

    /// <summary>The speed while turning: half when the waypoint is far to the side.</summary>
    private static float TurnSpeedFactor(float diff) => MathF.Abs(diff) > SlowTurnAngle ? 0.5f : 1f;

    /// <summary>Walks on the ground towards the waypoint, then the destination (0x45a7d4).</summary>
    private static void Walk(MdkObject obj)
    {
        const float Arrived = 5f;
        var final = obj.Waypoint == obj.MoveDestination;
        if (MathF.Abs(obj.Waypoint.X - obj.Position.X) + MathF.Abs(obj.Waypoint.Y - obj.Position.Y) < Arrived)
        {
            if (final)
            {
                Stop(obj);
                return;
            }

            obj.Waypoint = obj.MoveDestination;
        }

        var diff = TurnTowards(obj, obj.Waypoint);
        Accelerate(obj, obj.MaxSpeed * TurnSpeedFactor(diff));
        var direction = FromAngle(obj.Yaw);
        obj.Push.X += obj.Speed * direction.X;
        obj.Push.Y += obj.Speed * direction.Y;
    }

    /// <summary>Flies towards the waypoint, then the destination (0x45ae74).</summary>
    private static void Fly(MdkObject obj)
    {
        const float TurnFrom = 3f;
        const float ArrivedAcross = 4f;
        const float ArrivedUp = 3f;
        var final = obj.Waypoint == obj.MoveDestination;
        var toWaypoint = obj.Waypoint - obj.Position;
        var diff = 0f;
        var noTurning = (obj.Flags & MdkObject.FlagNoTurning) != 0;
        if (!noTurning && MathF.Abs(toWaypoint.X) + MathF.Abs(toWaypoint.Y) > TurnFrom)
        {
            diff = TurnTowards(obj, obj.Waypoint);
        }

        Accelerate(obj, obj.MaxSpeed * TurnSpeedFactor(diff));
        if (!noTurning)
        {
            var horizontal = new Vector2(toWaypoint.X, toWaypoint.Y).Length();
            var vertical = MathF.Abs(toWaypoint.Z);
            var climb = horizontal + vertical > 0f ? vertical / (horizontal + vertical) : 0f;
            var direction = FromAngle(obj.Yaw) * obj.Speed * (1f - climb);
            obj.Push += new Vector3(direction.X, direction.Y, obj.Speed * climb * MathF.Sign(toWaypoint.Z));
            if (horizontal < ArrivedAcross && vertical < ArrivedUp)
            {
                if (final)
                {
                    Stop(obj);
                }
                else
                {
                    obj.Waypoint = obj.MoveDestination;
                }
            }

            return;
        }

        var distance = MathF.Abs(toWaypoint.X) + MathF.Abs(toWaypoint.Y) + MathF.Abs(toWaypoint.Z);
        if (distance < 1f)
        {
            if (final)
            {
                obj.Position = obj.Waypoint;
                Stop(obj);
                return;
            }

            obj.Waypoint = obj.MoveDestination;
        }

        if (distance <= 0f)
        {
            return;
        }

        var step = toWaypoint * (obj.Speed * Dt / distance);
        for (var axis = 0; axis < 3; axis++)
        {
            if (MathF.Abs(step[axis]) > MathF.Abs(toWaypoint[axis]))
            {
                step[axis] = toWaypoint[axis];
            }
        }

        obj.Push += step / Dt;
    }

    /// <summary>Chases a point, flying at a speed in units per tick (movement command 6, 0x45e448).</summary>
    private static void Chase(MdkObject obj, Vector3 point)
    {
        const float Behind = 90f;
        const float Far = 30f;
        const float Ahead = 22.5f;
        const float Gain = 0.0444444f / 6f;
        const float MaxChase = 2.3333333f;
        const float Brake = 0.05f;
        const float MinChase = 0.3333333f;
        const float Climb = 0.25f;
        var targetYaw = obj.Yaw;
        if (point.X != obj.Position.X || point.Y != obj.Position.Y)
        {
            targetYaw = obj.YawTo(point);
        }

        var offAngle = MathF.Abs(WrapAngle(obj.Yaw - targetYaw));
        var chaseSpeed = obj.Speed;
        if (offAngle < Behind || MathF.Abs(obj.Position.X - point.X) > Far || MathF.Abs(obj.Position.Y - point.Y) > Far)
        {
            TurnTowards(obj, point);
            chaseSpeed = offAngle <= Ahead
                ? MathF.Min(chaseSpeed + (Ahead - offAngle) * Ticks * Gain, MaxChase)
                : MathF.Max(chaseSpeed - Ticks * Brake, MinChase);
            obj.Speed = chaseSpeed;
        }

        var step = chaseSpeed * Ticks;
        var direction = FromAngle(obj.Yaw) * step;
        obj.Position.X += direction.X;
        obj.Position.Y += direction.Y;
        obj.Position.Z = MoveToward(obj.Position.Z, point.Z, step * Climb);
    }

    /// <summary>Keeps a formation position around the leader (movement command 1).</summary>
    private static void FollowFormation(MdkObject obj)
    {
        var leader = obj.Leader;
        if (leader is null or { Dead: true })
        {
            obj.MoveCommand = 0;
            return;
        }

        var destination = FormationPosition(leader, obj.Waypoint);
        obj.MoveDestination = destination;
        var toDestination = destination - obj.Position;
        var maxStep = obj.MaxSpeed * Dt;
        if (toDestination.LengthSquared() < maxStep * maxStep)
        {
            obj.Position = destination;
            obj.Yaw = MoveToward(obj.Yaw, leader.Yaw, TurnSpeed * Dt);
            return;
        }

        TurnTowards(obj, destination);
        var direction = FromAngle(obj.Yaw) * obj.MaxSpeed;
        obj.Push.X += direction.X;
        obj.Push.Y += direction.Y;
        if (obj.Position.Z <= destination.Z - Ticks)
        {
            obj.Push.Z += Ticks;
        }
        else if (obj.Position.Z >= destination.Z + Ticks)
        {
            obj.Push.Z -= Ticks;
        }
        else
        {
            obj.Push.Z = destination.Z - obj.Position.Z;
        }
    }

    /// <summary>Position of a formation offset (obj+0x12c) around a leader.</summary>
    public static Vector3 FormationPosition(MdkObject leader, Vector3 offset)
    {
        var (s, c) = MathF.SinCos(float.DegreesToRadians(leader.Yaw));
        var p = leader.Position;
        return new Vector3(p.X + offset.X * s - offset.Y * c, p.Y - offset.Y * s - offset.X * c, p.Z + offset.Z);
    }

    /// <summary>Moves along the yaw and pitch (movement command 88, set_move_x).</summary>
    private static void MoveForward(MdkObject obj)
    {
        var targetSpeed = obj.MoveParameter != 0 ? obj.MaxSpeed : 0f;
        Accelerate(obj, targetSpeed);
        var direction = FromAngle(obj.Yaw);
        if ((obj.Flags & MdkObject.FlagGravity) != 0)
        {
            obj.Push.X += obj.Speed * direction.X;
            obj.Push.Y += obj.Speed * direction.Y;
        }
        else
        {
            var (sin, cos) = MathF.SinCos(float.DegreesToRadians(obj.Pitch));
            obj.Push += new Vector3(direction.X * cos, direction.Y * cos, sin) * obj.Speed;
        }

        if (obj.MoveParameter == 0 && obj.Speed == targetSpeed)
        {
            obj.MoveCommand = 0;
        }
    }

    /// <summary>Stays attached to another object (movement command 74, attach_to): reference point A
    /// of this object is kept on point B of the other one.</summary>
    private static void FollowAttachment(MdkObject obj)
    {
        var other = obj.Leader;
        if (other is null or { Dead: true } or { Health: 0 })
        {
            obj.MoveCommand = 0;
            return;
        }

        obj.Position += other.ReferencePoint(obj.AttachPoints.B) - obj.ReferencePoint(obj.AttachPoints.A);
        obj.Yaw = other.Yaw;
        obj.Pitch = other.Pitch;
    }

    /// <summary>Flies straight along the yaw and pitch until the lifetime (obj+0x302) runs out or
    /// something is hit (movement command 61, 0x45b6c8).</summary>
    private void FlyProjectile(MdkObject obj)
    {
        const float KurtMargin = 1f;
        var old = obj.Position;
        var (yawSin, yawCos) = MathF.SinCos(float.DegreesToRadians(obj.Yaw));
        var (pitchSin, pitchCos) = MathF.SinCos(float.DegreesToRadians(obj.Pitch));
        obj.Position += new Vector3(yawCos * pitchCos, yawSin * pitchCos, pitchSin) * obj.Speed * Dt;
        obj.Parameter -= Dt;
        if (obj.Parameter <= 0f)
        {
            runtime.Kill(obj);
            return;
        }

        if (runtime.Raycast(old, obj.Position) is { } hit)
        {
            obj.Position = hit.Point;
            runtime.Kill(obj);
            return;
        }

        var touch = runtime.GetKurtBox().Grow(KurtMargin).SegmentEntry(old, obj.Position);
        if (touch is { } point)
        {
            obj.ContactFlags |= MdkObject.ContactTouchedKurt;
            obj.Position = point;
            if (obj.TouchDamage > 0)
            {
                runtime.HurtKurt(obj.TouchDamage);
                runtime.Kill(obj);
            }
        }
        else
        {
            obj.ContactFlags &= ~MdkObject.ContactTouchedKurt;
        }
    }

    // Velocity.

    /// <summary>Applies friction, then moves by the velocity and this frame's push, colliding with the
    /// arena when the object has FlagCollides (0x45e810).</summary>
    private void ApplyVelocity(MdkObject obj)
    {
        obj.ContactFlags &= ~(MdkObject.ContactCollided | MdkObject.ContactFloor | ContactCleared);
        var gravity = (obj.Flags & MdkObject.FlagGravity) != 0;
        var moving = gravity ? obj.Velocity with { Z = 0f } : obj.Velocity;
        var length = moving.Length();
        if (length > 0f)
        {
            var factor = MathF.Max(length - obj.Friction * Dt, 0f) / length;
            obj.Velocity.X *= factor;
            obj.Velocity.Y *= factor;
            if (!gravity)
            {
                obj.Velocity.Z *= factor;
            }
        }

        var motion = (obj.Velocity + obj.Push) * Dt;
        obj.Push = Vector3.Zero;
        if (motion == Vector3.Zero)
        {
            return;
        }

        if ((obj.Flags & MdkObject.FlagCollides) == 0)
        {
            obj.Position += motion;
        }
        else if (Sweep(obj, motion) is { } normal)
        {
            obj.ContactFlags |= MdkObject.ContactCollided;
            if (motion.Z <= 0f)
            {
                obj.ContactFlags |= MdkObject.ContactFloor;
            }

            if ((obj.Flags & MdkObject.FlagBounces) != 0)
            {
                obj.Velocity += normal * Vector3.Dot(obj.Velocity, normal) * Bounce;
            }
            else
            {
                obj.Velocity.Z = 0f;
            }
        }

        if (obj.Position.Z < runtime.GetArenaFloor(obj.Arena) - FallOutDepth)
        {
            runtime.FallOut(obj);
        }
    }

    /// <summary>The collision bounds of an object without a model.</summary>
    private static readonly Box DefaultBounds = new(new Vector3(-1f, -1f, 0f), new Vector3(1f, 1f, 4f));

    /// <summary>Sweeps the object's collision box by the motion through its arena's BSP (0x45fec4),
    /// sliding along what it hits. Returns the plane normal of the first hit, or null.
    /// <code>
    ///   box: half extents = world bounds × 0.25 across, half the origin-to-top height,
    ///        bottom 0.05 above the origin when moving vertically (0.5 otherwise)
    /// </code></summary>
    private Vector3? Sweep(MdkObject obj, Vector3 motion)
    {
        const float WidthFactor = 0.25f;
        const float MinHeight = 0.5f;
        const float VerticalLift = 0.05f;
        const float Lift = 0.5f;
        var bsp = runtime.BspOf(obj.Arena);
        if (bsp == null)
        {
            obj.Position += motion;
            return null;
        }

        var bounds = obj.Model != null ? obj.PoseBounds() : DefaultBounds;
        var (s, c) = MathF.SinCos(float.DegreesToRadians(obj.Yaw));
        (s, c) = (MathF.Abs(s), MathF.Abs(c));
        var size = bounds.Size() * obj.Scale;
        var half = new Vector3((c * size.X + s * size.Y) * WidthFactor, (s * size.X + c * size.Y) * WidthFactor,
            MathF.Max(bounds.Max.Z * obj.Scale + obj.Lift, MinHeight) * 0.5f);
        var center = RotatedZ(bounds.Center(), obj.Yaw) * obj.Scale;
        center.Z = half.Z + (motion.Z != 0f ? VerticalLift : Lift);
        var a = obj.Position + center;

        // The first contact counts, even if the box slid off it and ended free.
        Vector3? first = null;
        bsp.SweepBox(a, a + motion, half, SweepIterations, SweepSlide, out var end, out _,
            contact => first ??= contact.Node >= 0 ? bsp.Arena.Nodes[contact.Node].Normal : Vector3.UnitZ);
        obj.Position += end - a;
        return first;
    }
}
