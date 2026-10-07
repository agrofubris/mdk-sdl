using System.Numerics;
using Mdk.Game.Audio;
using Mdk.Game.Objects;
using static Mdk.Game.Scripts.ScriptMath;

namespace Mdk.Game.Scripts;

/// <summary>Built-in behaviours of some kinds of objects, run by the engine besides their scripts
/// (the object update 0x43c7dc): doors (flag 0x100000, 0x43cc68), pickups (flag 0x200000,
/// 0x43daf4) and the holy cow (0x440074). A port of godot-mdk's <c>object_behaviors.gd</c>.
/// <code>
///   door:  CLOSED ─(Kurt near)─► OPENING ─(anim done)─► OPEN ─(Kurt away)─► CLOSING ─► CLOSED
/// </code></summary>
public sealed class ObjectBehaviors(ScriptRuntime runtime)
{
    // Door states (obj+0x312): the low 4 bits are the engine's, the others set by door_set_flags.
    public const int DoorEngineBits = 0xF;
    public const int DoorOpen = 0x1;
    public const int DoorOpening = 0x2;
    public const int DoorClosing = 0x4;
    public const int DoorClosed = 0x8;
    /// <summary>The door is not solid while open (object flag 0x10).</summary>
    public const int DoorOpenNotSolid = 0x10;
    public const int DoorStaysOpen = 0x20;
    public const int DoorLocked = 0x40;
    public const int DoorLockHidden = 0x100;
    /// <summary>Door state bits kept when it starts moving (0xF0), and when it opens (0x70).</summary>
    private const int DoorScriptBits = 0xF0;
    private const int DoorOpenKept = 0x70;
    private const string LockPart = "LOCK";
    private const string HatchPrefix = "HC";
    private const string NoSound = "NONE";

    /// <summary>Falling pickups open a chute at this speed.</summary>
    private const float PickupChuteSpeed = -15f;
    private const float PickupTurnSpeed = 180f;
    /// <summary>Taken pickups spin this much faster while they shrink away in 30 ticks.</summary>
    private const float CollectedSpin = 4f;
    private const float ShrinkTicks = 30f;
    /// <summary>Pickups float this high once they've landed.</summary>
    private const float PickupHover = 1.5f;
    private const string Chute = "SW_CHUTE";
    private const int ChuteFlags = 0x820;
    private const int AttachCommand = 74;
    /// <summary>The chute shrinks away at this scale.</summary>
    private const float ChuteGone = 0.2f;
    /// <summary>Pickups that don't spin.</summary>
    private static readonly string[] Still = ["SW_H150", "SW_SEAL", "SW_SBONE"];
    /// <summary>The pickup that runs away (0x43daf4): it starts within 20 units, stops beyond √1000
    /// (across), runs at 40 units/s and turns at 270°/s; idle it replays its animation with a chance
    /// of 72 in 32768 per tick.</summary>
    private const string Runner = "SW_H150";
    private const float RunnerStart = 20f;
    private const float RunnerStopSquared = 1000f;
    private const float RunnerSpeed = 40f;
    private const float RunnerTurn = 270f;
    private const int RunnerIdleChance = 72;
    private const int ChanceRange = 32768;
    /// <summary>The holy cow (0x440074): 50 units/s towards its target, 1000 damage to objects, 10 to
    /// Kurt, a blast hit (event -3, type -4).</summary>
    private const float CowSpeed = 50f;
    private const int CowDamage = 1000;
    private const int CowKurtDamage = 10;
    private const int CowHitEvent = -3;
    private const int CowHitType = -4;

    private const float Dt = ScriptRuntime.Tick;
    /// <summary>A door moved to its other side turns around (0x43ca00).</summary>
    private const float DoorTurn = 180f;
    private const float FullTurn = 360f;
    /// <summary>Ticks a door's closing may take when shut at once.</summary>
    private const int MaxShutTicks = 1000;

