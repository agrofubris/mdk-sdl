using System.Numerics;
using Mdk.Formats;
using Mdk.Game.Objects;

namespace Mdk.Game.Scripts;

/// <summary>Flying pieces (a port of godot-mdk's <c>debris.gd</c>): shattered triangle groups
/// (shatter_group, 0x40c828), sparks (0x4052d4: small tetrahedra in palette colours) and the parts of
/// an object's break-up model (0x405900). They fly, spin, fall, bounce off the arena, ride the fans'
/// updrafts and vanish after their lifetime (0x4061d8); some leave a smoke trail (0x406004).
/// <code>
///   shatter ─┐                       ┌─► hit: bounce (v -= 1.4 (v·n) n), life -20
///   spark   ─┼─► piece ─► each tick ─┤
///   break up ┘                       └─► free: fall, updraft, trail puff
/// </code></summary>
public sealed class Debris
{
    /// <summary>Gravity in units per tick² (about 64 units/s²).</summary>
    public const float Gravity = 0.284444f * 0.25f;
    /// <summary>Speed kept along the surface normal when bouncing (restitution 0.4).</summary>
    private const float Bounce = 1.4f;
    /// <summary>Life lost at each bounce, in ticks.</summary>
    private const int BounceTicks = 20;
    /// <summary>Most pieces alive at once (the original shares a pool of effects).</summary>
    public const int MaxPieces = 600;
    /// <summary>Sparks and pieces live 60-123 ticks; one in 4 leaves a smoke trail every 1-2 ticks.</summary>
    public const int LifeMin = 60;
    private const int TrailChance = 4;
    private const int LifeShift = 9;
    /// <summary>rand() is 0-32767; centred values are rand() - 0x4000.</summary>
    private const int RandRange = 32768;
    private const int RandHalf = 0x4000;
    private const float Rand14 = 1f / 16384f;
    private const float Rand15 = 1f / 32768f;
    /// <summary>Upward bias of a spark's launch (rand() - 0x800).</summary>
    private const int LaunchUp = 0x800;
    /// <summary>Spin: about ±14° per tick (0x46de70); a shatter's spin scales by 0.000854492.</summary>
    private const int SpinBias = 0x41c2;
    private const float SpinDegrees = 28f / RandRange;
    private const float ShatterSpinDegrees = 0.000854492f;
    /// <summary>A spark's corners are jittered by ±0.33 before scaling.</summary>
    private const float CornerJitter = 2e-5f;
    /// <summary>Pieces start 2 units above the exploding object.</summary>
    private const float BreakUpRise = 2f;
    private const float TicksPerSecond = 30f;
    /// <summary>A hit this close to the start is the surface the piece rests on: a segment starting
    /// on a plane crosses nothing (0x421470), so the piece goes on through it.</summary>
    private const float OnSurface = 1e-3f;

    /// <summary>A spark's tetrahedron (0x404b00) and its 4 faces.</summary>
    private static readonly Vector3[] SparkCorners = [new(0f, 0f, 0.5f), new(0.5f, 0f, -0.5f), new(-0.5f, 0.5f, -0.5f), new(-0.5f, -0.5f, -0.5f)];
    private static readonly int[] SparkFaces = [0, 2, 1, 0, 3, 2, 0, 1, 3, 1, 2, 3];

    /// <summary>How a spark starts: flying off at random, or still at its point (the fans' sparks).</summary>
    public enum Launch { Random, Still }

    /// <summary>A triangle of a shattered group: corners, UVs (texels) and material value.</summary>
    public readonly record struct Facet(Vector3 A, Vector3 B, Vector3 C, Vector2 UvA, Vector2 UvB, Vector2 UvC, int Material);

    /// <summary>A triangle group's facets and the material names their values index.</summary>
    public sealed record Group(IReadOnlyList<string> Names, IReadOnlyList<Facet> Facets);

    /// <summary>A flying piece: triangles around its centre (3 corners each), textured by material
    /// values indexing <see cref="Names"/>, or a spark (no names) in palette colours
    /// <see cref="ColourBase"/> + <see cref="ColourRange"/> × facing.</summary>
    public sealed class Piece
    {
        public string Arena = "";
        public IReadOnlyList<string>? Names;
        public List<int> Materials = [];
        public List<Vector3> Corners = [];
        public List<Vector2> Uvs = [];
        public int ColourBase;
        public int ColourRange;
        /// <summary>A smoke puff every this many ticks (0: none).</summary>
        public int TrailEvery;
        public int TrailTicks;
        public Vector3 Center;
        /// <summary>Units per tick.</summary>
        public Vector3 Velocity;
        public Quaternion Orientation = Quaternion.Identity;
        /// <summary>Turn per tick.</summary>
        public Quaternion Spin = Quaternion.Identity;
        public int Ticks;

