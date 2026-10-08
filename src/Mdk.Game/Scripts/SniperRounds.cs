using System.Numerics;
using Mdk.Game.Objects;

namespace Mdk.Game.Scripts;

/// <summary>Kurt's sniper rounds (3 slots at 0x573c98, updated by 0x462708 after the objects; a port
/// of godot-mdk's sniper_rounds.gd, docs/gameplay.md "Sniper mode"). A round starts at the eye along
/// the view and is tested every tick along the segment it moved: objects (their boxes, then their
/// parts' faces), then the arena.
/// <code>
///   type 0  bullet          1100 u/s, 75 ticks, 8 damage
///   type 1  homing bullet    400 u/s, 240 ticks, steers at the locked object
///   type 2  grenade         as the bullet, explodes
///   type 3  homing grenade  as the homing bullet, explodes
///   type 4  mortar          thrown at 150 u/s, drag, gravity, bounces; bomb_follow_path guides it
///
///   FREE ──fire──► FLYING ──object──► HIT │ KILLED (camera watches 30 ticks)
///                    │    ──wall, end─► DEAD (miss) │ EXPLODED ──linger──► FREE
/// </code></summary>
public sealed class SniperRounds(ScriptRuntime runtime)
{
    public const int Slots = 3;

    /// <summary>What a round's camera shows (0x461bcc).</summary>
    public enum Shot { Empty, Watching, Hit, Kill, Miss }

    /// <summary>A round camera: from behind the round while it watches, then a colour or the miss
    /// animation (<paramref name="Time"/>: ticks since the miss).</summary>
    public readonly record struct RoundCamera(Shot Shot, Vector3 Position, Vector3 Forward, float Time);

    private enum State { Free, Flying, Killed, Hit, Dead, Exploded }

    private enum Type { Bullet, Homing, Grenade, HomingGrenade, Mortar }

    private static readonly int[] Life = [75, 240, 75, 240, 450];
    private static readonly float[] Speed = [1100f, 400f, 1100f, 400f, 150f];
    private static readonly string[] Models = ["SW_SHOT", "SW_HOME", "SW_SGREN", "SW_HGREN", "SW_LGREN"];
    private const int Damage = 8;
    /// <summary>Objects this healthy aren't hurt by bullets.</summary>
    private const int Unhurt = 65000;
    /// <summary>Bullets and homing rounds spin 720° per second (not the mortar).</summary>
    private const float Spin = 720f;

    // Homing (0x463174): steering starts after 7 ticks; the yaw rate accelerates by 540°/s² up to
    // 270°/s, the pitch turns by at most 120°/s; the speed aims for 250 within 35° of the target,
    // else 100, rising by 200/s and falling by 500/s.
    private const float HomingStart = 233f;
    private const float HomingYawAcceleration = 540f;
    private const float HomingYawRate = 270f;
    private const float HomingPitchRate = 120f;
    private const float HomingAim = 35f;
    private const float HomingFast = 250f;
    private const float HomingSlow = 100f;
    private const float HomingSpeedUp = 200f;
    private const float HomingSlowDown = 500f;

    // The mortar (0x46360c): drag of 60 u/s² shared between the horizontal and vertical speeds,
    // gravity 32 u/s², bounces with v -= 1.75 (v·n) n; it settles after 15 ticks once it stops.
    private const float MortarDrag = 60f;
    private const float MortarGravity = 32f;
    private const float MortarMaxFall = 220f;
    private const float MortarBounce = 1.75f;
    private const float MortarSettleLift = 4f;
    private const float MortarSettleTicks = 15f;
    private const float MortarStillSpeed = 0.5f;
    private const float MortarStillRise = 1f;
    /// <summary>The half extents of the box the mortar sweeps through the arena (0x491f24).</summary>
    private static readonly Vector3 MortarBox = new(0.5f);
    /// <summary>A guided mortar round stays alive until its path's last key.</summary>
    private const float GuidedLife = 99f;

    // Explosions (0x4638cc): 150 damage to objects and triangle groups, 75 to Kurt, hit type -7,
    // within 25 (50 for the mortar), the effect at scale 2.
    private const int ExplosionDamage = 150;
    private const int ExplosionKurtDamage = 75;
    private const int HitExplosion = -7;
    private const float BlastRadius = 25f;
    private const float MortarBlastRadius = 50f;
    private const float ExplosionScale = 2f;
    private const int TargetsAll = 6;
    private const int TargetKurt = 1;