    /// <summary>Opens the door when Kurt comes closer than its distance, closes it when he goes away.</summary>
    public void UpdateDoor(MdkObject obj)
    {
        // A door seen from its other side moves into Kurt's arena (0x43cc68), so it stays live,
        // solid and drawn there after the arena behind it is put away.
        if (obj.Arena != runtime.CurrentArena && obj.Connects == runtime.CurrentArena)
        {
            MoveDoor(obj);
        }

        var state = obj.DoorState;
        if ((state & DoorOpening) != 0)
        {
            if (obj.IsAnimationDone)
            {
                state = (state & DoorOpenKept) | DoorOpen;
                Play(obj, obj.DoorSounds[2]);
            }
        }
        else if ((state & DoorClosing) != 0 && obj.IsAnimationDone)
        {
            state = (state & DoorScriptBits) | DoorClosed;
            Play(obj, obj.DoorSounds[3]);

            // The arena behind it goes.
            runtime.ShowArena("");
        }

        if (obj.DistanceTo(runtime.KurtPosition) >= obj.DoorDistance)
        {
            if ((state & (DoorClosing | DoorClosed | DoorStaysOpen)) == 0)
            {
                obj.RestartAnimation(runtime.GetAnimation(obj, obj.DoorAnimations[1]), MdkObject.Looping.Once);
                state = (state & DoorScriptBits) | DoorClosing;
                Play(obj, obj.DoorSounds[1]);
            }
        }
        else if ((state & (DoorOpen | DoorOpening | DoorLocked)) == 0)
        {
            obj.RestartAnimation(runtime.GetAnimation(obj, obj.DoorAnimations[0]), MdkObject.Looping.Once);
            state = (state & DoorScriptBits) | DoorOpening;
            Play(obj, obj.DoorSounds[0]);
            ShowBehind(obj);
        }

        obj.DoorState = state;

        // Parts named LOCK show a locked, closed door; parts named HC... are hidden while it's closed.
        var hidden = obj.HiddenParts;
        if ((state & DoorClosed) == 0)
        {
            hidden = (hidden | obj.LockParts) & ~obj.HatchParts;
        }
        else
        {
            hidden |= obj.HatchParts;
            if ((state & DoorLocked) != 0 && (state & DoorLockHidden) == 0)
            {
                hidden &= ~obj.LockParts;
            }
            else
            {
                hidden |= obj.LockParts;
            }
        }

        obj.HiddenParts = hidden;
        if ((state & DoorOpenNotSolid) == 0)
        {
            return;
        }

        obj.Flags = (state & DoorOpen) != 0 ? obj.Flags | MdkObject.FlagNotSolid : obj.Flags & ~MdkObject.FlagNotSolid;
    }

    /// <summary>Shuts a door at once, its closing played to the end (unless it stays open).</summary>
    public void Shut(MdkObject door)
    {
        if ((door.DoorState & (DoorClosed | DoorStaysOpen)) != 0)
        {
            return;
        }

        door.RestartAnimation(runtime.GetAnimation(door, door.DoorAnimations[1]), MdkObject.Looping.Once);
        for (var tick = 0; tick < MaxShutTicks && !door.IsAnimationDone; tick++)
        {
            door.AdvanceAnimation(Dt);
        }

        door.DoorState = (door.DoorState & DoorScriptBits) | DoorClosed;
    }

    /// <summary>Moves a door into the arena on its other side, turned around (0x43ca00): e.g. the
    /// door CDANT_1 → DANT_2 at yaw 90 becomes DANT_2 → CDANT_1 at yaw 270.</summary>
    public static void MoveDoor(MdkObject door)
    {
        (door.Arena, door.Connects) = (door.Connects, door.Arena);
        door.Yaw += DoorTurn;
        if (door.Yaw >= FullTurn)
        {
            door.Yaw -= FullTurn;
        }
    }

