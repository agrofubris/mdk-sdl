using System.Numerics;

namespace Mdk.Game.Stream;

/// <summary>A 3x3 basis by its columns (MDK coordinates): <see cref="X"/> right, <see cref="Y"/>
/// forward, <see cref="Z"/> up. As Godot's <c>Basis</c>, which the stream's reference uses.</summary>
public readonly record struct TubeBasis(Vector3 X, Vector3 Y, Vector3 Z)
{
    public static readonly TubeBasis Identity = new(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);

    /// <summary>The basis applied to a vector: X·v.x + Y·v.y + Z·v.z.</summary>
    public Vector3 Apply(Vector3 v) => X * v.X + Y * v.Y + Z * v.Z;

    public static TubeBasis operator *(TubeBasis a, TubeBasis b) => new(a.Apply(b.X), a.Apply(b.Y), a.Apply(b.Z));

    /// <summary>A rotation by <paramref name="degrees"/> about a unit axis (counter-clockwise seen
    /// from the axis's tip).</summary>
    public static TubeBasis Rotation(Vector3 axis, float degrees)
    {
        var angle = float.DegreesToRadians(degrees);
        var (sin, cos) = MathF.SinCos(angle);
        Vector3 Turn(Vector3 v) => v * cos + Vector3.Cross(axis, v) * sin + axis * (Vector3.Dot(axis, v) * (1f - cos));
        return new TubeBasis(Turn(Vector3.UnitX), Turn(Vector3.UnitY), Turn(Vector3.UnitZ));
    }

    /// <summary>The model transform (row vectors, as <see cref="Matrix4x4"/>) of this basis at <paramref name="origin"/>.</summary>
    public Matrix4x4 At(Vector3 origin) =>
        new(X.X, X.Y, X.Z, 0f, Y.X, Y.Y, Y.Z, 0f, Z.X, Z.Y, Z.Z, 0f, origin.X, origin.Y, origin.Z, 1f);
}
