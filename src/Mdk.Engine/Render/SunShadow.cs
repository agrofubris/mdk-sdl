using System.Numerics;

namespace Mdk.Engine.Render;

/// <summary>The sun's shadow map: an orthographic view along the sunlight, a square of
/// 2 × <c>radius</c> around a centre (the camera), deep enough for casters far towards the sun.
/// The centre snaps to whole texels so that shadows don't shimmer when the camera moves.
/// <code>
///        sun
///         ╲  ╲  ╲        ┌───────┐ ◄ near: Reach towards the sun
///          ╲  ╲  ╲       │   ●   │   centre (depth 0.5)
///   ────────▓▓──────     └───────┘ ◄ far: Reach beyond
/// </code></summary>
public static class SunShadow
{
    /// <summary>How far the map reaches towards and away from the sun, from the centre (units).</summary>
    public const float Reach = 4000f;

    /// <summary>World to the shadow map's clip space (x, y -1 to 1; depth 0 to 1, growing along the light).</summary>
    public static Matrix4x4 Matrix(Vector3 sunDirection, Vector3 centre, float radius, uint size)
    {
        var direction = Vector3.Normalize(sunDirection);
        var up = MathF.Abs(direction.Z) > 0.99f ? Vector3.UnitY : Vector3.UnitZ;
        var view = Matrix4x4.CreateLookAt(Vector3.Zero, direction, up);

        // The centre in light space, on whole texels; the view looks down -z.
        var texel = 2f * radius / size;
        var at = Vector3.Transform(centre, view);
        var x = MathF.Round(at.X / texel) * texel;
        var y = MathF.Round(at.Y / texel) * texel;
        var projection = Matrix4x4.CreateOrthographicOffCenter(x - radius, x + radius, y - radius, y + radius,
            -at.Z - Reach, -at.Z + Reach);
        return view * projection;
    }
}
