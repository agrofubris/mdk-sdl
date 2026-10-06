using System.Numerics;
using Mdk.Formats.Scripts;

namespace Mdk.Formats;

/// <summary>The 1996 demo's <c>LEVELn.CMI</c> (<see cref="BetaDemo"/>): <c>u32 size</c>, the four
/// directories of the retail file (offsets from 4, names in lower case), the scripts (an earlier
/// bytecode, <see cref="ScriptDialect.Beta1996"/>) and their paths, the models (the retail format)
/// and the animations, which the scripts point at. An arena's record is two strings: its music and
/// its ambient loop; arenas have no scripts yet.</summary>
public sealed partial class Cmi
{
    private const int BetaDirectoryOffset = 4;
    /// <summary>A path of the demo (0x32ff8): <c>u32 count</c>, the first position, then
    /// <c>count - 1</c> steps of 3 <c>f32</c>, one per frame.</summary>
    private const int PathStepSize = 12;
    /// <summary>A retail spline key: <c>s32 frame</c>, the position, the incoming and outgoing tangents.</summary>
    private const int PathKeySize = 40;
    private const int PathCountSize = 4;

    private readonly Dictionary<int, ModelAnimation> _betaAnimations = [];

    /// <summary>Arena to its ambient loop, played under the music (the demo's own).</summary>
    public Dictionary<string, string> ArenaAmbience { get; } = [];
    /// <summary>The demo's paths turned into retail spline records at the end of <see cref="Bytes"/>:
    /// the old offset to the new.</summary>
    public Dictionary<int, int> BetaPaths { get; } = [];

    /// <summary>A file of the 1996 demo.</summary>
    public static Cmi ParseBeta(byte[] bytes) => new(bytes, ScriptDialect.Beta1996);

    /// <summary>An animation of the demo's file, which scripts point at directly.</summary>
    public ModelAnimation GetBetaAnimation(int offset)
    {
        if (!_betaAnimations.TryGetValue(offset, out var animation))
        {
            animation = _betaAnimations[offset] = ModelAnimation.ParseBeta($"@{offset:x}", Bytes, offset);
        }

        return animation;
    }

    private void ParseBeta()
    {
        var r = new BinReader(Bytes, BetaDirectoryOffset);
        foreach (var directory in new[] { AlienScripts, ModelOffsets, ObjectScripts, ArenaScripts })
        {
            var count = r.U32();
            for (var i = 0; i < count; i++)
            {
                var name = r.Name(r.U8()).ToUpperInvariant();
                var offset = Absolute(r.U32());
                directory[name] = offset;
                if (offset != 0 && (directory == AlienScripts || directory == ObjectScripts))
                {
                    _entries.Add(offset);
                }
            }
        }

        foreach (var arena in ArenaScripts.Keys.ToList())
        {
            var record = new BinReader(Bytes, ArenaScripts[arena]);
            ArenaMusic[arena] = record.Name(record.U8()).ToUpperInvariant();
            ArenaAmbience[arena] = record.Name(record.U8()).ToUpperInvariant();
            ArenaScripts[arena] = 0;
        }

        ConvertPaths();
    }

    /// <summary>Turns the paths the scripts follow (found by walking them) into retail spline
    /// records appended to the file.</summary>
    private void ConvertPaths()
    {
        var paths = ScriptDecoder.BetaPaths(this);
        var bytes = new List<byte>(Bytes);
        foreach (var path in paths)
        {
            BetaPaths[path] = bytes.Count;
            bytes.AddRange(SplineOf(Bytes, path));
        }

        Bytes = [.. bytes];
    }

    /// <summary>A demo path as a spline with a key per frame whose tangents are the steps to its
    /// neighbours (straight lines between them); it starts again from its first position after the
    /// last frame.</summary>
    private static byte[] SplineOf(byte[] source, int path)
    {
        var count = (int)Bin.U32(source, path);
        var positions = new List<Vector3>();
        var position = Vector3.Zero;
        for (var i = 0; i < count; i++)
        {
            var p = path + PathCountSize + i * PathStepSize;
            var step = new Vector3(Bin.F32(source, p), Bin.F32(source, p + 4), Bin.F32(source, p + 8));
            position = i == 0 ? step : position + step;
            positions.Add(position);
        }

        if (positions.Count == 0)
        {
            positions.Add(Vector3.Zero);
        }

        positions.Add(positions[0]);
        var record = new byte[PathCountSize + positions.Count * PathKeySize];
        var w = new BinaryWriter(new MemoryStream(record));
        w.Write(positions.Count);
        for (var i = 0; i < positions.Count; i++)
        {
            var incoming = positions[i] - positions[Math.Max(i - 1, 0)];
            var outgoing = positions[Math.Min(i + 1, positions.Count - 1)] - positions[i];
            w.Write(i);
            foreach (var v in new[] { positions[i], incoming, outgoing })
            {
                w.Write(v.X);
                w.Write(v.Y);
                w.Write(v.Z);
            }
        }

        return record;
    }
}
