using System.Numerics;

namespace Mdk.Game.Scripts;

/// <summary>Fans (updrafts) of the arenas. None blow yet.</summary>
// TODO port fans.gd
public sealed class Fans
{
    public const int MaskKurt = 1;
    public const int MaskObjects = 2;
    public const int MaskEffects = 8;

    public void Create(string arena, int hotspot, string name, int param, int type, float strength)
    {
    }

    public void Remove(string arena, string name)
    {
    }

    public enum Power { Off, On }

    public void Enable(string arena, string name, Power power)
    {
    }

    public void Update()
    {
    }

    /// <summary>The vertical speed in a fan, or NaN outside every fan.</summary>
    public float Query(string arena, Vector3 point, float vz, int mask, float dt) => float.NaN;
}
