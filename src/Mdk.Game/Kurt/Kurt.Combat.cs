using System.Numerics;
using Mdk.Engine.Platform;

namespace Mdk.Game.Kurt;

/// <summary>Kurt's health, hits, knock-downs, death, chain gun and items (kurt.gd: hurt, knock_down,
/// _update_death, _update_firing, _update_muzzle, _update_items; godot-mdk docs/gameplay.md "Firing",
/// "Damage and death").
/// <code>
///   hit ──► health, red flash, knock damage (drains 2/s) ──5──► KNOCKED (K_BANG) ──► GET_UP (K_BFLIP)
///                                                               invulnerable 3 s
///   health 0 ──on the floor──► DEAD (K_BANG) ──skull fades in──► Died
///   fire ──► SHOT │ RUN_FIRE, chain gun loop, muzzle flash; the hits are the scripts runtime's
///   use  ──► THROW (K_SPWEP) ──frame 8──► ItemUsed │ in the air: ItemUsed at once
/// </code></summary>
public sealed partial class Kurt
{
    public const int MaxHealth = Inventory.MaxHealth;

    /// <summary>Red flash after hits (0x573b70): +25 per damage point, kept within 75-180; flashes
    /// fade by 4 per tick. Once Kurt is dead it's the skull's fade, rising by 2 per tick to 255.</summary>
    private const float HurtFlashPerDamage = 25f;
    private const float HurtFlashMin = 75f;
    private const float HurtFlashMax = 180f;
    private const float FlashFade = 4f * Ticks;
    private const float SkullFade = 2f * Ticks;
    private const float SkullEnd = 255f;

    /// <summary>Knocking down (damp_control 0x4664xx): the damage taken adds up (0x573b20, draining by
    /// 2 per second, at most 5); at 5 Kurt is knocked down and invulnerable for 3 seconds. In the air
    /// only within 13 units of a floor, and he's slammed onto it at 64 u/s.</summary>
    public const float KnockdownDamage = 5f;
    private const float KnockdownDrain = 2f;
    private const float KnockdownInvulnerability = 3f;
    private const float KnockdownFloorDistance = 13f;
    private const float KnockdownSlamSpeed = -64f;
    /// <summary>A push (push_kurt, 0x573c08) slows down by 0.1 u/tick per tick.</summary>
    private const float PushDrain = 0.1f * Ticks * Ticks;

    /// <summary>Damage on easy: 2/3 (at least 1); on hard: double (hurt_kurt 0x46a604).</summary>
    private const int EasyNumerator = 2;
    private const int EasyDenominator = 3;
    private const int HardFactor = 2;

    /// <summary>Frame of K_SPWEP at which the item leaves Kurt's hand (damp_animate).</summary>
    private const int ThrowFrame = 8;

    /// <summary>The chain gun's loop (0x46c3e4): MULTIFIRE, GATTFIRE with the super chain gun.</summary>
    private const string GunSound = "MULTIFIRE";
    private const string SuperGunSound = "GATTFIRE";

    /// <summary>K_MUZZF has 4 frames; a random offset of 0-4 pixels is added to the state's.</summary>
    private const int MuzzleFrameMask = 3;
    private const int MuzzleJitter = 5;

    /// <summary>Muzzle flash offsets (pixels) in the states that don't show the chain gun firing by
    /// themselves (damp_animate); SHOT and RUN_FIRE have the flash in their frames.</summary>
    private static readonly Dictionary<State, (int X, int Y)> MuzzleOffsets = new()
    {
        [State.Turn] = (0, 0),
        [State.Side] = (0, 0),
        [State.Fall] = (40, 6),
        [State.Jump] = (0, 0),
        [State.RunJump] = (0, -10),
        [State.Chute] = (20, 0),
    };

    /// <summary>On the board (state 201, K_SURF) the flash is drawn at (−42, 12).</summary>
    private const string SurfPose = "K_SURF";
    private static readonly (int X, int Y) SurfMuzzle = (-42, 12);

