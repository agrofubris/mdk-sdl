using System.Numerics;
using Mdk.Game.Kurt;

namespace Mdk.Game.Fall;

/// <summary>The fall before a level (fall.gd; game state 2, <c>fall_3d.c</c>: init 0x410018, each
/// frame 0x4114a4, the intro 0x41106c). See godot-mdk docs/gameplay.md, "The fall".
/// <code>
///   intro in space (150 ticks) ──► fall (33 s) ───────────────────────────► Landed (the level)
///                                   │ steering until 30 s, dark from 31 s
///                                   │ radar sees Kurt ──► missiles ──► hits ──► health 0 ──► GameOver
///                                   └ pickups fall on chutes ──► inventory
/// </code>
/// Kurt falls at 66.67 u/s from z 5270; the camera is 10 units above him. Pure logic: the screen
/// draws it (<see cref="FallView"/>) and plays its sounds (<see cref="Sound"/>).</summary>
public sealed partial class FallSim
{
    public enum Outcome { Running, Landed, GameOver }

    public enum KurtAnimation { Fall, Hit }

    /// <summary>A model animation's speed (1 = 30 frames/s) and length.</summary>
    public readonly record struct Animation(float Speed, int FrameCount);

    /// <summary>What a fall starts from: the level index (0-4), the difficulty, the pickups of
    /// <c>FALLPU_n</c> and the animations <c>KURTANIM</c>, <c>KURT_HIT</c>, <c>BONESANM</c>.</summary>
    public sealed record Setup(int Index, Difficulty Difficulty, IReadOnlyList<string> Pickups, Animation Kurt, Animation KurtHit, Animation Bones);

    public const float TicksPerSecond = 30f;
    private const float MaxDelta = 0.1f;
    public const int IntroTicks = 150;
    /// <summary>Kurt steers until 30 s, the view goes black from 31 s, the fall ends at 33 s.</summary>
    public const float SteerTime = 30f;
    private const float FadeTime = 31f;
    private const float FadeLength = 2f;
    public const float EndTime = 33f;
    public const float KurtStartZ = 5270f;
    public const float KurtSpeed = 2000f / 30f;
    private const float CameraAbove = 10f;
    /// <summary>After 30 s the camera brakes to a stop in 2 s.</summary>
    private const float CameraBrake = KurtSpeed / 2f;
    /// <summary>The camera follows Kurt's x and y by 0.85.</summary>
    private const float CameraFollow = 0.85f;
    /// <summary>Steering (<c>vel_accel_dt</c>): 11.765 u/s per tick up to 117.65 u/s, within ±58.82 × ±35.29.</summary>
    public const float SteerStep = 200f / 17f;
    public const float SteerMax = 2000f / 17f;
    public static readonly Vector2 Limit = new(1000f / 17f, 600f / 17f);
    /// <summary>Kurt's collision box: ±4, ±4, ±5 (+0x198...+0x1ac).</summary>
    private static readonly Vector3 KurtBox = new(4f, 4f, 5f);
    private const float BonesSpeed = 2000f / 27f;
    private const float BonesAbove = 20f;
    /// <summary>The wind level (0x5209b8): 0xC00 is full, the haze's level is it >> 8.</summary>
    public const int WindFull = 0xC00;
    public const int WindShift = 8;
    private const float WindSoundVolume = 0.625f;
    /// <summary>The wind follows the camera's braking: <c>−vz × 0.015 × 3072</c>.</summary>
    private const float WindPerSpeed = 0.015f * WindFull;
    public const int HazeFrames = 16;
    public const int CrawlerFrames = 8;
    /// <summary>The minecrawler's frames run at 15 per second.</summary>
    private const float CrawlerRate = 0.5f;
    private const string FirstMessage = "FALL_T1";
    private const int FirstMessageIndex = 0;
    /// <summary>Bones falls with Kurt above this index (LEVEL8).</summary>
    private const int BonesIndex = 3;

