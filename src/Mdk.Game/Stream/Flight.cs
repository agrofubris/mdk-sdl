using System.Numerics;
using Mdk.Game.Kurt;

namespace Mdk.Game.Stream;

/// <summary>Something flying along the tube: where along it (segments), across it, its speed
/// (segments/s), its yaw around the tube's forward axis and its animation frame.</summary>
public sealed class Thing
{
    public float T;
    public float X;
    public float Z;
    public float Speed;
    public float Yaw;
    public float Frame;
}

/// <summary>Kurt's steering keys: −1, 0 or 1 to the right and up.</summary>
public readonly record struct Steering(int Right, int Up);

/// <summary>An animation's length and speed (1 is 30 frames per second).</summary>
public readonly record struct Clip(int FrameCount, float Speed);

/// <summary>The screen's fade (as the fall's screen shader): towards white by <see cref="Whiten"/>
/// (1 = none), the red of death, then darkened by <see cref="Dark"/> (1 = none).</summary>
public readonly record struct Fade(float Whiten, float Dark, float Red);

/// <summary>What happened during an update, for the screen's sounds and messages.</summary>
public enum FlightEvent { BonesCame, KurtDied, WallHit, BonusTaken, Ended }

/// <summary>How the stream ended: Kurt goes on, or he died in the Gunter tube (game over).</summary>
public enum Outcome { Survived, Died }

/// <summary>The stream's play (game state 5, each frame 0x4352ac; stream.gd): Kurt flies down the
/// tube, steering, drifting on bends and bouncing off its walls; the lights fly back, the health
/// bonus walks ahead, then Bones' crane picks Kurt up (or Gunter leads him to the planet). No
/// drawing: <see cref="StreamScreen"/> shows it.
/// <code>
///   fade in ─► Kurt (steer, drift, walls) ─► others ─► camera ─┐  segment 177 or 1 health:
///      ▲                                                       │  Bones (normal) / tube's end (Gunter)
///      └───────────────────────────────────────────────────────┘  ─► fade out ─► Ended
/// </code></summary>
public sealed class Flight
{
    public const float TicksPerSecond = 30f;
    private const int BlinkMask = 31;
    private const float MaxDelta = 0.1f;
    /// <summary>Kurt's speed (segments/s), its recovery (segments/s²), its loss per hit and its minimum.</summary>
    private const float Speed = 6f;
    private const float SpeedRecovery = 1f;
    private const float SpeedLoss = 0.9f;
    private const float SpeedMin = 4.5f;
    /// <summary>Kurt starts this far into the tube, and the tube moves on when he's this far past its tail + 1.</summary>
    private const float KurtStart = 0.75f;
    /// <summary>Steering (°/s, ° around the neutral yaw 90 and pitch 0), and the sideways speed at full turn.</summary>
    private const float TurnRate = 180f;
    private const float TurnLimit = 45f;
    public const float NeutralYaw = 90f;
    private const float SideSpeed = 25f;
    /// <summary>On bends Kurt keeps going straight (a lagging direction) and drifts outwards at this rate.</summary>
    private const float Drift = 100f;
    private const float DirectionLag = 0.75f;
    /// <summary>Without control he drifts back to the axis (units/s).</summary>
    private const float AxisReturn = 6.25f;
    private const float WallMargin = 1.5f;
    /// <summary>Health lost per wall hit: a base and a random extra (0 or 1 times the step) per difficulty.</summary>
    private static readonly int[] HitDamage = [2, 2, 4];
    private static readonly int[] HitExtra = [0, 1, 2];
    /// <summary>Bones comes after this segment (normal variant); the rescue ends past this animation frame.</summary>
    public const int RescueSegment = 177;
    private const int RescueEndFrame = 80;
    /// <summary>The health bonus: where it starts, its speed, its reach and the health it gives.</summary>
    private const float BonusStart = 16f;
    private const float BonusSpeed = 5.48f;
    private const float BonusReach = 5f;
    private const int BonusHealth = 150;
    private const float GuntaStart = 5f;
    /// <summary>The camera: from the axis behind Kurt and a point past him, looking two segments ahead.</summary>
    private const float CameraBack = 0.75f;
    private const float CameraAhead = 2f;
    private const float CameraBlend = 0.4f;
    private const float CameraPast = 1.375f;
    /// <summary>The background scrolls with the view's turns (0x438bfc) over its 600 x 360 pixels.</summary>
    private const float BackgroundWidth = 600f;
    private const float BackgroundHeight = 360f;
    private const float ScrollX = 300f;
    private const float ScrollY = 180f;
    /// <summary>The fade lasts a second; on death it goes on to 2 (red, then black).</summary>
    private const float FadeDeath = 2f;
    public const int LowHealth = 1;
    private static readonly string[] HurtSounds = ["HURT1", "HURT2", "HURT3", "HURT4", "HURT5", "HURT6", "HURT7"];

