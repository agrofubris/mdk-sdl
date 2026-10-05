using System.Numerics;
using Mdk.Formats;
using Mdk.Game.Audio;
using Mdk.Game.Objects;
using static Mdk.Game.Scripts.ScriptMath;
using Item = Mdk.Game.Kurt.Inventory.Item;
using KurtState = Mdk.Game.Kurt.Kurt.State;

namespace Mdk.Game.Scripts;

/// <summary>Kurt's thrown items (0x46ce78 throws, 0x43deac updates) and blasts (0x463a94). A port of
/// godot-mdk's <c>items.gd</c> (see its docs/engine.md "Kurt's items").
/// <code>
///   use ──► thrown (pickup model, 25 u/s forward, 15 up) ──lands, hits, times out──► active:
///     grenade: blast │ decoy: walks, aliens aim at it │ bomb: spins, then blast │ tornado: twisters
///     mortar: thumps │ key: the nuke │ seal, bone: pickups again
/// </code></summary>
public sealed class Items(ScriptRuntime runtime, Bni sprites)
{
    /// <summary>Kurt's thrown items and effects (0x1000), and active ones (0x4000).</summary>
    public const int FlagThrown = 0x1000;
    public const int FlagActive = 0x4000;
    /// <summary>A thrown item: Kurt's effect, not a target, not solid for Kurt, gravity and collisions.</summary>
    private const int ThrownFlags = 0x818a6;
    /// <summary>Contact flag of an item that hit an object (0x43eb48).</summary>
    private const int ContactHitObject = 0x10;
    /// <summary>The XE's bombs (rides.gd's bomber): a grenade that noses down by 45°/s to -90°.</summary>
    public const int KindBomb = 0x81;
    private const float BombPitchRate = 45f;
    private const float BombPitchMin = -90f;

    /// <summary>Pickup models of the item types.</summary>
    private static readonly string[] Models = ["", "SW_DUMMY", "SW_INTER", "SW_TWIST", "SW_THUMP", "SW_HBOMB", "SW_GATT", "SW_KEY", "SW_SEAL", "SW_SBONE"];
    /// <summary>Thrown items fly this fast (grenades 3 times faster), 15 u/s upwards; 4 units above
    /// Kurt's feet; scale 0.1 growing by 3 per second.</summary>
    private const float ThrowSpeed = 25f;
    private const float ThrowUpSpeed = 15f;
    private const float GrenadeSpeedFactor = 3f;
    private const float ThrowHeight = 4f;
    private const float ThrowScale = 0.1f;
    private const float GrowRate = 3f;
    /// <summary>Flight ticks: 150, seals and bones 750.</summary>
    private const int FlightTicks = 150;
    private const int LongFlightTicks = 750;
    private const float SealRollRate = 30f;

    /// <summary>Hit types (obj+0x21d, if_hit_weapon).</summary>
    private const int HitMortar = -3;
    private const int HitGrenade = -7;
    private const int HitNuke = -8;
    private const int HitBomb = -9;
    /// <summary>Blast targets: Kurt, objects, triangle groups.</summary>
    private const int TargetKurt = 1;
    private const int TargetObjects = 2;
    private const int TargetGroups = 4;
    private const int TargetWorld = TargetObjects | TargetGroups;