    /// <summary>A muzzle flash: the K_MUZZF frame, drawn behind Kurt with its hotspot at his plus the offset (pixels, y down).</summary>
    public readonly record struct MuzzleFlash(int Frame, int X, int Y);

    /// <summary>Health (0x574324).</summary>
    public int Health { get; private set; } = MaxHealth;
    /// <summary>Red flash (0-255), and the skull's fade once dead.</summary>
    public float HurtFlash { get; private set; }
    /// <summary>White flash of the screen (0x573b68: the nuke, screen_flash, explosions).</summary>
    public float WhiteFlash;
    /// <summary>Invulnerability time in seconds (0x573bd4).</summary>
    public float Invulnerable;
    /// <summary>Damage taken recently (0x573b20), see <see cref="KnockdownDamage"/>.</summary>
    public float KnockDamage;
    /// <summary>The chain gun fires (0x573a38).</summary>
    public bool Firing { get; private set; }
    public MuzzleFlash? Muzzle { get; private set; }
    /// <summary>Cutscenes: he stands still, maybe hidden.</summary>
    public bool Frozen;
    public bool Visible = true;
    public Inventory Inventory { get; } = new();

    /// <summary>The skull has faded in: the original loads the last saved game.</summary>
    public event Action? Died;
    /// <summary>The selected item leaves Kurt's hand (0x46ce78).</summary>
    public event Action? ItemUsed;
    /// <summary>"Use" pressed while the World's Most Interesting Bomb is out (0x43f258).</summary>
    public event Action? BombTriggered;
    /// <summary>Whether an item may be used now (no while a thrown item is active).</summary>
    public Func<Inventory.Item, bool>? CanUseItem;

    public bool Knocked => Current is State.Knocked or State.GetUp;

    private static readonly Key[] SlotKeys = [Key.Item1, Key.Item2, Key.Item3, Key.Item4, Key.Item5];

    /// <summary>Horizontal push (u/s) while knocked down.</summary>
    private Vector2 _push;
    private int _gunVoice;
    private bool _gunSuper;
    private float _muzzleTicks;
    private int _muzzleTick = -1;
    private int _muzzleFrame;
    private readonly Random _random = new();
    /// <summary>Keys held at the previous step, for presses.</summary>
    private readonly HashSet<Key> _held = [];

    /// <summary>Damage from aliens (hurt_kurt 0x46a604): none while invulnerable, knocked down or dead.</summary>
    public void Hurt(int damage)
    {
        if (Health == 0)
        {
            return;
        }

        if (Invulnerable > 0f || Current is State.Dead or State.Knocked or State.GetUp or State.HardLand)
        {
            KnockDamage = 0f;
            return;
        }

        Damage(damage);
    }

    /// <summary>Damage by the difficulty, its red flash, and the knock damage (0x46a77c).</summary>
    private void Damage(int damage)
    {
        damage = Scaled(damage, Inventory.Difficulty);
        if (damage > 0)
        {
            HurtFlash = Math.Clamp(HurtFlash + damage * HurtFlashPerDamage, HurtFlashMin, HurtFlashMax);
        }

        Health = Math.Max(Health - damage, 0);
        KnockDamage += damage;
    }

    private static int Scaled(int damage, Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => Math.Max(damage * EasyNumerator / EasyDenominator, 1),
        Difficulty.Hard => damage * HardFactor,
        _ => damage,
    };

    /// <summary>Takes a pickup (damp_collect_pickups); returns its sound, or "" when it isn't taken.</summary>
    public string Collect(string pickup)
    {
        var health = Health;
        var sound = Inventory.Collect(pickup, ref health);
        Health = health;
        return sound;
    }

    /// <summary>Starts with another health than 100 (the fall's).</summary>
    public void SetHealth(int health) => Health = Math.Max(health, 0);

