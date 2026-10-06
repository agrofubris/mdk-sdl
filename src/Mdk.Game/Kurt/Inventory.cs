namespace Mdk.Game.Kurt;

/// <summary>The game's difficulty (0x57423e).</summary>
public enum Difficulty { Easy, Normal, Hard }

/// <summary>Kurt's inventory (0x57432c, 5 slots) and sniper ammo (0x5743f3), filled by the pickups
/// he runs through (damp_collect_pickups). A port of godot-mdk's <c>kurt/inventory.gd</c> (see its
/// docs/gameplay.md "Pickups").
/// <code>
///   pickup ──► instant? ──► ammo (+8, 3, 3, 8, 1) │ health │ easter egg
///          └─► item ────► grenades, super chain gun: stack in their slot
///                     └─► others: a new slot (at most 5), selected
/// </code></summary>
public sealed class Inventory
{
    /// <summary>Item types, by pickup model (table 0x4921ac).</summary>
    public enum Item { None, Dummy, InterestingBomb, Tornado, Mortar, Grenade, SuperChainGun, Key, Seal, SuperBone }

    public readonly record struct Slot(Item Item, int Count);

    public const int MaxSlots = 5;
    public const int AmmoTypes = 5;
    public const int MaxHealth = 100;

    private static readonly Dictionary<string, Item> ItemPickups = new()
    {
        ["SW_DUMMY"] = Item.Dummy,
        ["SW_INTER"] = Item.InterestingBomb,
        ["SW_TWIST"] = Item.Tornado,
        ["SW_THUMP"] = Item.Mortar,
        ["SW_HBOMB"] = Item.Grenade,
        ["SW_GATT"] = Item.SuperChainGun,
        ["SW_KEY"] = Item.Key,
        ["SW_SEAL"] = Item.Seal,
        ["SW_SBONE"] = Item.SuperBone,
    };

    /// <summary>Pickups used at once (table 0x4920e0): sniper ammo (0-4), health (5-9), easter eggs.</summary>
    private static readonly string[] InstantPickups =
        ["SW_HOME", "SW_SGREN", "SW_HGREN", "SW_LGREN", "SW_BONES", "SW_H25", "SW_H50", "SW_H100", "SW_H150", "SW_H01", "SW_EWJ", "BONEFLC"];

    private const int LastAmmoPickup = 4;
    private const int BonesPickup = 4;
    private const int Health25 = 5;
    private const int Health50 = 6;
    private const int Health100 = 7;
    private const int Health150 = 8;
    private const int Health01 = 9;
    private const int Groovy = 10;
    private const int BoneFlc = 11;

    /// <summary>Sniper ammo per pickup 0-4 (halved on hard when above 1).</summary>
    private static readonly int[] AmmoPerPickup = [8, 3, 3, 8, 1];
    /// <summary>Per difficulty: grenades per pickup, super chain gun ticks per pickup.</summary>
    private static readonly int[] Grenades = [5, 3, 1];
    private static readonly int[] SuperChainGunTicks = [400, 200, 100];
    private const int SmallHealth = 10;
    private const int BigHealth = 50;
    private const int SuperHealth = 150;

    private const string CollectSound = "COLLECT";
    private const string BombSound = "WMIB";
    private const string BonesSound = "BONES";
    private const string HealthSound = "APPLE";

    private readonly List<Slot> _slots = [];
    private readonly int[] _ammo = new int[AmmoTypes];

    public Difficulty Difficulty = Difficulty.Normal;

    public IReadOnlyList<Slot> Slots => _slots;
    public int Selected { get; private set; }
    /// <summary>Sniper ammo per round type.</summary>
    public IReadOnlyList<int> Ammo => _ammo;
    /// <summary>The selected round type: 1-5, 0 normal bullets.</summary>
    public int SelectedAmmo { get; private set; }
    /// <summary>Ticks of super chain gun left (0x5743ef).</summary>
    public int SuperChainGun { get; private set; }

    /// <summary>The selected item, or <see cref="Item.None"/>.</summary>
    public Item SelectedItem => _slots.Count == 0 ? Item.None : _slots[Selected].Item;

    /// <summary>A pickup was taken: its name, the text shown for 2 seconds (0x46d098).</summary>
    public event Action<string>? PickedUp;

    /// <summary>The pickups of a full save, as they were.</summary>
    public void Restore(IEnumerable<Slot> slots, int selected, IReadOnlyList<int> ammo, int selectedAmmo, int superChainGun)
    {
        _slots.Clear();
        _slots.AddRange(slots);
        Selected = selected;
        for (var i = 0; i < _ammo.Length && i < ammo.Count; i++)
        {
            _ammo[i] = ammo[i];
        }

        SelectedAmmo = selectedAmmo;
        SuperChainGun = superChainGun;
    }

    /// <summary>Takes a pickup by model name; health pickups change <paramref name="health"/>. Returns
    /// the sound to play, or "" when Kurt can't take it (full inventory, not a pickup).</summary>
    public string Collect(string pickup, ref int health)
    {
        var index = Array.IndexOf(InstantPickups, pickup);
        if (index >= 0)
        {
            // SW_H01 and BONEFLC show no message.
            if (index != Health01 && index != BoneFlc)
            {
                PickedUp?.Invoke(pickup);
            }

            return UseInstant(index, ref health);
        }

        if (!ItemPickups.TryGetValue(pickup, out var item))
        {
            return "";
        }

        var sound = CollectItem(item);
        if (sound.Length != 0)
        {
            PickedUp?.Invoke(pickup);
        }

        return sound;
    }

