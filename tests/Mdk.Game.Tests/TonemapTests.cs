using Mdk.Engine.Render;

namespace Mdk.Game.Tests;

/// <summary>The enhanced look's tone curve (shaders/enhanced.hlsl tonemap): the original's range
/// kept, brighter light rolled off below white.</summary>
public class TonemapTests
{
    private const float Tolerance = 1e-4f;
    private const float Step = 1e-3f;

    [Theory]
    [InlineData(0f)]
    [InlineData(0.25f)]
    [InlineData(Tonemap.Knee)]
    public void DarkerThanTheKneeIsKept(float light) => Assert.Equal(light, Tonemap.Map(light), Tolerance);

    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    [InlineData(100f)]
    public void BrightLightStaysBelowWhite(float light)
    {
        var mapped = Tonemap.Map(light);

        Assert.InRange(mapped, Tonemap.Knee, 1f);
        Assert.True(mapped < light);
    }

    [Fact]
    public void CurveRisesSmoothly()
    {
        // Rising everywhere; at the knee as steep on both sides (no crease).
        for (var x = 0f; x < 4f; x += 0.01f)
        {
            Assert.True(Tonemap.Map(x + 0.01f) > Tonemap.Map(x));
        }

        var below = (Tonemap.Map(Tonemap.Knee) - Tonemap.Map(Tonemap.Knee - Step)) / Step;
        var above = (Tonemap.Map(Tonemap.Knee + Step) - Tonemap.Map(Tonemap.Knee)) / Step;
        Assert.Equal(below, above, 1e-2f);
    }
}