    private readonly StreamTube _tube;
    private readonly StreamTube.Kind _kind;
    private readonly Difficulty _difficulty;
    private readonly WatcomRandom _random;
    private readonly Clip _kurtClip;
    private readonly Clip _rescueClip;
    private readonly Clip _otherClip;
    private readonly List<(StreamTube.Spawn Spawn, Thing Thing)> _sprites = [];
    private Vector3 _direction = Vector3.UnitY;
    private Vector3 _screenUp = Vector3.UnitZ;
    private Vector3[]? _viewRows;
    private float _fade;
    private bool _ending;
    private float _tickFraction;

    public Thing Kurt { get; } = new() { T = KurtStart, Yaw = NeutralYaw, Speed = Speed };
    /// <summary>Kurt plays the rescue (<c>BONESANIM</c>) once Bones has come.</summary>
    public Clip KurtClip => Bones == null ? _kurtClip : _rescueClip;
    public float Pitch { get; private set; }
    public Vector3 KurtPosition { get; private set; }
    public Thing? Bones { get; private set; }
    /// <summary>The health bonus (<c>SWH150</c>, normal tube) or Gunter carrying Bones (Gunter tube).</summary>
    public Thing? Other { get; private set; }
    public int Health { get; private set; }
    public bool Ended { get; private set; }
    public Outcome Outcome => Health < LowHealth ? Outcome.Died : Outcome.Survived;
    /// <summary>Counts ticks (0-31) for the health's blink.</summary>
    public int Blink { get; private set; }
    public Fade Fade { get; private set; } = new(0f, 1f, 0f);
    public float Time { get; private set; }

    /// <summary>The camera: its eye, forward, right and up (MDK coordinates).</summary>
    public Vector3 Eye { get; private set; }
    public Vector3 Forward { get; private set; }
    public Vector3 Right { get; private set; }
    public Vector3 Up => _screenUp;
    /// <summary>The background's top-left pixel.</summary>
    public Vector2 BackgroundOffset { get; private set; }

    /// <summary>Events and sounds of the last update.</summary>
    public List<FlightEvent> Events { get; } = [];
    public List<string> Sounds { get; } = [];

    public StreamTube Tube => _tube;
    public StreamTube.Kind Kind => _kind;
    public IEnumerable<(StreamTube.Spawn Spawn, Thing Thing)> Sprites => _sprites;
    public int LightCount { get; }

    public Flight(StreamTube tube, StreamTube.Kind kind, Difficulty difficulty, WatcomRandom random, int health,
        Clip kurt, Clip rescue, Clip other)
    {
        _tube = tube;
        _kind = kind;
        _difficulty = difficulty;
        _random = random;
        Health = health;
        (_kurtClip, _rescueClip, _otherClip) = (kurt, rescue, other);
        LightCount = AddSpawns();
        Other = kind == StreamTube.Kind.Gunter
            ? new Thing { T = GuntaStart, Speed = Speed, Yaw = NeutralYaw }
            : new Thing { T = BonusStart, Speed = BonusSpeed };
        UpdateCamera();
    }

    /// <summary>Kurt steers until Bones comes or he dies.</summary>
    private bool Controlled => Bones == null && Health > 0;

