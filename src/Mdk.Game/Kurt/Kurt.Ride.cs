using System.Numerics;
using Mdk.Engine.Platform;
using Mdk.Formats;
using Mdk.Game.Collision;

namespace Mdk.Game.Kurt;

/// <summary>Kurt on a ride (rides.gd, kurt.gd ride/walk_mode; godot-mdk docs/gameplay.md "Rides"):
/// the snowboard and the XE bomber move him instead of his walking (<see cref="Ride"/>); in the XD2
/// he walks hidden, without strafing or jumping (<see cref="Walk.Riding"/>). The rides and the end
/// of a level also pick his frames and the camera's view.
/// <code>
///   Update ──Ride set?──► the ride's step (board: Glide; bomber: follows the XE)
///          └─Riding────► turn, forward, gravity, no items, no gun (the XD2 follows him)
/// </code></summary>
public sealed partial class Kurt
{
    public enum Walk { Normal, Riding }

    /// <summary>Keys a walker ride reads: steering (forward or turn held) and fire.</summary>
    public readonly record struct RideKeys(bool Steer, bool Fire);

    /// <summary>The camera's pivot above the feet (0x573b7c).</summary>
    public const float DefaultPivot = 4.5f;

    /// <summary>A ride's step (the snowboard, the bomber) instead of walking.</summary>
    public Action<Input, float>? Ride;
    public Walk Walking = Walk.Normal;
    public RideKeys Keys { get; private set; }
    /// <summary>A frame shown instead of the state's (the board's K_SURF, the end's K_TAKEOF).</summary>
    public (string Name, int Frame)? Pose;
    public float CameraPivot = DefaultPivot;
    /// <summary>The bomber's view: this high above Kurt, looking down (0x4183f0).</summary>
    public float? TopDownHeight;
    /// <summary>Added to the camera pitch (degrees, negative looks up; 0x573b1c), and the end of a
    /// level is lifting him (no tilt in the air).</summary>
    public float CameraTilt;
    public bool Rising;

    private IReadOnlyList<object> _touched = [];
    private readonly List<object> _touchedNow = [];
    private readonly List<(Arena Arena, int Triangle)> _contacts = [];

    /// <summary>The objects Kurt's box touches (damp_collide_move's 0x573c2c), and his platform.</summary>
    public IReadOnlyList<object> Touched => _touched;

    /// <summary>A ride's move at <paramref name="velocity"/> (u/s): the walk move against the arena
    /// and the objects, sticking to downhill floors, gravity and the fall.</summary>
    public void Glide(Vector3 velocity, float delta)
    {
        var move = velocity * delta;
        if (move != Vector3.Zero)
        {
            Feet = Contact(space.Move(Feet, move, ArenaSpace.Motion.Walk, WalkSlide, Nearby(Feet + move))).Feet;
        }

        if (OnFloor)
        {
            GlueDownhill(move, delta);
        }
        else
        {
            VerticalSpeed = MathF.Max(VerticalSpeed - Gravity * delta, -MaxFallSpeed);
        }

        Fall(delta);
        UpdateTouched();
    }

    /// <summary>The chain gun and its muzzle flash on the board.</summary>
    public void UpdateGun(Input input, float delta)
    {
        UpdateFiring(input);
        UpdateMuzzle(delta);
    }

    /// <summary>Gets off the XD2: a running jump forward (40 u/s up, 20 u/s ahead).</summary>
    public void JumpOff()
    {
        Walking = Walk.Normal;
        Visible = true;
        VerticalSpeed = JumpSpeed;
        ForwardSpeed = MaxSpeed;
        OnFloor = false;
        SetState(State.RunJump);
    }

    /// <summary>Gets off the board or the bomber, keeping the ride's speeds (u/s) as his walking ones.</summary>
    public void GetOffBoard(float forward, float strafe)
    {
        Ride = null;
        Pose = null;
        TopDownHeight = null;
        CameraPivot = DefaultPivot;
        Visible = true;
        ForwardSpeed = forward;
        StrafeSpeed = strafe;
        SetState(State.Still);
    }

    /// <summary>In the XD2: gravity and the fall only; he stands, hidden.</summary>
    private void RideWalker(Input input, float forward, float delta)
    {
        VerticalSpeed = MathF.Max(VerticalSpeed - Gravity * delta, -MaxFallSpeed);
        Fall(delta);
        Keys = new RideKeys(forward != 0f || Axis(input, Key.TurnLeft, Key.TurnRight) != 0f, input.IsDown(Key.Fire));
        SetState(State.Still);
    }

    /// <summary>The arena triangles Kurt's moves ran into since the last call (0x46634e: his arena's
    /// groups get a hit).</summary>
    public List<(Arena Arena, int Triangle)> TakeContacts()
    {
        _taken.Clear();
        _taken.AddRange(_contacts);
        _contacts.Clear();
        return _taken;
    }

    /// <summary>The contacts last taken (kept: no list per step).</summary>
    private readonly List<(Arena Arena, int Triangle)> _taken = [];

    private ArenaSpace.Result Contact(ArenaSpace.Result result)
    {
        if (result.Hit && result.Bsp != null)
        {
            _contacts.Add((result.Bsp.Arena, result.Triangle));
        }

        return result;
    }

    private void UpdateTouched()
    {
        _touchedNow.Clear();
        ArenaSpace.Touching(Feet, Nearby(Feet), _touchedNow);
        if (Platform != null && !_touchedNow.Contains(Platform))
        {
            _touchedNow.Add(Platform);
        }

        _touched = _touchedNow;
    }
}
