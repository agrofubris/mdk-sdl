using System.Numerics;
using Mdk.Formats;
using Mdk.Game.Collision;
using Mdk.Game.Kurt;

namespace Mdk.Game.Objects;

/// <summary>The ridden board in front of the floors it stands above. The original draws an object
/// after the floor of its BSP leaf, so the board shows whole even where it dips into the slope
/// (uphill its feet lag a step under it, banked turns sink an edge); with a depth buffer that part
/// hid. As Kurt's sprite (<see cref="KurtSprite.InFront"/>) it's scaled towards the eye: the same
/// on screen, nearer than the floors the eye's rays to its box corners cross inside the box (a
/// crest farther on, hiding its nose, doesn't count).
/// <code>
///   eye ●────────────────────╱── floor
///                     board ▭  (under the floor at a corner) ─► pulled in front of it
/// </code></summary>
internal static class FloorPull
{
    /// <summary>Floors count where the rays cross them inside the box or this far under it.</summary>
    private const float FloorBelow = 1f;
    private const int Corners = 8;
    private const float MinDepth = 1e-3f;

    /// <summary>The board's <paramref name="transform"/> scaled towards the <paramref name="eye"/>
    /// looking along <paramref name="forward"/>.</summary>
    public static Matrix4x4 Of(MdkObject board, Matrix4x4 transform, Vector3 eye, Vector3 forward, ArenaSpace space)
    {
        if (board.Model == null)
        {
            return transform;
        }

        var box = board.Model.Bounds;
        if (!Matrix4x4.Invert(transform, out var toBoard))
        {
            return transform;
        }

        var scale = 1f;
        for (var i = 0; i < Corners; i++)
        {
            // Corner i: bit 0 picks x, bit 1 y, bit 2 z of the box.
            var local = new Vector3((i & 1) == 0 ? box.Min.X : box.Max.X, (i & 2) == 0 ? box.Min.Y : box.Max.Y, (i & 4) == 0 ? box.Min.Z : box.Max.Z);
            var corner = Vector3.Transform(local, transform);
            if (space.FloorCrossing(eye, corner) is not { } floor || !Inside(box, Vector3.Transform(floor.Point, toBoard)))
            {
                continue;
            }

            // A corner on the eye's plane has no depth to pull to.
            var depth = Vector3.Dot(corner - eye, forward);
            if (depth <= MinDepth)
            {
                continue;
            }

            scale = MathF.Min(scale, KurtSprite.InFront(depth, Vector3.Dot(floor.Point - eye, forward)));
        }

        return scale == 1f ? transform : transform * Matrix4x4.CreateScale(scale, eye);
    }

    /// <summary>Whether a point of the board's space is in its box, or under it by up to <see cref="FloorBelow"/>.</summary>
    private static bool Inside(Box box, Vector3 p) =>
        p.X >= box.Min.X && p.X <= box.Max.X && p.Y >= box.Min.Y && p.Y <= box.Max.Y && p.Z >= box.Min.Z - FloorBelow && p.Z <= box.Max.Z;
}
