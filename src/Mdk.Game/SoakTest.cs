using System.Globalization;
using System.Numerics;
using Mdk.Game.Level;
using Mdk.Game.Scripts;

namespace Mdk.Game;

/// <summary>What the soak test does in a level besides the keys: stay there, or tour every arena.</summary>
public enum SoakRoute { Stay, Tour }

/// <summary>The soak test in a level (--soak, --tour): Kurt is healed so he lives on, the tour
/// teleports him to each reachable arena in turn (to a floor under one of its records), and every
/// step looks for problems: NaN positions, Kurt standing outside every arena or falling through a
/// floor (falling into a pit is only noted). Problems print as "Soak problem ..." (once per kind
/// and arena), the counts at the end ("Soak end").
/// <code>
///   step ──► tour: next arena due? ──► teleport
///        ├─► health low ──► healed
///        └─► checks ──► problems (printed once)
/// </code></summary>
public sealed class SoakTest
{
    private const int HealBelow = Kurt.Inventory.MaxHealth / 2;
    /// <summary>Seconds standing outside every arena before it's a problem, falling before it's a pit.</summary>
    private const float LostTime = 3f;
    private const float FallTime = 6f;
    /// <summary>The feet stand this little above a floor; a step's move starts this high.</summary>
    private const float FloorLift = 0.05f;
    /// <summary>Feet this far under a floor went through it (they touch it standing).</summary>
    private const float Sink = 0.5f;
    /// <summary>Longer moves in a step are teleports (the fastest fall moves 250 / 60).</summary>
    private const float MaxStep = 10f;

    private readonly float _duration;
    private readonly List<(string Arena, Vector3 Point)> _stops = [];
    private readonly HashSet<string> _problems = [];
    private readonly HashSet<string> _notes = [];
    private readonly HashSet<string> _visited = [];
    private int _stop = -1;
    private float _lost;
    private float _falling;
    private int _heals;
    /// <summary>Where Kurt last stood (problems tell where a fall started).</summary>
    private Vector3 _floor;
    private Vector3 _previous;

    public SoakTest(LevelData level, Collision.ArenaSpace space, SoakRoute route, float duration)
    {
        _duration = duration;
        if (route == SoakRoute.Tour)
        {
            _stops.AddRange(Stops(level, space));
        }
    }

    /// <summary>After each game step at game time <paramref name="time"/>.</summary>
    public void Step(Kurt.Kurt kurt, ScriptRuntime scripts, Collision.ArenaSpace space, float time)
    {
        Travel(scripts, time);
        _visited.Add(scripts.CurrentArena);
        if (kurt.Health < HealBelow && kurt.Current != Kurt.Kurt.State.Dead)
        {
            kurt.SetHealth(Kurt.Inventory.MaxHealth);
            _heals++;
        }

        Check(kurt, scripts, space, time);
    }

