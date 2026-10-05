using Mdk.Formats;

namespace Mdk.Game.Level;

/// <summary>The triangles of each arena that share a group number (the top byte of their flags),
/// which scripts show, hide, retexture or make destructible together. Group 0 is the rest of the
/// arena and never changes. The drawing (<see cref="LevelView"/>) and, later, the collisions read it.</summary>
public sealed class TriangleGroups
{
    [Flags]
    public enum State
    {
        None = 0,
        /// <summary>Not drawn (triangle flag 0x10).</summary>
        Hidden = 0x10,
        /// <summary>Not collided with (triangle flag 0x20).</summary>
        NotSolid = 0x20,
    }

    /// <summary><c>group_set_state</c> operations.</summary>
    public enum Operation
    {
        HideAndRelease = 0,
        Hide = 2,
        Show = 3,
        Release = 4,
        Solidify = 5,
    }

    private const int GroupShift = 24;

    public sealed class Group(int number)
    {
        public int Number { get; } = number;
        public List<int> Triangles { get; } = [];
        public State State { get; set; }
        /// <summary>The material value every triangle got from <c>group_set_texture</c>, if any.</summary>
        public int? Material { get; set; }
    }

    private readonly Dictionary<string, Dictionary<int, Group>> _arenas = [];
    private readonly Dictionary<string, Arena> _data = [];

    /// <summary>A group changed: its arena and number.</summary>
    public event Action<string, int>? Changed;

    public void Add(Arena arena)
    {
        var groups = new Dictionary<int, Group>();
        for (var t = 0; t < arena.TriangleCount; t++)
        {
            var number = (int)(arena.TriangleFlags[t] >> GroupShift);
            if (!groups.TryGetValue(number, out var group))
            {
                groups[number] = group = new Group(number);
            }

            group.Triangles.Add(t);
        }

        _arenas[arena.Name] = groups;
        _data[arena.Name] = arena;
    }

    public IReadOnlyCollection<Group> Of(string arena) => _arenas.TryGetValue(arena, out var groups) ? groups.Values : [];

    public Group? Get(string arena, int number) =>
        _arenas.TryGetValue(arena, out var groups) ? groups.GetValueOrDefault(number) : null;

    /// <summary><c>group_set_state</c>: 0 hides and releases, 2/3 hide/show, 4/5 release/solidify,
    /// others show and solidify.</summary>
    public void SetState(string arena, int number, int operation)
    {
        var group = Get(arena, number);
        if (group == null || number == 0)
        {
            return;
        }

        group.State = (Operation)operation switch
        {
            Operation.HideAndRelease => group.State | State.Hidden | State.NotSolid,
            Operation.Hide => group.State | State.Hidden,
            Operation.Show => group.State & ~State.Hidden,
            Operation.Release => group.State | State.NotSolid,
            Operation.Solidify => group.State & ~State.NotSolid,
            _ => State.None,
        };

        // The original keeps the state in the triangles' flags, where the collisions read it.
        var flags = _data[arena].TriangleFlags;
        const uint StateBits = (uint)(State.Hidden | State.NotSolid);
        foreach (var t in group.Triangles)
        {
            flags[t] = (flags[t] & ~StateBits) | (uint)group.State;
        }

        Changed?.Invoke(arena, number);
    }

    /// <summary><c>group_set_texture</c>: every triangle of the group takes material <paramref name="value"/>.</summary>
    public void SetMaterial(string arena, int number, int value)
    {
        var group = Get(arena, number);
        if (group == null || number == 0)
        {
            return;
        }

        group.Material = value;
        Changed?.Invoke(arena, number);
    }
}
