using System.Numerics;
using Mdk.Engine.Render;

namespace Mdk.Game.Tests;

/// <summary>The frame's point lights: gathered up to a limit, the ones reaching nearest the camera
/// packed for the GPU.</summary>
public class PointLightsTests
{
    private static readonly Vector3 Warm = new(1f, 0.8f, 0.5f);

    private static PointLight At(float x, float radius = 1f) => new(new Vector3(x, 0f, 0f), Warm, radius);

    [Fact]
    public void LightsBeyondTheLimitAreDropped()
    {
        var lights = new PointLights();
        for (var i = 0; i < PointLights.Gathered + 5; i++)
        {
            lights.Add(At(i));
        }

        Assert.Equal(PointLights.Gathered, lights.Count);
    }

    [Fact]
    public void ClearEmptiesTheFrame()
    {
        var lights = new PointLights();
        lights.Add(At(1f));

        lights.Clear();

        Assert.Equal(0, lights.Count);
        Assert.Equal(0, lights.Pack(Vector3.Zero, new PointLight[PointLights.Shown]));
    }

    [Fact]
    public void FewLightsAreAllPacked()
    {
        var lights = new PointLights();
        lights.Add(At(5f));
        lights.Add(At(3f));
        var packed = new PointLight[PointLights.Shown];

        Assert.Equal(2, lights.Pack(Vector3.Zero, packed));
    }

    [Fact]
    public void NearestLightsArePacked()
    {
        // 30 lights from x = 30 down to 1: the 16 nearest are x = 1 to 16.
        var lights = new PointLights();
        for (var x = 30; x >= 1; x--)
        {
            lights.Add(At(x));
        }

        var packed = new PointLight[PointLights.Shown];
        var count = lights.Pack(Vector3.Zero, packed);

        Assert.Equal(PointLights.Shown, count);
        Assert.Equal(Enumerable.Range(1, PointLights.Shown).Select(x => (float)x), packed.Select(l => l.Position.X).Order());
    }

    [Fact]
    public void FarReachBeatsNearness()
    {
        // A blast 100 away reaching 200 is nearer than any of 16 sparks 10 away reaching 1.
        var lights = new PointLights();
        for (var i = 0; i < PointLights.Shown; i++)
        {
            lights.Add(At(10f));
        }

        lights.Add(At(100f, 200f));
        var packed = new PointLight[PointLights.Shown];
        lights.Pack(Vector3.Zero, packed);

        Assert.Contains(packed, l => l.Radius == 200f);
    }

    [Fact]
    public void DarkOrPointlessLightsAreSkipped()
    {
        var lights = new PointLights();
        lights.Add(new PointLight(Vector3.Zero, Vector3.Zero, 10f));
        lights.Add(new PointLight(Vector3.Zero, Warm, 0f));

        Assert.Equal(0, lights.Count);
    }
}