    /// <summary>Ticks a slot stays busy after its round ended, and the camera's watch after a hit.</summary>
    private const float MissLinger = 30f;
    private const float HitLinger = 45f;
    private const float WatchTicks = 30f;
    /// <summary>The round camera backs off by 10 u/s, up to 10 behind the round (0x461d80).</summary>
    private const float CameraBackSpeed = 10f;
    private const float CameraBack = 10f;
    /// <summary>Below the arena's lowest point by this much a round is lost (ObjectMotion's).</summary>
    private const float FallOutDepth = 200f;
    /// <summary>Objects flagged so are neither hit nor targets.</summary>
    private const int Untouchable = MdkObject.FlagNotSolid | MdkObject.FlagNotTarget;
    private const string HeadPart = "HEAD";

    private sealed class Round
    {
        public State State;
        public Type Type;
        public Vector3 Position;
        public float Yaw;
        /// <summary>Positive looks down.</summary>
        public float Pitch;
        public float Roll;
        public float Speed;
        /// <summary>The mortar's vertical speed.</summary>
        public float Vertical;
        public float Life;
        public float Linger;
        public MdkObject? Target;
        public int TargetPart = -1;
        public float YawRate;
        /// <summary>bomb_follow_path (opcode 28): the path and its time.</summary>
        public int Path;
        public float PathTime;
        public MdkObject? Visual;
        public float CameraDistance;
        public Vector3 CameraPosition;
        public float Watch;
    }

    private readonly Round[] _rounds = [new(), new(), new()];
    /// <summary>The mortar round that just hit a triangle group (0x491ef0, for bomb_follow_path).</summary>
    private Round? _lastMortar;

    /// <summary>The rounds' models in flight (drawn with the objects).</summary>
    public List<MdkObject> Visuals
    {
        get
        {
            _visuals.Clear();
            foreach (var round in _rounds)
            {
                if (round.State == State.Flying && round.Visual != null)
                {
                    _visuals.Add(round.Visual);
                }
            }

            return _visuals;
        }
    }

    /// <summary>The flying rounds' models (kept: no list per frame).</summary>
    private readonly List<MdkObject> _visuals = [];

    /// <summary>Fires a round of <paramref name="type"/> from <paramref name="eye"/> along the view
    /// (0x461e88); homing rounds chase <paramref name="target"/>. Returns false when all the slots are busy.</summary>
    public bool Fire(int type, Vector3 eye, float yaw, float pitch, MdkObject? target)
    {
        var round = Array.Find(_rounds, r => r.State == State.Free);
        if (round == null)
        {
            return false;
        }

        var kind = (Type)type;
        var homing = kind is Type.Homing or Type.HomingGrenade;
        round.State = State.Flying;
        round.Type = kind;
        round.Position = eye;
        round.Yaw = yaw;
        round.Pitch = pitch;
        round.Roll = 0f;
        round.Life = Life[type];
        round.Linger = 0f;
        round.Speed = Speed[type];
        round.Vertical = 0f;
        round.Path = 0;
        round.YawRate = 0f;
        round.Target = homing ? target : null;
        round.TargetPart = round.Target != null ? HeadOf(round.Target) : -1;
        round.CameraDistance = 0f;
        round.CameraPosition = eye;
        round.Watch = 0f;
        if (kind == Type.Mortar)
        {
            round.Speed = Speed[type] * MathF.Cos(float.DegreesToRadians(pitch));
            round.Vertical = -Speed[type] * MathF.Sin(float.DegreesToRadians(pitch));
        }

        round.Visual = MakeVisual(kind);
        return true;
    }

    /// <summary>What the camera of slot <paramref name="index"/> shows.</summary>
    public RoundCamera Camera(int index)
    {
        var round = _rounds[index];
        if (round.State == State.Free)
        {
            return new RoundCamera(Shot.Empty, default, default, 0f);
        }

        if (round.State == State.Flying || round.Watch > 0f)
        {
            return new RoundCamera(Shot.Watching, round.CameraPosition, Direction(round), 0f);
        }

        var shot = round.State switch
        {
            State.Hit => Shot.Hit,
            State.Dead => Shot.Miss,
            _ => Shot.Kill,
        };
        return new RoundCamera(shot, default, default, MissLinger - round.Linger);
    }

