using System.Numerics;

namespace Mdk.Formats;

/// <summary>A vertex animation of a model's parts.</summary>
public sealed class ModelAnimation
{
    private const uint SignMask = 0x7FFFFFFF;
    private const int MatrixValues = 12;
    private const int TrackNameLength = 12;
    /// <summary>Each frame's box (6 floats) in the 1996 demo's animations.</summary>
    private const int BoundsSize = 24;

    /// <summary>The retail layout, or the 1996 demo's (<see cref="ParseBeta"/>).</summary>
    private enum Layout { Retail, Beta }

    public string Name = "";
    /// <summary>1.0 means 30 frames per second.</summary>
    public float Speed = 1f;
    public int FrameCount;
    /// <summary>Per frame, the object's movement in model space.</summary>
    public Vector3[] RootMotion = [];
    /// <summary>Per reference point, its place in each frame.</summary>
    public List<Vector3[]> ReferencePoints = [];

    private byte[] _bytes = [];
    private Layout _layout;
    /// <summary>Lowercase track name to its offset.</summary>
    private readonly Dictionary<string, int> _tracks = [];

    public static ModelAnimation Parse(string name, byte[] bytes, int offset)
    {
        var animation = new ModelAnimation { Name = name, _bytes = bytes };
        var r = new BinReader(bytes, offset);
        animation.Speed = r.F32();
        var trackCount = r.U32();
        animation.FrameCount = (int)r.U32();
        for (var i = 0; i < trackCount; i++)
        {
            var trackOffset = offset + 4 + (int)r.U32();
            animation._tracks[Bin.Ascii(bytes, trackOffset, 12).ToLowerInvariant()] = trackOffset;
        }

        animation.RootMotion = new Vector3[animation.FrameCount];
        for (var f = 0; f < animation.FrameCount; f++)
        {
            animation.RootMotion[f] = r.Vec3();
        }

        var pointCount = r.U32();
        for (var i = 0; i < pointCount; i++)
        {
            var points = new Vector3[animation.FrameCount];
            for (var f = 0; f < animation.FrameCount; f++)
            {
                points[f] = r.Vec3();
            }

            animation.ReferencePoints.Add(points);
        }

        return animation;
    }

    /// <summary>An animation of the 1996 demo (<see cref="BetaDemo"/>), kept in <c>LEVELn.CMI</c>:
    /// <code>
    /// +0   u32 track count T
    /// +4   u32 frame count F
    /// +8   u32 track offset[T]         relative to the animation
    ///      f32 root motion[F][3]
    ///      f32 bounds[F][6]            the model's box in each frame
    ///      u32 reference point count R, f32[R][F][3]
    /// track: char[12] part name, u32 vertex count, f32 scale, f32 base[n][3],
    ///        then for each of the other F - 1 frames s8 delta[n][3]
    /// </code>
    /// No speed, no frame numbers in the deltas, no matrix tracks.</summary>
    public static ModelAnimation ParseBeta(string name, byte[] bytes, int offset)
    {
        var animation = new ModelAnimation { Name = name, _bytes = bytes, _layout = Layout.Beta };
        var r = new BinReader(bytes, offset);
        var trackCount = r.U32();
        animation.FrameCount = (int)r.U32();
        for (var i = 0; i < trackCount; i++)
        {
            var trackOffset = offset + (int)r.U32();
            animation._tracks[Bin.Ascii(bytes, trackOffset, TrackNameLength).ToLowerInvariant()] = trackOffset;
        }

        animation.RootMotion = new Vector3[animation.FrameCount];
        for (var f = 0; f < animation.FrameCount; f++)
        {
            animation.RootMotion[f] = r.Vec3();
        }

        r.Skip(animation.FrameCount * BoundsSize);
        var pointCount = r.U32();
        for (var i = 0; i < pointCount; i++)
        {
            var points = new Vector3[animation.FrameCount];
            for (var f = 0; f < animation.FrameCount; f++)
            {
                points[f] = r.Vec3();
            }

            animation.ReferencePoints.Add(points);
        }

        return animation;
    }

