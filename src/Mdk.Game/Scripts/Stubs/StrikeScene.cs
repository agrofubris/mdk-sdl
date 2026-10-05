namespace Mdk.Game.Scripts;

/// <summary>The full-screen strike (0x4398f0). Ends at once.</summary>
// TODO port strike_scene.gd
public sealed class StrikeScene
{
    public enum Kind { Bones, Kurt }

    public enum Plane { WithPilot, Only }

    public event Action? Finished;

    public void Play(ScriptRuntime runtime, Kind kind, Plane plane) => Finished?.Invoke();
}
