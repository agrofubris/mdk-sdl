using Mdk.Game.Audio;
using Mdk.Game.Objects;

namespace Mdk.Game.Scripts;

/// <summary>The objects Kurt rides (0x573c30, <c>damp_control</c> 0x466368; rides.gd): the snowboard
/// of level 4 (<c>XSNOWB</c>, <see cref="Snowboard"/>), the <c>XD2</c> of level 7's <c>DANT_9</c>
/// (0x46a840) and the <c>XE</c> bomber of its <c>DANT_5</c> (<see cref="Bomber"/>). A script makes
/// an object rideable (flag 0x2000000); Kurt gets on when he stands on it (or, for the XD2 and the
/// XE, touches it) without firing, and off when the script clears the flag.
/// <code>
///   rideable + Kurt on it ──► riding ──(flag cleared)──► off (the XD2: a jump; the board: thrown;
///                                                        the XE: a drop)
/// </code></summary>
public sealed class Rides(ScriptRuntime runtime)
{
    /// <summary>Object flags: rideable, controls locked (the board, the XE), ridden, the rider fires (the XD2).</summary>
    public const int FlagRideable = 0x2000000;
    public const int FlagLocked = 0x4000000;
    private const int FlagRidden = 0x80000;
    private const int FlagFiring = 0x1;
    /// <summary>On the board: Kurt passes through it (+0x800) and it's no platform any more (−0x800100).</summary>
    private const int BoardFlagsSet = 0x80800;
    private const int BoardFlagsClear = 0x800100;
    private const string Board = "XSNOWB";
    private static readonly string[] Walkers = ["XD", "XD2"];
    private static readonly string[] Bombers = ["XE", "X_STRIKE"];
    /// <summary>The XD2: DUMMY at volume 0x2000 while steered, ALERT and the alarm while firing; Kurt
    /// takes 50 damage if it goes while he rides it.</summary>
    private const string WalkerSound = "DUMMY";
    private const string AlarmSound = "ALERT";
    private const int WalkerSoundVolume = 0x2000;
    private const int AlarmTicks = 10;
    private const int LostDamage = 50;

    private Snowboard? _board;
    private int _walkerSound;

    /// <summary>The object Kurt rides, if any.</summary>
    public MdkObject? Ridden { get; private set; }

    /// <summary>The bomber ride, while Kurt is on an XE.</summary>
    public Bomber? Bomber { get; private set; }

    /// <summary>Each tick, after Kurt moved: getting on, riding the XD2, getting off.</summary>
    public void Update()
    {
        if (Ridden == null)
        {
            TryMount();
            return;
        }

        var off = (Ridden.Flags & FlagRideable) == 0 || Ridden.Dead || (_board != null && runtime.Kurt.Health == 0);
        if (off)
        {
            Dismount();
            return;
        }

        if (_board == null && Bomber == null)
        {
            UpdateWalker();
        }
    }

    /// <summary>Each step, after the ticks: the XD2 is where Kurt is, Kurt where the XE is. Kurt
    /// moves each step, they each tick: else every other frame draws them apart (a ghost).</summary>
    public void Follow()
    {
        if (Ridden == null || _board != null)
        {
            return;
        }

        var kurt = runtime.Kurt;
        if (Bomber != null)
        {
            kurt.Feet = Ridden.Position;
            kurt.Yaw = Ridden.Yaw;
            return;
        }

        Ridden.Position = kurt.Feet;
        Ridden.Yaw = ScriptMath.Wrap360(kurt.Yaw);
    }

    /// <summary>Whether Kurt rides the snowboard.</summary>
    public bool OnBoard() => _board != null;

    /// <summary>Puts Kurt on a walker at once, as touching it does (tests: --ride).</summary>
    public void RideWalker(MdkObject obj)
    {
        obj.Flags |= FlagRideable;
        MountWalker(obj);
    }

    /// <summary>Whether an object isn't drawn: the XE once the view is inside it.</summary>
    public bool Hides(MdkObject obj) => Bomber != null && obj == Ridden && Bomber.HidesXe;

    /// <summary>Whether hits on Kurt go to the object he rides (the XD2, the XE).</summary>
    public bool TakesHits() => Ridden != null && _board == null;