    /// <summary>Sets up a new door (spawn_connector): masks of its LOCK and HC... parts.</summary>
    public void SetupDoor(MdkObject obj)
    {
        // New doors are closed (0x45cdec, spawn flag 1); starting mid-way they'd close and hide the arena behind.
        obj.DoorState = DoorClosed;
        obj.LockParts = 0;
        obj.HatchParts = 0;
        if (obj.Model == null)
        {
            return;
        }

        for (var i = 0; i < obj.Model.PartList.Count; i++)
        {
            var name = obj.Model.PartList[i].Name;
            if (name == LockPart)
            {
                obj.LockParts |= 1 << i;
            }
            else if (name.StartsWith(HatchPrefix, StringComparison.Ordinal))
            {
                obj.HatchParts |= 1 << i;
            }
        }
    }

    /// <summary>Pickups fall with a chute, then float and spin; taken ones shrink away in 30 ticks.</summary>
    public void UpdatePickup(MdkObject obj)
    {
        if ((obj.Flags & MdkObject.FlagCollected) != 0)
        {
            obj.ParameterTimer -= 1f;
            obj.Scale = MathF.Max(obj.ParameterTimer / ShrinkTicks, 0f);
            obj.Yaw = Wrap360(obj.Yaw + Dt * PickupTurnSpeed * CollectedSpin);
            if (obj.ParameterTimer <= 0f)
            {
                runtime.Remove(obj);
            }

            return;
        }

        if ((obj.Flags & (MdkObject.FlagGravity | MdkObject.FlagLanded)) == MdkObject.FlagGravity)
        {
            Fall(obj);
            return;
        }

        // The chute shrinks away once the pickup has landed.
        var chute = obj.Attached;
        if (chute != null && chute.Scale > ChuteGone)
        {
            chute.Scale -= Dt;
            if (chute.Scale <= ChuteGone)
            {
                runtime.Remove(chute);
                obj.Attached = null;
            }
        }

        if (obj.TypeName == Runner && obj.Attached == null)
        {
            UpdateRunner(obj);
        }

        if (!Still.Contains(obj.TypeName))
        {
            obj.Yaw = Wrap360(obj.Yaw + Dt * PickupTurnSpeed);
        }
    }

    /// <summary>A falling pickup: it lands (SW_H150 on the floor, the others floating), or opens its chute.</summary>
    private void Fall(MdkObject obj)
    {
        if ((obj.ContactFlags & MdkObject.ContactFloor) != 0)
        {
            var hover = obj.TypeName == Runner ? 0f : PickupHover;
            obj.HeightOffset = hover;
            obj.Flags |= MdkObject.FlagLanded;
            obj.Position.Z += hover;
            return;
        }

        if (obj.Velocity.Z >= PickupChuteSpeed)
        {
            return;
        }

        obj.Velocity.Z = PickupChuteSpeed;
        if (obj.Attached != null)
        {
            return;
        }

        var chute = runtime.Spawn(obj, Chute, obj.Position, obj.Yaw, -1, 0, ScriptRuntime.Spawning.Plain);
        if (chute == null)
        {
            return;
        }

        chute.Flags = ChuteFlags;
        chute.Leader = obj;
        chute.AttachPoints = (0, 0);
        chute.MoveCommand = AttachCommand;
        obj.Attached = chute;
    }

