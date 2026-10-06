using System.Numerics;
using Mdk.Formats;

namespace Mdk.Game.Scripts;

/// <summary>Bullet holes (special_130 0x45d140; godot-mdk script_runtime.gd <c>stamp_bullet_hole</c>):
/// the hole sprite (<c>BHOLE</c>, <c>BHOLE2</c> without gore) is copied onto the texture of the face
/// a sniper round hit, centred on the hit's texel, its transparent pixels left out. Textures are
/// shared: every object using one gets the hole. The port finds the textured face of the hit part
/// nearest to the point (the original keeps the face the round's test found).
/// <code>
///   hit point ──► nearest face (barycentric weights) ──► UV ──► hole pixels into frame 0 (wrapping)
/// </code></summary>
public static class BulletHoles
{
    public const string Hole = "BHOLE";
    public const string GorelessHole = "BHOLE2";

    /// <summary>A face of a part and the weights of its corners at the point.</summary>
    public readonly record struct Face(int Triangle, Vector3 Weights);

    /// <summary>The face nearest to <paramref name="point"/> (model space) among those
    /// <paramref name="textured"/> accepts, or null.</summary>
    public static Face? Nearest(Model.Part part, Vector3[] vertices, Vector3 point, Func<int, bool> textured)
    {
        Face? best = null;
        var bestDistance = float.MaxValue;
        for (var t = 0; t < part.TriangleMaterials.Length; t++)
        {
            if (!textured(part.TriangleMaterials[t]))
            {
                continue;
            }

            var a = vertices[part.TriangleIndices[t * 3]];
            var b = vertices[part.TriangleIndices[t * 3 + 1]];
            var c = vertices[part.TriangleIndices[t * 3 + 2]];
            var normal = Vector3.Cross(b - a, c - a);
            if (normal == Vector3.Zero)
            {
                continue;
            }

            normal = Vector3.Normalize(normal);
            var weights = Weights(point - normal * Vector3.Dot(normal, point - a), a, b, c);
            var distance = Vector3.DistanceSquared(a * weights.X + b * weights.Y + c * weights.Z, point);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = new Face(t, weights);
            }
        }

        return best;
    }

    /// <summary>Barycentric weights of a point of the triangle's plane, clamped into the triangle
    /// (as Godot's get_triangle_barycentric_coords, clamped and normalised).</summary>
    private static Vector3 Weights(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        const float Tiny = 1e-6f;
        var v0 = b - a;
        var v1 = c - a;
        var v2 = p - a;
        var d00 = Vector3.Dot(v0, v0);
        var d01 = Vector3.Dot(v0, v1);
        var d11 = Vector3.Dot(v1, v1);
        var d20 = Vector3.Dot(v2, v0);
        var d21 = Vector3.Dot(v2, v1);
        var denominator = d00 * d11 - d01 * d01;
        var v = (d11 * d20 - d01 * d21) / denominator;
        var w = (d00 * d21 - d01 * d20) / denominator;
        var weights = Vector3.Clamp(new Vector3(1f - v - w, v, w), Vector3.Zero, Vector3.One);
        return weights / MathF.Max(weights.X + weights.Y + weights.Z, Tiny);
    }

    /// <summary>Copies <paramref name="hole"/> onto frame 0 of <paramref name="texture"/> centred on
    /// <paramref name="uv"/> (texels), wrapping at its edges; index 0 is transparent.</summary>
    public static void Stamp(Texture texture, Vector2 uv, Texture hole)
    {
        var left = (int)MathF.Round(uv.X, MidpointRounding.AwayFromZero) - hole.Width / 2;
        var top = (int)MathF.Round(uv.Y, MidpointRounding.AwayFromZero) - hole.Height / 2;
        for (var y = 0; y < hole.Height; y++)
        {
            for (var x = 0; x < hole.Width; x++)
            {
                var index = hole.Indices[y * hole.Width + x];
                if (index == 0)
                {
                    continue;
                }

                var tx = Wrap(left + x, texture.Width);
                var ty = Wrap(top + y, texture.Height);
                texture.Indices[ty * texture.Width + tx] = index;
            }
        }
    }

    private static int Wrap(int value, int size) => (value % size + size) % size;
}