    // Intro timeline (ticks left): fade in from black until 90, Kurt flies in from 90 over 60,
    // the wind rises from 120 to 60, fade to white from 60.
    private const int IntroFadeIn = 90;
    private const int IntroFadeOut = 60;
    private const float IntroFadeTicks = 60f;
    private const int IntroWindStart = 120;
    private const int IntroWindFull = 61;
    private const int IntroWindSteps = 12;
    private static readonly Vector3 IntroKurtStart = new(-30f, -10f, 0f);
    private static readonly Vector3 IntroKurtMove = new(30f, 10f, -10f);

    // Difficulty (0x410018) by Easy, Normal, Hard; i is the level index.
    /// <summary>Radar beam speed: 117.65 × (1 + factor × i).</summary>
    private static readonly float[] BeamFactor = [0.1f, 0.2f, 1f / 3f];
    /// <summary>Missiles per detection: 2 + i / divisor.</summary>
    private static readonly int[] MissileDivisor = [5, 3, 2];
    private const int MissilesBase = 2;
    /// <summary>Missile aim spread: base − i.</summary>
    private static readonly float[] SpreadBase = [7.5f, 6.5f, 5.5f];
    /// <summary>Ticks between missiles: 32 − step × i (+ 0-31).</summary>
    private static readonly int[] MissileGapStep = [1, 3, 5];
    private const int MissileGapBase = 32;
    /// <summary>Ticks before the next radar: 63 − step × i (+ 0-63).</summary>
    private static readonly int[] RadarGapStep = [3, 7, 9];
    private const int RadarGapBase = 63;

    private readonly Setup _setup;
    private readonly Random _random;
    private int _introLeft = IntroTicks;
    private float _tickFraction;
    private float _cameraSpeed = -KurtSpeed;
    private bool _kurtFinished;
    private bool _bonesPassed;

    // Palette effects (0x5209c8 and its target and rate).
    private float _brightness;
    private float _target = 1f;
    private float _rate = SettleRate;
    private const float FlashRate = 3f;
    private const float SettleRate = 0.5f;
    private const float Flicker = 0.9f;
    /// <summary>A flicker starts on 1 tick in 32.</summary>
    private const int FlickerChance = 32;
    private const float HitBrightness = 3f;
    private const float BrightnessEpsilon = 1e-5f;
    /// <summary>Dying: the red rises by 5/255 a tick while the screen is at full brightness.</summary>
    private const float RedPerTick = 5f / 255f;

    public FallSim(Setup setup, Random random)
    {
        _setup = setup;
        _random = random;
        var i = setup.Index;
        var d = (int)setup.Difficulty;
        BeamSpeed = SteerMax * (1f + BeamFactor[d] * i);
        MissilesPerDetection = MissilesBase + i / MissileDivisor[d];
        Spread = SpreadBase[d] - i;
        MissileGap = MissileGapBase - MissileGapStep[d] * i;
        RadarGap = RadarGapBase - RadarGapStep[d] * i;
        _pickupNames = [.. setup.Pickups];
        Inventory.Difficulty = setup.Difficulty;
    }

    /// <summary>A sound to play (<c>FALL3D.SNI</c>).</summary>
    public event Action<string>? Sound;
    /// <summary>A message to show (<c>MDKFONT.FTI</c> text name, zoomed, 3 seconds).</summary>
    public event Action<string>? Message;

    public float BeamSpeed { get; }
    public int MissilesPerDetection { get; }
    public float Spread { get; }
    public int MissileGap { get; }
    public int RadarGap { get; }

    public Outcome State { get; private set; }
    /// <summary>Whole ticks (1/30 s) of the last update.</summary>
    public int Ticks { get; private set; }
    public bool InIntro => _introLeft > 0;
    /// <summary>Ticks left of the intro (0 once the fall started).</summary>
    public int IntroLeft => Math.Max(_introLeft, 0);
    /// <summary>Seconds since the intro ended.</summary>
    public float Time { get; private set; }
    public bool Dying { get; private set; }
    public int Health { get; private set; } = Inventory.MaxHealth;
    public Inventory Inventory { get; } = new();
    /// <summary>The pickups taken, in order (they go on to the level).</summary>
    public IReadOnlyList<string> Collected => _collected;
    private readonly List<string> _collected = [];

