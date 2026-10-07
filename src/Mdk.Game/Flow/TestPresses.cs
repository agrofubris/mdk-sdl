using System.Globalization;
using Mdk.Engine.Platform;

namespace Mdk.Game.Flow;

/// <summary>Keys pressed once at given game times (--press=QuickSave@1,Menu.Accept@1.5; tests): game
/// keys (<see cref="Key"/>) by name, menu keys (<see cref="MenuKey"/>) by name after "Menu.".</summary>
public sealed class TestPresses
{
    private const char Separator = ',';
    private const char At = '@';
    private const string MenuPrefix = "Menu.";

    private sealed record Press(string Name, float Time);

    private readonly List<Press> _pending;

    private TestPresses(List<Press> pending) => _pending = pending;

    public static TestPresses Parse(string text) => new(text.Split(Separator, StringSplitOptions.RemoveEmptyEntries)
        .Select(p => p.Split(At, 2))
        .Select(p => new Press(p[0], p.Length > 1 ? float.Parse(p[1], CultureInfo.InvariantCulture) : 0f))
        .ToList());

    /// <summary>Presses the keys due at <paramref name="time"/>, each once.</summary>
    public void Apply(Input input, float time)
    {
        foreach (var press in _pending.Where(p => p.Time <= time).ToList())
        {
            _pending.Remove(press);
            if (press.Name.StartsWith(MenuPrefix, StringComparison.Ordinal))
            {
                if (Enum.TryParse<MenuKey>(press.Name[MenuPrefix.Length..], out var menuKey))
                {
                    input.Press(menuKey);
                }

                continue;
            }

            if (Enum.TryParse<Key>(press.Name, out var key))
            {
                input.Press(key);
            }
        }
    }
}