    /// <summary>Knocks Kurt down (state 901): he stops firing, falls and gets up (K_BANG, K_BFLIP), and is
    /// invulnerable for 3 seconds.</summary>
    public void KnockDown(Vector2 push)
    {
        KnockDamage = 0f;
        Invulnerable = KnockdownInvulnerability;
        _push += push;
        StopFiring();
        ChuteOpen = false;
        SetState(State.Knocked);
    }

    public void StopFiring()
    {
        LeaveSniper();
        Firing = false;
        mixer.StopVoice(_gunVoice);
        _gunVoice = 0;
        Muzzle = null;
    }

    /// <summary>Back on his feet with full health (after <see cref="Died"/>).</summary>
    public void Revive()
    {
        Health = MaxHealth;
        HurtFlash = 0f;
        KnockDamage = 0f;
        _push = Vector2.Zero;
        SetState(State.Still);
    }

    private void FadeFlashes(float delta)
    {
        HurtFlash = MathF.Max(HurtFlash - FlashFade * delta, 0f);
        WhiteFlash = MathF.Max(WhiteFlash - FlashFade * delta, 0f);
    }

    private void DrainPush(float delta) =>
        _push = new Vector2(MoveToward(_push.X, 0f, PushDrain * delta), MoveToward(_push.Y, 0f, PushDrain * delta));

    /// <summary>At 5 damage taken recently Kurt is knocked down: on a floor, or in the air above one.</summary>
    private void UpdateKnockDamage(float delta)
    {
        if (Health > 0 && KnockDamage >= KnockdownDamage && !Knocked)
        {
            var knocked = OnFloor;
            if (!knocked && space.Floor(Feet, Feet - new Vector3(0f, 0f, KnockdownFloorDistance), out _))
            {
                knocked = true;
                VerticalSpeed = MathF.Min(VerticalSpeed, KnockdownSlamSpeed);
            }

            if (knocked)
            {
                KnockDown(Vector2.Zero);
                return;
            }
        }

        KnockDamage = Math.Clamp(KnockDamage, 0f, KnockdownDamage);
        KnockDamage = MathF.Max(KnockDamage - KnockdownDrain * delta, 0f);
    }

    /// <summary>Dead Kurt falls to the floor and lies still while the skull fades in, then
    /// <see cref="Died"/>.</summary>
    private void UpdateDeath(float delta)
    {
        if (HurtFlash > SkullEnd)
        {
            return;
        }

        ForwardSpeed = 0f;
        StrafeSpeed = 0f;
        VerticalSpeed = OnFloor ? 0f : MathF.Max(VerticalSpeed - Gravity * delta, -MaxFallSpeed);
        UpdateUpdraft(delta);
        Fall(delta);
        StateTime += delta;
        AnimationFrame = MathF.Min(AnimationFrame + Ticks * delta, frameCount(State.Dead) - 1);
        HurtFlash += SkullFade * delta;
        if (HurtFlash > SkullEnd)
        {
            Died?.Invoke();
        }
    }

    /// <summary>Whether a key went down since the last step.</summary>
    private bool Pressed(Input input, Key key)
    {
        if (!input.IsDown(key))
        {
            _held.Remove(key);
            return false;
        }

        return _held.Add(key);
    }

    /// <summary>Item keys (damp_move 0x46ca38): select a slot (1-5, next, previous) or use the
    /// selected item: on the floor Kurt throws it, in the air it's used at once.</summary>
    private void UpdateItems(Input input)
    {
        if (Pressed(input, Key.ItemNext))
        {
            Inventory.SelectNext(1);
        }

        if (Pressed(input, Key.ItemPrevious))
        {
            Inventory.SelectNext(-1);
        }

        for (var i = 0; i < SlotKeys.Length; i++)
        {
            if (Pressed(input, SlotKeys[i]))
            {
                Inventory.Select(i);
            }
        }

        var item = Inventory.SelectedItem;
        if (!Pressed(input, Key.UseItem) || Current is State.Throw or State.Dead || Knocked
            || item is Inventory.Item.None or Inventory.Item.SuperChainGun)
        {
            return;
        }

        // The bomb is out (or another item): "use" sets the bomb off.
        if (CanUseItem != null && !CanUseItem(item))
        {
            BombTriggered?.Invoke();
            return;
        }

        if (OnFloor)
        {
            SetState(State.Throw);
        }
        else if (Current is State.Jump or State.RunJump or State.Fall or State.Chute)
        {
            ItemUsed?.Invoke();
        }
    }