    /// <summary>Esc: the stream fades out (not in the original).</summary>
    public void Stop() => _ending = true;

    /// <summary>One frame of <paramref name="delta"/> seconds.</summary>
    public void Update(float delta, Steering steering)
    {
        Events.Clear();
        Sounds.Clear();
        if (Ended)
        {
            return;
        }

        var dt = MathF.Min(delta, MaxDelta);
        Time += dt;
        _tickFraction += dt * TicksPerSecond;
        var ticks = (int)_tickFraction;
        _tickFraction -= ticks;
        Blink = (Blink + ticks) & BlinkMask;

        CheckEnd();
        if (!UpdateFade(dt))
        {
            return;
        }

        UpdateKurt(dt, steering);
        UpdateOthers(dt);
        UpdateCamera();
    }

    /// <summary>The rescue after segment 177 or at 1 health, or the end of the Gunter tube.</summary>
    private void CheckEnd()
    {
        if (Health <= 0 || (_tube.Tail <= RescueSegment && Health != LowHealth))
        {
            return;
        }

        if (_kind == StreamTube.Kind.Gunter)
        {
            _ending |= _tube.Head >= StreamTube.LastSegment;
            return;
        }

        if (Bones == null)
        {
            StartRescue();
        }

        if (Bones!.Frame > RescueEndFrame)
        {
            _ending = true;
        }
    }

    /// <summary>The fade (0x520860): in from white (black after LEVEL8) over 1 s, out the same way
    /// once the stream ends; on death in the Gunter tube a red flash, then black. False once over.</summary>
    private bool UpdateFade(float dt)
    {
        if (_ending)
        {
            _fade -= dt;
            if (_fade < 0f)
            {
                Ended = true;
                Events.Add(FlightEvent.Ended);
                return false;
            }
        }
        else if (_fade < 1f)
        {
            _fade = MathF.Min(_fade + dt, 1f);
        }

        var f = Math.Clamp(_fade, 0f, FadeDeath);
        Fade = (Health <= 0, f > 1f, _kind) switch
        {
            (true, true, _) => new Fade(1f, 1f, FadeDeath - f),
            (true, false, _) => new Fade(1f, f, 1f),
            (false, _, StreamTube.Kind.Gunter) => new Fade(1f, MathF.Min(f, 1f), 0f),
            _ => new Fade(MathF.Min(f, 1f), 1f, 0f),
        };
        return true;
    }

    /// <summary>The lights and the planet the tube made: they fly along it. Returns how many.</summary>
    private int AddSpawns()
    {
        var spawns = _tube.TakeSpawns();
        foreach (var spawn in spawns)
        {
            _sprites.Add((spawn, new Thing { T = spawn.T, X = spawn.X, Z = spawn.Z, Speed = spawn.Speed }));
        }

        return spawns.Count;
    }

    /// <summary>Moves something along the tube (0x436440); false once it leaves the rings still alive.</summary>
    private bool Move(Thing thing, float dt)
    {
        thing.T += thing.Speed * dt;
        thing.Frame += dt * TicksPerSecond;
        return _tube.Contains(thing.T);
    }

    /// <summary>Where something is and how it's turned: the tube's frame at its place, turned by its yaw.</summary>
    public Matrix4x4 Transform(Thing thing) =>
        (_tube.Frame(thing.T, _screenUp) * TubeBasis.Rotation(Vector3.UnitZ, thing.Yaw)).At(_tube.Place(thing.T, thing.X, thing.Z));

    /// <summary>Kurt's transform: the tube's frame turned by his yaw and pitch.</summary>
    public Matrix4x4 KurtTransform()
    {
        var turn = TubeBasis.Rotation(Vector3.UnitZ, Kurt.Yaw) * TubeBasis.Rotation(-Vector3.UnitY, Pitch);
        return (_tube.Frame(Kurt.T, _screenUp) * turn).At(KurtPosition);
    }

    /// <summary>A looping animation's frame for a thing.</summary>
    public int OtherFrame(Thing thing) => _otherClip.FrameCount == 0 ? 0 : (int)thing.Frame % _otherClip.FrameCount;

