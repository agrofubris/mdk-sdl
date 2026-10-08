using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Formats;
using Mdk.Game.Flow;

namespace Mdk.Game.Level;

/// <summary>The enhanced look of a level (after the Godot port's <c>Level._enhance</c>): each
/// arena's light from the level's sky (<see cref="SkyLight"/>). Arenas under the sky get a sun
/// (stronger under a brighter sky) with shadows, the sky's light from above and the ground's from
/// below; covered ones (<see cref="ArenaShape"/>) no sun, more light all around. The exposure evens
/// each arena's mean light to the original's unlit textures. A light haze in the colour of the
/// sky's horizon. Arenas and objects are lit, sprites only filtered.
/// <code>
///   sky panorama ─► mean above / below the horizon ─► sun, sky, ground colours ─┐
///   arena faces ──► covered? mean up, mean facing the sun ─────────────────────┴─► Lighting
/// </code></summary>
public static class EnhancedLook
{
    /// <summary>The sun's rotation as the Godot port's light (degrees; pitch about X, then yaw about
    /// Y; Godot's Y up, the light shining along its -Z).</summary>
    private const float SunPitch = -55f;
    private const float SunYaw = 35f;
    private const float ShadowDistance = 300f;
    private const float GlowBloom = 0f;
    private const float HazeDensity = 0.0002f;

    /// <summary>The sun's strength: from the weakest under a black sky to the strongest under a sky
    /// this bright (linear luminance) or brighter; its hue a little of the sky's.</summary>
    private const float SunWeakest = 0.4f;
    private const float SunStrongest = 1f;
    private const float BrightSky = 0.3f;
    private const float SunSaturation = 0.25f;
    /// <summary>Under the sky: its light from above, the ground's from below, a third of their hue.</summary>
    private const float OpenAbove = 0.6f;
    private const float OpenBelow = 0.35f;
    private const float AmbientSaturation = 0.3f;
    /// <summary>Under a roof: light from above and below, a little of the ground's hue.</summary>
    private const float CoveredAbove = 1f;
    private const float CoveredBelow = 0.7f;
    private const float CoveredSaturation = 0.15f;

    /// <summary>The share of faces turned to the sun that it reaches (the rest is shadowed), for
    /// the exposure's mean light.</summary>
    public const float SunlitShare = 0.7f;
    /// <summary>The mean light the exposure gives an arena's faces (1: the original's textures).</summary>
    public const float MeanLight = 1.15f;

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

    /// <summary>An arena's light in the level of <paramref name="dti"/>; the haze takes the colour
    /// below the sky panorama (its horizon).</summary>
    public static Lighting Lighting(Dti dti, Arena arena)
    {
        var colours = SkyLight.Of(dti);
        var cover = ArenaShape.CoverOf(arena);
        Vector3 sun, sky, ground;
        if (cover == Cover.Open)
        {
            var strength = float.Lerp(SunWeakest, SunStrongest, Math.Clamp(SkyLight.Luminance(colours.Sky) / BrightSky, 0f, 1f));
            sun = SkyLight.Tint(colours.Sky, SunSaturation) * strength;
            sky = SkyLight.Tint(colours.Sky, AmbientSaturation) * OpenAbove;
            ground = SkyLight.Tint(colours.Ground, AmbientSaturation) * OpenBelow;
        }
        else
        {
            var tint = SkyLight.Tint(colours.Ground, CoveredSaturation);
            sun = Vector3.Zero;
            sky = tint * CoveredAbove;
            ground = tint * CoveredBelow;
        }

        // The hemisphere is linear in the up component: its mean is at the faces' mean up.
        var up = ArenaShape.MeanUp(arena) * 0.5f + 0.5f;
        var mean = Vector3.Lerp(ground, sky, up) + sun * ArenaShape.MeanFacing(arena, SunDirection) * SunlitShare;
        var exposure = MeanLight / MathF.Max(SkyLight.Luminance(mean), float.Epsilon);

        var at = dti.SkyBottomColor * 4;
        var rgba = dti.Palette.Rgba;
        var horizon = new Vector4(rgba[at], rgba[at + 1], rgba[at + 2], byte.MaxValue) / byte.MaxValue;
        var shadows = cover == Cover.Open ? Shadows.On : Shadows.Off;
        return new Lighting(SunDirection, sun, sky, ground, exposure, shadows, ShadowDistance, GlowBloom, horizon, HazeDensity);
    }

    /// <summary>How the level's surfaces are shaded in a look.</summary>
    public static Shading Surfaces(Graphics graphics) => graphics == Graphics.Enhanced ? Shading.Lit : Shading.Original;

    /// <summary>How sprites (Kurt, effects) are shaded in a look.</summary>
    public static Shading Sprites(Graphics graphics) => graphics == Graphics.Enhanced ? Shading.Sprite : Shading.Original;

    /// <summary>How the sky is sampled in a look.</summary>
    public static Sampling Sky(Graphics graphics) => graphics == Graphics.Enhanced ? Sampling.Linear : Sampling.Nearest;
}
