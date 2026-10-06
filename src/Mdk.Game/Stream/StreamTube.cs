using System.Numerics;
using Mdk.Game.Kurt;

namespace Mdk.Game.Stream;

/// <summary>A corner of the tube's wall: its place, its colour in the ramp (0-63) and its distance
/// level (0-5, see <see cref="StreamTube.Rgba"/>).</summary>
public readonly record struct TubeVertex(Vector3 Position, int Colour, int Level);

/// <summary>The stream's tube (0x434838; stream_tube.gd): a ring buffer of 32 rings of 16 points,
/// one ring per segment of 10 units, each turned from the previous one by three randomly walking
/// angles. The segment between two rings has 32 wall planes facing in, and a colour per point. See
/// godot-mdk docs/gameplay.md, "The stream".
/// <code>
///   ring:   tail ... head - 1          (head - tail = 31 while generating)
///   slot:   ring &amp; 31
///
///         P[n+1][j] ---- P[n+1][j+1]      two triangles (and planes) per quad,
///            |    \            |          32 per segment
///         P[n][j] ------ P[n][j+1]
/// </code></summary>
public sealed class StreamTube
{
    /// <summary>The normal tube, or the one after LEVEL8 that straightens and ends at a planet.</summary>
    public enum Kind { Normal, Gunter }

    /// <summary>What <see cref="Advance"/> asks the stream to create.</summary>
    public enum SpawnKind { Light, Planet }

    /// <summary>A light or the planet: where along the tube (segments), across it, its size and speed
    /// (segments/s).</summary>
    public sealed record Spawn(SpawnKind Kind, float T, float X, float Z, float Size, float Speed);

    public const int Rings = 32;
    private const int SlotMask = Rings - 1;
    public const int Points = 16;
    private const int PlanesPerSegment = Points * 2;
    public const float SegmentLength = 10f;
    private const float StartRadius = 10f;
    /// <summary>The Gunter tube straightens after this segment and stops after the last one.</summary>
    private const int StraightenSegment = 168;
    public const int LastSegment = 186;
    /// <summary><c>(rand() − 0x4000) × WalkStep</c> is ±1.</summary>
    private const int RandHalf = 0x4000;
    private const double WalkStep = 6.10352e-5;
    /// <summary>Ring points are jittered by ±10 %: <c>(rand() − 0x4000 + Jitter) / Jitter</c>.</summary>
    private const double Jitter = 163840.0;
    private const float TurnDamping = 0.8f;
    /// <summary>Colours: 64 ramp entries; the shade target changes every 10 segments.</summary>
    public const int RampSize = 64;
    private const double ShadeStep = 0.1;
    /// <summary>Vertex alpha (of 256) by distance from the tail (0x5744d8): levels 0-5 every 5 segments.</summary>
    private static readonly int[] Alpha = [0x5A, 0x55, 0x50, 0x3C, 0x28, 0x0F];
    private const int LevelSegments = 5;
    private static int FarLevel => Alpha.Length - 1;
    /// <summary>The ramp (0x491ccc): a start colour, then 8 keys of 8 steps.</summary>
    private static readonly (int R, int G, int B) RampStart = (222, 206, 90);
    private static readonly (int R, int G, int B)[] RampKeys =
        [(140, 123, 33), (123, 49, 8), (198, 165, 132), (132, 123, 140), (214, 198, 231), (198, 165, 132), (123, 49, 8), (222, 206, 90)];
    private const int RampSteps = 8;
    /// <summary>The difficulty's limits (0x433b50): max turn = index + base, radius 10 − index/2 to max.</summary>
    private static readonly float[] TurnBase = [6f, 8f, 10f];
    /// <summary>Lights (0x434f64): in 3 segments of 4, up to ±4 units off the axis, 4-8 in size,
    /// flying back at 3-7 segments/s. The planet (0x4350dc) is 3 segments before the end, size 36.</summary>
    private const int LightChance = 3;
    private const float LightSize = 4f;
    private const float LightSpeed = -3f;
    private const double SizeUnit = 8192.0;
    private const double OffsetUnit = 4096.0;
    private const int PlanetBack = 3;
    private const float PlanetSize = 36f;

    private readonly Kind _kind;
    private readonly WatcomRandom _random;
    private readonly TubeBasis[] _bases = new TubeBasis[Rings];
    private readonly Vector3[] _origins = new Vector3[Rings];
    private readonly Vector3[][] _points = new Vector3[Rings][];
    private readonly int[][] _colours = new int[Rings][];
    private readonly Vector3[][] _normals = new Vector3[Rings][];
    private readonly float[][] _distances = new float[Rings][];
    private readonly (byte R, byte G, byte B)[] _ramp = new (byte, byte, byte)[RampSize];
    private List<Spawn> _spawns = [];
    /// <summary>The turn per segment about x, y (forward: roll) and z, in degrees.</summary>
    private readonly float[] _angles = new float[3];
    private double _radius = StartRadius;
    private int _shadeFrom;
    private int _shadeTo;
    private double _shade;
    private bool _planetSpawned;

