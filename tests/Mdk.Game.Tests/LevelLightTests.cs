using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Formats;
using Mdk.Game.Level;

namespace Mdk.Game.Tests;

/// <summary>The enhanced look's light per level and arena: the sky's colours (SkyLight), the roof
/// over an arena (ArenaShape) and the light made of them (EnhancedLook).</summary>
public class LevelLightTests
{
    private const float Tolerance = 1e-3f;
    private const byte Red = 1;
    private const byte Blue = 2;
    private const byte White = 3;
    private const int Width = 4;
    private const int Extra = 4;
    private const int Rows = 4;

    private static Palette Colours()
    {
        var rgb = new byte[Palette.Size * 3];
        rgb[Red * 3] = byte.MaxValue;
        rgb[Blue * 3 + 2] = byte.MaxValue;
        rgb[White * 3] = rgb[White * 3 + 1] = rgb[White * 3 + 2] = byte.MaxValue;
        return Palette.FromRgb(rgb);
    }

    /// <summary>A panorama of <see cref="Rows"/> rows: <paramref name="above"/> above the horizon,
    /// <paramref name="below"/> from it down, its wrap columns white.</summary>
    private static Dti SkyOf(byte above, byte below, int horizon)
    {
        var width = Width + Extra;
        var indices = new byte[width * Rows];
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < width; column++)
            {
                indices[row * width + column] = column >= Width ? White : row < horizon ? above : below;
            }
        }

        return new Dti
        {
            Palette = Colours(),
            Sky = new Texture { Width = width, Height = Rows, Indices = indices },
            SkyWrapWidth = Width,
            SkyHorizonRow = horizon,
            SkyTopColor = White,
            SkyBottomColor = White,
        };
    }

    private static void Near(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
        Assert.Equal(expected.Z, actual.Z, Tolerance);
    }

    [Fact]
    public void SkyAveragesAboveAndBelowTheHorizon()
    {
        var sky = SkyLight.Of(SkyOf(Blue, Red, Rows / 2));

        Near(Vector3.UnitZ, sky.Sky);
        Near(Vector3.UnitX, sky.Ground);
    }

    [Fact]
    public void SkyBeyondThePanoramaTakesItsColours()
    {
        // The horizon above the panorama: nothing above it but the top colour.
        var sky = SkyLight.Of(SkyOf(Blue, Red, 0));

        Near(Vector3.One, sky.Sky);
        Near(Vector3.UnitX, sky.Ground);
    }

    [Fact]
    public void SkyAveragesInLinearColour()
    {
        // Half red, half blue rows above: half of each in linear light.
        var dti = SkyOf(Blue, Red, Rows / 2);
        dti.Sky.Indices[0] = Red;
        dti.Sky.Indices[1] = Red;

        var sky = SkyLight.Of(dti);

        Near(new Vector3(0.25f, 0f, 0.75f), sky.Sky);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void TintKeepsTheBrightness(float saturation)
    {
        var tint = SkyLight.Tint(new Vector3(0.1f, 0.2f, 0.6f), saturation);

        Assert.Equal(1f, SkyLight.Luminance(tint), Tolerance);
    }

    [Fact]
    public void TintOfBlackIsWhite()
    {
        Near(Vector3.One, SkyLight.Tint(Vector3.Zero, 1f));
        Near(Vector3.One, SkyLight.Tint(Vector3.UnitZ, 0f));
    }

    /// <summary>Triangles as MDK winds them: the cross product of their edges points into the solid
    /// (a floor's down).</summary>
    private static Arena ArenaOf(params Vector3[][] triangles)
    {
        var vertices = triangles.SelectMany(t => t).ToArray();
        return new Arena
        {
            Vertices = vertices,
            TriangleIndices = Enumerable.Range(0, vertices.Length).ToArray(),
            TriangleMaterials = new int[triangles.Length],
            TriangleFlags = new uint[triangles.Length],
        };
    }

    private static readonly Vector3[] Floor = [Vector3.Zero, Vector3.UnitY, Vector3.UnitX];
    private static readonly Vector3[] Ceiling = [Vector3.UnitZ, Vector3.UnitZ + Vector3.UnitX, Vector3.UnitZ + Vector3.UnitY];
    private static readonly Vector3[] Wall = [Vector3.Zero, Vector3.UnitX, Vector3.UnitZ];

    [Fact]
    public void FloorAloneIsOpen() => Assert.Equal(Cover.Open, ArenaShape.CoverOf(ArenaOf(Floor, Wall)));

    [Fact]
    public void FloorUnderACeilingIsCovered() => Assert.Equal(Cover.Covered, ArenaShape.CoverOf(ArenaOf(Floor, Ceiling, Wall)));

    [Fact]
    public void FacesAreMeanedByArea()
    {
        // A floor and a wall of the same area: up 1 and 0.
        var arena = ArenaOf(Floor, Wall);

        Assert.Equal(0.5f, ArenaShape.MeanUp(arena), Tolerance);
        Assert.Equal(0.5f, ArenaShape.MeanFacing(arena, -Vector3.UnitZ), Tolerance);
    }

    [DataTheory]
    [InlineData(3, "HMO_1", Cover.Open)]
    [InlineData(3, "HMO_2", Cover.Covered)]
    [InlineData(4, "MEAT_3", Cover.Open)]
    [InlineData(4, "MEAT_1", Cover.Covered)]
    [InlineData(5, "MUSE_1", Cover.Covered)]
    [InlineData(6, "OLYM_5", Cover.Covered)]
    [InlineData(7, "DANT_1", Cover.Covered)]
    [InlineData(7, "DANT_5", Cover.Open)]
    [InlineData(7, "CDANT_1", Cover.Covered)]
    [InlineData(8, "GUNT_1", Cover.Open)]
    public void LevelArenasAreCoveredOrOpen(int level, string name, Cover cover)
    {
        var arena = new LevelData(MdkData.Find()!, level).ArenaNamed(name)!;

        Assert.Equal(cover, ArenaShape.CoverOf(arena));
    }

    [Fact]
    public void CoveredArenasHaveNoSun()
    {
        var light = EnhancedLook.Lighting(SkyOf(Blue, Red, Rows / 2), ArenaOf(Floor, Ceiling, Wall));

        Assert.Equal(Vector3.Zero, light.Sun);
        Assert.Equal(Shadows.Off, light.Shadows);
    }

    [Fact]
    public void OpenArenasHaveSunAndShadows()
    {
        var light = EnhancedLook.Lighting(SkyOf(Blue, Red, Rows / 2), ArenaOf(Floor, Wall));

        Assert.True(SkyLight.Luminance(light.Sun) > 0f);
        Assert.Equal(Shadows.On, light.Shadows);
    }

    [Fact]
    public void LightFromAboveIsTheSkys()
    {
        // A blue sky over red ground: blue from above, red from below.
        var light = EnhancedLook.Lighting(SkyOf(Blue, Red, Rows / 2), ArenaOf(Floor, Wall));

        Assert.True(light.Sky.Z > light.Sky.X);
        Assert.True(light.Ground.X > light.Ground.Z);
        Assert.True(SkyLight.Luminance(light.Sky) > SkyLight.Luminance(light.Ground));
    }

    [Fact]
    public void BrighterSkyMakesAStrongerSun()
    {
        var arena = ArenaOf(Floor, Wall);

        var dark = EnhancedLook.Lighting(SkyOf(Blue, Red, Rows / 2), arena);
        var bright = EnhancedLook.Lighting(SkyOf(White, Red, Rows / 2), arena);

        Assert.True(SkyLight.Luminance(bright.Sun) > SkyLight.Luminance(dark.Sun));
    }

    [Theory]
    [InlineData(Cover.Open)]
    [InlineData(Cover.Covered)]
    public void ExposureEvensTheArenasMeanLight(Cover cover)
    {
        var arena = cover == Cover.Open ? ArenaOf(Floor, Wall) : ArenaOf(Floor, Ceiling, Wall);
        var light = EnhancedLook.Lighting(SkyOf(Blue, Red, Rows / 2), arena);

        // The hemisphere's mean over the faces, plus the sun on its lit share.
        var up = ArenaShape.MeanUp(arena) * 0.5f + 0.5f;
        var mean = Vector3.Lerp(light.Ground, light.Sky, up)
            + light.Sun * ArenaShape.MeanFacing(arena, light.SunDirection) * EnhancedLook.SunlitShare;

        Assert.Equal(EnhancedLook.MeanLight, SkyLight.Luminance(mean) * light.Exposure, Tolerance);
    }
}
