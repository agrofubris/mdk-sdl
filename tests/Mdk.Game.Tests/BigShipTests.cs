using System.Numerics;
using Mdk.Engine.Audio;
using Mdk.Formats;
using Mdk.Game.Audio;
using Mdk.Game.Collision;
using Mdk.Game.Level;
using Mdk.Game.Objects;
using Mdk.Game.Scripts;

namespace Mdk.Game.Tests;

/// <summary>Level 8's start ship (GUNT_1 XBSHIP_0): indestructible (65000), but a sniper round on
/// one of its 7 blue turrets T1-T7 blows it off; without them it falls and explodes. Rounds hit
/// the parts' faces (0x414668), not their boxes: the hull's box holds the turrets.</summary>
public class BigShipTests
{
    private const int Level = 8;
    private const float Tick = 1f / 30f;
    private const string Arena = "GUNT_1";
    private const string Ship = "XBSHIP";
    private const int BulletRound = 0;
    private const int Turrets = 0x7F;
    /// <summary>On the floor, every turret in sight once the ship hovers at (62, 680, 207).</summary>
    private static readonly Vector3 Stand = new(0f, 160f, 10f);
    private const float EyeHeight = 6f;
    /// <summary>The ship reaches its hovering spot after about 20 s.</summary>
    private const int ArriveTicks = 25 * 30;
    private const int ShootTicks = 30 * 30;
    private const int CrashTicks = 20 * 30;

    private static readonly Lazy<MdkData> Data = new(() => MdkData.Find()!);
    private static readonly AudioDevice Device = new(Output.Muted);

    private static ScriptRuntime CreateRuntime()
    {
        var level = new LevelData(Data.Value, Level);
        var cmi = Cmi.Load(Data.Value.PathOf($"TRAVERSE/LEVEL{Level}/LEVEL{Level}.CMI"));
        var sprites = Bni.Load(Data.Value.PathOf("TRAVERSE/TRAVSPRT.BNI"));
        var space = new ArenaSpace();
        foreach (var arena in level.Arenas.Where(a => level.IsReachable(a.Name)))
        {
            space.Add(arena);
        }

        var mixer = new SoundMixer(Device, _ => null);
        return new ScriptRuntime(level, cmi, sprites, space, new TriangleGroups(), mixer, new Kurt.Kurt(space, mixer, _ => 1));
    }

    /// <summary>The centres of the turrets still on the ship, as drawn.</summary>
    private static Vector3[] TurretCentres(MdkObject ship)
    {
        var pose = ship.PoseParts();
        return [.. Enumerable.Range(0, pose.Length)
            .Where(p => ScriptRuntime.IsWeakPart(ship, p) && pose[p].Length > 0 && (ship.HiddenParts & (1 << p)) == 0)
            .Select(p => Vector3.Transform(pose[p].Aggregate(Vector3.Add) / pose[p].Length, ship.Transform))];
    }

    [DataFact]
    public void SnipedTurretsBringTheShipDown()
    {
        var runtime = CreateRuntime();
        runtime.TeleportKurt(Arena, Stand, 90f);
        for (var i = 0; i < ArriveTicks; i++)
        {
            runtime.Update(Tick);
        }

        var ship = runtime.Objects.First(o => o.TypeName == Ship);
        var eye = runtime.KurtPosition + new Vector3(0f, 0f, EyeHeight);

        // Bullets at each remaining turret in turn.
        for (var i = 0; i < ShootTicks && (ship.HiddenParts & Turrets) != Turrets; i++)
        {
            var targets = TurretCentres(ship);
            var to = targets[i % targets.Length] - eye;
            var yaw = float.RadiansToDegrees(MathF.Atan2(to.Y, to.X));
            var pitch = -float.RadiansToDegrees(MathF.Atan2(to.Z, new Vector2(to.X, to.Y).Length()));
            runtime.SniperRounds.Fire(BulletRound, eye, yaw, pitch, null);
            runtime.Update(Tick);
        }

        Assert.Equal(Turrets, ship.HiddenParts & Turrets);

        for (var i = 0; i < CrashTicks && !ship.Dead && ship.Health > 0; i++)
        {
            runtime.Update(Tick);
        }

        Assert.True(ship.Dead || ship.Health == 0);
    }
}