    /// <summary>The oldest ring still alive (≈ Kurt's segment), and the next ring to make.</summary>
    public int Tail { get; private set; }
    public int Head { get; private set; }

    public float MaxTurn { get; }
    public float MinRadius { get; }
    public float MaxRadius { get; }

    /// <summary><paramref name="index"/>: the level just played (0-4).</summary>
    public StreamTube(int index, Difficulty difficulty, Kind kind, WatcomRandom random)
    {
        _kind = kind;
        _random = random;

        var half = index >> 1;
        MaxTurn = index + TurnBase[(int)difficulty];
        MinRadius = StartRadius - half;
        MaxRadius = difficulty switch
        {
            Difficulty.Easy => 17f - half,
            Difficulty.Normal => 17f - index,
            _ => 13f - half,
        };

        BuildRamp();
        for (var i = 0; i < Rings; i++)
        {
            _bases[i] = TubeBasis.Identity;
            _points[i] = [];
            _colours[i] = [];
            _normals[i] = [];
            _distances[i] = [];
        }

        // 31 rings ahead of Kurt.
        _shadeFrom = _random.Next() & (RampSize - 1);
        _shadeTo = _random.Next() & (RampSize - 1);
        for (var i = 0; i < Rings - 1; i++)
        {
            Generate();
        }
    }

    /// <summary>The ramp: each key blended from the previous one in 8 steps.</summary>
    private void BuildRamp()
    {
        var previous = RampStart;
        var k = 0;
        foreach (var key in RampKeys)
        {
            for (var i = 0; i < RampSteps; i++)
            {
                int Blend(int to, int from) => (to * i + from * (RampSteps - i)) / RampSteps;
                _ramp[k++] = ((byte)Blend(key.R, previous.R), (byte)Blend(key.G, previous.G), (byte)Blend(key.B, previous.B));
            }

            previous = key;
        }
    }

    /// <summary>The 64 colours of the walls.</summary>
    public IReadOnlyList<(byte R, byte G, byte B)> Ramp => _ramp;

    /// <summary>Kurt passed a segment: the oldest ring goes, a new one comes.</summary>
    public void Advance()
    {
        Tail++;
        Generate();
    }

    /// <summary>The lights and the planet made since the last call.</summary>
    public List<Spawn> TakeSpawns()
    {
        var spawns = _spawns;
        _spawns = [];
        return spawns;
    }

    /// <summary>Whether <paramref name="t"/> (in segments) is within the rings still alive.</summary>
    public bool Contains(float t)
    {
        var s = (int)MathF.Floor(t);
        return s >= Tail && s < Head;
    }

    /// <summary>The axis at <paramref name="t"/> (0x436668).</summary>
    public Vector3 Centre(float t)
    {
        var s = (int)MathF.Floor(t);
        return Vector3.Lerp(_origins[s & SlotMask], _origins[(s + 1) & SlotMask], t - s);
    }

    /// <summary>The frame at <paramref name="t"/> (0x4366f4): forward along the axis, right from the camera's up.</summary>
    public TubeBasis Frame(float t, Vector3 up)
    {
        var y = Vector3.Normalize(Centre(t + 1f) - Centre(t));
        var x = Vector3.Normalize(Vector3.Cross(y, up));
        return new TubeBasis(x, y, Vector3.Cross(x, y));
    }

    /// <summary>A point of segment <paramref name="t"/> at (<paramref name="x"/>, <paramref name="z"/>) across it.</summary>
    public Vector3 Place(float t, float x, float z)
    {
        var s = (int)MathF.Floor(t);
        var slot = s & SlotMask;
        return _origins[slot] + _bases[slot].Apply(new Vector3(x, SegmentLength * (t - s), z));
    }

    /// <summary>The walls (0x43637c): for the first plane of segment <paramref name="t"/> that
    /// <paramref name="position"/> is within <paramref name="margin"/> of, the fraction of the way to
    /// the axis that puts it back; −1 when it touches none.</summary>
    public float WallHit(Vector3 position, float t, float margin)
    {
        var slot = (int)MathF.Floor(t) & SlotMask;
        var normals = _normals[slot];
        var distances = _distances[slot];
        for (var i = 0; i < normals.Length; i++)
        {
            var d = Vector3.Dot(normals[i], position) + distances[i] - margin;
            if (d > 0f)
            {
                continue;
            }

            var towards = Vector3.Dot(normals[i], position - Centre(t));
            return towards != 0f ? d / towards : 0f;
        }

        return -1f;
    }