        public bool IsSpark => Names == null;

        /// <summary>Back to a new piece's state (taken again from the pool).</summary>
        public void Reset()
        {
            Arena = "";
            Names = null;
            Materials.Clear();
            Corners.Clear();
            Uvs.Clear();
            (ColourBase, ColourRange, TrailEvery, TrailTicks, Ticks) = (0, 0, 0, 0, 0);
            (Center, Velocity) = (Vector3.Zero, Vector3.Zero);
            (Orientation, Spin) = (Quaternion.Identity, Quaternion.Identity);
        }
    }

    private readonly Random rng;

    /// <summary>All pieces are made with the level (<see cref="MaxPieces"/>), then reused.</summary>
    public Debris(Random rng)
    {
        this.rng = rng;
        for (var i = 0; i < MaxPieces; i++)
        {
            _free.Push(new Piece());
        }
    }

    private readonly Stack<Piece> _free = new(MaxPieces);
    /// <summary>A spark's corners before they go into its piece.</summary>
    private readonly Vector3[] _sparkCorners = new Vector3[SparkCorners.Length];

    private Piece Take()
    {
        if (!_free.TryPop(out var piece))
        {
            return new Piece();
        }

        piece.Reset();
        return piece;
    }

    /// <summary>The arena's nearest hit along a segment: (arena, from, to).</summary>
    public Func<string, Vector3, Vector3, ScriptRuntime.RayHit?>? Ray;
    /// <summary>Leaves a smoke puff (see <see cref="Effects.SpawnTrail"/>).</summary>
    public Action<string, Vector3>? Trail;
    /// <summary>The fans' push: (arena, point, vertical speed u/s, seconds) → the new speed, or NaN.</summary>
    public Func<string, Vector3, float, float, float>? Updraft;
    /// <summary>The triangles of an arena's group, or null.</summary>
    public Func<string, int, Group?>? GroupFacets;

    private readonly List<Piece> _pieces = [];

    public IReadOnlyList<Piece> Pieces => _pieces;

    /// <summary>Shatters a triangle group: <paramref name="life"/> in seconds, pieces no larger than
    /// size / 4, <paramref name="speed"/> (units per tick) at <paramref name="point"/>, falling off to 0
    /// at the group's farthest corner; the pieces fly along <paramref name="direction"/>, or away from
    /// the point when it's zero.</summary>
    public void Shatter(string arena, int group, float life, float size, float speed, Vector3 point, Vector3 direction)
    {
        if (GroupFacets?.Invoke(arena, group) is not { Facets.Count: > 0 } shape)
        {
            return;
        }

        var facets = shape.Facets;

        // The squared distance to the farthest corner of the group's box, axis by axis.
        var min = facets[0].A;
        var max = min;
        foreach (var f in facets)
        {
            min = Vector3.Min(min, Vector3.Min(f.A, Vector3.Min(f.B, f.C)));
            max = Vector3.Max(max, Vector3.Max(f.A, Vector3.Max(f.B, f.C)));
        }

        var shot = new Shot(arena, shape.Names, size * size * 0.25f, life, speed, point,
            direction == Vector3.Zero ? Vector3.Zero : Vector3.Normalize(direction),
            Vector3.Max(Vector3.Abs(min - point), Vector3.Abs(max - point)).LengthSquared());
        foreach (var f in facets)
        {
            Split([f.A, f.B, f.C], [f.UvA, f.UvB, f.UvC], f.Material, shot);
        }
    }

    /// <summary>A shatter's settings; a zero <see cref="Direction"/> means radial.</summary>
    private readonly record struct Shot(string Arena, IReadOnlyList<string> Names, float MaxCross, float Life, float Speed, Vector3 Point, Vector3 Direction, float Far);

