using System.Globalization;
using System.Numerics;
using Mdk.Engine.Platform;
using Mdk.Game.Scripts;

namespace Mdk.Game;

/// <summary>Where <c>--teleport=ARENA,x,y,z</c> sends Kurt (teleport_player).</summary>
public readonly record struct TeleportTarget(string Arena, Vector3 Position)
{
    /// <summary>From <c>ARENA,x,y,z</c>.</summary>
    public static TeleportTarget Parse(string text)
    {
        var parts = text.Split(',');
        var v = parts[1..].Select(p => float.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        return new TeleportTarget(parts[0], new Vector3(v[0], v[1], v[2]));
    }
}

/// <summary>How the tests' <c>--bomber</c> ends: on the XE, or also dropping a bomb.</summary>
public enum BomberTest { Ride, Drop }

/// <summary>The tests' rides (like the Godot port's --teleport, --ride, --kill and --bomber), after
/// the delay: a teleport, getting on a walker, killing an object, and the XE: the comm device of
/// DANT_5 is hit, the XE it calls comes, Kurt drops onto it and (drop) fires a bomb once unlocked.
/// <code>
///   delay ──► teleport ──► kill ──► ride
///         └─1 s──► hit group 16 ──► XE rideable ──► Kurt above it ──► unlocked ──► fire (2 steps)
/// </code></summary>
public sealed class RideTest(ViewerOptions options)
{
    private const string BomberArena = "DANT_5";
    private const int BomberCallGroup = 16;
    private const float BomberCallDelay = 1f;
    private const float AboveXe = 1f;
    private const int FireSteps = 2;
    private const float TeleportYaw = 90f;

    private bool _started;
    private bool _called;
    private bool _boarded;
    private int _fireSteps = -1;

    /// <summary>Before each game step at game time <paramref name="time"/>.</summary>
    public void Step(Kurt.Kurt kurt, ScriptRuntime scripts, Input input, float time)
    {
        if (time < options.Delay)
        {
            return;
        }

        if (!_started)
        {
            _started = true;
            Start(scripts);
        }

        if (options.Bomber is { } bomber)
        {
            StepBomber(kurt, scripts, input, time, bomber);
        }
    }

    /// <summary>The profile's lines about the ride.</summary>
    public static void Report(ScriptRuntime scripts)
    {
        if (scripts.Rides.Ridden is { } ridden)
        {
            Console.WriteLine($"riding {ridden.TypeName}");
        }

        if (scripts.Rides.Bomber is { } bomber)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"bomber view {bomber.ViewHeight:0.0}, locked {bomber.Locked.ToString().ToLowerInvariant()}, bombs {bomber.BombsLeft}"));
        }
    }

    private void Start(ScriptRuntime scripts)
    {
        if (options.Teleport is { } teleport)
        {
            scripts.TeleportKurt(teleport.Arena, teleport.Position, TeleportYaw);
        }

        if (options.Kill is { } victim && scripts.FindObjectNamed(victim) is { } killed)
        {
            scripts.Kill(killed);
        }

        if (options.Ride is { } type && scripts.FindObjectNamed(type) is { } walker)
        {
            scripts.Rides.RideWalker(walker);
        }
    }

    private void StepBomber(Kurt.Kurt kurt, ScriptRuntime scripts, Input input, float time, BomberTest test)
    {
        if (!_called && time >= options.Delay + BomberCallDelay)
        {
            _called = true;
            scripts.HitGroup(BomberArena, BomberCallGroup, 1, ScriptRuntime.HitChainGun, 0);
        }

        if (_called && !_boarded && scripts.Objects.FirstOrDefault(o => o.TypeName == "XE" && (o.Flags & Rides.FlagRideable) != 0) is { } xe)
        {
            _boarded = true;
            var top = scripts.GetWorldBounds(xe).Max.Z;
            kurt.Teleport(xe.Position with { Z = top + AboveXe }, kurt.Yaw);
        }

        if (test != BomberTest.Drop || scripts.Rides.Bomber is not { Locked: false })
        {
            return;
        }

        _fireSteps = _fireSteps < 0 ? FireSteps : Math.Max(_fireSteps - 1, 0);
        input.Hold(Key.Fire, _fireSteps > 0 ? Input.State.Down : Input.State.Up);
    }
}
