using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Formats;
using Mdk.Game.Flow;

namespace Mdk.Game.Level;

/// <summary>The enhanced look of a level (the Godot port's <c>Level._enhance</c>): one sun with
/// shadows, the same in every level; white light all around, so shaded faces keep about the
/// texture's brightness; a little glow; a light haze in the colour of the sky's horizon. Arenas and
/// objects are lit, sprites only filtered.</summary>
public static class EnhancedLook
{
    /// <summary>The sun's rotation as the Godot port's light (degrees; pitch about X, then yaw about
    /// Y; Godot's Y up, the light shining along its -Z).</summary>
    private const float SunPitch = -55f;
    private const float SunYaw = 35f;
    private const float SunEnergy = 0.5f;
    private const float AmbientEnergy = 0.75f;
    private const float ShadowDistance = 300f;
    private const float GlowBloom = 0.05f;
    private const float HazeDensity = 0.0007f;

    /// <summary>The way the sunlight goes (MDK coordinates, Z up).</summary>
    public static Vector3 SunDirection { get; } = Sun(SunPitch, SunYaw);

    /// <summary>The way a Godot light rotated by <paramref name="pitch"/> and <paramref name="yaw"/>
    /// degrees shines, in MDK coordinates: Godot (x, y, z) is MDK (x, -z, y).
    /// <code>
    ///   Godot: Ry(yaw) · Rx(pitch) · (0, 0, -1) = (-cos p · sin y, sin p, -cos p · cos y)
    /// </code></summary>
    public static Vector3 Sun(float pitch, float yaw)
    {
        var p = float.DegreesToRadians(pitch);
        var y = float.DegreesToRadians(yaw);
        var godot = new Vector3(-MathF.Cos(p) * MathF.Sin(y), MathF.Sin(p), -MathF.Cos(p) * MathF.Cos(y));
        return new Vector3(godot.X, -godot.Z, godot.Y);
    }

    /// <summary>The level's light: the haze takes the colour below the sky panorama (its horizon).</summary>
    public static Lighting Lighting(Dti dti)
    {
        var at = dti.SkyBottomColor * 4;
        var rgba = dti.Palette.Rgba;
        var horizon = new Vector4(rgba[at], rgba[at + 1], rgba[at + 2], byte.MaxValue) / byte.MaxValue;
        return new Lighting(SunDirection, SunEnergy, AmbientEnergy, ShadowDistance, GlowBloom, horizon, HazeDensity);
    }

    /// <summary>How the level's surfaces are shaded in a look.</summary>
    public static Shading Surfaces(Graphics graphics) => graphics == Graphics.Enhanced ? Shading.Lit : Shading.Original;

    /// <summary>How sprites (Kurt, effects) are shaded in a look.</summary>
    public static Shading Sprites(Graphics graphics) => graphics == Graphics.Enhanced ? Shading.Sprite : Shading.Original;

    /// <summary>How the sky is sampled in a look.</summary>
    public static Sampling Sky(Graphics graphics) => graphics == Graphics.Enhanced ? Sampling.Linear : Sampling.Nearest;
}
