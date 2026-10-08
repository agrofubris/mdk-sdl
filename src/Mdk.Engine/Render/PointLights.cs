using System.Numerics;

namespace Mdk.Engine.Render;

/// <summary>A point light of the enhanced look: where it is, its linear colour (its strength in
/// it), and how far it reaches (it fades to nothing there).</summary>
public readonly record struct PointLight(Vector3 Position, Vector3 Colour, float Radius);

/// <summary>The frame's point lights (muzzle flashes, explosions, fires): the game adds up to
/// <see cref="Gathered"/>, the GPU lights with the <see cref="Shown"/> reaching nearest the camera
/// (distance minus radius). Fixed arrays: no allocation per frame.
/// <code>
///   Add ×64 ──► Pack(camera): nearest reach first ──► 16 ──► shaders/enhanced.hlsl
/// </code></summary>
public sealed class PointLights
{
    public const int Gathered = 64;
    public const int Shown = 16;

    private readonly PointLight[] _lights = new PointLight[Gathered];
    /// <summary>Pack's scratch: whether a light is packed already.</summary>
    private readonly bool[] _taken = new bool[Gathered];

    public int Count { get; private set; }

    public void Clear() => Count = 0;

    /// <summary>Adds a light that shines (a colour, a reach); beyond the limit it's dropped.</summary>
    public void Add(PointLight light)
    {
        if (Count == Gathered || light.Radius <= 0f || light.Colour == Vector3.Zero)
        {
            return;
        }

        _lights[Count++] = light;
    }

    /// <summary>Copies the lights reaching nearest <paramref name="camera"/> into
    /// <paramref name="into"/> (at most its length); returns how many.</summary>
    public int Pack(Vector3 camera, Span<PointLight> into)
    {
        Array.Clear(_taken, 0, Count);
        var packed = 0;
        while (packed < into.Length && packed < Count)
        {
            var best = Nearest(camera);
            _taken[best] = true;
            into[packed++] = _lights[best];
        }

        return packed;
    }

    /// <summary>The untaken light whose reach comes nearest the camera.</summary>
    private int Nearest(Vector3 camera)
    {
        var best = -1;
        var bestGap = float.MaxValue;
        for (var i = 0; i < Count; i++)
        {
            if (_taken[i])
            {
                continue;
            }

            var gap = Vector3.Distance(camera, _lights[i].Position) - _lights[i].Radius;
            if (gap < bestGap)
            {
                best = i;
                bestGap = gap;
            }
        }

        return best;
    }
}
