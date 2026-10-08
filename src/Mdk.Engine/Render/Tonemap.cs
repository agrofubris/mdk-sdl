namespace Mdk.Engine.Render;

/// <summary>The enhanced look's tone curve, per linear channel (shaders/enhanced.hlsl
/// <c>tonemap</c>): light up to the knee is kept as is (the original's textures, unlit, stay
/// theirs; no black is crushed), brighter light rolls off exponentially towards white, its slope
/// unbroken at the knee: lit faces brighten without clipping, their hue fading to white.
/// <code>
///   out 1 ┤          ___────── white
///         │      _.-'
///    knee ┤    ／   1 - (1 - k)·e^(-(x - k)/(1 - k))
///         │  ／  identity
///       0 ┼──┬───────────── in
///            k
/// </code></summary>
public static class Tonemap
{
    public const float Knee = 0.6f;

    public static float Map(float light)
    {
        if (light <= Knee)
        {
            return light;
        }

        var room = 1f - Knee;
        return 1f - room * MathF.Exp(-(light - Knee) / room);
    }
}