    /// <summary>The segments drawn, far to near (0x436b00).</summary>
    public IEnumerable<int> DrawnSegments()
    {
        for (var n = Head - 2; n >= Tail; n--)
        {
            yield return n;
        }
    }

    /// <summary>The triangles of segment <paramref name="n"/> facing <paramref name="eye"/>.</summary>
    public void AddTriangles(int n, Vector3 eye, List<TubeVertex> into)
    {
        var slot = n & SlotMask;
        var older = _points[slot];
        var newer = _points[(n + 1) & SlotMask];
        var olderLevel = LevelOf(n);
        var newerLevel = LevelOf(n + 1);

        // The newest ring has no colours yet: the first ramp colour at the farthest level.
        var newest = n + 2 >= Head;
        int OlderColour(int j) => ColourOf(n, j);
        int NewerColour(int j) => newest ? 0 : ColourOf(n + 1, j);
        if (newest)
        {
            newerLevel = FarLevel;
        }

        var normals = _normals[slot];
        var distances = _distances[slot];
        for (var j = 0; j < Points; j++)
        {
            var j1 = (j + 1) % Points;
            for (var i = 0; i < 2; i++)
            {
                var plane = j * 2 + i;
                if (Vector3.Dot(normals[plane], eye) + distances[plane] < 0f)
                {
                    continue;
                }

                into.Add(new TubeVertex(older[j], OlderColour(j), olderLevel));
                if (i == 0)
                {
                    into.Add(new TubeVertex(newer[j1], NewerColour(j1), newerLevel));
                    into.Add(new TubeVertex(older[j1], OlderColour(j1), olderLevel));
                    continue;
                }

                into.Add(new TubeVertex(newer[j], NewerColour(j), newerLevel));
                into.Add(new TubeVertex(newer[j1], NewerColour(j1), newerLevel));
            }
        }
    }

    /// <summary>A vertex's colour (0x436b00): its ramp colour and its level's alpha (of 256), the
    /// RGBA table entry <c>64 level + colour</c>, Gouraud-blended across the triangle.</summary>
    public (byte R, byte G, byte B, byte A) Rgba(TubeVertex v)
    {
        var (r, g, b) = _ramp[v.Colour];
        return (r, g, b, (byte)Alpha[v.Level]);
    }

    /// <summary>Ring <paramref name="n"/>'s distance level: 0 at the tail, one more every 5 segments.</summary>
    private int LevelOf(int n) => n > Tail ? Math.Min((n - Tail - 1) / LevelSegments, FarLevel) : 0;

    private int ColourOf(int n, int j)
    {
        var colours = _colours[n & SlotMask];
        return j < colours.Length ? colours[j] : 0;
    }

    /// <summary>Ring <paramref name="n"/>'s centre, a point, a colour and a wall plane of its segment
    /// (tests and the reference dump).</summary>
    public Vector3 Origin(int n) => _origins[n & SlotMask];

    public Vector3 Point(int n, int j) => _points[n & SlotMask][j];

    public int Colour(int n, int j) => ColourOf(n, j);

    public (Vector3 Normal, float Distance) Plane(int n, int i) => (_normals[n & SlotMask][i], _distances[n & SlotMask][i]);

    public Vector3 Angles => new(_angles[0], _angles[1], _angles[2]);

    public float Radius => (float)_radius;

    /// <summary>Makes ring <see cref="Head"/> (0x434838): its frame follows the previous one turned
    /// by the three angles, its points are jittered, and the segment before it gets its wall planes
    /// and colours.</summary>
    private void Generate()
    {
        var n = Head;
        if (_kind == Kind.Gunter && n > LastSegment)
        {
            SpawnPlanet(n);
            return;
        }

        var slot = n & SlotMask;
        if (n == Tail)
        {
            _bases[slot] = TubeBasis.Identity;
            _origins[slot] = Vector3.Zero;
        }
        else
        {
            TurnRing(n);
            SpawnLight(n);
        }

        MakePoints(slot);
        if (n != Tail)
        {
            BuildSegment(n - 1);
        }

        Head++;
        WalkAngles();
    }

    /// <summary>The angles walk randomly by ±1° per segment (the Gunter tube straightens at the end),
    /// in the order z, x, y; then the radius by ±1.</summary>
    private void WalkAngles()
    {
        foreach (var axis in (ReadOnlySpan<int>)[2, 0, 1])
        {
            var angle = (double)_angles[axis];
            if (_kind == Kind.Gunter && Head > StraightenSegment)
            {
                angle = MoveToward(angle, 0.0, 1.0);
            }
            else
            {
                angle += (_random.Next() - RandHalf) * WalkStep;
            }

            if (Math.Abs(angle) > MaxTurn)
            {
                angle *= TurnDamping;
            }

            _angles[axis] = (float)angle;
        }

        _radius = Math.Clamp(_radius + (_random.Next() - RandHalf) * WalkStep, MinRadius, MaxRadius);
    }

