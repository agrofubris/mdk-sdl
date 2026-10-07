using System.Numerics;
using Mdk.Formats;
using Mdk.Game.Objects;
using Mdk.Game.Scripts;

namespace Mdk.Game.Tests;

/// <summary>Sparks, pieces, sprite effects and fans (debris.gd, effects.gd, fans.gd), without a level.</summary>
public class EffectsTests
{
    private const string Arena = "TEST";
    private const int Seed = 1;
    /// <summary>Fire sparks' palette colours.</summary>
    private const int FireBase = 0x30;
    private const int FireRange = 0x10;
    /// <summary>A fan: hotspot 3 lifting from z 0 to 20, rising through it in 2.5 s (type 6).</summary>
    private const int Hotspot = 3;
    private const int Timed = 6;
    private const float RiseTime = 2.5f;
    private static readonly Vector3 BoxStart = new(-10f, -10f, 0f);
    private static readonly Vector3 BoxEnd = new(10f, 10f, 20f);
    private const float Dt = 1f / 30f;

    private static Debris NewDebris() => new(new Random(Seed));

    [Fact]
    public void SparksAreTetrahedraInTheirColours()
    {
        var debris = NewDebris();
        debris.Spark(Arena, Vector3.Zero, 16, 1f, FireBase, FireRange);

        Assert.Equal(16, debris.PieceCount());
        Assert.True(debris.HasPieces(Arena));
        foreach (var piece in debris.Pieces)
        {
            Assert.True(piece.IsSpark);
            Assert.Equal(12, piece.Corners.Count);
            Assert.Equal((FireBase, FireRange), (piece.ColourBase, piece.ColourRange));
            Assert.InRange(piece.Ticks, Debris.LifeMin, Debris.LifeMin + 63);

            // Thrown at up to 1 unit per tick across, -0.125 to 1.875 up.
            Assert.InRange(MathF.Abs(piece.Velocity.X), 0f, 1f);
            Assert.InRange(piece.Velocity.Z, -0.125f, 1.875f);
        }
    }

    [Fact]
    public void StillSparksStandAtTheirPoint()
    {
        var debris = NewDebris();
        var point = new Vector3(1f, 2f, 3f);
        debris.Spark(Arena, point, 1, 0.5f, FireBase, FireRange, 1f, Debris.Launch.Still);

        var spark = Assert.Single(debris.Pieces);
        Assert.Equal(point, spark.Center);
        Assert.Equal(Vector3.Zero, spark.Velocity);
        Assert.Equal(0, spark.TrailEvery);
    }

    [Fact]
    public void PiecesAreLimited()
    {
        var debris = NewDebris();
        debris.Spark(Arena, Vector3.Zero, Debris.MaxPieces + 10, 0.5f, FireBase, FireRange);
        Assert.Equal(Debris.MaxPieces, debris.PieceCount());
    }

    [Fact]
    public void PiecesFallThenVanish()
    {
        var debris = NewDebris();
        debris.Spark(Arena, Vector3.Zero, 1, 0.5f, FireBase, FireRange, 1f, Debris.Launch.Still);
        var spark = debris.Pieces[0];
        var ticks = spark.Ticks;

        debris.Update(1f);
        Assert.Equal(-Debris.Gravity, spark.Velocity.Z, 4);

        for (var i = 1; i < ticks; i++)
        {
            debris.Update(1f);
        }

        Assert.Equal(0, debris.PieceCount());
    }

    [Fact]
    public void PiecesBounceOffTheArena()
    {
        // A floor at z 0: the piece stops on it, keeps 40% of its speed into it, reversed, and loses 20 ticks.
        var debris = NewDebris();
        debris.Ray = (_, from, to) => to.Z < 0f
            ? new ScriptRuntime.RayHit(new Vector3(to.X, to.Y, 0f), Vector3.UnitZ, Arena, 0)
            : null;
        debris.Spark(Arena, new Vector3(0f, 0f, 0.5f), 1, 0.5f, FireBase, FireRange, 1f, Debris.Launch.Still);
        var spark = debris.Pieces[0];
        spark.Velocity = new Vector3(0f, 0f, -1f);
        var ticks = spark.Ticks;

        debris.Update(1f);

        Assert.Equal(0.4f, spark.Velocity.Z, 4);
        Assert.Equal(ticks - 21, spark.Ticks);
        Assert.Equal(0f, spark.Center.Z);
    }

