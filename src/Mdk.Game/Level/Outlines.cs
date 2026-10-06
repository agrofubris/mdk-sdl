using System.Numerics;
using Mdk.Formats;

namespace Mdk.Game.Level;

/// <summary>Outlined arena triangles (0x40b7f0; godot-mdk mdk_mesh_builder.gd <c>_add_outlines</c>):
/// flag bit 23 draws lines in the triangle's own colour along the edges bits 20-22 pick, so the
/// untextured glass panes get coloured frames.
/// <code>
///        v2
///   bit 22 ╱╲ bit 21
///        ╱    ╲
///      v0──────v1
///        bit 20
/// </code></summary>
public static class Outlines
{
    public const uint Outlined = 1u << 23;

    private static readonly (uint Bit, int From, int To)[] Edges = [(1u << 20, 0, 1), (1u << 21, 1, 2), (1u << 22, 2, 0)];

    /// <summary>The outlined edges of <paramref name="triangles"/>, two points each.</summary>
    public static List<Vector3> Of(Arena arena, IEnumerable<int> triangles)
    {
        var lines = new List<Vector3>();
        foreach (var t in triangles)
        {
            var flags = arena.TriangleFlags[t];
            if ((flags & Outlined) == 0)
            {
                continue;
            }

            foreach (var (bit, from, to) in Edges)
            {
                if ((flags & bit) == 0)
                {
                    continue;
                }

                lines.Add(arena.Vertices[arena.TriangleIndices[t * 3 + from]]);
                lines.Add(arena.Vertices[arena.TriangleIndices[t * 3 + to]]);
            }
        }

        return lines;
    }
}