    /// <summary>Grenades: 150 within 40 (75 on Kurt), explosion ×2.</summary>
    private const int GrenadeDamage = 150;
    private const int GrenadeKurtDamage = 75;
    private const float GrenadeRadius = 40f;
    private const float GrenadeExplosion = 2f;
    /// <summary>The decoy walks forward at 5 u/s for 450 ticks.</summary>
    private const int DecoyTicks = 450;
    private const float DecoySpeed = 5f;
    /// <summary>The bomb spins at 235°/s for 600 ticks, then blasts 450 within 80 (67 on Kurt), explosion ×3.</summary>
    private const int BombTicks = 600;
    private const float BombSpin = 235f;
    private const int BombDamage = 450;
    private const int BombKurtDamage = 67;
    private const float BombRadius = 80f;
    private const float BigExplosion = 3f;
    /// <summary>The tornado spins at 360°/s for 60 ticks, a twister at once and every 15 ticks from 45 left.</summary>
    private const int TornadoTicks = 60;
    private const int TornadoFirstTwister = 45;
    private const int TornadoTwisterEvery = 15;
    private const float TornadoSpin = 360f;
    /// <summary>The nuke: 900 ticks, from frame 1; white from frame 70; doors within 50 blown open;
    /// blast of 200 within 60 (19 on Kurt).</summary>
    private const int NukeTicks = 900;
    private const int NukeWhiteFrame = 70;
    private const float NukeShake = 3f;
    private const float NukeDoorRangeSquared = 2500f;
    private const int NukeDoorOpen = 0x80;
    private const int DoorStateKeep = 0x1F;
    private const int NukeDamage = 200;
    private const int NukeKurtDamage = 19;
    private const float NukeRadius = 60f;
    /// <summary>The mortar thumps on these frames of SW_THUMP (0x491e4c): 4 damage to the arena's
    /// aliens (+5 u/s up when standing), the screen shakes; from the 4th Kurt is knocked down.</summary>
    private static readonly int[] MortarThumps = [28, 54, 64, 71, 77, 82, 87, 92, 97, 102, 107, 112, 117, 122, 127];
    private static readonly string[] MortarSpared = ["XE", "XF"];
    private const int MortarActiveTicks = 30;
    private const int MortarDamage = 4;
    private const float MortarLift = 5f;
    private const float MortarShake = 5f;
    private const int MortarKnockThump = 4;
    private const int MortarIgnored = 0x1030;
    /// <summary>Seals and bones become pickups again after 150 ticks (750 - 600); seals then shrink.</summary>
    private const int SealPickupTicks = 601;
    private const float SealShrink = 0.9f;
    private const float SealGone = 0.1f;
    /// <summary>Script flags of seals and bones: something holds it, it has landed.</summary>
    private const int SealHeld = 1;
    private const int SealLanded = 2;
    /// <summary>Blasts lift their centre 1 unit off the floor; Kurt's damage is at most 15, at double the distance.</summary>
    private const float BlastLift = 1f;
    private const int BlastKurtMax = 15;
    private const float BlastKurtDistance = 2f;
    /// <summary>The decoy's and bomb's animations, the nuke's model and the seal's landing.</summary>
    private const string DecoyThrown = "SW_DUM_I";
    private const string DecoyWalk = "SW_DUM_M";
    private const string BombAnimation = "SW_INTER";
    private const string MortarAnimation = "SW_THUMP";
    private const string Nuke = "SW_NUKE";
    private const string SealLanding = "XMT_LAND";

    /// <summary>The World's Most Interesting Bomb (0x573c24), and the decoy (0x573c20), while out.</summary>
    public MdkObject? Bomb;
    public MdkObject? Decoy;

    private readonly Dictionary<string, ModelAnimation?> _animations = [];
    private readonly List<Twister> _twisters = [];

    public IReadOnlyList<Twister> Twisters => _twisters;

    /// <summary>An item that goes before it's done (its arena put away).</summary>
    public void Forget(MdkObject obj)
    {
        if (obj == Bomb)
        {
            Bomb = null;
        }

        if (obj == Decoy)
        {
            Decoy = null;
        }
    }

    /// <summary>A model animation of the items (TRAVSPRT.BNI: SW_INTER, SW_DUM_I, H150_R...).</summary>
    public ModelAnimation? GetAnimation(string name)
    {
        if (!_animations.TryGetValue(name, out var animation))
        {
            animation = _animations[name] = sprites.Has(name) ? ModelAnimation.Parse(name, sprites.Bytes, sprites.Entries[name].Offset) : null;
        }

        return animation;
    }

