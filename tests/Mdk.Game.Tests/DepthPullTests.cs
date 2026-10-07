using System.Numerics;
using Mdk.Engine.Render;

namespace Mdk.Game.Tests;

/// <summary>Layers and outlines drawn nearer than the surfaces they lie on, at the same place on screen.</summary>
public class DepthPullTests
{
    private const float Near = 0.5f;
    private const float Far = 20000f;
    private static readonly Vector3 Eye = new(-2222f, 3200f, -2885f);
    private static readonly Matrix4x4 Camera =
        Matrix4x4.CreateLookAt(Eye, Eye - Vector3.UnitY, Vector3.UnitZ) * Matrix4x4.CreatePerspectiveFieldOfView(1.2f, 4f / 3f, Near, Far);

    private static Vector3 Ndc(Vector3 point, Matrix4x4 transform)
    {
        var clip = Vector4.Transform(new Vector4(point, 1f), transform);
        return new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
    }

    private static Vector3 Ahead(float distance) => Eye + new Vector3(0.3f * distance, -distance, -0.1f * distance);

    [Theory]
    [InlineData(10f)]
    [InlineData(100f)]
    [InlineData(1000f)]
    public void AnEdgeIsPulledTowardsTheEye(float distance)
    {
        // Seen at a grazing angle: half a pixel off the edge, the surface is that much nearer.
        var edge = Ahead(distance);
        var surface = edge + new Vector3(0f, 0.004f * distance, 0f);

        var line = Ndc(edge, DepthPull.Of(Eye, Primitive.Lines, 0) * Camera);
        var plain = Ndc(edge, Camera);

        Assert.Equal(plain.X, line.X, 1e-4f);
        Assert.Equal(plain.Y, line.Y, 1e-4f);
        Assert.True(line.Z < Ndc(surface, Camera).Z);
    }

    [Theory]
    [InlineData(10f)]
    [InlineData(100f)]
    [InlineData(1000f)]
    [InlineData(4000f)]
    public void ALayerIsNearerThanItsWallInTheDepthBuffer(float distance)
    {
        // A 32-bit float depth keeps 24 bits: the pull must span more than one step.
        const float DepthStep = 1f / (1 << 24);
        var point = Ahead(distance);

        var wall = Ndc(point, Camera);
        var poster = Ndc(point, DepthPull.Of(Eye, Primitive.Triangles, 1) * Camera);

        Assert.Equal(wall.X, poster.X, 1e-4f);
        Assert.Equal(wall.Y, poster.Y, 1e-4f);
        Assert.True(wall.Z - poster.Z > 4f * DepthStep);
    }

    [Fact]
    public void PlainTrianglesStayPut()
    {
        Assert.Equal(Matrix4x4.Identity, DepthPull.Of(Eye, Primitive.Triangles, 0));
    }
}