    /// <summary>SW_H150 runs away (0x43daf4): when Kurt comes within 20 units it plays H150_R and runs
    /// at 40 units/s, turning away from him at up to 270°/s, until he's 31.6 units away (across); idle,
    /// it plays H150_I now and then (about every 15 s).
    /// <code>
    ///   IDLE ──(Kurt within 20)──► RUN ──(Kurt beyond 31.6)──► IDLE
    /// </code></summary>
    private void UpdateRunner(MdkObject obj)
    {
        var run = runtime.Items.GetAnimation("H150_R");
        var kurt = runtime.KurtPosition;
        if (obj.Animation != run)
        {
            if ((obj.Animation == null || obj.IsAnimationDone) && runtime.Rng.Next(ChanceRange) < RunnerIdleChance)
            {
                obj.RestartAnimation(runtime.Items.GetAnimation("H150_I"), MdkObject.Looping.Once);
                return;
            }

            if (Vector3.DistanceSquared(obj.Position, kurt) < RunnerStart * RunnerStart)
            {
                obj.RestartAnimation(run, MdkObject.Looping.Loop);
                runtime.Mixer.Play("RUNNER", SoundMixer.Start.Once);
            }

            return;
        }

        var ahead = FromAngle(obj.Yaw) * RunnerSpeed;
        obj.Push.X += ahead.X;
        obj.Push.Y += ahead.Y;
        var away = Heading(obj.Position, kurt) + HalfTurn;
        var turn = Math.Clamp(WrapAngle(away - obj.Yaw), -RunnerTurn * Dt, RunnerTurn * Dt);
        obj.Yaw = Wrap360(obj.Yaw + turn);
        obj.Flags |= MdkObject.FlagGravity;
        if (new Vector2(kurt.X - obj.Position.X, kurt.Y - obj.Position.Y).LengthSquared() > RunnerStopSquared)
        {
            obj.RestartAnimation(runtime.Items.GetAnimation("H150_I"), MdkObject.Looping.Once);
        }
    }

    /// <summary>The holy cow (0x440074): falling, it slides towards its target at 50 units/s on each
    /// axis and hits once what it lands on (1000 damage to objects, 10 to Kurt); it blows up 0.5 s
    /// after landing.</summary>
    public void UpdateCow(MdkObject obj)
    {
        var target = obj.CowTarget is { Dead: false } cowTarget ? cowTarget.Position : runtime.KurtPosition;
        var landed = (obj.ContactFlags & MdkObject.ContactFloor) != 0;
        if ((obj.Flags & MdkObject.FlagNotTarget) == 0)
        {
            CowHits(obj);
            if (!landed)
            {
                obj.Position.X = MoveToward(obj.Position.X, target.X, CowSpeed * Dt);
                obj.Position.Y = MoveToward(obj.Position.Y, target.Y, CowSpeed * Dt);
            }
        }

        if (!landed)
        {
            return;
        }

        obj.Flags |= MdkObject.FlagNotTarget;
        obj.ParameterTimer -= Dt;
        if (obj.ParameterTimer < 0f)
        {
            runtime.Kill(obj);
        }
    }

    /// <summary>What the cow overlaps: objects of its arena lose 1000 health (a blast hit), Kurt 10.</summary>
    private void CowHits(MdkObject obj)
    {
        var bounds = runtime.GetWorldBounds(obj);
        var hit = false;
        foreach (var other in runtime.Objects.ToList())
        {
            if (other == obj || other.Dead || other.Arena != obj.Arena || (other.Flags & (MdkObject.FlagNotTarget | MdkObject.FlagNotSolid2)) != 0)
            {
                continue;
            }

            if (!bounds.Intersects(runtime.GetWorldBounds(other)))
            {
                continue;
            }

            hit = true;
            other.HitEvent = CowHitEvent;
            other.HitType = CowHitType;
            if (other.Health >= ScriptRuntime.Indestructible)
            {
                continue;
            }

            other.Health -= CowDamage;
            if (other.Health <= 0)
            {
                runtime.Kill(other);
            }
        }

        if (bounds.Intersects(runtime.GetKurtBox()))
        {
            hit = true;
            runtime.HurtKurt(CowKurtDamage);
        }

        if (hit)
        {
            obj.Flags |= MdkObject.FlagNotTarget;
        }
    }

    /// <summary>An opening door shows the arena behind it (0x43cc68): its other side, or its own arena,
    /// whichever is neither Kurt's nor already the active second arena.</summary>
    private void ShowBehind(MdkObject obj)
    {
        foreach (var side in new[] { obj.Connects, obj.Arena })
        {
            if (side.Length == 0 || side == runtime.CurrentArena || (runtime.SecondActive && side == runtime.SecondArena))
            {
                continue;
            }

            runtime.ShowArena(side);
            return;
        }
    }

    private void Play(MdkObject obj, string name)
    {
        if (name.Length != 0 && name != NoSound)
        {
            runtime.PlaySound(obj, name, 0, null);
        }
    }
}
