using System.Numerics;
using Mdk.Game.Objects;
using static Mdk.Game.Scripts.ScriptMath;

namespace Mdk.Game.Scripts;

/// <summary>A twister of the tornado item (0x40741c): it spirals out of the tornado for 2 seconds,
/// then splits into one twister per object of the arena (0x407774), each chasing its object
/// (0x407974) for 5 seconds, bouncing off walls and hurting whatever it's inside by 2 health per
/// tick. A port of godot-mdk's <c>twister.gd</c>; drawn as a <see cref="Scripts.Ribbon"/> along its last positions.
/// <code>
///   spiral (720°/s, 15 out and up at 1440°) ──► split ──► chase: v = 0.9 v + 20 towards the target
/// </code></summary>
public sealed class Twister(ScriptRuntime runtime, string arena, Vector3 origin, float yaw)
{
    private const float SpiralRate = 720f;
    private const float SpiralEnd = 1440f;
    private const float SpiralSize = 15f;
    private const float SpiralSpeed = 200f;
    /// <summary>150 ticks of life, a tick less for each tick inside an object.</summary>
    private const int Lifetime = 150;
    private const float Keep = 0.9f;
    private const float Pull = 20f;
    private const int Damage = 2;
    /// <summary>Stops this far off a wall it meets.</summary>
    private const float WallGap = 0.1f;
    private const int HitEvent = -1;

    public string Arena { get; } = arena;
    public Vector3 Position { get; private set; } = origin;
    /// <summary>The ribbon along its last positions (0x439690).</summary>
    public Ribbon Ribbon { get; } = new();

    private readonly Vector3 _origin = origin;
    private float _angle;
    private Vector3 _velocity;
    private MdkObject? _target;
    private bool _chasing;
    private int _life = Lifetime;

    /// <summary>One tick; <paramref name="twisters"/> gets the split ones. Returns false when it's gone.</summary>
    public bool Tick(List<Twister> twisters)
    {
        if (!_chasing)
        {
            Spiral(twisters);
            return true;
        }

        // Chase, bouncing off the walls.
        var next = Position + _velocity * ScriptRuntime.Tick;
        if (runtime.Raycast(Position, next) is { } hit)
        {
            var normal = Facing(hit.Normal, next - Position);
            _velocity -= normal * Vector3.Dot(_velocity, normal) * 2f;
            next = hit.Point + normal * WallGap;
        }

        Position = next;
        Ribbon.Push(Position);
        HurtObjects();
        _life--;
        if (_life <= 0)
        {
            return false;
        }

        if (_target is { Dead: false, Health: > 0 } target)
        {
            var toward = Vector3.Normalize(runtime.GetWorldBounds(target).Center() - Position);
            _velocity = _velocity * Keep + toward * Pull;
        }
        else
        {
            _target = null;
        }

        return true;
    }

    private void Spiral(List<Twister> twisters)
    {
        _angle += ScriptRuntime.Tick * SpiralRate;
        var r = _angle / SpiralEnd;
        var direction = FromAngle(_angle + yaw);
        MoveTo(_origin + new Vector3(direction, 1f) * SpiralSize * r);
        Ribbon.Push(Position);
        _velocity = new Vector3(direction.Y, -direction.X, 0f) * SpiralSpeed;
        if (_angle <= SpiralEnd)
        {
            return;
        }

        _chasing = true;
        Split(twisters);
    }

    /// <summary>The objects it's inside lose 2 health a tick (and the twister a tick of life).</summary>
    private void HurtObjects()
    {
        foreach (var obj in runtime.Objects.ToList())
        {
            if (obj.Dead || obj.Arena != Arena || obj.Health == 0 || (obj.Flags & (MdkObject.FlagNotSolid | MdkObject.FlagNotTarget)) != 0)
            {
                continue;
            }

            var bounds = runtime.GetWorldBounds(obj);
            if (!bounds.Contains(Position))
            {
                continue;
            }

            var direction = Heading(runtime.KurtPosition, bounds.Center());
            if (obj.Health < ScriptRuntime.Indestructible)
            {
                obj.Health -= Damage;
            }

            obj.HitEvent = HitEvent;
            obj.HitDirection = direction;
            _life--;
            if (obj.Health < 1)
            {
                runtime.Kill(obj, direction + HalfTurn);
            }
        }
    }

    /// <summary>The spiral is over: one twister for each object of the arena (0x407774).</summary>
    private void Split(List<Twister> twisters)
    {
        foreach (var obj in runtime.Objects)
        {
            if (obj.Dead || obj.Arena != Arena || (obj.Flags & (MdkObject.FlagNotSolid | MdkObject.FlagNotTarget)) != 0)
            {
                continue;
            }

            if (_target == null)
            {
                _target = obj;
                continue;
            }

            twisters.Add(new Twister(runtime, Arena, _origin, yaw)
            {
                Position = Position, _velocity = _velocity, _chasing = true, _target = obj,
            });
        }
    }

    private void MoveTo(Vector3 next)
    {
        if (runtime.Raycast(Position, next) is { } hit)
        {
            next = hit.Point + Facing(hit.Normal, next - Position) * WallGap;
        }

        Position = next;
    }

    /// <summary>A wall's normal turned against the move.</summary>
    private static Vector3 Facing(Vector3 normal, Vector3 move) => Vector3.Dot(normal, move) > 0f ? -normal : normal;
}
