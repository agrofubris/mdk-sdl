using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Game.Kurt;

namespace Mdk.Game.Tests;

/// <summary>The stereo eyes (<see cref="View.Eye"/>): the eyes' images meet at the convergence
/// distance, everything farther converges behind the screen and everything nearer in front, the
/// sky (a direction: no distance) keeps the frustums' shift, the full eye separation over it.</summary>
public class StereoTests
{
    private const float Tolerance = 1e-3f;
    private const float Separation = 0.5f;
    private const float Convergence = 10f;
    private const float Near = 0.5f;
    private const float Far = 20000f;
    private const float FieldOfView = 71.5f;
    private const float Aspect = 4f / 3f;
    /// <summary>The projection's horizontal scale (System.Numerics' perspective matrix).</summary>
    private const float ScaleX = 1f / (Aspect * 0.71984f);
    private static readonly Vector3 Position = new(1f, 2f, 3f);
    private static readonly Vector3 Forward = Vector3.Normalize(new Vector3(0.2f, 1f, -0.1f));
    private static readonly Vector3 Up = new(0f, 0f, 1f);

    private static View Center() => CameraMath.View(Position, Forward, Up, FieldOfView, Aspect, Near, Far);

    private static (View Left, View Right) Eyes(float convergence) =>
        (Center().Eye(-1f, Separation, convergence), Center().Eye(+1f, Separation, convergence));

    /// <summary>Where a world point (w = 1) or direction (w = 0) lands in NDC x.</summary>
    private static float NdcX(View view, Vector3 point, float w = 1f)
    {
        var clip = Vector4.Transform(new Vector4(point, w), view.ViewProjection);
        return clip.X / clip.W;
    }

    [Fact]
    public void NoStereoLeavesTheView()
    {
        var view = Center();
        Assert.Equal(view.ViewProjection, view.Eye(0f, Separation, Convergence).ViewProjection);
    }

    [Fact]
    public void WithoutACameraTheViewStays()
    {
        var view = new View(Matrix4x4.Identity, Matrix4x4.Identity, Vector3.Zero);
        Assert.Equal(Matrix4x4.Identity, view.Eye(-1f, Separation, Convergence).ViewProjection);
        Assert.Equal(Matrix4x4.Identity, view.Eye(+1f, Separation, Convergence).ViewProjection);
    }

    [Fact]
    public void TheConvergencePlaneMeets()
    {
        var (left, right) = Eyes(Convergence);
        var point = Position + Forward * Convergence;

        Assert.Equal(0f, NdcX(left, point), Tolerance);
        Assert.Equal(0f, NdcX(right, point), Tolerance);
    }

    [Fact]
    public void FartherThanConvergenceGoesUncrossed()
    {
        // The left eye's image to the left, the right eye's to the right: it appears behind.
        var (left, right) = Eyes(Convergence);
        var far = Position + Forward * 200f;

        Assert.True(NdcX(left, far) < -Tolerance);
        Assert.Equal(NdcX(left, far), -NdcX(right, far), Tolerance);
    }

    [Fact]
    public void NearerThanConvergenceGoesCrossed()
    {
        var (left, right) = Eyes(Convergence);
        var near = Position + Forward * (Convergence * 0.5f);

        Assert.True(NdcX(left, near) > Tolerance);
        Assert.Equal(NdcX(left, near), -NdcX(right, near), Tolerance);
    }

    [Fact]
    public void TheSkyKeepsItsFullSeparation()
    {
        // A direction (w 0): the two cameras see it along their axes, so its NDC is the frustums'
        // shift alone (the full eye separation over the convergence distance).
        foreach (var convergence in new[] { 3f, 10f, 1000f })
        {
            var (left, right) = Eyes(convergence);
            var apart = Separation * ScaleX / (2f * convergence);

            Assert.Equal(apart, NdcX(right, Forward, w: 0f), Tolerance);
            Assert.Equal(-apart, NdcX(left, Forward, w: 0f), Tolerance);
        }

        Assert.Equal(0f, NdcX(Center(), Forward, w: 0f), Tolerance);
    }
}
