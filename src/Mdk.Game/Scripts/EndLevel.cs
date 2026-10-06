using System.Globalization;
using System.Numerics;
using Mdk.Formats;

namespace Mdk.Game.Scripts;

/// <summary>The end of a level (endlev.c: 0x40a9e0, 0x40ad9c each frame, pieces 0x40b280, Kurt
/// 0x40b558; end_level.gd). The visible triangles of Kurt's arena are taken from the highest down:
/// each tick 0-7 more are torn off and fly up (vz += 0.025 per tick) spinning around Kurt (the spin
/// grows by 0.15° per tick up to 2.5°), until they're 500 above him. Kurt takes off (K_TAKEOF, then
/// K_FLOATC), turns ever faster (0.15° per tick², up to 2.5° per tick) and rises with them (0.025
/// per tick²); once he rises faster than 3 units per tick his rise speeds up three times as fast
/// and the view tilts up (22.5° per second, to (-60 - the arena's pitch) / 2), then the screen goes
/// white (8 per tick); past 300 the level is over.
/// <code>
///   arena triangles, highest first ──0-7 a tick──► pieces: rise, spin around Kurt ──500 above──► gone
///   Kurt: rise ──vz &gt; 3──► tilt up ──► white flash ──&gt; 300──► Finished
/// </code></summary>
public sealed class EndLevel(int takeoffFrames)
{
    /// <summary>A torn-off triangle: where its centre is and how far it turned (degrees).</summary>
    public sealed class Piece(int triangle, Vector3 centre)
    {
        public int Triangle { get; } = triangle;
        public Vector3 Centre { get; set; } = centre;
        public float Angle { get; set; }
        public float RiseSpeed { get; set; }
        public float Spin { get; set; }
    }

    public const string Takeoff = "K_TAKEOF";
    private const string Floating = "K_FLOATC";
    private const float Rise = 0.025f;
    private const float SpinGrowth = 0.15f;
    private const float SpinMax = 2.5f;
    private const float Limit = 500f;
    private const int PiecesPerTick = 8;
    private const float FastRise = 3f;
    private const float FastRiseFactor = 2f;
    private const float TurnGrowth = 0.15f;
    private const float TurnMax = 2.5f;
    private const float TiltSpeed = 22.5f;
    private const float TiltTarget = -60f;
    private const float TiltShare = 0.5f;
    private const float DefaultPitch = 4f;
    private const float FlashStep = 8f;
    private const float FlashEnd = 300f;
    private const float FlashMax = 255f;
    private const float StartShake = 5f;
    /// <summary>Triangle flag of hidden triangles (group_set_state).</summary>
    private const uint Hidden = 0x10;

    private readonly Queue<int> _queue = [];
    private readonly List<int> _torn = [];
    private readonly List<Piece> _pieces = [];
    private ScriptRuntime? _runtime;
    private Arena? _arena;
    private float _riseSpeed;
    private float _turn;
    private float _flash;
    private float _takeoff;
    private bool _done;

    public event Action? Finished;

    /// <summary>Added to the camera pitch (degrees, negative looks up; 0x573b1c).</summary>
    public float PitchOffset { get; private set; }

    /// <summary>The arena torn apart.</summary>
    public string Arena => _arena?.Name ?? "";

    /// <summary>The triangles torn off so far, in order (the view hides them).</summary>
    public IReadOnlyList<int> Torn => _torn;

    public IReadOnlyList<Piece> Pieces => _pieces;

    /// <summary>Starts in Kurt's arena: he stops, the screen shakes.</summary>
    public void Start(ScriptRuntime runtime)
    {
        _runtime = runtime;
        _arena = runtime.Level.Arenas.Find(a => a.Name == runtime.CurrentArena);
        if (_arena != null)
        {
            foreach (var triangle in HighestFirst(_arena))
            {
                _queue.Enqueue(triangle);
            }
        }

        runtime.Kurt.Frozen = true;
        runtime.Kurt.Rising = true;
        runtime.RaiseShake(StartShake);
    }

    /// <summary>The visible triangles of an arena, the highest top first.</summary>
    public static IEnumerable<int> HighestFirst(Arena arena) =>
        Enumerable.Range(0, arena.TriangleCount)
            .Where(t => (arena.TriangleFlags[t] & Hidden) == 0)
            .OrderByDescending(t => Top(arena, t));

