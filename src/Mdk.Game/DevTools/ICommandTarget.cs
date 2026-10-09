using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Game.Flow;
using Mdk.Game.Kurt;
using Mdk.Game.Mods;

namespace Mdk.Game.DevTools;

/// <summary>A cheat or a tool turned on or off.</summary>
public enum Switch { Off, On }

/// <summary>Where Kurt is: the level, his arena, his feet and yaw (degrees).</summary>
public readonly record struct Placement(int Level, string Arena, Vector3 Feet, float Yaw);

/// <summary>What the console's commands act on: the game, on any screen, and the level when one
/// is played.
/// <code>
///   commands ──► ICommandTarget (map, load, god, look, fps...) ──► ILevelTarget? (tp, give, kill...)
/// </code></summary>
public interface ICommandTarget
{
    /// <summary>The level being played; null on the other screens (menus, the fall, the stream).</summary>
    ILevelTarget? Level { get; }

    /// <summary>Plays a level from the start, or from a floor of <paramref name="arena"/> ("": the
    /// level's start). False for an unknown level.</summary>
    bool Map(int level, string arena);

    /// <summary>Loads a saved game; false when there's none of that name.</summary>
    bool Load(string slot);

    /// <summary>The session's god mode, noclip and onehit: the level's Kurt now, or the next level's.</summary>
    Switch ToggleGod();
    Switch ToggleNoclip();
    Switch ToggleOneHit();

    void SetDifficulty(Difficulty difficulty);

    /// <summary>The look, saved; returns whether the level reloads with it now.</summary>
    bool SetLook(Graphics look);

    void SetAntiAliasing(AntiAliasing antiAliasing);

    /// <summary>The stereo mode, and the eyes' separation and convergence when given; saved and
    /// applied at once.</summary>
    void SetStereo(Stereo stereo, float? separation = null, float? convergence = null);
    void SetTimeScale(float scale);
    Switch ToggleOverlay();
    void Quit();

    /// <summary>The mods found, in order, each on or off.</summary>
    IReadOnlyList<string> ModList();
}

/// <summary>What the console's commands act on in the level being played.</summary>
public interface ILevelTarget
{
    /// <summary>What the level took from the mods.</summary>
    ModReport Mods { get; }

    Placement Where();

    /// <summary>Kurt to a point (in its arena, or the one given), or to a floor of an arena when
    /// <paramref name="point"/> is null. False for an unknown arena.</summary>
    bool Teleport(string arena, Vector3? point);

    void SetGod(Switch god);
    void SetNoclip(Switch noclip);

    /// <summary>Kurt's hits kill at once.</summary>
    void SetOneHit(Switch oneHit);

    /// <summary>Kurt takes a pickup (its model name); false when he can't.</summary>
    bool Give(string pickup);

    void SetHealth(int health);

    /// <summary>Kills the enemies of Kurt's arena; returns how many.</summary>
    int KillEnemies();

    /// <summary>A full save (as F2); false when the level can't be saved now.</summary>
    bool Save(string slot);

    /// <summary>Kurt's inventory follows the difficulty.</summary>
    void SetDifficulty(Difficulty difficulty);

    /// <summary>The level's state as a full save, to reload it; null when it can't be saved now.</summary>
    string? Snapshot();
}