    /// <summary>Splits a triangle at the middle of its longest side until it's small enough (0x40cbe0),
    /// then makes it a piece.</summary>
    private void Split(Vector3[] c, Vector2[] uv, int material, Shot shot)
    {
        if (_pieces.Count >= MaxPieces)
        {
            return;
        }

        if (Vector3.Cross(c[1] - c[0], c[1] - c[2]).LengthSquared() > shot.MaxCross)
        {
            var a = Longest(c);
            var b = (a + 1) % 3;
            var o = (a + 2) % 3;
            var middle = (c[a] + c[b]) * 0.5f;
            var middleUv = (uv[a] + uv[b]) * 0.5f;
            Split([c[a], middle, c[o]], [uv[a], middleUv, uv[o]], material, shot);
            Split([middle, c[b], c[o]], [middleUv, uv[b], uv[o]], material, shot);
            return;
        }

        var piece = Take();
        (piece.Arena, piece.Names, piece.Center) = (shot.Arena, shot.Names, (c[0] + c[1] + c[2]) / 3f);
        piece.Materials.Add(material);
        foreach (var corner in c)
        {
            piece.Corners.Add(corner - piece.Center);
        }

        piece.Uvs.AddRange(uv);

        // Slower farther from the point.
        var f = shot.Far > 0f ? 1f - Vector3.DistanceSquared(piece.Center, shot.Point) / shot.Far : 1f;
        if (shot.Direction != Vector3.Zero)
        {
            piece.Velocity = shot.Direction * shot.Speed * f;
        }
        else if (piece.Center == shot.Point)
        {
            piece.Velocity = new Vector3(0f, 0f, shot.Speed * f);
        }
        else
        {
            piece.Velocity = Vector3.Normalize(piece.Center - shot.Point) * shot.Speed * f;
        }

        piece.Spin = RandomSpin(ShatterSpinDegrees * f);
        piece.Ticks = (int)MathF.Round(shot.Life * TicksPerSecond);
        _pieces.Add(piece);
    }

    /// <summary>The corner starting the longest side.</summary>
    private static int Longest(Vector3[] c)
    {
        var longest = 0;
        for (var k = 1; k < 3; k++)
        {
            if (Vector3.DistanceSquared(c[(k + 1) % 3], c[k]) > Vector3.DistanceSquared(c[(longest + 1) % 3], c[longest]))
            {
                longest = k;
            }
        }

        return longest;
    }

    /// <summary>Sparks (0x41e8f4, 0x4052d4) at <paramref name="point"/>: <paramref name="size"/> 0.5 or
    /// 1.0, palette colours <paramref name="colour"/> to colour + <paramref name="range"/> (e.g. 3, 3:
    /// green), velocity scaled by <paramref name="speed"/>.</summary>
    public void Spark(string arena, Vector3 point, int count, float size, int colour, int range, float speed = 1f,
        Launch launch = Launch.Random)
    {
        for (var i = 0; i < count && _pieces.Count < MaxPieces; i++)
        {
            var piece = Take();
            (piece.ColourBase, piece.ColourRange) = (colour, range);
            var s = (1f + RandomHalf() * Rand15) * size;
            var corners = _sparkCorners;
            for (var c = 0; c < SparkCorners.Length; c++)
            {
                corners[c] = (SparkCorners[c] + new Vector3(RandomHalf(), RandomHalf(), RandomHalf()) * CornerJitter) * s;
            }

            foreach (var k in SparkFaces)
            {
                piece.Corners.Add(corners[k]);
            }

            piece.Center = point + new Vector3(RandomHalf() * Rand14, RandomHalf() * Rand14, RandomHalf() * Rand15);
            Start(piece, arena, speed);

            // The fans' sparks stand exactly at their point (0x414230 sets it after 0x4052d4).
            if (launch == Launch.Still)
            {
                piece.Velocity = Vector3.Zero;
                piece.Center = point;
            }

            // Only the bigger sparks can trail.
            if (s < 1f)
            {
                piece.TrailEvery = 0;
            }

            _pieces.Add(piece);
        }
    }

