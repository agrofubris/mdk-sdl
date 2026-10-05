using System.Numerics;

namespace Mdk.Formats;

/// <summary>Axis-aligned box in MDK coordinates.</summary>
public readonly record struct Box(Vector3 Min, Vector3 Max);

/// <summary>A model (object, alien, weapon...) from an arena's models section or a level's CMI.</summary>
public sealed class Model
{
    /// <summary>Whether the model starts with a flags word (absent in <c>XGHEAD</c> and the fall's models).</summary>
    public enum Header { Flags, NoFlags }

    /// <summary>Parts layout when there is no flags word.</summary>
    public enum Parts { Single, Named }

    /// <summary>The game keeps the first 10 characters of material names.</summary>
    private const int MaterialNameLength = 10;

    public sealed class Part
    {
        public string Name = "";
        /// <summary>Unused by the game.</summary>
        public Vector3 Pivot;
        public Vector3[] Vertices = [];
        /// <summary>Same layout as <see cref="Arena"/>.</summary>
        public int[] TriangleIndices = [];
        public int[] TriangleMaterials = [];
        public Vector2[] TriangleUvs = [];
        public Box Bounds;
    }

    public string Name = "";
    public List<string> Materials = [];
    public List<Part> PartList = [];
    public List<Vector3> ReferencePoints = [];
    public Box Bounds;

    public static Model Parse(string name, byte[] bytes, int offset, Header header = Header.Flags, Parts parts = Parts.Single)
    {
        var model = new Model { Name = name };
        var r = new BinReader(bytes, offset);
        var named = header == Header.Flags ? r.U32() != 0 : parts == Parts.Named;
        var materialCount = r.U32();
        for (var i = 0; i < materialCount; i++)
        {
            var material = r.Name(16);
            model.Materials.Add(material.Length > MaterialNameLength ? material[..MaterialNameLength] : material);
        }

        var partCount = named ? r.U32() : 1;
        for (var i = 0; i < partCount; i++)
        {
            model.PartList.Add(ParsePart(r, named));
        }

        model.Bounds = ReadBounds(r);
        var referenceCount = r.U32();
        for (var i = 0; i < referenceCount; i++)
        {
            model.ReferencePoints.Add(r.Vec3());
        }

        return model;
    }

    private static Part ParsePart(BinReader r, bool named)
    {
        var part = new Part();
        if (named)
        {
            part.Name = r.Name(12);
            part.Pivot = r.Vec3();
        }

        part.Vertices = new Vector3[r.U32()];
        for (var v = 0; v < part.Vertices.Length; v++)
        {
            part.Vertices[v] = r.Vec3();
        }

        var triangleCount = (int)r.U32();
        part.TriangleIndices = new int[triangleCount * 3];
        part.TriangleMaterials = new int[triangleCount];
        part.TriangleUvs = new Vector2[triangleCount * 3];
        for (var t = 0; t < triangleCount; t++)
        {
            part.TriangleIndices[t * 3] = r.S16();
            part.TriangleIndices[t * 3 + 1] = r.S16();
            part.TriangleIndices[t * 3 + 2] = r.S16();
            part.TriangleMaterials[t] = r.S16();
            for (var k = 0; k < 3; k++)
            {
                part.TriangleUvs[t * 3 + k] = new Vector2(r.F32(), r.F32());
            }

            r.Skip(4);
        }

        if (named)
        {
            part.Bounds = ReadBounds(r);
        }

        return part;
    }

    /// <summary>Stored as <c>min x, max x, min y, max y, min z, max z</c>.</summary>
    private static Box ReadBounds(BinReader r)
    {
        float x0 = r.F32(), x1 = r.F32(), y0 = r.F32(), y1 = r.F32(), z0 = r.F32(), z1 = r.F32();
        return new Box(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1));
    }

    /// <summary>Every part's vertices, in part order.</summary>
    public Vector3[][] RestPose() => PartList.Select(p => (Vector3[])p.Vertices.Clone()).ToArray();
}