    /// <summary>Whether Kurt may use an item: one thrown item at a time (decoys, seals and bones
    /// excepted), and none while the bomb is out ("use" then sets it off).</summary>
    public bool CanUse(Item item)
    {
        if (Bomb is { Dead: false })
        {
            return false;
        }

        var thrown = runtime.Objects.Any(o => !o.Dead && o.ThrownKind > 0 && !IsDecoyKind((Item)o.ThrownKind));
        return !thrown || IsDecoyKind(item);
    }

    private static bool IsDecoyKind(Item item) => item is Item.Dummy or Item.Seal or Item.SuperBone;

    /// <summary>"Use" while the bomb is out: it goes off at the end of its animation (0x43f258).</summary>
    public void TriggerBomb()
    {
        if (Bomb is { Dead: false } bomb && bomb.AnimationEndFrame >= 0)
        {
            bomb.AnimationEndFrame = MdkObject.NoHoldFrame;
        }
    }

    /// <summary>Throws the selected item (0x46ce78): its pickup model leaves Kurt 4 units up, along his yaw.</summary>
    public void UseItem()
    {
        var kurt = runtime.Kurt;
        var item = kurt.Inventory.SelectedItem;
        if (item == Item.None || runtime.CurrentArena.Length == 0)
        {
            return;
        }

        var yaw = Wrap360(kurt.Yaw);
        var controller = runtime.GetArenaState(runtime.CurrentArena).Controller;
        var thrown = runtime.Spawn(controller, Models[(int)item], kurt.Feet + new Vector3(0f, 0f, ThrowHeight), yaw, -1, 0, ScriptRuntime.Spawning.Plain);
        if (thrown == null)
        {
            return;
        }

        thrown.Flags |= ThrownFlags;
        thrown.ThrownKind = (int)item;
        thrown.ItemTicks = item is Item.Seal or Item.SuperBone ? LongFlightTicks : FlightTicks;
        thrown.Friction = 0f;
        thrown.Scale = ThrowScale;
        var direction = FromAngle(yaw) * (item == Item.Grenade ? ThrowSpeed * GrenadeSpeedFactor : ThrowSpeed);
        thrown.Velocity = new Vector3(direction, kurt.Current == KurtState.Chute ? 0f : ThrowUpSpeed);
        if (item == Item.Dummy)
        {
            thrown.RestartAnimation(GetAnimation(DecoyThrown), MdkObject.Looping.Once);
            thrown.AnimationEndFrame = 0;
        }

        kurt.Inventory.Consume();
    }

    /// <summary>A thrown item after it moved (0x43deac): it flies until it lands, hits or times out.</summary>
    public void UpdateThrown(MdkObject obj)
    {
        // A seal or bone Kurt took back shrinks away like a pickup.
        if ((obj.Flags & MdkObject.FlagCollected) != 0)
        {
            runtime.Behaviors.UpdatePickup(obj);
            return;
        }

        if ((obj.Flags & FlagActive) != 0)
        {
            UpdateActive(obj);
            return;
        }

        obj.Scale = MathF.Min(obj.Scale + ScriptRuntime.Tick * GrowRate, 1f);
        if (obj.ThrownKind == (int)Item.Seal)
        {
            obj.Roll = Wrap360(obj.Roll + ScriptRuntime.Tick * SealRollRate);
        }

        if (obj.ThrownKind == KindBomb)
        {
            obj.Pitch = MathF.Max(obj.Pitch - BombPitchRate * ScriptRuntime.Tick, BombPitchMin);
        }

        if (HitsObject(obj))
        {
            obj.ContactFlags |= ContactHitObject;
        }

        obj.ItemTicks--;
        const int Stopped = MdkObject.ContactCollided | MdkObject.ContactFloor | ContactHitObject;
        if ((obj.ContactFlags & Stopped) == 0 && obj.ItemTicks > 0)
        {
            return;
        }

        // The mortar only works on a floor.
        if (obj.ThrownKind == (int)Item.Mortar && (obj.ContactFlags & MdkObject.ContactFloor) == 0)
        {
            return;
        }

        Activate(obj);
    }