    /// <summary>The summary line at the end.</summary>
    public void Report(ScriptRuntime scripts, Kurt.Kurt kurt, float time)
    {
        var stats = scripts.Stats;
        var f = kurt.Feet;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Soak end t {time:0.0}: arenas {_visited.Count(a => a.Length != 0)} ({scripts.CurrentArena}), objects {scripts.Objects.Count}, " +
            $"shots {stats.Shots}, hits {stats.ShotHits}, kills {stats.Kills}, heals {_heals}, " +
            $"Kurt at {f.X:0} {f.Y:0} {f.Z:0} {kurt.Current}, problems {_problems.Count}"));
    }

    /// <summary>Teleports Kurt to the next arena of the tour when its time comes.</summary>
    private void Travel(ScriptRuntime scripts, float time)
    {
        if (_stops.Count == 0)
        {
            return;
        }

        var stop = Math.Min((int)(time / (_duration / _stops.Count)), _stops.Count - 1);
        if (stop == _stop)
        {
            return;
        }

        _stop = stop;
        var (arena, point) = _stops[stop];
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Soak t {time:0.0}: teleport to {arena} {point.X:0} {point.Y:0} {point.Z:0}"));
        scripts.TeleportKurt(arena, point, scripts.Kurt.Yaw);
        _lost = 0f;
        _falling = 0f;
        _floor = point;
        _previous = point;
    }

    private void Check(Kurt.Kurt kurt, ScriptRuntime scripts, Collision.ArenaSpace space, float time)
    {
        var arena = scripts.CurrentArena;
        if (!IsFinite(kurt.Feet) || !float.IsFinite(kurt.Yaw))
        {
            Problem(time, arena, "Kurt's position is not a number", $"{kurt.Feet} yaw {kurt.Yaw}");
        }

        foreach (var obj in scripts.Objects.Where(o => !IsFinite(o.Position)))
        {
            Problem(time, obj.Arena, $"{obj.TypeName} position is not a number", $"{obj.TypeName}_{obj.InstanceId} {obj.Position}");
        }

        if (kurt.OnFloor)
        {
            _floor = kurt.Feet;
        }

        // Standing outside every arena (in the air: a jump, a fan, a pit, checked below; dead: he
        // fell 50 below his arena, damp_gravity).
        var dead = kurt.Current == Kurt.Kurt.State.Dead;
        var outside = space.ArenaAt(kurt.Feet) == null && kurt.OnFloor && !dead;
        if (dead && space.ArenaAt(kurt.Feet) == null)
        {
            Note(arena, "Kurt fell out of the arena", Where(kurt));
        }

        _lost = outside ? _lost + Viewer.Step : 0f;
        if (_lost >= LostTime)
        {
            Problem(time, arena, "Kurt outside every arena", Where(kurt));
        }

        _falling = kurt.OnFloor || kurt.Platform != null ? 0f : _falling + Viewer.Step;

        // The feet went down through a floor this step: the sweep let them through (teleports aside).
        var lifted = _previous + new Vector3(0f, 0f, FloorLift);
        var falling = !kurt.OnFloor && kurt.Feet.Z < _previous.Z && Vector3.Distance(_previous, kurt.Feet) < MaxStep;
        if (falling && space.Floor(lifted, kurt.Feet, out var crossed) && crossed.Z - kurt.Feet.Z > Sink)
        {
            Problem(time, arena, "Kurt fell through a floor", $"{Where(kurt)}, floor at {crossed.Z:0.0}");
        }

        _previous = kurt.Feet;
        if (_falling < FallTime)
        {
            return;
        }

        Note(arena, "Kurt falls into a pit", Where(kurt));
    }

    /// <summary>Prints a problem the first time its kind shows in an arena.</summary>
    private void Problem(float time, string arena, string kind, string detail)
    {
        if (!_problems.Add($"{kind} in {arena}"))
        {
            return;
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Soak problem t {time:0.00} in {arena}: {kind}: {detail}"));
    }

    /// <summary>Prints something that isn't a problem (pits are in the levels) once per arena.</summary>
    private void Note(string arena, string kind, string detail)
    {
        if (_notes.Add($"{kind} in {arena}"))
        {
            Console.WriteLine($"Soak note in {arena}: {kind}: {detail}");
        }
    }

    private string Where(Kurt.Kurt kurt) => string.Create(CultureInfo.InvariantCulture,
        $"at {kurt.Feet.X:0.0} {kurt.Feet.Y:0.0} {kurt.Feet.Z:0.0} {kurt.Current} vz {kurt.VerticalSpeed:0.0}, stood at {_floor.X:0.0} {_floor.Y:0.0} {_floor.Z:0.0}");

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    /// <summary>A point on a floor in each reachable arena (<see cref="ArenaStops"/>).</summary>
    private static IEnumerable<(string, Vector3)> Stops(LevelData level, Collision.ArenaSpace space)
    {
        var points = new HashSet<Vector3>();
        foreach (var entry in level.Dti.Arenas.Where(a => level.IsReachable(a.Name)))
        {
            // A corridor's connections are those of its arenas (CHMO_1 ends in HMO_1): visited once.
            if (ArenaStops.Find(level, space, entry.Name) is not { } stop || !points.Add(stop))
            {
                continue;
            }

            yield return (entry.Name, stop);
        }
    }
}
