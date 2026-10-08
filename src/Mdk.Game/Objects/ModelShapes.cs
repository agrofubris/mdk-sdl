using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Formats;
using Mdk.Game.Smoothing;

namespace Mdk.Game.Objects;

/// <summary>How the enhanced look shapes models: flat faces (the original look), smooth normals, or
/// smooth normals on subdivided triangles.</summary>
public enum ModelShape { Flat, Smooth, Subdivided }

/// <summary>Models' smooth shapes (the enhanced look's): per part, the corners drawn (subdivided or
/// not, <see cref="SmoothMesh"/>, <see cref="Subdivision"/>) and where its creases are, found once
/// from its rest pose at a level's load; each drawn pose (baked, shared) then fills the part's own
/// buffers: its normals, and with subdivision its edges' midpoints. No frame allocates; nothing is
/// kept per pose (a level bakes hundreds of thousands of part poses).
/// <code>
///   part (rest pose) ──load──► PartShape: corners ─► positions, UVs (texels), source triangles; creases
///   draw: pose ─► Fill ─► part.Positions (the pose's, or with the midpoints), part.Normals per corner (packed)
/// </code></summary>
public sealed class ModelShapes(ModelShape shape)
{
    private const int TriangleVertices = 3;

    /// <summary>A part's corners as drawn: their position in <see cref="Positions"/>, their UV
    /// (texels), and per triangle the part's triangle it comes from; and the pose last filled.</summary>
    public sealed class PartShape(int[] vertices, Vector2[] uvs, int[] sources, SmoothMesh mesh, Subdivision? subdivision)
    {
        public readonly int[] Vertices = vertices;
        public readonly Vector2[] Uvs = uvs;
        public readonly int[] Sources = sources;

        /// <summary>The filled pose's positions and packed normals per corner (<see cref="Fill"/>).</summary>
        public Vector3[] Positions { get; private set; } = [];
        public readonly uint[] Normals = new uint[vertices.Length];

        private readonly Vector3[] _slots = new Vector3[mesh.SlotCount];
        private readonly uint[] _packed = new uint[mesh.SlotCount];
        private readonly Vector3[] _corners = new Vector3[subdivision == null ? 0 : mesh.Indices.Length];
        private readonly Vector3[] _subdivided = new Vector3[subdivision?.PositionCount ?? 0];
        private readonly Vector3[] _subdividedNormals = new Vector3[subdivision == null ? 0 : vertices.Length];
        private Vector3[]? _filled;

        /// <summary>Takes a pose's positions (nothing to do when it's the one taken last: objects of
        /// a type often share a frame).</summary>
        public void Fill(Vector3[] positions)
        {
            if (ReferenceEquals(positions, _filled))
            {
                return;
            }

            _filled = positions;
            mesh.SlotNormals(positions, _slots);
            if (subdivision == null)
            {
                // Shaders turn a normal to its face's side: a slot's serves every corner.
                Positions = positions;
                for (var s = 0; s < _slots.Length; s++)
                {
                    _packed[s] = Vertex.PackNormal(_slots[s]);
                }

                for (var c = 0; c < Normals.Length; c++)
                {
                    Normals[c] = _packed[mesh.SlotOf(c)];
                }

                return;
            }

            mesh.Corners(_slots, _corners);
            subdivision.Pose(positions, _corners, _subdivided, _subdividedNormals);
            Positions = _subdivided;
            for (var c = 0; c < Normals.Length; c++)
            {
                Normals[c] = Vertex.PackNormal(_subdividedNormals[c]);
            }
        }
    }

    private readonly Dictionary<Model, PartShape[]> _parts = [];

    public ModelShape Shape => shape;

    /// <summary>A model's parts, shaped the first time (a level's load): triangles whose material
    /// value is <paramref name="pinned"/> (glass, mirrors) keep their edges straight.</summary>
    public PartShape[] PartsOf(Model model, Func<int, bool>? pinned = null)
    {
        if (!_parts.TryGetValue(model, out var parts))
        {
            parts = _parts[model] = Build(model, pinned);
        }

        return parts;
    }

    /// <summary>Fills the visible parts' <see cref="PartShape.Positions"/> and <see cref="PartShape.Normals"/> for a pose.</summary>
    public PartShape[] Pose(Model model, Vector3[][] pose, int hidden)
    {
        var parts = PartsOf(model);
        for (var p = 0; p < parts.Length; p++)
        {
            if ((hidden & (1 << p)) == 0)
            {
                parts[p].Fill(pose[p]);
            }
        }

        return parts;
    }

    /// <summary>Apart from <see cref="PartsOf"/>: a lambda capturing its parameter would be made on every call.</summary>
    private PartShape[] Build(Model model, Func<int, bool>? pinned) => [.. model.PartList.Select(p => Build(p, pinned))];

    private PartShape Build(Model.Part part, Func<int, bool>? pinned)
    {
        var mesh = SmoothMesh.Build(part.Vertices, part.TriangleIndices, SmoothMesh.DefaultCrease);
        if (shape != ModelShape.Subdivided)
        {
            var sources = Enumerable.Range(0, part.TriangleMaterials.Length).ToArray();
            return new PartShape(part.TriangleIndices, part.TriangleUvs, sources, mesh, null);
        }

        var pins = pinned == null ? [] : part.TriangleMaterials.Select(pinned).ToArray();
        var subdivision = Subdivision.Build(mesh, part.TriangleUvs, pins);
        return new PartShape(subdivision.Vertices, subdivision.Uvs, subdivision.Sources, mesh, subdivision);
    }

    /// <summary>Models shaped, and their triangles drawn (each model once).</summary>
    public int ModelCount => _parts.Count;

    public int TriangleCount => _parts.Values.Sum(parts => parts.Sum(p => p.Vertices.Length / TriangleVertices));
}
