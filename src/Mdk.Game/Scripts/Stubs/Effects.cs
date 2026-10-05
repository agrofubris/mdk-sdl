using System.Numerics;
using Mdk.Game.Objects;

namespace Mdk.Game.Scripts;

/// <summary>Sprite effects: bleeding wounds, slime drops, bubbles, trails. Not drawn yet.</summary>
// TODO port effects.gd
public sealed class Effects
{
    public void Attach(MdkObject obj, int slot, int towards)
    {
    }

    public void Detach(MdkObject obj, int slot)
    {
    }

    public void SpawnDrop(string arena, Vector3 point, Vector3 velocity, float scale)
    {
    }

    public void SpawnTrail(string arena, Vector3 point)
    {
    }

    public void SpawnBubble(string arena, Vector3 point)
    {
    }

    public bool HasEffects(string arena) => false;

    public void Update(float ticks)
    {
    }
}
