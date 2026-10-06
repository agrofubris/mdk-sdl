using Mdk.Engine.Platform;

namespace Mdk.Game;

/// <summary>The soak test's player (--soak=seed): random but seeded keys, a new action every
/// 0.2-1.5 s: a move, a turn, turbo, fire, jump, and sometimes a press (use, select, sniper); in
/// the menus a menu key every 0.3 s. The same seed and game times give the same keys.
/// <code>
///   time ──► action over? ──► roll the next one ──► hold its keys (a press: its first 0.1 s)
/// </code></summary>
public sealed class SoakKeys(int seed)
{
    private const float MinAction = 0.2f;
    private const float MaxAction = 1.5f;
    private const float PressTime = 0.1f;
    private const double TurboChance = 0.3;
    private const double FireChance = 0.4;
    private const double JumpChance = 0.1;
    private const double PressChance = 0.25;

    private static readonly Key[] Moves = [Key.Forward, Key.Forward, Key.Back, Key.StrafeLeft, Key.StrafeRight];
    private static readonly Key[] Turns = [Key.TurnLeft, Key.TurnRight];
    private static readonly Key[] Presses = [Key.UseItem, Key.ItemNext, Key.ItemPrevious, Key.Sniper, Key.ZoomIn, Key.ZoomOut];
    private static readonly Key[] All = [.. Moves.Distinct(), .. Turns, .. Presses, Key.Turbo, Key.Fire, Key.Jump];
    /// <summary>The menus' keys, "accept" and "down" more often (to get on through the menus).</summary>
    private static readonly MenuKey[] MenuKeys =
        [MenuKey.Up, MenuKey.Down, MenuKey.Down, MenuKey.Left, MenuKey.Right, MenuKey.Accept, MenuKey.Accept, MenuKey.Back];
    private const float MenuPeriod = 0.3f;

    private readonly Random _random = new(seed);
    private readonly HashSet<Key> _held = [];
    private Key? _press;
    private float _start = float.NegativeInfinity;
    private float _end = float.NegativeInfinity;
    private float _nextMenu;

    /// <summary>Holds the keys of the action at game time <paramref name="time"/>.</summary>
    public void Hold(Input input, float time)
    {
        if (time >= _end)
        {
            Roll(time);
        }

        foreach (var key in All)
        {
            var down = _held.Contains(key) || (key == _press && time < _start + PressTime);
            input.Hold(key, down ? Input.State.Down : Input.State.Up);
        }
    }

    /// <summary>A random menu key every 0.3 s of game time <paramref name="time"/>.</summary>
    public void PressMenu(Input input, float time)
    {
        if (time < _nextMenu)
        {
            return;
        }

        _nextMenu = time + MenuPeriod;
        input.Press(MenuKeys[_random.Next(MenuKeys.Length)]);
    }

    private void Roll(float time)
    {
        _held.Clear();
        _start = time;
        _end = time + MinAction + (float)_random.NextDouble() * (MaxAction - MinAction);

        // An index past the array: no move, no turn.
        var move = _random.Next(Moves.Length + 1);
        if (move < Moves.Length)
        {
            _held.Add(Moves[move]);
        }

        var turn = _random.Next(Turns.Length + 1);
        if (turn < Turns.Length)
        {
            _held.Add(Turns[turn]);
        }

        Maybe(Key.Turbo, TurboChance);
        Maybe(Key.Fire, FireChance);
        Maybe(Key.Jump, JumpChance);
        _press = _random.NextDouble() < PressChance ? Presses[_random.Next(Presses.Length)] : null;
    }

    private void Maybe(Key key, double chance)
    {
        if (_random.NextDouble() < chance)
        {
            _held.Add(key);
        }
    }
}