    /// <summary>Kurt's frame: looping while he flies, held at the last of the rescue.</summary>
    public int KurtFrame() => Bones == null ? (int)Kurt.Frame : Math.Min((int)Kurt.Frame, _rescueClip.FrameCount - 1);

    /// <summary>Bones' frame of the rescue, held at the last.</summary>
    public int BonesFrame() => Math.Min((int)(Bones?.Frame ?? 0f), _rescueClip.FrameCount - 1);

    /// <summary>The lights and the planet, the health bonus or Gunter, and Bones.</summary>
    private void UpdateOthers(float dt)
    {
        _sprites.RemoveAll(s => !Move(s.Thing, dt));
        if (Other != null && !Move(Other, dt))
        {
            Other = null;
        }

        if (Other != null && _kind == StreamTube.Kind.Normal)
        {
            TakeBonus();
        }

        if (Bones != null)
        {
            Bones.Frame += dt * TicksPerSecond * _rescueClip.Speed;
        }
    }

    /// <summary>The walking health bonus (0x435b18): caught within 5 units, it gives 150 health.</summary>
    private void TakeBonus()
    {
        if (Vector3.Distance(KurtPosition, _tube.Place(Other!.T, Other.X, Other.Z)) >= BonusReach)
        {
            return;
        }

        Health = BonusHealth;
        Sounds.Add("APPLE");
        Events.Add(FlightEvent.BonusTaken);
        Other = null;
    }

    /// <summary>Kurt (0x435c4c): speed, the tube moving on, his animation, drift, steering and walls.</summary>
    private void UpdateKurt(float dt, Steering steering)
    {
        Kurt.Speed = MathF.Min(Kurt.Speed + SpeedRecovery * dt, Speed);
        Kurt.T += Kurt.Speed * dt;

        // A new segment each time Kurt passes one.
        if (Kurt.T - KurtStart > _tube.Tail + 1)
        {
            _tube.Advance();
            AddSpawns();
        }

        var clip = KurtClip;
        Kurt.Frame += dt * TicksPerSecond * clip.Speed;
        if (Bones == null && clip.FrameCount > 0)
        {
            Kurt.Frame %= clip.FrameCount;
        }

        var frame = _tube.Frame(Kurt.T, _screenUp);
        DriftKurt(frame, dt);
        Steer(dt, Controlled ? steering : default);

        var centre = _tube.Centre(Kurt.T);
        KurtPosition = centre + frame.X * Kurt.X + frame.Z * Kurt.Z;
        if (Controlled)
        {
            Collide(centre, frame);
        }
    }

    /// <summary>On bends Kurt keeps going straight and drifts to the outer wall; without control he
    /// goes back to the axis.</summary>
    private void DriftKurt(TubeBasis frame, float dt)
    {
        if (!Controlled)
        {
            Kurt.X = MoveToward(Kurt.X, 0f, AxisReturn * dt);
            Kurt.Z = MoveToward(Kurt.Z, 0f, AxisReturn * dt);
            return;
        }

        _direction = Vector3.Normalize(DirectionLag * _direction + (1f - DirectionLag) * frame.Y);
        Kurt.X += Drift * dt * Vector3.Dot(_direction, frame.X);
        Kurt.Z += Drift * dt * Vector3.Dot(_direction, frame.Z);
    }

    /// <summary>Steering turns Kurt up to 45° either way and moves him sideways; he turns back when
    /// the keys are released. Left and up turn him left and up.</summary>
    private void Steer(float dt, Steering steering)
    {
        if (steering.Right != 0)
        {
            Kurt.Yaw = Math.Clamp(Kurt.Yaw - steering.Right * TurnRate * dt, NeutralYaw - TurnLimit, NeutralYaw + TurnLimit);
        }
        else
        {
            Kurt.Yaw = MoveToward(Kurt.Yaw, NeutralYaw, TurnRate * dt);
        }

        if (steering.Up != 0)
        {
            Pitch = Math.Clamp(Pitch + steering.Up * TurnRate * dt, -TurnLimit, TurnLimit);
        }
        else
        {
            Pitch = MoveToward(Pitch, 0f, TurnRate * dt);
        }

        Kurt.X += MathF.Cos(float.DegreesToRadians(Kurt.Yaw)) * SideSpeed * dt;
        Kurt.Z += MathF.Sin(float.DegreesToRadians(Pitch)) * SideSpeed * dt;
    }

