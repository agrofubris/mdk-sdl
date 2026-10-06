using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Game.Flow;
using Mdk.Game.Level;

namespace Mdk.Game.Tests;

/// <summary>The enhanced look: the sun (level.gd _enhance) and its shadow map.</summary>
public class EnhancedLookTests
{
    private const float Tolerance = 1e-3f;
    private const float Radius = 300f;
    private const uint Size = 2048;

    private static Vector3 Clip(Matrix4x4 matrix, Vector3 point)
    {
        var clip = Vector4.Transform(new Vector4(point, 1f), matrix);
        return new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
    }

    [Fact]
    public void SunShinesAsGodots()
    {
        // Godot's light at (-55, 35, 0) degrees shines along (-0.329, -0.819, -0.470): Y up, -Z ahead.
        var sun = EnhancedLook.SunDirection;

        Assert.Equal(-0.329f, sun.X, Tolerance);
        Assert.Equal(0.470f, sun.Y, Tolerance);
        Assert.Equal(-0.819f, sun.Z, Tolerance);
        Assert.Equal(1f, sun.Length(), Tolerance);
    }

    [Fact]
    public void StraightDownSunHasNoYaw()
    {
        var sun = EnhancedLook.Sun(-90f, 35f);

        Assert.Equal(-1f, sun.Z, Tolerance);
    }

    [Fact]
    public void ShadowMapIsCentredOnTheCamera()
    {
        var centre = new Vector3(-1167f, -1173f, -27f);
        var matrix = SunShadow.Matrix(EnhancedLook.SunDirection, centre, Radius, Size);

        // Snapped to whole texels: off by half a texel at most.
        var halfTexel = 1f / Size;
        var clip = Clip(matrix, centre);
        Assert.InRange(MathF.Abs(clip.X), 0f, halfTexel + Tolerance);
        Assert.InRange(MathF.Abs(clip.Y), 0f, halfTexel + Tolerance);
        Assert.Equal(0.5f, clip.Z, Tolerance);
    }

    [Fact]
    public void DepthGrowsAlongTheSunlight()
    {
        var sun = EnhancedLook.SunDirection;
        var matrix = SunShadow.Matrix(sun, Vector3.Zero, Radius, Size);

        var near = Clip(matrix, -sun * 100f);
        var far = Clip(matrix, sun * 100f);

        Assert.True(near.Z < far.Z);
        Assert.Equal(near.X, far.X, Tolerance);
        Assert.Equal(near.Y, far.Y, Tolerance);
    }

    [Fact]
    public void ShadowMapCoversTheRadius()
    {
        var matrix = SunShadow.Matrix(-Vector3.UnitZ, Vector3.Zero, Radius, Size);

        // Straight down: the square's edges are the radius away on the ground.
        var edge = Clip(matrix, new Vector3(Radius, 0f, 0f));

        Assert.Equal(1f, MathF.Abs(edge.X) + MathF.Abs(edge.Y), Tolerance);
    }

    [Fact]
    public void SmallMovesKeepTheTexelGrid()
    {
        var sun = EnhancedLook.SunDirection;
        var texel = 2f * Radius / Size;

        var a = SunShadow.Matrix(sun, Vector3.Zero, Radius, Size);
        var b = SunShadow.Matrix(sun, new Vector3(texel * 0.1f, 0f, 0f), Radius, Size);

        Assert.Equal(Clip(a, Vector3.One).X, Clip(b, Vector3.One).X, Tolerance);
    }

    [Fact]
    public void LooksPickShading()
    {
        Assert.Equal(Shading.Original, EnhancedLook.Surfaces(Graphics.Original));
        Assert.Equal(Shading.Lit, EnhancedLook.Surfaces(Graphics.Enhanced));
        Assert.Equal(Shading.Sprite, EnhancedLook.Sprites(Graphics.Enhanced));
        Assert.Equal(Sampling.Linear, EnhancedLook.Sky(Graphics.Enhanced));
        Assert.Equal(Sampling.Nearest, EnhancedLook.Sky(Graphics.Original));
    }
}