    [Fact]
    public void FansLiftSparksThroughWhatTheyHit()
    {
        // A grate at z 1, hit from either side (as the arena's segment test), above a spark lifted
        // at 1 unit per tick: it bounces off once, then passes from the contact (0x4061d8, 0x421470).
        const float Grate = 1f;
        var debris = NewDebris();
        debris.Ray = (_, from, to) => (from.Z - Grate) * (to.Z - Grate) <= 0f
            ? new ScriptRuntime.RayHit(new Vector3(to.X, to.Y, Grate), Vector3.UnitZ, Arena, 0)
            : null;
        debris.Updraft = (_, _, _, _) => 30f;
        debris.Spark(Arena, new Vector3(0f, 0f, 0.75f), 1, 0.5f, FireBase, FireRange, 1f, Debris.Launch.Still);

        for (var i = 0; i < 4; i++)
        {
            debris.Update(1f);
        }

        Assert.True(debris.Pieces[0].Center.Z > Grate + 1f, $"spark at {debris.Pieces[0].Center.Z}");
    }

    [Fact]
    public void FansLiftPieces()
    {
        var debris = NewDebris();
        debris.Updraft = (_, _, _, _) => 30f;
        debris.Spark(Arena, Vector3.Zero, 1, 0.5f, FireBase, FireRange, 1f, Debris.Launch.Still);

        debris.Update(1f);

        // 30 u/s is 1 unit per tick.
        Assert.Equal(1f, debris.Pieces[0].Velocity.Z, 4);
    }

    [Fact]
    public void ShatteredTrianglesAreSplitSmall()
    {
        var debris = NewDebris();
        var facet = new Debris.Facet(new(0f, 0f, 0f), new(8f, 0f, 0f), new(0f, 8f, 0f), Vector2.Zero, new(64f, 0f), new(0f, 64f), 0);
        debris.GroupFacets = (_, _) => new Debris.Group(["WALL"], [facet]);
        const float Size = 2f;

        debris.Shatter(Arena, 1, 2f, Size, 1f, new Vector3(0f, 0f, -1f), Vector3.Zero);

        Assert.True(debris.PieceCount() > 1);
        foreach (var piece in debris.Pieces)
        {
            Assert.False(piece.IsSpark);
            var cross = Vector3.Cross(piece.Corners[1] - piece.Corners[0], piece.Corners[1] - piece.Corners[2]);
            Assert.True(cross.LengthSquared() <= Size * Size * 0.25f);
            Assert.Equal(60, piece.Ticks);

            // Away from the point below the group.
            Assert.True(piece.Velocity.Z >= 0f);
        }
    }

    [Fact]
    public void BreakUpThrowsTheModelsParts()
    {
        var debris = NewDebris();
        var part = new Model.Part
        {
            Name = "ARM",
            Vertices = [Vector3.Zero, Vector3.UnitX, Vector3.UnitY],
            TriangleIndices = [0, 1, 2],
            TriangleMaterials = [0],
            TriangleUvs = [Vector2.Zero, Vector2.UnitX, Vector2.UnitY],
        };
        var model = new Model { Name = "XGD", Materials = ["SKIN"], PartList = [part, part] };
        var obj = new MdkObject { Arena = Arena, Position = new Vector3(5f, 0f, 0f), PreviousPosition = new Vector3(4f, 0f, 0f) };

        debris.BreakUp(obj, model);

        Assert.Equal(2, debris.PieceCount());
        Assert.All(debris.Pieces, p => Assert.Equal(new Vector3(5f, 0f, 2f), p.Center));
        Assert.All(debris.Pieces, p => Assert.InRange(p.Velocity.X, 0f, 2f));
    }

    private static Effects NewEffects(int frames = 4) => new(new Random(Seed)) { FrameCount = (_, _) => frames };

    [Fact]
    public void DropsFallAndEnd()
    {
        var effects = NewEffects();
        effects.SpawnDrop(Arena, Vector3.Zero, new Vector3(0f, 0f, -0.1f), 5f);
        var drop = Assert.Single(effects.All);

        // 4 frames over 4 ticks each: the first frame first.
        Assert.Equal(0, drop.Frame);
        Assert.Equal(15f, drop.Life);

        effects.Update(1f);
        Assert.True(drop.Position.Z < 0f);

        for (var i = 0; i < 15; i++)
        {
            effects.Update(1f);
        }

        Assert.Empty(effects.All);
    }

    [Fact]
    public void EffectsNeedTheirTexture()
    {
        var effects = NewEffects(frames: 0);
        effects.SpawnBubble(Arena, Vector3.Zero);
        Assert.False(effects.HasEffects(Arena));
    }