    /// <summary>Grenades stop on any alien, the others only on objects with flag 0x1000000 (0x43eb48).</summary>
    private bool HitsObject(MdkObject obj)
    {
        const int Excluded = 0x810;
        const int BombExcluded = 0x830;
        const int Catches = 0x1000000;
        var excluded = obj.ThrownKind == KindBomb ? BombExcluded : Excluded;
        var needed = obj.ThrownKind is (int)Item.Grenade or KindBomb ? 0 : Catches;
        foreach (var other in runtime.Objects)
        {
            if (other == obj || other.Dead || other.Health == 0 || other.Arena != obj.Arena || (other.Flags & excluded) != 0
                || (needed != 0 && (other.Flags & needed) == 0))
            {
                continue;
            }

            var bounds = runtime.GetWorldBounds(other);
            if (bounds.Contains(obj.Position) || bounds.SegmentEntry(obj.PreviousPosition, obj.Position) != null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>An item landed (or hit something, or its time ran out): it stops and does its thing.</summary>
    private void Activate(MdkObject obj)
    {
        const int Moving = MdkObject.FlagGravity | MdkObject.FlagCollides;
        obj.Velocity = Vector3.Zero;
        obj.Flags = (obj.Flags & ~Moving) | FlagActive;
        obj.ContactFlags &= ~(MdkObject.ContactCollided | MdkObject.ContactFloor | ContactHitObject);
        switch (obj.ThrownKind)
        {
            case (int)Item.Grenade or KindBomb:
                Explode(obj, GrenadeDamage, GrenadeKurtDamage, GrenadeRadius, HitGrenade, obj, GrenadeExplosion);
                break;
            case (int)Item.Dummy:
            {
                // The decoy walks forward for 15 seconds, and aliens aim at it.
                obj.ItemTicks = DecoyTicks;
                obj.Velocity = new Vector3(FromAngle(obj.Yaw) * DecoySpeed, 0f);
                obj.Flags |= Moving;
                obj.RestartAnimation(GetAnimation(DecoyWalk), MdkObject.Looping.Loop);
                runtime.SetLoopSound(obj, "DUMMY");
                Decoy = obj;
                break;
            }
            case (int)Item.InterestingBomb:
                // It spins on its first frame for 20 seconds (or until Kurt sets it off), then plays
                // its animation and blows up.
                obj.RestartAnimation(GetAnimation(BombAnimation), MdkObject.Looping.Once);
                obj.AnimationEndFrame = 0;
                obj.ItemTicks = BombTicks;
                Bomb = obj;
                break;
            case (int)Item.Tornado:
                _twisters.Add(new Twister(runtime, obj.Arena, obj.Position, 0f));
                obj.ItemTicks = TornadoTicks;
                obj.Parameter = TornadoFirstTwister;
                runtime.Mixer.Play("TORNADO", SoundMixer.Start.Once);
                break;
            case (int)Item.Mortar:
                obj.RestartAnimation(GetAnimation(MortarAnimation), MdkObject.Looping.Once);
                obj.ItemTicks = MortarActiveTicks;
                obj.Parameter = 0f;
                UpdateMortar(obj);
                break;
            case (int)Item.Key:
                StartNuke(obj);
                break;
        }
    }

    /// <summary>A grenade's or the bomb's blast: on the world, then on Kurt, an explosion and its sound.</summary>
    private void Explode(MdkObject obj, int damage, int kurtDamage, float radius, int hitType, MdkObject? source, float scale)
    {
        var center = obj.Position;
        Blast(center, damage, radius, TargetWorld, hitType, source);
        Blast(center, kurtDamage, radius, TargetKurt, hitType, source);
        runtime.SpawnExplosion(obj.Arena, center, scale);
        runtime.PlaySoundAt(ScriptRuntime.ExplodeSound, center);
        runtime.Remove(obj);
    }

    /// <summary>The "key" becomes the nuke (SW_NUKE): a white flash, its animation, then the blast.</summary>
    private void StartNuke(MdkObject obj)
    {
        var model = runtime.FindModel(obj.Arena, Nuke);
        if (model != null)
        {
            obj.TypeName = Nuke;
            obj.Model = model;
        }

        obj.ItemTicks = NukeTicks;
        obj.RestartAnimation(GetAnimation(Nuke), MdkObject.Looping.Once);
        obj.AnimationTime = 1f;
        runtime.Kurt.WhiteFlash = MathF.Max(runtime.Kurt.WhiteFlash, byte.MaxValue);

        // A loop without position, until the nuke goes.
        obj.LoopSound = runtime.Mixer.Play("NUKE");
    }

    /// <summary>Active items (0x43e860 decoy, 0x43f18c bomb, 0x43e690 mortar, 0x43efcc nuke, 0x43e980
    /// seal, 0x43ea84 bone, 0x43ee98 tornado).</summary>
    private void UpdateActive(MdkObject obj)
    {
        switch (obj.ThrownKind)
        {
            case (int)Item.Tornado:
                UpdateTornado(obj);
                break;
            case (int)Item.Mortar:
                UpdateMortar(obj);
                break;
            case (int)Item.Key:
                UpdateNuke(obj);
                break;
            case (int)Item.Seal or (int)Item.SuperBone:
                UpdateSeal(obj);
                break;
            case (int)Item.InterestingBomb:
                UpdateBomb(obj);
                break;
            default:
                // The decoy walks until its time is up.
                obj.ItemTicks--;
                if (obj.ItemTicks > 0)
                {
                    return;
                }

                Forget(obj);
                runtime.Remove(obj);
                break;
        }
    }

    private void UpdateTornado(MdkObject obj)
    {
        obj.Yaw = Wrap360(obj.Yaw + ScriptRuntime.Tick * TornadoSpin);
        obj.ItemTicks--;
        if (obj.ItemTicks < obj.Parameter)
        {
            obj.Parameter -= TornadoTwisterEvery;
            _twisters.Add(new Twister(runtime, obj.Arena, obj.Position, obj.Yaw));
        }

        if (obj.ItemTicks <= 0)
        {
            runtime.Kill(obj);
        }
    }

    private void UpdateBomb(MdkObject obj)
    {
        Bomb = obj;
        if (obj.AnimationEndFrame == 0)
        {
            obj.Yaw = Wrap360(obj.Yaw + ScriptRuntime.Tick * BombSpin);
            obj.ItemTicks--;
            if (obj.ItemTicks < 1)
            {
                obj.AnimationEndFrame = MdkObject.NoHoldFrame;
            }

            return;
        }

        if (!obj.IsAnimationDone)
        {
            return;
        }

        Explode(obj, BombDamage, BombKurtDamage, BombRadius, HitBomb, null, BigExplosion);
        Bomb = null;
    }

    /// <summary>Moves the twisters of Kurt's arena (one tick).</summary>
    public void UpdateTwisters()
    {
        foreach (var twister in _twisters.ToList())
        {
            if (twister.Arena != runtime.CurrentArena || twister.Tick(_twisters))
            {
                continue;
            }

            _twisters.Remove(twister);
        }
    }

    /// <summary>The mortar (0x43e690) pounds the ground on the frames of <see cref="MortarThumps"/>: the
    /// screen shakes and every alien of the arena (flying ones spared) loses 4 health, thrown up if it
    /// stands. From the fourth thump on, Kurt is knocked down if he stands on a floor. It blows up at the end.</summary>
    private void UpdateMortar(MdkObject obj)
    {
        if (obj.Animation == null || obj.IsAnimationDone)
        {
            runtime.Kill(obj);
            return;
        }

        var thump = (int)obj.Parameter;
        if (thump >= MortarThumps.Length || obj.AnimationFrame < MortarThumps[thump])
        {
            return;
        }

        thump++;
        obj.Parameter = thump;
        if (thump >= MortarKnockThump && runtime.Kurt.OnFloor)
        {
            runtime.Kurt.KnockDamage = Mdk.Game.Kurt.Kurt.KnockdownDamage;
        }

        runtime.RaiseShake(MortarShake);
        foreach (var other in runtime.Objects.ToList())
        {
            if (other == obj || other.Dead || other.Arena != obj.Arena || (other.Flags & MortarIgnored) != 0
                || MortarSpared.Contains(other.TypeName.ToUpperInvariant()))
            {
                continue;
            }

            if ((other.ContactFlags & MdkObject.ContactFloor) != 0)
            {
                other.Velocity.Z += MortarLift;
            }

            var direction = Heading(runtime.KurtPosition, runtime.GetWorldBounds(other).Center());
            if (other.Health < ScriptRuntime.Indestructible)
            {
                other.Health -= MortarDamage;
            }

            other.HitEvent = -1;
            other.HitType = HitMortar;
            other.HitDirection = direction;
            if (other.Health <= 0)
            {
                runtime.Kill(other, direction + HalfTurn);
            }
        }
    }

    /// <summary>The nuke (0x43efcc) shakes the screen while its animation plays (turning white towards
    /// the end), then blows open the doors nearby and blasts everything within 60 units.</summary>
    private void UpdateNuke(MdkObject obj)
    {
        if (obj.Animation != null && !obj.IsAnimationDone)
        {
            runtime.RaiseShake(NukeShake);
            if (obj.AnimationFrame > NukeWhiteFrame)
            {
                var flash = MathF.Round((obj.AnimationFrame - NukeWhiteFrame) * (float)byte.MaxValue
                    / Math.Max(obj.Animation.FrameCount - NukeWhiteFrame, 1));
                runtime.Kurt.WhiteFlash = MathF.Max(runtime.Kurt.WhiteFlash, flash);
            }

            return;
        }

        var center = obj.Position;
        foreach (var other in runtime.Objects)
        {
            if (!other.Dead && (other.Flags & MdkObject.FlagDoor) != 0 && Vector3.DistanceSquared(other.Position, center) < NukeDoorRangeSquared)
            {
                other.DoorState = (other.DoorState & DoorStateKeep) | NukeDoorOpen;
            }
        }

        Blast(center, NukeDamage, NukeRadius, TargetWorld, HitNuke, null);
        Blast(center, NukeKurtDamage, NukeRadius, TargetKurt, HitNuke, null);
        runtime.PlaySoundAt(ScriptRuntime.ExplodeSound, center);
        runtime.SparkFire(obj.Arena, center);
        runtime.SpawnExplosion(obj.Arena, center, BigExplosion);
        runtime.Remove(obj);
    }

    /// <summary>A thrown seal or bone (0x43e980, 0x43ea84) plays XMT_LAND once it lands. Unless something
    /// has got hold of it (script flag 1), Kurt can take it back after 5 seconds; a seal shrinks away after 25.</summary>
    private void UpdateSeal(MdkObject obj)
    {
        if ((obj.ScriptFlags & SealHeld) == 0)
        {
            if (obj.ItemTicks < SealPickupTicks)
            {
                obj.Flags |= MdkObject.FlagPickup;
            }

            if (obj.ThrownKind == (int)Item.Seal)
            {
                obj.ItemTicks--;
                if (obj.ItemTicks < 1)
                {
                    obj.Scale *= SealShrink;
                    if (obj.Scale < SealGone)
                    {
                        runtime.Kill(obj);
                    }

                    return;
                }
            }
            else if (obj.ItemTicks >= SealPickupTicks)
            {
                obj.ItemTicks--;
            }
        }
        else
        {
            obj.Scale = 1f;
            obj.Flags &= ~MdkObject.FlagPickup;
        }

        if (obj.Animation != null || (obj.ScriptFlags & SealLanded) != 0)
        {
            return;
        }

        obj.Roll = 0f;
        obj.ScriptFlags |= SealLanded;
        obj.Flags |= MdkObject.FlagGravity | MdkObject.FlagCollides;
        obj.Flags &= ~MdkObject.FlagLoop;
        obj.RestartAnimation(runtime.FindArenaAnimation(obj.Arena, SealLanding), MdkObject.Looping.Once);
    }

    /// <summary>Whether a blast's kills count for the statistics.</summary>
    public enum Kills { Counted, Ignored }

    /// <summary>A blast (0x463a94) of <paramref name="damage"/> within <paramref name="radius"/>:
    /// <paramref name="targets"/> 1 Kurt, 2 objects, 4 triangle groups. Damage falls off with the
    /// distance to a target's box (less half its size), and walls stop it.</summary>
    public void Blast(Vector3 center, int damage, float radius, int targets, int hitType, MdkObject? source, Kills kills = Kills.Counted)
    {
        if ((targets & TargetObjects) != 0)
        {
            BlastObjects(center, damage, radius, hitType, source, kills);
        }

        if ((targets & TargetKurt) != 0)
        {
            BlastKurt(center, damage, radius);
        }

        if ((targets & TargetGroups) != 0)
        {
            BlastGroups(center, damage, radius, hitType, kills == Kills.Counted ? ScriptRuntime.HitBlast : ScriptRuntime.HitOtherBlast);
        }
    }

    private void BlastObjects(Vector3 center, int damage, float radius, int hitType, MdkObject? source, Kills kills)
    {
        const int BlastEvent = -2;
        foreach (var obj in runtime.Objects.ToList())
        {
            if (obj.Dead || obj.Arena != runtime.CurrentArena || obj.Health == 0
                || (obj.Flags & (MdkObject.FlagNotSolid | MdkObject.FlagNotTarget)) != 0)
            {
                continue;
            }

            var (best, distance, point, hitEvent) = BlastWeakParts(obj, center, damage, radius);
            if (obj == source)
            {
                // The grenade itself takes it all.
                (best, distance, point) = (damage, 0f, runtime.GetWorldBounds(obj).Center());
            }
            else
            {
                var box = runtime.GetWorldBounds(obj);
                var (amount, at) = BlastDamage(box, center, damage, radius);
                if (amount > best)
                {
                    (best, distance, point) = (amount, at, box.Center());
                }
            }

            if (best <= 0 || distance > obj.BlastRange)
            {
                continue;
            }

            var direction = Heading(center, point);
            if (obj.Health < ScriptRuntime.Indestructible)
            {
                obj.Health -= best;
            }

            obj.HitEvent = hitEvent == 0 ? BlastEvent : hitEvent;
            obj.HitType = hitType;
            obj.HitDirection = direction;
            if (obj.Health >= 1)
            {
                continue;
            }

            if (kills == Kills.Counted)
            {
                runtime.Stats.CountEnemy(obj.TypeName, GameStats.Kill.Killed);
            }

            runtime.Kill(obj, direction + HalfTurn);
        }
    }

    /// <summary>A blast on an object's weak parts: the best damage, its distance and point, and the
    /// event of a part it destroyed (part + 1, or 0).</summary>
    private (int Best, float Distance, Vector3 Point, int Event) BlastWeakParts(MdkObject obj, Vector3 center, int damage, float radius)
    {
        var result = (Best: 0, Distance: 0f, Point: obj.Position, Event: 0);
        if ((obj.Flags & MdkObject.FlagWeakParts) == 0 || obj.Model == null)
        {
            return result;
        }

        var partBounds = obj.PartBounds();
        for (var i = 0; i < obj.Model.PartList.Count; i++)
        {
            if ((obj.HiddenParts & (1 << i)) != 0 || !ScriptRuntime.IsWeakPart(obj, i) || i >= obj.PartHealth.Length
                || partBounds[i] is not { } part)
            {
                continue;
            }

            var box = runtime.GetWorldBounds(obj, part);
            var (amount, distance) = BlastDamage(box, center, damage, radius);
            if (amount <= 0)
            {
                continue;
            }

            if (amount > result.Best)
            {
                result = (amount, distance, box.Center(), result.Event);
            }

            obj.PartHealth[i] -= amount;
            if (obj.PartHealth[i] < 1)
            {
                obj.PartHealth[i] = 0;
                result.Event = i + 1;
            }
        }

        return result;
    }

    /// <summary>Kurt is hurt at double the distance (walls stop it), at most 15; blasts knock him down
    /// twice as easily.</summary>
    private void BlastKurt(Vector3 center, int damage, float radius)
    {
        var kurtPoint = runtime.KurtPosition + new Vector3(0f, 0f, BlastLift);
        var distance = Vector3.Distance(center, kurtPoint);
        if (runtime.Raycast(center + new Vector3(0f, 0f, BlastLift), kurtPoint) != null)
        {
            distance = radius + 1f;
        }

        distance *= BlastKurtDistance;
        if (distance >= radius)
        {
            return;
        }

        runtime.Kurt.Hurt(Math.Min(Round(damage * (1f - distance / radius)), BlastKurtMax));
        runtime.Kurt.KnockDamage *= 2f;
    }

    /// <summary>A blast on the triangle groups that react to hits: each gets one hit, on the first of
    /// its triangles within the radius that the blast reaches, of the damage less the falloff.</summary>
    private void BlastGroups(Vector3 center, int damage, float radius, int hitType, int kind)
    {
        var arena = runtime.CurrentArena;
        var state = runtime.GetArenaState(arena);
        var origin = center + new Vector3(0f, 0f, BlastLift);
        for (var i = 0; i < ScriptRuntime.GroupCount; i++)
        {
            if (state.GroupHitFlags[i] == 0 && state.GroupHitScripts[i] == 0)
            {
                continue;
            }

            foreach (var triangle in runtime.GroupCenters(arena, i + 1))
            {
                if (Vector3.DistanceSquared(origin, triangle) > radius * radius)
                {
                    continue;
                }

                // Another group in the way stops it; the group's own triangle in the way is the hit.
                var point = triangle;
                if (runtime.Raycast(origin, triangle) is { } hit)
                {
                    if (hit.Group != i + 1)
                    {
                        continue;
                    }

                    point = hit.Point;
                }

                var distance = Vector3.Distance(origin, point);
                runtime.HitGroup(arena, i + 1, Round(damage * (radius - distance) / radius), kind, hitType);
                break;
            }
        }
    }

    /// <summary>Damage of a blast on a box (0x463958) and its distance: to the box's centre, less half
    /// the box's size; 0 beyond the radius or behind a wall.</summary>
    private (int Damage, float Distance) BlastDamage(Box box, Vector3 center, int damage, float radius)
    {
        var size = box.Size().Length();
        var target = box.Center();

        // Lift the blast off the floor it may lie on, so the wall test doesn't hit that floor.
        center += new Vector3(0f, 0f, BlastLift);
        var squared = Vector3.DistanceSquared(center, target) - size * size * 0.25f;
        if (squared > radius * radius || runtime.Raycast(center, target) != null)
        {
            return (0, 0f);
        }

        var distance = MathF.Sqrt(MathF.Max(squared, 0f));
        return (Round(damage * (1f - distance / radius)), distance);
    }
}