    /// <summary>bomb_follow_path (opcode 28): the mortar round that just hit a triangle group follows a path.</summary>
    public void GuideLastMortar(int path)
    {
        if (_lastMortar is not { State: State.Flying, Type: Type.Mortar, Path: 0 } round)
        {
            return;
        }

        round.Path = path;
        round.PathTime = 0f;
    }

    /// <summary>A part named HEAD is aimed at, else the whole object.</summary>
    private static int HeadOf(MdkObject obj) =>
        obj.Model?.PartList.FindIndex(p => p.Name.Contains(HeadPart, StringComparison.OrdinalIgnoreCase)) ?? -1;

    private MdkObject? MakeVisual(Type type)
    {
        var name = Models[(int)type];
        var model = runtime.FindModel(runtime.CurrentArena, name);
        if (model == null)
        {
            return null;
        }

        // The orientation is set as a whole (see VisualBasis).
        return new MdkObject { TypeName = name, Model = model, Arena = runtime.CurrentArena, Flags = MdkObject.FlagRolling };
    }

    /// <summary>Updates the rounds by <paramref name="ticks"/>.</summary>
    public void Update(float ticks)
    {
        var dt = ticks / Kurt.Kurt.Ticks;
        foreach (var round in _rounds)
        {
            if (round.State == State.Free)
            {
                continue;
            }

            if (round.State != State.Flying)
            {
                round.Linger -= ticks;
                round.Watch -= ticks;
                if (round.Linger <= 0f)
                {
                    round.State = State.Free;
                }

                continue;
            }

            Fly(round, ticks, dt);
        }
    }

    private void Fly(Round round, float ticks, float dt)
    {
        round.Life -= ticks;
        if (round.Type != Type.Mortar)
        {
            round.Roll = (round.Roll + Spin * dt) % 360f;
        }

        if (round.Path != 0)
        {
            FollowPath(round, ticks);
        }
        else if (round.Type == Type.Mortar)
        {
            MoveMortar(round, dt);
        }
        else
        {
            if (round.Target != null)
            {
                Steer(round, dt);
            }

            MoveStraight(round, dt);
        }

        if (round.State == State.Flying && round.Life <= 0f)
        {
            if (round.Type >= Type.Grenade)
            {
                Explode(round, round.Type == Type.Mortar ? MortarBlastRadius : BlastRadius, null);
            }
            else
            {
                End(round, State.Dead, MissLinger);
            }
        }

        if (round.State != State.Flying)
        {
            return;
        }

        round.CameraDistance = MathF.Min(round.CameraDistance + CameraBackSpeed * dt, CameraBack);
        round.CameraPosition = round.Position - Direction(round) * round.CameraDistance;
        if (round.Visual != null)
        {
            round.Visual.Position = round.Position;
            round.Visual.RollingBasis = VisualBasis(round);
        }
    }

    /// <summary>The round models stand upright (their length along +Z): the nose is turned from +Z
    /// to the flight direction, and the spin is around the length.</summary>
    private static Matrix4x4 VisualBasis(Round round) =>
        Matrix4x4.CreateRotationZ(float.DegreesToRadians(round.Roll))
        * Matrix4x4.CreateRotationY(MathF.PI / 2f + float.DegreesToRadians(round.Pitch))
        * Matrix4x4.CreateRotationZ(float.DegreesToRadians(round.Yaw));

    private static Vector3 Direction(Round round) => Kurt.Scope.Direction(round.Yaw, round.Pitch);

    /// <summary>A ray hit's normal, turned towards where the ray came from.</summary>
    private static Vector3 Facing(Vector3 normal, Vector3 motion) => Vector3.Dot(normal, motion) > 0f ? -normal : normal;

