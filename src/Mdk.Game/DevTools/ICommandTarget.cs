using System.Numerics;
using Mdk.Engine.Render;
using Mdk.Game.Flow;
using Mdk.Game.Kurt;

namespace Mdk.Game.DevTools;

/// <summary>A cheat or a tool turned on or off.</summary>
public enum Switch { Off, On }

/// <summary>Where Kurt is: the level, his arena, his feet and yaw (degrees).</summary>
public readonly record struct Placement(int Level, string Arena, Vector3 Feet, float Yaw);

/// <summary>What the console's commands act on: the level being played.</summary>
public interface ICommandTarget
{
    Placement Where();

    /// <summary>Kurt to a point (in its arena, or the one given), or to a floor of an arena when
    /// <paramref name="point"/> is null. False for an unknown arena.</summary>
    bool Teleport(string arena, Vector3? point);

    /// <summary>Plays a level from the start, or from a floor of <paramref name="arena"/> ("": the
    /// level's start). False for an unknown level.</summary>
    bool Map(int level, string arena);

    Switch ToggleGod();
    Switch ToggleNoclip();

    /// <summary>Kurt takes a pickup (its model name); false when he can't.</summary>
    bool Give(string pickup);

    void SetHealth(int health);

    /// <summary>Kills the enemies of Kurt's arena; returns how many.</summary>
    int KillEnemies();

    /// <summary>A full save (as F2); false when the level can't be saved now.</summary>
    bool Save(string slot);

    /// <summary>Loads a saved game; false when there's none of that name.</summary>
    bool Load(string slot);

    void SetDifficulty(Difficulty difficulty);

    /// <summary>The look, saved; returns whether the level reloads with it now.</summary>
    bool SetLook(Graphics look);

    void SetAntiAliasing(AntiAliasing antiAliasing);
    void SetTimeScale(float scale);
    Switch ToggleOverlay();
    void Quit();
}