    private string CollectItem(Item item)
    {
        // Grenades and the super chain gun stack in their slot.
        var stacked = item is Item.Grenade or Item.SuperChainGun ? _slots.FindIndex(s => s.Item == item) : -1;
        if (stacked >= 0)
        {
            _slots[stacked] = Add(_slots[stacked], item);
            if (item != Item.SuperChainGun)
            {
                Selected = stacked;
            }

            return CollectSound;
        }

        if (_slots.Count >= MaxSlots)
        {
            return "";
        }

        // The new item is selected, but the super chain gun only in an empty inventory.
        if (item != Item.SuperChainGun || _slots.Count == 0)
        {
            Selected = _slots.Count;
        }

        _slots.Add(Add(new Slot(item, 0), item));
        return item == Item.InterestingBomb ? BombSound : CollectSound;
    }

    private Slot Add(Slot slot, Item item)
    {
        switch (item)
        {
            case Item.Grenade:
                return slot with { Count = slot.Count + Grenades[(int)Difficulty] };
            case Item.SuperChainGun:
                SuperChainGun += SuperChainGunTicks[(int)Difficulty];
                return slot with { Count = 1 };
            default:
                return slot with { Count = 1 };
        }
    }

    private string UseInstant(int index, ref int health)
    {
        if (index <= LastAmmoPickup)
        {
            var amount = AmmoPerPickup[index];
            if (Difficulty == Difficulty.Hard && amount > 1)
            {
                amount /= 2;
            }

            _ammo[index] += amount;
            SelectedAmmo = index + 1;
            return index == BonesPickup ? BonesSound : CollectSound;
        }

        switch (index)
        {
            case Health25:
                health = Heal(health, SmallHealth);
                break;
            case Health50:
                health = Heal(health, BigHealth);
                break;
            case Health100:
                health = Math.Max(health, MaxHealth);
                break;
            case Health150:
                health = Math.Max(health, SuperHealth);
                break;
            case Health01:
                health = Heal(health, 1);
                break;
            case Groovy:
                return CollectSound;
            case BoneFlc:
                return BonesSound;
        }

        return HealthSound;
    }

    /// <summary>Adds health up to 100; above 100 (SW_H150) it stays.</summary>
    private static int Heal(int health, int amount) => health < MaxHealth ? Math.Min(health + amount, MaxHealth) : health;

    /// <summary>Selects slot <paramref name="index"/> (the item keys 1-5), if there is one.</summary>
    public void Select(int index)
    {
        if (index >= 0 && index < _slots.Count)
        {
            Selected = index;
        }
    }

    /// <summary>Selects the next (<paramref name="step"/> 1) or previous (-1) slot, wrapping around.</summary>
    public void SelectNext(int step)
    {
        if (_slots.Count == 0)
        {
            return;
        }

        Selected = ((Selected + step) % _slots.Count + _slots.Count) % _slots.Count;
    }

    /// <summary>Selects a sniper round type (0 normal bullets, 1-5).</summary>
    public void SelectAmmo(int type) => SelectedAmmo = Math.Clamp(type, 0, AmmoTypes);

    /// <summary>A sniper round of <paramref name="type"/> was fired; normal bullets never run out.</summary>
    public void UseAmmo(int type)
    {
        if (type > 0)
        {
            _ammo[type - 1] = Math.Max(_ammo[type - 1] - 1, 0);
        }
    }

    /// <summary>The next (<paramref name="step"/> 1) or previous (-1) round type with rounds,
    /// wrapping around; normal bullets always have some (0x46c900).</summary>
    public int NextAmmo(int step)
    {
        const int Types = AmmoTypes + 1;
        var type = SelectedAmmo;
        for (var i = 0; i < Types; i++)
        {
            type = ((type + step) % Types + Types) % Types;
            if (type == 0 || _ammo[type - 1] > 0)
            {
                break;
            }
        }

        return type;
    }

    /// <summary>One of the selected item is used up; an empty slot goes.</summary>
    public void Consume()
    {
        if (_slots.Count == 0)
        {
            return;
        }

        var slot = _slots[Selected];
        if (slot.Count > 1)
        {
            _slots[Selected] = slot with { Count = slot.Count - 1 };
            return;
        }

        RemoveSlot(Selected);
    }

    /// <summary>The super chain gun's time runs (while firing); at 0 its slot goes.</summary>
    public void TickSuperChainGun(int ticks)
    {
        if (SuperChainGun <= 0)
        {
            return;
        }

        SuperChainGun -= ticks;
        if (SuperChainGun > 0)
        {
            return;
        }

        SuperChainGun = 0;
        var index = _slots.FindIndex(s => s.Item == Item.SuperChainGun);
        if (index >= 0)
        {
            RemoveSlot(index);
        }
    }

    private void RemoveSlot(int index)
    {
        _slots.RemoveAt(index);
        Selected = Math.Clamp(Selected, 0, Math.Max(_slots.Count - 1, 0));
    }
}