    /// <summary>Straight flight (0x462f24), tested against objects and the arena.</summary>
    private void MoveStraight(Round round, float dt)
    {
        var start = round.Position;
        var end = start + Direction(round) * round.Speed * dt;
        var hit = TestObjects(start, end);
        var wall = runtime.Raycast(start, end);
        if (hit is { } h && (wall is not { } w || Vector3.Distance(start, h.Point) < Vector3.Distance(start, w.Point)))
        {
            round.Position = h.Point;
            HitObject(round, h.Object, h.Part, h.Point);
            return;
        }

        if (wall is { } stop)
        {
            // Taken back a unit off the wall; a reacting group gets one orange spark, a wall 3 grey ones.
            round.Position = stop.Point + Facing(stop.Normal, end - start);
            var reacted = (runtime.HitGroupAt(stop, round.Type < Type.Grenade ? runtime.KurtDamage(Damage, ScriptRuntime.Indestructible) : 0, ScriptRuntime.HitShot, (int)round.Type) & 1) != 0;
            runtime.SparkAt(round.Position, reacted ? 1 : 3, "", reacted ? ScriptRuntime.Spark.Group : ScriptRuntime.Spark.Hard);
            if (round.Type >= Type.Grenade)
            {
                Explode(round, BlastRadius, null);
                return;
            }

            End(round, State.Dead, MissLinger);
            return;
        }

        round.Position = end;
        if (round.Position.Z < runtime.GetArenaFloor(runtime.CurrentArena) - FallOutDepth)
        {
            End(round, State.Dead, MissLinger);
        }
    }

    /// <summary>Homing (0x463028, 0x463174): the yaw rate accelerates towards the error (from 0
    /// when it has the wrong sign), never overshooting; the pitch turns at a limited rate.</summary>
    private void Steer(Round round, float dt)
    {
        var target = round.Target!;
        if (target.Dead)
        {
            round.Target = null;
            return;
        }

        if (round.Life > HomingStart)
        {
            return;
        }

        var bounds = runtime.GetWorldBounds(target);
        var parts = target.PartBounds();
        if (round.TargetPart >= 0 && round.TargetPart < parts.Length && (target.HiddenParts & (1 << round.TargetPart)) == 0
            && parts[round.TargetPart] is { } part)
        {
            bounds = runtime.GetWorldBounds(target, part);
        }

        var aim = bounds.Center() - round.Position;
        var yawError = WrapAngle(float.RadiansToDegrees(MathF.Atan2(aim.Y, aim.X)) - round.Yaw);
        if (round.YawRate * yawError < 0f)
        {
            round.YawRate = 0f;
        }

        round.YawRate = Math.Clamp(round.YawRate + MathF.Sign(yawError) * HomingYawAcceleration * dt, -HomingYawRate, HomingYawRate);
        var yawStep = round.YawRate * dt;
        if (MathF.Abs(yawStep) > MathF.Abs(yawError))
        {
            yawStep = yawError;
        }

        round.Yaw = Wrap360(round.Yaw + yawStep);
        var pitchGoal = -float.RadiansToDegrees(MathF.Atan2(aim.Z, new Vector2(aim.X, aim.Y).Length()));
        var pitchError = WrapAngle(pitchGoal - round.Pitch);
        round.Pitch += Math.Clamp(pitchError, -HomingPitchRate * dt, HomingPitchRate * dt);
        var goal = MathF.Abs(yawError) + MathF.Abs(pitchError) <= HomingAim ? HomingFast : HomingSlow;
        var rate = goal > round.Speed ? HomingSpeedUp : HomingSlowDown;
        round.Speed = MathF.Abs(goal - round.Speed) <= rate * dt ? goal : round.Speed + MathF.Sign(goal - round.Speed) * rate * dt;
    }

