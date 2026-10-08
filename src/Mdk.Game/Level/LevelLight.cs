using Mdk.Engine.Render;

namespace Mdk.Game.Level;

/// <summary>The enhanced look's light as the camera goes from arena to arena: each arena's own
/// (<see cref="EnhancedLook.Lighting"/>), made once at the level's load.</summary>
public sealed class LevelLight(Renderer renderer, LevelData level)
{
    private readonly Dictionary<string, Lighting> _arenas = level.Arenas.ToDictionary(a => a.Name, a => EnhancedLook.Lighting(level.Dti, a));

    /// <summary>Lights the scene as <paramref name="arena"/> (the current one); unknown: unchanged.</summary>
    public void Update(string arena)
    {
        if (_arenas.TryGetValue(arena, out var lighting))
        {
            renderer.Lighting = lighting;
        }
    }
}