    /// <summary>An object's break-up (0x405900): one piece per part of its break-up model, turned like
    /// the object (yaw and bank) 2 units higher, thrown with its velocity plus a spark's; parts hidden
    /// on the object stay behind.</summary>
    public void BreakUp(MdkObject obj, Model model)
    {
        var turn = Matrix4x4.CreateRotationX(float.DegreesToRadians(obj.Roll)) * Matrix4x4.CreateRotationZ(float.DegreesToRadians(obj.Yaw));
        var velocity = obj.Position - obj.PreviousPosition;
        foreach (var part in model.PartList)
        {
            if (_pieces.Count >= MaxPieces)
            {
                return;
            }

            var index = obj.FindPart(part.Name);
            if (index >= 0 && (obj.HiddenParts & (1 << index)) != 0)
            {
                continue;
            }

            var piece = Take();
            piece.Names = model.Materials;
            piece.Materials.AddRange(part.TriangleMaterials);
            foreach (var i in part.TriangleIndices)
            {
                piece.Corners.Add(Vector3.Transform(part.Vertices[i], turn));
            }

            piece.Uvs.AddRange(part.TriangleUvs);
            piece.Center = obj.Position + new Vector3(0f, 0f, BreakUpRise);
            Start(piece, obj.Arena, 1f);
            piece.Velocity += velocity;
            _pieces.Add(piece);
        }
    }

    /// <summary>A spark's start (0x4052d4): thrown mostly upwards (±1, ±1, -0.125...1.875 units per
    /// tick), a random spin, 60-123 ticks of life, one in 4 trailing smoke.</summary>
    private void Start(Piece piece, string arena, float speed)
    {
        piece.Arena = arena;
        piece.Velocity = new Vector3(RandomHalf() * Rand14, RandomHalf() * Rand14, (rng.Next(RandRange) - LaunchUp) * Rand14) * speed;
        piece.Spin = RandomSpin(SpinDegrees);
        piece.Ticks = (rng.Next(RandRange) >> LifeShift) + LifeMin;
        if (rng.Next(TrailChance) == 0)
        {
            piece.TrailEvery = rng.Next(2) + 1;
        }
    }

    /// <summary>A turn of (rand() - 0x41c2) × <paramref name="degrees"/> about each axis.</summary>
    private Quaternion RandomSpin(float degrees)
    {
        float Angle() => float.DegreesToRadians((rng.Next(RandRange) - SpinBias) * degrees);
        return Quaternion.CreateFromYawPitchRoll(Angle(), Angle(), Angle());
    }

    private int RandomHalf() => rng.Next(RandRange) - RandHalf;

    /// <summary>Whether an arena has any piece or spark.</summary>
    public bool HasPieces(string arena)
    {
        foreach (var piece in _pieces)
        {
            if (piece.Arena == arena)
            {
                return true;
            }
        }

        return false;
    }

    public int PieceCount() => _pieces.Count;

    /// <summary>Moves the pieces by <paramref name="ticks"/> (0x4061d8).</summary>
    public void Update(float ticks)
    {
        for (var i = _pieces.Count - 1; i >= 0; i--)
        {
            var piece = _pieces[i];
            piece.Orientation = Quaternion.Normalize(piece.Orientation * piece.Spin);
            Move(piece, ticks);
            piece.Ticks -= (int)MathF.Round(ticks);
            if (piece.Ticks <= 0)
            {
                _pieces.RemoveAt(i);
                _free.Push(piece);
                continue;
            }

            // The smoke trail (0x406070).
            if (piece.TrailEvery == 0 || Trail == null)
            {
                continue;
            }

            piece.TrailTicks += (int)MathF.Round(ticks);
            if (piece.TrailTicks >= piece.TrailEvery)
            {
                piece.TrailTicks = 0;
                Trail(piece.Arena, piece.Center);
            }
        }
    }

    /// <summary>Flies freely (falling) or bounces off what it hits, stopping at the contact; the fans
    /// lift it either way (0x4061d8).</summary>
    private void Move(Piece piece, float ticks)
    {
        var motion = piece.Velocity * ticks;
        var hit = motion != Vector3.Zero ? Ray?.Invoke(piece.Arena, piece.Center, piece.Center + motion) : null;
        if (hit is { } h && Vector3.DistanceSquared(h.Point, piece.Center) > OnSurface * OnSurface)
        {
            piece.Center = h.Point;
            piece.Velocity -= h.Normal * Vector3.Dot(piece.Velocity, h.Normal) * Bounce;
            piece.Ticks -= BounceTicks;
        }
        else
        {
            piece.Center += motion;
            piece.Velocity.Z -= Gravity * ticks;
        }

        // Fans push sparks and pieces (mask 8); their speeds are in units per second.
        var vz = Updraft?.Invoke(piece.Arena, piece.Center, piece.Velocity.Z * TicksPerSecond, ticks / TicksPerSecond) ?? float.NaN;
        if (!float.IsNaN(vz))
        {
            piece.Velocity.Z = vz / TicksPerSecond;
        }
    }
}
