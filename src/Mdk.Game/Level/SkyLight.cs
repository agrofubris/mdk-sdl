using System.Numerics;
using Mdk.Formats;

namespace Mdk.Game.Level;

/// <summary>The colours of a level's sky panorama, for the enhanced look's light: the mean linear
/// colour above its horizon (the sky) and from it down (the ground, mountains, haze). Only the
/// columns of the full turn count; rows beyond the panorama take its top and bottom colours.
/// <code>
///   row 0 ┌──────────────┐
///         │ sky          │ ◄ mean: Sky
///   horizon ────────────── 
///         │ ground       │ ◄ mean: Ground
///         └──────────────┘
/// </code></summary>
public static class SkyLight
{
    /// <summary>The palette's sRGB to linear light (as the shaders' GAMMA).</summary>
    private const float Gamma = 2.2f;
    /// <summary>Rec. 709 luminance of linear RGB.</summary>
    private static readonly Vector3 LuminanceWeights = new(0.2126f, 0.7152f, 0.0722f);
    /// <summary>Darker colours have no hue to keep: white.</summary>
    private const float Black = 1e-4f;

    /// <summary>Mean linear colours above and below the horizon.</summary>
    public readonly record struct Colours(Vector3 Sky, Vector3 Ground);

    public static Colours Of(Dti dti)
    {
        var height = dti.Sky.Height;
        var horizon = Math.Clamp(dti.SkyHorizonRow, 0, height);
        var sky = horizon > 0 ? Mean(dti, 0, horizon) : Linear(dti.Palette, dti.SkyTopColor);
        var ground = horizon < height ? Mean(dti, horizon, height) : Linear(dti.Palette, dti.SkyBottomColor);
        return new Colours(sky, ground);
    }

    /// <summary>The hue of <paramref name="colour"/> at luminance 1: white blended towards it by
    /// <paramref name="saturation"/> (0 white, 1 its own hue).</summary>
    public static Vector3 Tint(Vector3 colour, float saturation)
    {
        var luminance = Luminance(colour);
        if (luminance < Black)
        {
            return Vector3.One;
        }

        return Vector3.Lerp(Vector3.One, colour / luminance, saturation);
    }

    public static float Luminance(Vector3 colour) => Vector3.Dot(colour, LuminanceWeights);

    /// <summary>The mean linear colour of rows [first, end) of the full turn's columns.</summary>
    private static Vector3 Mean(Dti dti, int first, int end)
    {
        var sky = dti.Sky;
        var columns = Math.Min(dti.SkyWrapWidth, sky.Width);
        var sum = Vector3.Zero;
        for (var row = first; row < end; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                sum += Linear(dti.Palette, sky.Indices[row * sky.Width + column]);
            }
        }

        return sum / Math.Max(1, (end - first) * columns);
    }

    private static Vector3 Linear(Palette palette, int index)
    {
        var rgba = palette.Rgba;
        var srgb = new Vector3(rgba[index * 4], rgba[index * 4 + 1], rgba[index * 4 + 2]) / byte.MaxValue;
        return new Vector3(MathF.Pow(srgb.X, Gamma), MathF.Pow(srgb.Y, Gamma), MathF.Pow(srgb.Z, Gamma));
    }
}
