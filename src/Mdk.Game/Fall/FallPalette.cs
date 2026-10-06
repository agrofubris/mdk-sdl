using System.Numerics;

namespace Mdk.Game.Fall;

/// <summary>The fall's palette effects on <c>FALLPn</c> (0x5209c8): the red of death raises the red
/// of colours 1-254, then the brightness <c>b</c> whitens every component (0x410cc0):
/// <code>
///   c' = (c × k + (256 − k) × 255) >> 8      k = round(256 × clamp(b, 0, 1))
///   b = 1: unchanged    b = 0.5: 0 → 127, 100 → 177    b = 0: white
/// </code>
/// Colour 0 is whitened only in the fall's first second.</summary>
public sealed class FallPalette
{
    public enum Zero { Whitened, Kept }

    private const int Unit = 256;
    private const int Shift = 8;
    private const int Channels = 4;
    private const int LastReddened = 254;

    private readonly byte[] _base;
    private (int Level, int Red, Zero Zero) _applied = (Unit, 0, Zero.Whitened);

    /// <summary>The palette with the effects, RGBA.</summary>
    public byte[] Rgba { get; }

    public FallPalette(byte[] rgba)
    {
        _base = rgba;
        Rgba = (byte[])rgba.Clone();
    }

    /// <summary>k of a brightness.</summary>
    public static int Level(float brightness) => (int)MathF.Round(Unit * Math.Clamp(brightness, 0f, 1f));

    public static byte Whiten(byte c, int level) => (byte)((c * level + (Unit - level) * byte.MaxValue) >> Shift);

    /// <summary>Applies a brightness and a red (0-1, added to R); false when nothing changed.</summary>
    public bool Set(float brightness, float red, Zero zero)
    {
        var applied = (Level(brightness), (int)MathF.Round(red * byte.MaxValue), zero);
        if (applied == _applied)
        {
            return false;
        }

        _applied = applied;
        var (level, add, _) = applied;
        var first = zero == Zero.Kept ? 1 : 0;
        Array.Copy(_base, Rgba, _base.Length);
        for (var i = first; i < Rgba.Length / Channels; i++)
        {
            var at = i * Channels;
            if (i is > 0 and <= LastReddened)
            {
                Rgba[at] = (byte)Math.Min(Rgba[at] + add, byte.MaxValue);
            }

            for (var c = 0; c < Channels - 1; c++)
            {
                Rgba[at + c] = Whiten(Rgba[at + c], level);
            }
        }

        return true;
    }

    /// <summary>Colour <paramref name="index"/> with the effects (0-1).</summary>
    public Vector4 Colour(int index) =>
        new Vector4(Rgba[index * Channels], Rgba[index * Channels + 1], Rgba[index * Channels + 2], byte.MaxValue) / byte.MaxValue;

    /// <summary>A colour outside the palette (0-1, alpha kept) with the effects of colours 1-254.</summary>
    public Vector4 Apply(Vector4 colour)
    {
        var (level, add, _) = _applied;
        var red = MathF.Min(colour.X + add / (float)byte.MaxValue, 1f);
        var keep = level / (float)Unit;
        var white = (Unit - level) / (float)Unit;
        return new Vector4(new Vector3(red, colour.Y, colour.Z) * keep + new Vector3(white), colour.W);
    }
}
