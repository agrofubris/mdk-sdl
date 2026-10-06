namespace Mdk.Game.Scripts;

/// <summary>The end of a level (endlev.c: 0x40a9e0, 0x40ad9c each frame, Kurt 0x40b558;
/// end_level.gd). Kurt takes off, turns ever faster (0.15° per tick², up to 2.5° per tick) and rises
/// (0.025 per tick²); once he rises faster than 3 units per tick his rise speeds up three times as
/// fast and the view tilts up (22.5° per second, to (-60 - the arena's pitch) / 2), then the screen
/// goes white (8 per tick); past 300 the level is over.
/// <code>
///   rise ──vz &gt; 3──► tilt up ──► white flash ──&gt; 300──► Finished
/// </code></summary>
// TODO the arena's triangles torn off from the highest down, flying up around Kurt (end_level.gd _queue, Piece)
// TODO Kurt's K_TAKEOF and K_FLOATC frames, and the camera's tilt (PitchOffset)
public sealed class EndLevel
{
    private const float Rise = 0.025f;
    private const float FastRise = 3f;
    private const float TurnGrowth = 0.15f;
    private const float TurnMax = 2.5f;
    private const float TiltSpeed = 22.5f;
    private const float TiltTarget = -60f;
    private const float DefaultPitch = 4f;
    private const float FlashStep = 8f;
    private const float FlashEnd = 300f;
    private const float FlashMax = 255f;
    private const float StartShake = 5f;

    private ScriptRuntime? _runtime;
    private float _riseSpeed;
    private float _turn;
    private float _flash;
    private bool _done;

    public event Action? Finished;

    /// <summary>Added to the camera pitch (degrees, negative looks up; 0x573b1c).</summary>
    public float PitchOffset { get; private set; }

    /// <summary>Starts in Kurt's arena: he stops, the screen shakes.</summary>
    public void Start(ScriptRuntime runtime)
    {
        _runtime = runtime;
        runtime.Kurt.Frozen = true;
        runtime.RaiseShake(StartShake);
    }

    public void Update(float ticks)
    {
        if (_done || _runtime == null)
        {
            return;
        }

        // Kurt turns and rises (0x40b558).
        var kurt = _runtime.Kurt;
        _turn = MathF.Min(_turn + TurnGrowth * ticks, TurnMax);
        kurt.Yaw -= _turn * ticks;
        _riseSpeed += Rise * ticks;
        if (_riseSpeed > FastRise)
        {
            _riseSpeed += Rise * ticks * 2f;
        }

        kurt.Feet.Z += _riseSpeed * ticks;
        if (_riseSpeed <= FastRise)
        {
            return;
        }

        // The view tilts up, then the screen goes white.
        var arena = _runtime.Level.Dti.Arenas.FirstOrDefault(a => a.Name == _runtime.CurrentArena);
        var tilt = (TiltTarget - (arena?.Pitch ?? DefaultPitch)) * 0.5f;
        if (PitchOffset > tilt)
        {
            PitchOffset = MathF.Max(PitchOffset - TiltSpeed * ticks / Kurt.Kurt.Ticks, tilt);
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
}