    private static double MoveToward(double from, double to, double step) =>
        Math.Abs(to - from) <= step ? to : from + Math.Sign(to - from) * step;

    /// <summary>Ring <paramref name="n"/>'s frame: one segment along the previous ring's forward
    /// axis, turned by the angles, its columns normalised.</summary>
    private void TurnRing(int n)
    {
        var slot = n & SlotMask;
        var previous = (n - 1) & SlotMask;
        var turn = TubeBasis.Rotation(Vector3.UnitX, _angles[0]) * TubeBasis.Rotation(Vector3.UnitY, _angles[1])
            * TubeBasis.Rotation(Vector3.UnitZ, _angles[2]);
        var basis = _bases[previous] * turn;
        _origins[slot] = _origins[previous] + SegmentLength * _bases[previous].Y;
        _bases[slot] = new TubeBasis(Vector3.Normalize(basis.X), Vector3.Normalize(basis.Y), Vector3.Normalize(basis.Z));
    }

    /// <summary>The ring's 16 points around its axis (point 0 on top), each coordinate jittered by ±10 %.</summary>
    private void MakePoints(int slot)
    {
        var points = new Vector3[Points];
        for (var j = 0; j < Points; j++)
        {
            var angle = Math.Tau * j / Points;
            var jx = (_random.Next() - RandHalf + Jitter) / Jitter;
            var jz = (_random.Next() - RandHalf + Jitter) / Jitter;
            var local = new Vector3((float)(_radius * Math.Sin(angle) * jx), 0f, (float)(_radius * Math.Cos(angle) * jz));
            points[j] = _origins[slot] + _bases[slot].Apply(local);
        }

        _points[slot] = points;
    }

    /// <summary>A light in the segment before ring <paramref name="n"/>, in 3 segments of 4.</summary>
    private void SpawnLight(int n)
    {
        if ((_random.Next() & LightChance) == 0)
        {
            return;
        }

        var size = LightSize + _random.Next() / SizeUnit;
        var x = (_random.Next() - RandHalf) / OffsetUnit;
        var z = (_random.Next() - RandHalf) / OffsetUnit;
        var speed = LightSpeed - _random.Next() / SizeUnit;
        _spawns.Add(new Spawn(SpawnKind.Light, n - 1f, (float)x, (float)z, (float)size, (float)speed));
    }

    /// <summary>The planet at the end of the Gunter tube, once.</summary>
    private void SpawnPlanet(int n)
    {
        if (_planetSpawned)
        {
            return;
        }

        _planetSpawned = true;
        _spawns.Add(new Spawn(SpawnKind.Planet, n - PlanetBack, 0f, 0f, PlanetSize, 0f));
    }

    /// <summary>The wall planes (two per quad, facing in) and the colours of the segment from ring
    /// <paramref name="p"/> to <paramref name="p"/> + 1.</summary>
    private void BuildSegment(int p)
    {
        var slot = p & SlotMask;
        var older = _points[slot];
        var newer = _points[(p + 1) & SlotMask];
        var shade = (int)((1.0 - _shade) * _shadeFrom + _shade * _shadeTo);
        var colours = new int[Points];
        var normals = new Vector3[PlanesPerSegment];
        var distances = new float[PlanesPerSegment];
        for (var j = 0; j < Points; j++)
        {
            // A triangle wave around the ring, one step further each segment: spiral stripes.
            var x = (p + 1 + j) & SlotMask;
            var wave = x < Points ? x : SlotMask - x;
            colours[j] = (wave + shade) & (RampSize - 1);

            var j1 = (j + 1) % Points;
            SetPlane(normals, distances, j * 2, older[j], newer[j1], older[j1]);
            SetPlane(normals, distances, j * 2 + 1, older[j], newer[j], newer[j1]);
        }

        _colours[slot] = colours;
        _normals[slot] = normals;
        _distances[slot] = distances;

        // The shade drifts to a new random target every 10 segments.
        _shade += ShadeStep;
        if (_shade > 1.0)
        {
            _shade = 0.0;
            _shadeFrom = _shadeTo;
            _shadeTo = _random.Next() & (RampSize - 1);
        }
    }

    private static void SetPlane(Vector3[] normals, float[] distances, int i, Vector3 a, Vector3 b, Vector3 c)
    {
        var normal = Vector3.Normalize(Vector3.Cross(b - a, c - b));
        normals[i] = normal;
        distances[i] = -Vector3.Dot(normal, a);
    }
}