    public Vector3 KurtPosition { get; private set; }
    private Vector3 _kurtVelocity;
    public bool KurtVisible { get; private set; }
    public KurtAnimation KurtPose { get; private set; }
    public float KurtFrame { get; private set; }
    public Vector3 CameraPosition { get; private set; }
    public Vector3? BonesPosition { get; private set; }
    public float BonesFrame { get; private set; }
    private float _bonesTicks;

    public int Wind { get; private set; }
    public int HazeFrame { get; private set; }
    public float CrawlerFrame { get; private set; }
    /// <summary>The screen towards white (1 none), darkened (1 none) and red (0 none).</summary>
    public float Whiten { get; private set; } = 1f;
    public float Dark { get; private set; }
    public float Red { get; private set; }

    /// <summary>The wind loop's volume.</summary>
    public float WindVolume => (float)Wind / WindFull * WindSoundVolume;
    /// <summary>The minecrawler's grind: from 2/3 to full over 30 s.</summary>
    public float GrindVolume => (MathF.Min(Time / SteerTime, 1f) + 2f) / 3f;

    /// <summary>One frame of <paramref name="delta"/> seconds; <paramref name="steer"/> is −1, 0 or
    /// 1 per axis (x right, y up the screen).</summary>
    public void Update(float delta, Vector2 steer)
    {
        if (State != Outcome.Running)
        {
            return;
        }

        var dt = MathF.Min(delta, MaxDelta);
        _tickFraction += dt * TicksPerSecond;
        var ticks = (int)_tickFraction;
        _tickFraction -= ticks;
        Ticks = ticks;
        if (_introLeft > 0)
        {
            UpdateIntro(ticks);
            return;
        }

        UpdateFall(dt, ticks, steer);
    }

    /// <summary>Esc skips the fall (the port's addition).</summary>
    public void Skip()
    {
        if (State == Outcome.Running && !Dying)
        {
            State = Outcome.Landed;
        }
    }

    // --- The intro in space (0x41106c) ---

    private void UpdateIntro(int ticks)
    {
        _introLeft -= ticks;
        var left = Math.Max(_introLeft, 0);

        // Fade in from black, then plain, then fade to white.
        if (left > IntroFadeIn)
        {
            SetScreen(1f, 1f - (left - IntroFadeIn) / IntroFadeTicks);
        }
        else if (left < IntroFadeOut)
        {
            SetScreen(1f - (IntroFadeOut - left) / IntroFadeTicks, 1f);
        }
        else
        {
            SetScreen(1f, 1f);
        }

        if (left < IntroWindStart)
        {
            Wind = left < IntroWindFull ? WindFull : ((IntroWindStart - left) * IntroWindSteps / IntroFadeOut) << WindShift;
            HazeFrame = (HazeFrame + ticks) % HazeFrames;
        }

        CameraPosition = Vector3.Zero;

        // Kurt flies in from the camera to 10 units below it, easing out.
        if (left < IntroFadeIn)
        {
            KurtVisible = true;
            AnimateKurt(ticks);
            var f = 1f - MathF.Pow(1f - MathF.Min((IntroFadeIn - left) / IntroFadeTicks, 1f), 2f);
            KurtPosition = IntroKurtStart + IntroKurtMove * f;
        }

        if (_introLeft <= 0)
        {
            StartFall();
        }
    }

    private void StartFall()
    {
        _introLeft = 0;
        Time = 0f;
        StartRadars();
        StartPickups();
        Wind = WindFull;
        KurtPosition = new Vector3(0f, 0f, KurtStartZ);
        if (_setup.Index > BonesIndex)
        {
            BonesPosition = new Vector3(0f, 0f, KurtStartZ + BonesAbove);
        }

        if (_setup.Index == FirstMessageIndex)
        {
            Message?.Invoke(FirstMessage);
        }
    }

    // --- The fall (0x4114a4) ---