    /// <summary>The mortar (0x46360c): drag shared between the horizontal and the vertical speed
    /// (only while rising), gravity, bounces off the arena.</summary>
    private void MoveMortar(Round round, float dt)
    {
        var fraction = round.Speed > 0f ? round.Speed / (MathF.Abs(round.Vertical) + round.Speed) : 0f;
        round.Speed = MathF.Max(round.Speed - fraction * MortarDrag * dt, 0f);
        if (round.Vertical > 0f)
        {
            round.Vertical = MathF.Max(round.Vertical - (1f - fraction) * MortarDrag * dt, 0f);
        }

        round.Vertical = MathF.Max(round.Vertical - MortarGravity * dt, -MortarMaxFall);
        var yaw = float.DegreesToRadians(round.Yaw);
        var velocity = new Vector3(MathF.Cos(yaw) * round.Speed, MathF.Sin(yaw) * round.Speed, round.Vertical);
        var start = round.Position;
        var end = start + velocity * dt;
        if (TestObjects(start, end) is { } hit)
        {
            round.Position = hit.Point;
            Explode(round, MortarBlastRadius, hit.Object);
            return;
        }

        // A box swept as the original's, not a ray: faces seen from their back don't stop it.
        if (runtime.Sweep(start, end, MortarBox) is not { } wall)
        {
            round.Position = end;
            return;
        }

        var normal = wall.Normal;
        round.Position = wall.Point;
        _lastMortar = round;
        runtime.HitGroupAt(wall, 0, ScriptRuntime.HitShot, (int)Type.Mortar);
        if (round.Path != 0)
        {
            return;
        }

        velocity -= normal * Vector3.Dot(velocity, normal) * MortarBounce;
        if (velocity.Z > 0f && velocity.Z < MortarSettleLift)
        {
            velocity.Z = 0f;
        }

        round.Speed = new Vector2(velocity.X, velocity.Y).Length();
        round.Vertical = velocity.Z;
        if (round.Speed > 0f)
        {
            round.Yaw = float.RadiansToDegrees(MathF.Atan2(velocity.Y, velocity.X));
        }

        if (round.Life > MortarSettleTicks && round.Speed < MortarStillSpeed && round.Vertical < MortarStillRise)
        {
            round.Life = MortarSettleTicks;
        }
    }

    /// <summary>A mortar round guided by bomb_follow_path (0x4634ac): along the path (absolute
    /// positions) without collisions until its last key, then it goes off.</summary>
    private void FollowPath(Round round, float ticks)
    {
        var motion = runtime.Motion;
        var last = motion.PathKeyFrame(round.Path, motion.PathKeyCount(round.Path) - 1) - 1f;
        round.PathTime += ticks;
        if (round.PathTime < last)
        {
            round.Life = GuidedLife;
        }
        else
        {
            round.PathTime = last;
            round.Life = 0f;
        }

        round.Position = motion.PathPosition(round.Path, round.PathTime);
    }

    private readonly record struct ObjectHit(MdkObject Object, Vector3 Point, int Part);

    /// <summary>The first object the segment crosses (part -1 for the whole box): objects of Kurt's
    /// arena, alive, not flagged 0x30.</summary>
    private ObjectHit? TestObjects(Vector3 start, Vector3 end)
    {
        ObjectHit? best = null;
        var bestDistance = float.MaxValue;
        foreach (var obj in runtime.Objects)
        {
            if (obj.Dead || obj.Arena != runtime.CurrentArena || obj.Health == 0 || (obj.Flags & Untouchable) != 0)
            {
                continue;
            }

            if (runtime.GetWorldBounds(obj).SegmentEntry(start, end) is not { } entry)
            {
                continue;
            }

            var (point, part) = obj.Model != null ? HitPart(obj, start, end) : (entry, -1);
            if (point is not { } p)
            {
                continue;
            }

            var distance = Vector3.Distance(start, p);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = new ObjectHit(obj, p, part);
            }
        }