    private static float Top(Arena arena, int triangle) =>
        Enumerable.Range(0, 3).Max(k => arena.Vertices[arena.TriangleIndices[triangle * 3 + k]].Z);

    private static Vector3 Centre(Arena arena, int triangle) =>
        Enumerable.Range(0, 3).Aggregate(Vector3.Zero, (sum, k) => sum + arena.Vertices[arena.TriangleIndices[triangle * 3 + k]]) / 3f;

    public void Update(float ticks)
    {
        if (_done || _runtime == null)
        {
            return;
        }

        var kurt = _runtime.Kurt;
        TearOff(_runtime.Rng.Next(PiecesPerTick));
        MovePieces(kurt.Feet, ticks);

        // Kurt takes off, turns and rises (0x40b558).
        _turn = MathF.Min(_turn + TurnGrowth * ticks, TurnMax);
        kurt.Yaw -= _turn * ticks;
        _riseSpeed = RiseSpeed(_riseSpeed, ticks);
        kurt.Feet += new Vector3(0f, 0f, _riseSpeed * ticks);
        _takeoff += ticks;
        kurt.Pose = _takeoff < takeoffFrames ? (Takeoff, (int)_takeoff) : (Floating, (int)_takeoff);
        if (_riseSpeed <= FastRise)
        {
            return;
        }

        // The view tilts up, then the screen goes white.
        var arenaPitch = _runtime.Level.Dti.Arenas.FirstOrDefault(a => a.Name == Arena)?.Pitch ?? DefaultPitch;
        var tilt = (TiltTarget - arenaPitch) * TiltShare;
        if (PitchOffset > tilt)
        {
            PitchOffset = MathF.Max(PitchOffset - TiltSpeed * ticks / Kurt.Kurt.Ticks, tilt);
            kurt.CameraTilt = PitchOffset;
            return;
        }

        _flash += FlashStep * ticks;
        kurt.WhiteFlash = MathF.Max(kurt.WhiteFlash, MathF.Min(_flash, FlashMax));
        if (_flash <= FlashEnd)
        {
            return;
        }

        _done = true;
        Finished?.Invoke();
    }

    /// <summary>For tests: the triangles torn off, the pieces flying, the tilt and the flash.</summary>
    public string Describe() => string.Create(CultureInfo.InvariantCulture,
        $"end of level: torn {_torn.Count}, pieces {_pieces.Count}, rise {_riseSpeed:0.00}, tilt {PitchOffset:0.0}, flash {_flash:0}");

    /// <summary>Kurt's rise speed after a step: 0.025 per tick², three times that above 3 per tick.</summary>
    public static float RiseSpeed(float speed, float ticks)
    {
        speed += Rise * ticks;
        return speed > FastRise ? speed + Rise * ticks * FastRiseFactor : speed;
    }

    /// <summary>The next <paramref name="count"/> highest triangles become pieces.</summary>
    private void TearOff(int count)
    {
        for (var i = 0; i < count && _queue.Count > 0; i++)
        {
            var triangle = _queue.Dequeue();
            _torn.Add(triangle);
            _pieces.Add(new Piece(triangle, Centre(_arena!, triangle)));
        }
    }

    /// <summary>The pieces rise and spin around Kurt; those 500 above him go.</summary>
    private void MovePieces(Vector3 kurt, float ticks)
    {
        var limit = kurt.Z + Limit;
        for (var i = _pieces.Count - 1; i >= 0; i--)
        {
            var piece = _pieces[i];
            piece.RiseSpeed += Rise * ticks;
            piece.Spin = MathF.Min(piece.Spin + SpinGrowth * ticks, SpinMax);
            var turn = Matrix3x2.CreateRotation(float.DegreesToRadians(piece.Spin * ticks));
            var around = Vector2.Transform(new Vector2(piece.Centre.X - kurt.X, piece.Centre.Y - kurt.Y), turn);
            piece.Centre = new Vector3(kurt.X + around.X, kurt.Y + around.Y, piece.Centre.Z + piece.RiseSpeed * ticks);
            piece.Angle += piece.Spin * ticks;
            if (piece.Centre.Z > limit)
            {
                _pieces.RemoveAt(i);
            }
        }
    }
}
