using Mdk.Engine.Render;
using Mdk.Game.Objects;
using Mdk.Game.Scripts;

namespace Mdk.Game.Level;

/// <summary>The enhanced look's light as the camera goes from arena to arena: each arena's own
/// (<see cref="EnhancedLook.Lighting"/>), made once at the level's load; and the frame's point
/// lights (<see cref="LightSources"/>).</summary>
public sealed class LevelLight(Renderer renderer, LevelData level)
{
    private readonly Dictionary<string, Lighting> _arenas = level.Arenas.ToDictionary(a => a.Name, a => EnhancedLook.Lighting(level.Dti, a));

    /// <summary>Lights the scene as the scripts' current arena (unknown: unchanged), with this
    /// frame's point lights.</summary>
    public void Update(ScriptRuntime scripts, Kurt.Kurt kurt)
    {
        LightSources.Gather(renderer.Lights, scripts, kurt);
        if (_arenas.TryGetValue(scripts.CurrentArena, out var lighting))
        {
            renderer.Lighting = lighting;
        }
    }
}