    /// <summary>Per frame, every part's vertices. Parts without a track keep their model vertices.</summary>
    public Vector3[][][] Bake(Model model)
    {
        var perPart = model.PartList
            .Select(p => _tracks.TryGetValue(p.Name.ToLowerInvariant(), out var offset) ? DecodeTrack(offset, p.Vertices.Length) : null)
            .ToArray();
        var frames = new Vector3[FrameCount][][];
        for (var f = 0; f < FrameCount; f++)
        {
            frames[f] = new Vector3[model.PartList.Count][];
            for (var p = 0; p < model.PartList.Count; p++)
            {
                frames[f][p] = perPart[p]?[f] ?? model.PartList[p].Vertices;
            }
        }

        return frames;
    }

    private List<Vector3[]> DecodeTrack(int offset, int vertexCount)
    {
        var scaleBits = Bin.U32(_bytes, offset + 16);
        if (_layout == Layout.Beta)
        {
            return DecodeBetaTrack(offset, vertexCount, Bin.F32(_bytes, offset + 16));
        }

        return (scaleBits & SignMask) == 0
            ? DecodeMatrixTrack(offset, vertexCount)
            : DecodeDeltaTrack(offset, vertexCount, Bin.F32(_bytes, offset + 16));
    }

    /// <summary>Base vertices, then records <c>s16 frame, s8 delta[n][3]</c> (ending with frame -1)
    /// adding <c>delta * scale</c> to the previous frame.</summary>
    private List<Vector3[]> DecodeDeltaTrack(int offset, int vertexCount, float scale)
    {
        var frames = new List<Vector3[]>();
        var r = new BinReader(_bytes, offset + 20);
        var vertices = new Vector3[vertexCount];
        for (var v = 0; v < vertexCount; v++)
        {
            vertices[v] = r.Vec3();
        }

        frames.Add((Vector3[])vertices.Clone());
        int next = r.S16();
        for (var f = 1; f < FrameCount; f++)
        {
            if (next == f)
            {
                for (var v = 0; v < vertexCount; v++)
                {
                    vertices[v] += new Vector3((sbyte)r.U8(), (sbyte)r.U8(), (sbyte)r.U8()) * scale;
                }

                next = r.S16();
            }

            frames.Add((Vector3[])vertices.Clone());
        }

        return frames;
    }

    /// <summary>The 1996 demo's tracks: base vertices, then <c>s8 delta[n][3]</c> for every next frame.</summary>
    private List<Vector3[]> DecodeBetaTrack(int offset, int vertexCount, float scale)
    {
        var frames = new List<Vector3[]>();
        var r = new BinReader(_bytes, offset + 20);
        var vertices = new Vector3[vertexCount];
        for (var v = 0; v < vertexCount; v++)
        {
            vertices[v] = r.Vec3();
        }

        frames.Add((Vector3[])vertices.Clone());
        for (var f = 1; f < FrameCount; f++)
        {
            for (var v = 0; v < vertexCount; v++)
            {
                vertices[v] += new Vector3((sbyte)r.U8(), (sbyte)r.U8(), (sbyte)r.U8()) * scale;
            }

            frames.Add((Vector3[])vertices.Clone());
        }

        return frames;
    }

    /// <summary>Rigid parts: shifts, base vertices, then a 3x4 <c>s16</c> matrix per frame.</summary>
    private List<Vector3[]> DecodeMatrixTrack(int offset, int vertexCount)
    {
        var frames = new List<Vector3[]>();
        var rotationScale = 1f / (0x8000 >> _bytes[offset + 20]);
        var positionScale = 1f / (0x8000 >> _bytes[offset + 21]);
        var r = new BinReader(_bytes, offset + 22);
        var baseVertices = new Vector3[vertexCount];
        for (var v = 0; v < vertexCount; v++)
        {
            baseVertices[v] = r.Vec3();
        }

        var m = new float[MatrixValues];
        for (var f = 0; f < FrameCount; f++)
        {
            for (var i = 0; i < MatrixValues; i++)
            {
                m[i] = r.S16();
            }

            var vertices = new Vector3[vertexCount];
            for (var v = 0; v < vertexCount; v++)
            {
                var b = baseVertices[v];
                vertices[v] = new Vector3(
                    (m[0] * b.X + m[1] * b.Y + m[2] * b.Z) * rotationScale + m[3] * positionScale,
                    (m[4] * b.X + m[5] * b.Y + m[6] * b.Z) * rotationScale + m[7] * positionScale,
                    (m[8] * b.X + m[9] * b.Y + m[10] * b.Z) * rotationScale + m[11] * positionScale);
            }

            frames.Add(vertices);
        }

        return frames;
    }
}
