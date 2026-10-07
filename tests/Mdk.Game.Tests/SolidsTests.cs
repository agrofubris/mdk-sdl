using System.Numerics;
using Mdk.Formats;
using Mdk.Game.Collision;
using Mdk.Game.Objects;
using static Mdk.Game.Collision.Solids;

namespace Mdk.Game.Tests;

/// <summary>Kurt against objects' part boxes (damp_collide_move's object pass, damp_platform_floor).</summary>
public class SolidsTests
{
    /// <summary>Kurt's walking box (half extents).</summary>
    private static readonly Vector3 Half = new(0.6f, 0.6f, 2.5f);
    /// <summary>A crate: x and y 10-12, z 0-4.</summary>
    private static readonly Solid Crate = new(new Box(new Vector3(10f, 10f, 0f), new Vector3(12f, 12f, 4f)), "crate", Footing.Platform);
    private const float Tolerance = 0.05f;

    [Fact]
    public void HeadOnWalkStopsAtTheBox()
    {
        var end = Walk(new Vector3(5f, 11f, 3f), new Vector3(15f, 11f, 3f), Half, [Crate]);
        Assert.NotNull(end);
        Assert.Equal(10f - Half.X, end.Value.X, Tolerance);
        Assert.Equal(11f, end.Value.Y, Tolerance);
    }

    [Fact]
    public void ObliqueWalkSlidesAlongTheFace()
    {
        var end = Walk(new Vector3(5f, 11f, 3f), new Vector3(15f, 13f, 3f), Half, [Crate]);
        Assert.NotNull(end);
        Assert.Equal(10f - Half.X, end.Value.X, Tolerance);
        Assert.Equal(13f, end.Value.Y, Tolerance);
    }

    [Fact]
    public void BoxesAboveOrBelowAndBoxesKurtIsInAreIgnored()
    {
        Assert.Null(Walk(new Vector3(5f, 11f, 9f), new Vector3(15f, 11f, 9f), Half, [Crate]));
        Assert.Null(Walk(new Vector3(11f, 11f, 3f), new Vector3(15f, 11f, 3f), Half, [Crate]));
    }

    [Fact]
    public void PlatformTopIsFoundUnderTheFeet()
    {
        var platform = Platform(new Vector3(11f, 11f, 4.5f), 3.9f, [Crate]);
        Assert.Equal((4f, (object)"crate"), platform);

        // Beside it, or a wall (no footing), there's none.
        Assert.Null(Platform(new Vector3(13f, 11f, 4.5f), 3.9f, [Crate]));
        Assert.Null(Platform(new Vector3(11f, 11f, 4.5f), 3.9f, [Crate with { Footing = Footing.Wall }]));
    }

    /// <summary>What Kurt does with an object: walls block his walk, floors carry him.</summary>
    [Flags]
    public enum Meets { PassesThrough = 0, Blocked = 1, StandsOn = 2, Solid = Blocked | StandsOn }

    /// <summary>The original's rule: walls are objects without 0x10 and 0x800 (damp_collide_move
    /// 0x465e34), floors those with 0x100 and without 0x10 (damp_platform_floor 0x41d2c4); 0x800000
    /// plays no part. E.g. the ridden snowboard (0x800900) is a floor only.</summary>
    [Theory]
    [InlineData(0x0, Meets.Blocked)]
    [InlineData(0x100, Meets.Solid)]
    [InlineData(0x800100, Meets.Solid)]
    [InlineData(0x10, Meets.PassesThrough)]
    [InlineData(0x800110, Meets.PassesThrough)]
    [InlineData(0x800, Meets.PassesThrough)]
    [InlineData(0x900, Meets.StandsOn)]
    [InlineData(0x800900, Meets.StandsOn)]
    [InlineData(0x910, Meets.PassesThrough)]
    public void FlagsDecideWallsAndFloors(int flags, Meets expected)
    {
        var obj = new MdkObject { Flags = flags };
        var solids = obj.Footing is { } footing ? [Crate with { Owner = obj, Footing = footing }] : Array.Empty<Solid>();

        var blocked = Walk(new Vector3(5f, 11f, 3f), new Vector3(15f, 11f, 3f), Half, solids) != null;
        var standsOn = Platform(new Vector3(11f, 11f, 4.5f), 3.9f, solids) != null;
        var meets = (blocked ? Meets.Blocked : Meets.PassesThrough) | (standsOn ? Meets.StandsOn : Meets.PassesThrough);
        Assert.Equal(expected, meets);
    }
}
