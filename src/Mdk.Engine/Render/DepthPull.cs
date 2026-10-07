using System.Numerics;

namespace Mdk.Engine.Render;

/// <summary>The original draws without a depth buffer: a poster drawn after its wall covers it, a
/// glass pane's outline covers the edge it lies on. Here they'd flicker or be hidden by halves, so
/// they're drawn pulled towards the eye by a share of their distance: the same place on screen (no
/// cracks to their neighbours), nearer in depth.
/// <code>
///   eye ●─────────────────────○── wall
///                           ◄─┘ poster: 1/256 of the distance a layer
///                         ◄───┘ outline: 1/128 (half a pixel off the edge at a grazing angle)
/// </code></summary>
internal static class DepthPull
{
    private const float LayerShare = 1f / 256f;
    private const float LineShare = 1f / 128f;

    /// <summary>World to world: scaled towards the eye.</summary>
    public static Matrix4x4 Of(Vector3 eye, Primitive primitive, int layer)
    {
        var share = layer * LayerShare + (primitive == Primitive.Lines ? LineShare : 0f);
        return share == 0f ? Matrix4x4.Identity : Matrix4x4.CreateScale(1f - share, eye);
    }
}