    /// <summary>The ridden object went (0x43d734): Kurt is off, and but for the board he takes 50 damage.</summary>
    public void Lost(MdkObject obj)
    {
        if (obj != Ridden)
        {
            return;
        }

        var hurts = _board == null;
        Dismount();
        if (hurts)
        {
            runtime.Kurt.Hurt(LostDamage);
        }
    }

    private void TryMount()
    {
        var kurt = runtime.Kurt;
        if (kurt.Firing)
        {
            return;
        }

        foreach (var owner in kurt.Touched)
        {
            if (owner is not MdkObject obj || obj.Dead || (obj.Flags & FlagRideable) == 0)
            {
                continue;
            }

            var type = obj.TypeName.ToUpperInvariant();
            if (type == Board)
            {
                MountBoard(obj);
                return;
            }

            if (Walkers.Contains(type) && kurt.OnFloor)
            {
                MountWalker(obj);
                return;
            }

            if (Bombers.Contains(type))
            {
                MountBomber(obj);
                return;
            }
        }
    }

    private void MountBoard(MdkObject obj)
    {
        Ridden = obj;
        obj.Flags = (obj.Flags | BoardFlagsSet) & ~BoardFlagsClear;
        runtime.Kurt.LeaveSniper();
        _board = new Snowboard(runtime, obj);
        runtime.Kurt.Ride = _board.Update;
    }

    /// <summary>The XD2: Kurt goes where it is, facing its way, and walks it (without strafing or
    /// jumping). His moves ignore it (<c>damp_collide_move</c>), so he doesn't collide with it.</summary>
    private void MountWalker(MdkObject obj)
    {
        var kurt = runtime.Kurt;
        Ridden = obj;
        obj.Flags |= FlagRidden;
        kurt.StopFiring();
        kurt.Feet = obj.Position;
        kurt.Yaw = obj.Yaw;
        kurt.ForwardSpeed = 0f;
        kurt.StrafeSpeed = 0f;
        kurt.Walking = Kurt.Kurt.Walk.Riding;
        kurt.Visible = false;
    }

    /// <summary>The XE (also in the air): Kurt isn't drawn and goes with it.</summary>
    private void MountBomber(MdkObject obj)
    {
        var kurt = runtime.Kurt;
        Ridden = obj;
        kurt.StopFiring();
        kurt.Visible = false;
        Bomber = new Bomber(runtime, obj);
        kurt.Ride = Bomber.Update;
    }

    /// <summary>The XD2 goes with Kurt, animating while it moves; its sound while the keys are held;
    /// firing sounds the alarm.</summary>
    private void UpdateWalker()
    {
        var kurt = runtime.Kurt;
        var walker = Ridden!;
        walker.Position = runtime.KurtPosition;
        walker.Yaw = runtime.KurtYaw;
        var moving = kurt.ForwardSpeed != 0f || kurt.TurnRate != 0f;
        walker.AnimationRate = moving ? MdkObject.AnimationFps : 0f;

        var mixer = runtime.Mixer;
        if (kurt.Keys.Steer && !mixer.IsVoicePlaying(_walkerSound))
        {
            _walkerSound = mixer.Play(WalkerSound, SoundMixer.Start.New);
            mixer.SetVolume(_walkerSound, WalkerSoundVolume);
        }
        else if (!kurt.Keys.Steer)
        {
            mixer.StopVoice(_walkerSound);
            _walkerSound = 0;
        }

        if (kurt.Keys.Fire)
        {
            walker.Flags |= FlagFiring;
            mixer.Play(AlarmSound, SoundMixer.Start.Once);
            runtime.AlarmTicks = AlarmTicks;
        }
        else if ((walker.Flags & FlagFiring) != 0)
        {
            walker.Flags &= ~FlagFiring;
            runtime.AlarmTicks = 0;
        }
    }

    private void Dismount()
    {
        var kurt = runtime.Kurt;
        if (_board != null)
        {
            _board.GetOff();
            _board = null;
        }
        else if (Bomber != null)
        {
            // Kurt drops from where the XE is, and it shows again.
            Ridden!.Flags &= ~MdkObject.FlagNotSolid;
            kurt.GetOffBoard(0f, 0f);
            Bomber = null;
        }
        else if (Ridden != null)
        {
            runtime.Mixer.StopVoice(_walkerSound);
            _walkerSound = 0;
            kurt.JumpOff();
        }

        Ridden = null;
    }
}