    /// <summary>A wall hit pushes Kurt back towards the axis, turns him inwards, hurts him and slows him down.</summary>
    private void Collide(Vector3 centre, TubeBasis frame)
    {
        var k = _tube.WallHit(KurtPosition, Kurt.T, WallMargin);
        if (k < 0f)
        {
            return;
        }

        // Facing the axis at full turn, e.g. at the right wall (x > 0) he turns left (yaw 135).
        var r = new Vector2(Kurt.X, Kurt.Z).Length();
        if (r > 0f)
        {
            Kurt.Yaw = NeutralYaw + TurnLimit * Kurt.X / r;
            Pitch = -TurnLimit * Kurt.Z / r;
        }

        Kurt.X *= 1f - k;
        Kurt.Z *= 1f - k;
        KurtPosition = centre + frame.X * Kurt.X + frame.Z * Kurt.Z;
        Kurt.Speed = MathF.Max(Kurt.Speed * SpeedLoss, SpeedMin);

        Events.Add(FlightEvent.WallHit);
        Sounds.Add(HurtSounds[_random.Next() % HurtSounds.Length]);
        Hurt();
    }

    /// <summary>Wall damage by difficulty. At 0 health Kurt dies in the Gunter tube, else Bones comes for him.</summary>
    private void Hurt()
    {
        if (Health <= 0)
        {
            return;
        }

        var d = (int)_difficulty;
        Health -= HitDamage[d] + HitExtra[d] * (_random.Next() & 1);
        if (Health >= LowHealth)
        {
            return;
        }

        if (_kind == StreamTube.Kind.Normal)
        {
            Health = LowHealth;
            return;
        }

        Health = 0;
        _ending = true;
        _fade = FadeDeath;
        Events.Add(FlightEvent.KurtDied);
    }

    /// <summary>Bones picks Kurt up: both play <c>BONESANIM</c> (the crane, the rope, Bones and Kurt) once.</summary>
    private void StartRescue()
    {
        Bones = new Thing();
        Sounds.Add("RESCUE");
        Events.Add(FlightEvent.BonesCame);
        Kurt.Frame = 0f;
    }

    /// <summary>The camera (0x4352ac, 0x436828): behind Kurt and to his side, looking at the axis two
    /// segments ahead, its roll carried over from frame to frame.</summary>
    private void UpdateCamera()
    {
        var target = _tube.Centre(Kurt.T + CameraAhead);
        var past = target + CameraPast * (KurtPosition - target);
        var eye = CameraBlend * _tube.Centre(Kurt.T - CameraBack) + (1f - CameraBlend) * past;
        var forward = Vector3.Normalize(target - eye);
        var right = Vector3.Normalize(Vector3.Cross(forward, _screenUp));
        _screenUp = Vector3.Cross(right, forward);
        (Eye, Forward, Right) = (eye, forward, right);

        // The background scrolls with the view's turns (0x438bfc).
        var down = -_screenUp;
        if (_viewRows is [var r0, var u0, var f0])
        {
            var y = PositiveMod(BackgroundOffset.Y + ScrollY * (down.Z * f0.Z - forward.Z * u0.Z), BackgroundHeight);
            var x = PositiveMod(BackgroundOffset.X + ScrollX * (right.X * u0.X - down.X * r0.X), BackgroundWidth);
            BackgroundOffset = new Vector2(x, y);
        }

        _viewRows = [right, down, forward];
    }

    private static float PositiveMod(float value, float modulus) => ((value % modulus) + modulus) % modulus;

    private static float MoveToward(float from, float to, float step) =>
        MathF.Abs(to - from) <= step ? to : from + MathF.Sign(to - from) * step;
}