    [Fact]
    public void EffectsAreLimited()
    {
        var effects = NewEffects();
        for (var i = 0; i < Effects.MaxEffects + 5; i++)
        {
            effects.SpawnTrail(Arena, Vector3.Zero);
        }

        Assert.Equal(Effects.MaxEffects, effects.All.Count);
    }

    [Fact]
    public void WoundsBleedUntilTheObjectDies()
    {
        var effects = NewEffects();
        var obj = new MdkObject { Arena = Arena };
        effects.Attach(obj, 0, 0);
        effects.Attach(obj, 0, 0);
        Assert.Single(effects.All);

        for (var i = 0; i < 30; i++)
        {
            effects.Update(1f);
        }

        Assert.Contains(effects.All, e => e.Kind == Effects.Kind.Drop);

        obj.Dead = true;
        effects.Update(1f);
        Assert.DoesNotContain(effects.All, e => e.Kind == Effects.Kind.Wound);
    }

    [Fact]
    public void DetachStopsTheWound()
    {
        var effects = NewEffects();
        var obj = new MdkObject { Arena = Arena };
        effects.Attach(obj, 1, 0);
        effects.Detach(obj, 1);
        Assert.Empty(effects.All);
    }

    [Fact]
    public void BubblesRiseThenPop()
    {
        var effects = NewEffects();
        effects.SpawnBubble(Arena, Vector3.Zero);
        var bubble = effects.All[0];
        for (var i = 0; i < 64; i++)
        {
            effects.Update(1f);
        }

        Assert.Equal(Effects.Kind.Pop, bubble.Kind);
        Assert.True(bubble.Position.Z > 0f);
    }

    private static Fans NewFans()
    {
        var record = new Dti.Record(7, Hotspot, 0f, BoxStart, BoxEnd, "");
        var fans = new Fans([new Dti.ArenaEntry(Arena, 0f, [record])], new Random(Seed));
        fans.Create(Arena, Hotspot, "FAN1", 0, Timed, RiseTime);
        return fans;
    }

    [Fact]
    public void FansLiftWhatIsInTheirBox()
    {
        var fans = NewFans();

        // Strength: 20 units in 2 s (2.5 - 0.5); from rest the speed grows by (10 + 64) × dt; a
        // faster one is halved towards 10.
        var vz = fans.Query(Arena, new Vector3(0f, 0f, 5f), 0f, Fans.MaskKurt, Dt);
        Assert.Equal((10f + 64f) * Dt, vz, 4);
        Assert.Equal(30f, fans.Query(Arena, new Vector3(0f, 0f, 5f), 50f, Fans.MaskKurt, Dt), 4);

        Assert.True(float.IsNaN(fans.Query(Arena, new Vector3(11f, 0f, 5f), 0f, Fans.MaskKurt, Dt)));
        Assert.True(float.IsNaN(fans.Query(Arena, new Vector3(0f, 0f, 26f), 0f, Fans.MaskKurt, Dt)));
        Assert.True(float.IsNaN(fans.Query("OTHER", new Vector3(0f, 0f, 5f), 0f, Fans.MaskKurt, Dt)));
    }

    [Fact]
    public void DisabledFansStillLiftObjects()
    {
        // fan_enable 0 clears bit 0 (Kurt) of a mask that starts full.
        var fans = NewFans();
        fans.Enable(Arena, "FAN1", Fans.Power.Off);
        Assert.True(float.IsNaN(fans.Query(Arena, new Vector3(0f, 0f, 5f), 0f, Fans.MaskKurt, Dt)));
        Assert.False(float.IsNaN(fans.Query(Arena, new Vector3(0f, 0f, 5f), 0f, Fans.MaskObjects, Dt)));

        fans.Enable(Arena, "FAN1", Fans.Power.On);
        Assert.False(float.IsNaN(fans.Query(Arena, new Vector3(0f, 0f, 5f), 0f, Fans.MaskKurt, Dt)));

        fans.Remove(Arena, "FAN1");
        Assert.Equal(0, fans.Count);
    }

    [Fact]
    public void FansLetOutSparksInLiveArenas()
    {
        var fans = NewFans();
        var sparks = new List<Vector3>();
        fans.Spark = (_, point) => sparks.Add(point);
        for (var i = 0; i < 400; i++)
        {
            fans.Update();
        }

        // About one tick in 8, just above the bottom of the box.
        Assert.InRange(sparks.Count, 25, 75);
        Assert.All(sparks, p => Assert.Equal(BoxStart.Z - 0.5f + 0.25f, p.Z, 4));

        sparks.Clear();
        fans.IsLive = _ => false;
        fans.Update();
        Assert.Empty(sparks);
    }
}