        return best;
    }

    /// <summary>The nearest face of a visible part the segment crosses (0x414668): the segment in
    /// model space against the pose's triangles, from either side. Faces, not part boxes: LEVEL8's
    /// XBSHIP hull box holds its turrets.</summary>
    private static (Vector3? Point, int Part) HitPart(MdkObject obj, Vector3 start, Vector3 end)
    {
        if (!Matrix4x4.Invert(obj.Transform, out var toModel))
        {
            return (null, -1);
        }

        // An affine map keeps the fraction along the segment.
        var from = Vector3.Transform(start, toModel);
        var to = Vector3.Transform(end, toModel);
        var nearest = 1f;
        var part = -1;
        var pose = obj.PoseParts();
        for (var i = 0; i < pose.Length; i++)
        {
            if ((obj.HiddenParts & (1 << i)) != 0)
            {
                continue;
            }

            var vertices = pose[i];
            var indices = obj.Model!.PartList[i].TriangleIndices;
            for (var t = 0; t < indices.Length; t += 3)
            {
                var hit = SegmentFace(from, to, vertices[indices[t]], vertices[indices[t + 1]], vertices[indices[t + 2]]);
                if (hit < nearest)
                {
                    nearest = hit;
                    part = i;
                }
            }
        }

        return part < 0 ? (null, -1) : (Vector3.Lerp(start, end, nearest), part);
    }

    /// <summary>Where the segment crosses the triangle, as a fraction of it (0x4144c0, either side);
    /// infinity when it doesn't.</summary>
    private static float SegmentFace(Vector3 from, Vector3 to, Vector3 a, Vector3 b, Vector3 c)
    {
        const float Parallel = 1e-9f;
        var along = to - from;
        var ab = b - a;
        var ac = c - a;
        var p = Vector3.Cross(along, ac);
        var det = Vector3.Dot(ab, p);
        if (MathF.Abs(det) < Parallel)
        {
            return float.PositiveInfinity;
        }

        // Barycentric u, v and the fraction t (Moller-Trumbore).
        var inverse = 1f / det;
        var s = from - a;
        var u = Vector3.Dot(s, p) * inverse;
        if (u < 0f || u > 1f)
        {
            return float.PositiveInfinity;
        }

        var q = Vector3.Cross(s, ab);
        var v = Vector3.Dot(along, q) * inverse;
        if (v < 0f || u + v > 1f)
        {
            return float.PositiveInfinity;
        }

        var t = Vector3.Dot(ac, q) * inverse;
        return t is >= 0f and <= 1f ? t : float.PositiveInfinity;
    }

    /// <summary>A round hits an object (0x462708): grenades explode; bullets take 8 hit points (not
    /// from objects with 65000 or more) and kill it at 0, else make sparks. The hit event is the part
    /// + 1 (if_hit_part).</summary>
    private void HitObject(Round round, MdkObject obj, int part, Vector3 point)
    {
        const int WholeObject = -2;
        obj.HitEvent = part >= 0 ? part + 1 : WholeObject;
        obj.HitType = (int)round.Type;
        obj.HitDirection = round.Yaw;
        obj.ShotPart = part + 1;
        obj.ShotPoint = point;
        runtime.Stats.SniperHits++;
        if (round.Type >= Type.Grenade)
        {
            Explode(round, BlastRadius, obj);
            return;
        }

        if (obj.Health < Unhurt)
        {
            obj.Health -= runtime.KurtDamage(obj, Damage);
        }

        if (obj.Health > 0)
        {
            runtime.SparkAt(point, 3, obj.Labels[1], obj.Indestructible ? ScriptRuntime.Spark.Hard : ScriptRuntime.Spark.Flesh);
            End(round, State.Hit, HitLinger);
            return;
        }

        obj.Health = 0;
        runtime.Stats.CountEnemy(obj.TypeName, GameStats.Kill.Killed);
        runtime.Kill(obj, round.Yaw + 180f);
        End(round, State.Killed, HitLinger);
    }

    /// <summary>An explosion (0x4638cc) within <paramref name="radius"/>.</summary>
    private void Explode(Round round, float radius, MdkObject? source)
    {
        runtime.Items.Blast(round.Position, ExplosionDamage, radius, TargetsAll, HitExplosion, source);
        runtime.Items.Blast(round.Position, ExplosionKurtDamage, radius, TargetKurt, HitExplosion, source);
        runtime.SpawnExplosion(runtime.CurrentArena, round.Position, ExplosionScale);
        runtime.PlaySoundAt(ScriptRuntime.ExplodeSound, round.Position);
        End(round, State.Exploded, MissLinger);
    }

    private static void End(Round round, State state, float linger)
    {
        round.State = state;
        round.Linger = linger;
        round.Watch = state is State.Killed or State.Hit ? WatchTicks : 0f;
    }

    private static float WrapAngle(float degrees) => ((degrees + 180f) % 360f + 360f) % 360f - 180f;

    private static float Wrap360(float degrees) => (degrees % 360f + 360f) % 360f;
}
