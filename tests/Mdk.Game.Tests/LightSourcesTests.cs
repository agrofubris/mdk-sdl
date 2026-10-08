using Mdk.Game.Objects;

namespace Mdk.Game.Tests;

/// <summary>The enhanced look's point lights of the game: explosions fade as they play, fires flicker.</summary>
public class LightSourcesTests
{
    private const float Tolerance = 1e-4f;
    private const int Frames = 20;

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(10f, 0.5f)]
    [InlineData(20f, 0f)]
    [InlineData(25f, 0f)]
    public void ExplosionsFadeAsTheyPlay(float time, float strength) =>
        Assert.Equal(strength, LightSources.Fade(time, Frames), Tolerance);

    [Fact]
    public void FiresFlickerWithinTheirRange()
    {
        var seen = new HashSet<float>();
        for (var tick = 0; tick < 100; tick++)
        {
            var flicker = LightSources.Flicker(tick, 3);
            Assert.InRange(flicker, 1f - LightSources.FlickerDepth, 1f);
            seen.Add(flicker);
        }

        Assert.True(seen.Count > 1);
    }
}
