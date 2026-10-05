namespace Mdk.Game.Scripts;

/// <summary>The end of a level: the arena breaks up around Kurt as he rises. Ends on its first tick.</summary>
// TODO port end_level.gd
public sealed class EndLevel
{
    private bool _done;

    public event Action? Finished;

    public void Start(ScriptRuntime runtime)
    {
    }

    public void Update(float ticks)
    {
        if (_done)
        {
            return;
        }

        _done = true;
        Finished?.Invoke();
    }
}
