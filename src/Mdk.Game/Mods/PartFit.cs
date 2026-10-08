using System.Numerics;

namespace Mdk.Game.Mods;

/// <summary>How a model part moves from its rest pose to a pose, as one affine transform: the
/// least-squares fit of the posed points to the rest ones. MDK animates parts by moving their
/// vertices (rigid matrix tracks, or per-vertex deltas); a replaced part (a mod's mesh) follows the
/// original part through this fit. Rigid motion is fitted exactly; deformation approximately.
/// The points are the part's vertices and, per triangle, a point off its plane (so flat parts fit).
/// <code>
///   rest p, posed q (centred):  A = (Σ q pᵀ)(Σ p pᵀ)⁻¹,  t = q̄ − A p̄      posed ≈ A · rest + t
///   apex of a triangle: centroid + normal / √|normal|   (a length of the triangle's size)
/// </code>
/// Degenerate parts (a point, a line) only move.</summary>
public sealed class PartFit
{
    /// <summary>The covariance's determinant below this share of its scale is degenerate.</summary>
    private const float Degenerate = 1e-6f;
    private const int TriangleCorners = 3;

    private readonly int[] _triangles;
    /// <summary>The model's own vertices: its rest pose, shared by unanimated objects.</summary>
    private readonly Vector3[] _vertices;
    private readonly int _vertexCount;
    private readonly Vector3 _restCentre;
    /// <summary>The rest points, centred.</summary>
    private readonly Vector3[] _rest;
    /// <summary>(Σ p pᵀ)⁻¹, or null when degenerate.</summary>
    private readonly Matrix4x4? _inverse;
    /// <summary>The posed points (scratch: no allocation per fit).</summary>
    private readonly Vector3[] _posed;

    public PartFit(Vector3[] vertices, int[] triangles)
    {
        _triangles = triangles;
        _vertices = vertices;
        _vertexCount = vertices.Length;
        _posed = new Vector3[vertices.Length + triangles.Length / TriangleCorners];
        _rest = new Vector3[_posed.Length];
        Points(vertices, _rest);
        _restCentre = Centre(_rest);
        for (var i = 0; i < _rest.Length; i++)
        {
            _rest[i] -= _restCentre;
        }

        var covariance = new Matrix4x4();
        foreach (var p in _rest)
        {
            covariance += Outer(p, p);
        }

        covariance.M44 = 1f;
        var scale = covariance.M11 + covariance.M22 + covariance.M33;
        var determinant = covariance.GetDeterminant();
        if (_rest.Length > 0 && MathF.Abs(determinant) > Degenerate * scale * scale * scale && Matrix4x4.Invert(covariance, out var inverse))
        {
            _inverse = inverse;
        }
    }

    /// <summary>The transform (System.Numerics' row vectors: <c>Vector3.Transform(rest, fit)</c>)
    /// taking the rest pose to <paramref name="posed"/> (the part's vertices in a pose).</summary>
    public Matrix4x4 Fit(Vector3[] posed)
    {
        if (_rest.Length == 0 || posed.Length != _vertexCount || posed == _vertices)
        {
            return Matrix4x4.Identity;
        }

        Points(posed, _posed);
        var centre = Centre(_posed);
        if (_inverse is not { } inverse)
        {
            return Matrix4x4.CreateTranslation(centre - _restCentre);
        }

        // Σ q pᵀ in row-vector form: p · M = q  ⇒  M = C⁻¹ (Σ p qᵀ).
        var cross = new Matrix4x4();
        for (var i = 0; i < _rest.Length; i++)
        {
            cross += Outer(_rest[i], _posed[i] - centre);
        }

        cross.M44 = 1f;
        var linear = inverse * cross;
        linear.M44 = 1f;
        var translation = centre - Vector3.Transform(_restCentre, linear);
        linear.M41 = translation.X;
        linear.M42 = translation.Y;
        linear.M43 = translation.Z;
        return linear;
    }

    /// <summary>The vertices, then each triangle's apex.</summary>
    private void Points(Vector3[] vertices, Vector3[] points)
    {
        vertices.CopyTo(points, 0);
        for (var t = 0; t < _triangles.Length / TriangleCorners; t++)
        {
            var a = vertices[_triangles[t * TriangleCorners]];
            var b = vertices[_triangles[t * TriangleCorners + 1]];
            var c = vertices[_triangles[t * TriangleCorners + 2]];
            var normal = Vector3.Cross(b - a, c - a);
            var length = normal.Length();
            var centroid = (a + b + c) / TriangleCorners;
            points[vertices.Length + t] = length > 0f ? centroid + normal / MathF.Sqrt(length) : centroid;
        }
    }

    private static Vector3 Centre(Vector3[] points)
    {
        var sum = Vector3.Zero;
        foreach (var p in points)
        {
            sum += p;
        }

        return points.Length == 0 ? sum : sum / points.Length;
    }

    /// <summary>a bᵀ in the 3 x 3 corner.</summary>
    private static Matrix4x4 Outer(Vector3 a, Vector3 b) => new(
        a.X * b.X, a.X * b.Y, a.X * b.Z, 0f,
        a.Y * b.X, a.Y * b.Y, a.Y * b.Z, 0f,
        a.Z * b.X, a.Z * b.Y, a.Z * b.Z, 0f,
        0f, 0f, 0f, 0f);
}
