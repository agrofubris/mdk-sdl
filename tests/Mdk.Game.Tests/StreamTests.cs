using System.Numerics;
using Mdk.Game.Kurt;
using Mdk.Game.Stream;

namespace Mdk.Game.Tests;

/// <summary>The stream (stream.gd, stream_tube.gd): the tube's generator against the Godot port's
/// numbers (its StreamTube with Watcom's rand(), seed 1), steering and the walls.</summary>
public class StreamTests
{
    private const float Tolerance = 2e-3f;
    private const float Step = 1f / 60f;
    private static readonly Clip Loop = new(10, 1f);

    private static StreamTube Tube(int index, Difficulty difficulty, StreamTube.Kind kind, uint seed = WatcomRandom.DefaultSeed) =>
        new(index, difficulty, kind, new WatcomRandom(seed));

    private static Flight FlightOf(StreamTube.Kind kind, int health) =>
        new(Tube(0, Difficulty.Normal, kind), kind, Difficulty.Normal, new WatcomRandom(), health, Loop, Loop, Loop);

    private static void Near(Vector3 expected, Vector3 actual, float tolerance = Tolerance) =>
        Assert.True(Vector3.Distance(expected, actual) < tolerance, $"expected {expected}, got {actual}");

    [Fact]
    public void RandIsWatcoms()
    {
        var random = new WatcomRandom();
        Assert.Equal([16838, 5758, 10113], [random.Next(), random.Next(), random.Next()]);
    }

    [Theory]
    [InlineData(0, Difficulty.Normal, 8f, 10f, 17f)]
    [InlineData(2, Difficulty.Hard, 12f, 9f, 12f)]
    [InlineData(4, Difficulty.Normal, 12f, 8f, 13f)]
    [InlineData(3, Difficulty.Easy, 9f, 9f, 16f)]
    public void LimitsFollowLevelAndDifficulty(int index, Difficulty difficulty, float turn, float min, float max)
    {
        var tube = Tube(index, difficulty, StreamTube.Kind.Normal);
        Assert.Equal((turn, min, max), (tube.MaxTurn, tube.MinRadius, tube.MaxRadius));
    }

    /// <summary>A vertex takes its ramp colour and its level's alpha (of 256), blended per vertex.</summary>
    [Theory]
    [InlineData(0, 0, 222, 206, 90, 0x5A)]
    [InlineData(10, 2, 0x87, 0x68, 0x1a, 0x50)]
    [InlineData(63, 5, 209, 186, 79, 0x0F)]
    public void VertexTakesRampAndLevel(int colour, int level, int r, int g, int b, int a)
    {
        var tube = Tube(0, Difficulty.Normal, StreamTube.Kind.Normal);
        var rgba = tube.Rgba(new TubeVertex(Vector3.Zero, colour, level));
        Assert.Equal((r, g, b, a), ((int)rgba.R, (int)rgba.G, (int)rgba.B, (int)rgba.A));
    }

    /// <summary>godot-mdk, StreamTube.new(0, 1, NORMAL) with Watcom's rand().</summary>
    [Fact]
    public void TubeMatchesGodot()
    {
        var tube = Tube(0, Difficulty.Normal, StreamTube.Kind.Normal);
        Assert.Equal((222, 206, 90), ((int)tube.Ramp[0].R, (int)tube.Ramp[0].G, (int)tube.Ramp[0].B));
        Assert.Equal((0x87, 0x68, 0x1a), ((int)tube.Ramp[10].R, (int)tube.Ramp[10].G, (int)tube.Ramp[10].B));
        Assert.Equal((0, 31), (tube.Tail, tube.Head));

        var spawns = tube.TakeSpawns();
        Assert.Equal(27, spawns.Count);
        Assert.Equal(0f, spawns[0].T);
        Assert.Equal(-0.6729f, spawns[0].X, 1e-4f);
        Assert.Equal(7.3076f, spawns[0].Size, 1e-4f);
        Assert.Equal(-6.9166f, spawns[0].Speed, 1e-4f);

        Near(new Vector3(0f, 10f, 0f), tube.Origin(1));
        Near(new Vector3(-0.036f, 9.973f, 9.425f), tube.Point(1, 0));
        Near(new Vector3(-3.523f, 98.642f, 14.672f), tube.Point(10, 0));
        Near(new Vector3(-79.963f, 249.386f, 101.282f), tube.Origin(30));
        Near(new Vector3(-83.717f, 257.434f, 93.861f), tube.Point(30, 5));
        Assert.Equal([40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 49, 48, 47, 46, 45, 44], Enumerable.Range(0, 16).Select(j => tube.Colour(5, j)));
        var (normal, distance) = tube.Plane(5, 0);
        Near(new Vector3(-0.521f, -0.089f, -0.849f), normal);
        Assert.Equal(15.546f, distance, Tolerance);

        Near(new Vector3(-6.703f, 104.371f, 5.385f), tube.Centre(10.5f));
        Near(new Vector3(-5.318f, 104.421f, 7.140f), tube.Place(10.5f, 1f, 2f));
        var frame = tube.Frame(10.5f, Vector3.UnitZ);
        Near(new Vector3(0.976f, 0.216f, 0f), frame.X);
        Near(new Vector3(0.032f, -0.145f, 0.989f), frame.Z);
        Assert.Equal(-1f, tube.WallHit(tube.Centre(10.5f), 10.5f, 1.5f));
        Assert.Equal(0.0080f, tube.WallHit(tube.Centre(10.5f) + frame.X * 9.5f, 10.5f, 1.5f), 1e-3f);

        var lights = 0;
        for (var i = 0; i < 200; i++)
        {
            tube.Advance();
            lights += tube.TakeSpawns().Count;
        }

        Assert.Equal((200, 231, 142), (tube.Tail, tube.Head, lights));
        Near(new Vector3(-466.086f, 254.029f, -304.175f), tube.Origin(230), 0.05f);
        Near(new Vector3(0.962f, -7.344f, 5.093f), tube.Angles, 0.01f);
        Assert.Equal(16.0577f, tube.Radius, 1e-3f);
    }