    private void UpdateFall(float dt, int ticks, Vector2 steer)
    {
        Time += dt;
        UpdateKurt(dt, ticks, steer);
        UpdateCamera(dt);
        UpdateRadar(dt, ticks);
        UpdateMissiles(dt, ticks);
        UpdateExplosions(ticks);
        UpdatePickups(dt, ticks);
        UpdateBones(dt, ticks);
        UpdatePalette(dt, ticks);
        HazeFrame = (HazeFrame + ticks) % HazeFrames;
        CrawlerFrame = (CrawlerFrame + ticks * CrawlerRate) % CrawlerFrames;

        if (Dying || State != Outcome.Running)
        {
            return;
        }

        if (Time > EndTime)
        {
            State = Outcome.Landed;
        }
    }

    private void UpdateKurt(float dt, int ticks, Vector2 steer)
    {
        AnimateKurt(ticks);
        var velocity = new Vector2(_kurtVelocity.X, _kurtVelocity.Y);
        var p = new Vector2(KurtPosition.X, KurtPosition.Y);
        if (Time <= SteerTime && !Dying)
        {
            velocity.X = Steer(velocity.X, MathF.Sign(steer.X), dt);
            velocity.Y = Steer(velocity.Y, MathF.Sign(steer.Y), dt);
        }
        else if (Time > SteerTime)
        {
            if (!_kurtFinished)
            {
                _kurtFinished = true;
                Sound?.Invoke("K_FINISH");
            }

            // Pulled back to the centre: v = (v − 2 p) / 2 a tick.
            for (var i = 0; i < ticks; i++)
            {
                velocity = (velocity - 2f * p) / 2f;
            }
        }

        (p, velocity) = Clamp(p + velocity * dt, velocity);
        KurtPosition = new Vector3(p, KurtPosition.Z - KurtSpeed * dt);
        _kurtVelocity = new Vector3(velocity, -KurtSpeed);
    }

    /// <summary>Hitting a limit zeroes that speed.</summary>
    private static (Vector2 Position, Vector2 Velocity) Clamp(Vector2 p, Vector2 velocity)
    {
        if (MathF.Abs(p.X) > Limit.X)
        {
            p.X = MathF.Sign(p.X) * Limit.X;
            velocity.X = 0f;
        }

        if (MathF.Abs(p.Y) > Limit.Y)
        {
            p.Y = MathF.Sign(p.Y) * Limit.Y;
            velocity.Y = 0f;
        }

        return (p, velocity);
    }

    /// <summary>One axis of Kurt's steering (<c>vel_accel_dt</c> 0x409270 with a key, 0x4687a4
    /// without): a key accelerates by a step a tick, the opposite key first resets the speed to one
    /// step, no key brakes by a step a tick.</summary>
    public static float Steer(float speed, int direction, float dt)
    {
        var step = SteerStep * TicksPerSecond * dt;
        if (direction == 0)
        {
            return MathF.Abs(speed) <= step ? 0f : speed - MathF.Sign(speed) * step;
        }

        if (speed * direction < 0f)
        {
            return direction * SteerStep;
        }

        return Math.Clamp(speed + direction * step, -SteerMax, SteerMax);
    }

    /// <summary><c>KURTANIM</c> loops; a hit plays <c>KURT_HIT</c> once (looping <c>KURTANIM</c>
    /// forever once he's dying).</summary>
    private void AnimateKurt(int ticks)
    {
        var animation = KurtPose == KurtAnimation.Hit ? _setup.KurtHit : _setup.Kurt;
        KurtFrame += ticks * animation.Speed;
        if (KurtFrame < animation.FrameCount)
        {
            return;
        }

        if (KurtPose == KurtAnimation.Hit && !Dying)
        {
            KurtPose = KurtAnimation.Fall;
            KurtFrame = 0f;
            return;
        }

        KurtFrame %= animation.FrameCount;
    }

