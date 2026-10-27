using Mdk.Engine.Render;
using Mdk.Game.Objects;
using Mdk.Game.Scripts;

namespace Mdk.Game.Level;

/// <summary>The enhanced look's light as the camera goes from arena to arena: each arena's own
/// (<see cref="EnhancedLook.Lighting"/>), made once at the level's load; and the frame's point
/// lights (<see cref="LightSources"/>).</summary>
public sealed class LevelLight(Renderer renderer, LevelData level)
{
    private readonly Dictionary<string, Lighting> _arenas = Light(level);

    /// <summary>Each arena's light, on every core: each one averages the whole sky panorama.</summary>
    private static Dictionary<string, Lighting> Light(LevelData level)
    {
        var lightings = new Lighting[level.Arenas.Count];
        Parallel.For(0, lightings.Length, a => lightings[a] = EnhancedLook.Lighting(level.Dti, level.Arenas[a]));
        var arenas = new Dictionary<string, Lighting>();
        for (var a = 0; a < lightings.Length; a++)
        {
            arenas.Add(level.Arenas[a].Name, lightings[a]);
        }

        return arenas;
    }

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