    /// <summary>godot-mdk, StreamTube.new(4, 1, GUNTER): it straightens and ends at the planet.</summary>
    [Fact]
    public void GunterTubeEndsAtPlanet()
    {
        var tube = Tube(4, Difficulty.Normal, StreamTube.Kind.Gunter);
        tube.TakeSpawns();
        var lights = 0;
        var planet = -1f;
        for (var i = 0; i < 200; i++)
        {
            tube.Advance();
            foreach (var spawn in tube.TakeSpawns())
            {
                if (spawn.Kind == StreamTube.SpawnKind.Planet)
                {
                    planet = spawn.T;
                    continue;
                }

                lights++;
            }
        }

        Assert.Equal((187, 115, 184f), (tube.Head, lights, planet));
        Assert.Equal(Vector3.Zero, tube.Angles);
        Near(new Vector3(-316.770f, 205.873f, -2.547f), tube.Origin(186), 0.05f);
    }

    [Fact]
    public void SameSeedSameTube()
    {
        var a = Tube(1, Difficulty.Normal, StreamTube.Kind.Normal, 7);
        var b = Tube(1, Difficulty.Normal, StreamTube.Kind.Normal, 7);
        var c = Tube(1, Difficulty.Normal, StreamTube.Kind.Normal, 8);
        Assert.Equal(a.Point(30, 3), b.Point(30, 3));
        Assert.NotEqual(a.Point(30, 3), c.Point(30, 3));
    }

    [Fact]
    public void WallPlanesFaceTheAxis()
    {
        var tube = Tube(0, Difficulty.Normal, StreamTube.Kind.Normal);
        for (var n = 0; n < 29; n++)
        {
            var centre = tube.Centre(n + 0.5f);
            for (var i = 0; i < StreamTube.Points * 2; i++)
            {
                var (normal, distance) = tube.Plane(n, i);
                Assert.True(Vector3.Dot(normal, centre) + distance > 0f, $"segment {n} plane {i}");
            }
        }
    }

    [Fact]
    public void SteeringTurnsUpTo45AndBack()
    {
        // 180°/s: a quarter of a second to full turn, before Kurt reaches a wall.
        var flight = FlightOf(StreamTube.Kind.Normal, 100);
        for (var i = 0; i < 15; i++)
        {
            flight.Update(Step, new Steering(1, 1));
        }

        Assert.Equal(Flight.NeutralYaw - 45f, flight.Kurt.Yaw, 1e-3f);
        Assert.Equal(45f, flight.Pitch, 1e-3f);

        for (var i = 0; i < 15; i++)
        {
            flight.Update(Step, default);
        }

        Assert.Equal(Flight.NeutralYaw, flight.Kurt.Yaw);
        Assert.Equal(0f, flight.Pitch);
    }

    [Fact]
    public void WallHitPushesBackHurtsAndSlows()
    {
        var flight = FlightOf(StreamTube.Kind.Normal, 100);
        flight.Update(Step, default);
        flight.Kurt.X = 30f;
        flight.Update(Step, default);

        Assert.Contains(FlightEvent.WallHit, flight.Events);
        Assert.InRange(flight.Health, 97, 98);
        Assert.Equal(5.4f, flight.Kurt.Speed, 1e-4f);
        Assert.True(flight.Kurt.X < 30f);
        Assert.Equal(Flight.NeutralYaw + 45f, flight.Kurt.Yaw, 1f);
    }

    [Fact]
    public void NormalTubeKeepsOneHealthAndBonesComes()
    {
        // 3 health less a hit of 2 or 3 leaves 1.
        var flight = FlightOf(StreamTube.Kind.Normal, 3);
        flight.Update(Step, default);
        flight.Kurt.X = 30f;
        flight.Update(Step, default);
        Assert.Equal(Flight.LowHealth, flight.Health);

        flight.Update(Step, default);
        Assert.Contains(FlightEvent.BonesCame, flight.Events);
        Assert.NotNull(flight.Bones);
    }

    [Fact]
    public void GunterTubeKills()
    {
        var flight = FlightOf(StreamTube.Kind.Gunter, Flight.LowHealth);
        flight.Update(Step, default);
        flight.Kurt.X = 30f;
        flight.Update(Step, default);
        Assert.Contains(FlightEvent.KurtDied, flight.Events);

        while (!flight.Ended)
        {
            flight.Update(Step, default);
        }

        Assert.Equal(Outcome.Died, flight.Outcome);
    }

    [Fact]
    public void BonesComesAfterSegment177()
    {
        var flight = FlightOf(StreamTube.Kind.Normal, 1000);
        while (flight.Bones == null)
        {
            flight.Update(Step, default);
        }

        Assert.Equal(Flight.RescueSegment + 1, flight.Tube.Tail);
        while (!flight.Ended)
        {
            flight.Update(Step, default);
        }

        Assert.Equal(Outcome.Survived, flight.Outcome);
    }
}