    /// <summary>The camera (0x413440): above Kurt until 30 s, then it brakes to a stop in 2 s.</summary>
    private void UpdateCamera(float dt)
    {
        var z = KurtPosition.Z + CameraAbove;
        if (Time > SteerTime)
        {
            _cameraSpeed = MathF.Min(_cameraSpeed + CameraBrake * dt, 0f);
            z = CameraPosition.Z + _cameraSpeed * dt;
            Wind = (int)(-_cameraSpeed * WindPerSpeed);
        }

        CameraPosition = new Vector3(CameraFollow * KurtPosition.X, CameraFollow * KurtPosition.Y, z);
    }

    /// <summary>Whether a move crosses Kurt's box (0x45ef80).</summary>
    public static bool CrossesBox(Vector3 centre, Vector3 from, Vector3 to)
    {
        var min = centre - KurtBox;
        var max = centre + KurtBox;
        if (Vector3.Clamp(to, min, max) == to)
        {
            return true;
        }

        // Slabs: the part of the segment inside each axis's range.
        var enter = 0f;
        var leave = 1f;
        var d = to - from;
        for (var axis = 0; axis < 3; axis++)
        {
            if (MathF.Abs(d[axis]) < float.Epsilon)
            {
                if (from[axis] < min[axis] || from[axis] > max[axis])
                {
                    return false;
                }

                continue;
            }

            var a = (min[axis] - from[axis]) / d[axis];
            var b = (max[axis] - from[axis]) / d[axis];
            enter = MathF.Max(enter, MathF.Min(a, b));
            leave = MathF.Min(leave, MathF.Max(a, b));
        }

        return enter <= leave;
    }

    // --- Bones (0x4133b8) ---

    private void UpdateBones(float dt, int ticks)
    {
        if (BonesPosition is not { } bones)
        {
            return;
        }

        _bonesTicks += ticks;
        BonesFrame = _bonesTicks * _setup.Bones.Speed % _setup.Bones.FrameCount;
        BonesPosition = bones with { Z = bones.Z - BonesSpeed * dt };
        if (!_bonesPassed && BonesPosition.Value.Z < KurtPosition.Z)
        {
            _bonesPassed = true;
            Sound?.Invoke("BONES");
        }
    }

    // --- Palette effects (0x5209c8, 0x410cc0, 0x410c28) ---

    private void UpdatePalette(float dt, int ticks)
    {
        if (Dying)
        {
            // Red with the skull growing, then black; at 0 it's game over.
            if (_brightness >= 1f)
            {
                Red = MathF.Min(Red + ticks * RedPerTick, 1f);
            }

            _brightness -= dt;
            SetScreen(1f, Math.Clamp(_brightness, 0f, 1f));
            if (_brightness <= 0f)
            {
                State = Outcome.GameOver;
            }

            return;
        }

        Flash(dt, ticks);
        var dark = Time > FadeTime ? MathF.Max(1f - (Time - FadeTime) / FadeLength, 0f) : 1f;
        SetScreen(Math.Clamp(_brightness, 0f, 1f), dark);
    }

    /// <summary>The brightness fades in over the first second, flashes on detections, launches and
    /// hits, and flickers now and then.</summary>
    private void Flash(float dt, int ticks)
    {
        if (Time < 1f)
        {
            _brightness = Time;
            return;
        }

        if (MathF.Abs(_brightness - _target) > BrightnessEpsilon)
        {
            var step = _rate * dt;
            _brightness = MathF.Abs(_target - _brightness) <= step ? _target : _brightness + MathF.Sign(_target - _brightness) * step;
            return;
        }

        if (_target < 1f)
        {
            _target = 1f;
            _rate = SettleRate;
            return;
        }

        for (var i = 0; i < ticks; i++)
        {
            if (_random.Next(FlickerChance) != 0)
            {
                continue;
            }

            _target = Flicker;
            _rate = SettleRate;
            break;
        }
    }

    private void SetScreen(float whiten, float dark)
    {
        Whiten = whiten;
        Dark = dark;
    }

    /// <summary>A random point within Kurt's limits.</summary>
    private Vector2 RandomPoint() => new(RandomRange(Limit.X), RandomRange(Limit.Y));

    /// <summary>Uniform in ±<paramref name="range"/>.</summary>
    private float RandomRange(float range) => (_random.NextSingle() * 2f - 1f) * range;
}