    /// <summary>Holding fire fires the chain gun (damp_move): its sound loops; the hits are the
    /// scripts runtime's (fire_chain_gun).</summary>
    private void UpdateFiring(Input input)
    {
        var fire = input.IsDown(Key.Fire) && Health > 0 && Current != State.Throw && !Knocked;
        var superGun = Inventory.SuperChainGun > 0;
        if (fire == Firing && (!Firing || superGun == _gunSuper))
        {
            return;
        }

        StopFiring();
        Firing = fire;
        _gunSuper = superGun;
        if (Firing)
        {
            _gunVoice = mixer.PlayLooped(superGun ? SuperGunSound : GunSound);
        }
    }

    /// <summary>Every other tick while firing, a random muzzle flash frame is drawn behind Kurt in the
    /// states whose frames don't show it: the next frame is (previous + 1-3) &amp; 3.</summary>
    private void UpdateMuzzle(float delta)
    {
        _muzzleTicks += Ticks * delta;
        var tick = (int)_muzzleTicks;
        if (!Firing || MuzzleOffset() is not { } offset || (tick & 1) == 0)
        {
            Muzzle = null;
            return;
        }

        if (tick == _muzzleTick)
        {
            return;
        }

        _muzzleTick = tick;
        _muzzleFrame = (_muzzleFrame + _random.Next(MuzzleFrameMask) + 1) & MuzzleFrameMask;
        Muzzle = new MuzzleFlash(_muzzleFrame, offset.X + _random.Next(MuzzleJitter), offset.Y + _random.Next(MuzzleJitter));
    }

    /// <summary>The flash's offset in the current state or pose, or null when the frames show it.</summary>
    private (int X, int Y)? MuzzleOffset()
    {
        if (Pose?.Name == SurfPose)
        {
            return SurfMuzzle;
        }

        return MuzzleOffsets.TryGetValue(Current, out var offset) ? offset : null;
    }

    /// <summary>The states that override walking: death, knock-down, throwing. Returns whether one did.</summary>
    private bool UpdateCombatState(float delta)
    {
        if (Health == 0 && OnFloor)
        {
            // Dead: the death animation once on the floor (state 1002).
            StopFiring();
            HurtFlash = 0f;
            SetState(State.Dead);
            return true;
        }

        if (Current == State.Knocked)
        {
            // K_BANG, then K_BFLIP, one frame per tick; the push stops when he flips back up.
            AnimationFrame += Ticks * delta;
            var bang = frameCount(State.Knocked);
            if (AnimationFrame < bang)
            {
                return true;
            }

            var flip = AnimationFrame - bang;
            _push = Vector2.Zero;
            SetState(State.GetUp);
            AnimationFrame = flip;
            return true;
        }

        if (Current == State.GetUp)
        {
            AnimationFrame += Ticks * delta;
            if (AnimationFrame < frameCount(State.GetUp))
            {
                return true;
            }

            SetState(State.Still);
            return false;
        }

        if (Current != State.Throw || !OnFloor || AnimationFrame >= frameCount(State.Throw) - 1)
        {
            return false;
        }

        // Kurt stands still while throwing; the item leaves his hand on frame 8.
        var previous = (int)AnimationFrame;
        AnimationFrame += Ticks * delta;
        if (previous < ThrowFrame && (int)AnimationFrame >= ThrowFrame)
        {
            ItemUsed?.Invoke();
        }

        return true;
    }
}
