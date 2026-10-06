namespace Mdk.Game.Stream;

/// <summary>Watcom's <c>rand()</c> (0x4794dd): <c>state = state × 0x41C64E6D + 0x3039</c>, returns
/// bits 16-30 (0-0x7FFF). The stream draws its tube, lights and damage from it.</summary>
public sealed class WatcomRandom(uint seed = WatcomRandom.DefaultSeed)
{
    /// <summary>The C library's seed when <c>srand</c> wasn't called.</summary>
    public const uint DefaultSeed = 1;
    public const int Max = 0x7FFF;
    private const uint Multiplier = 0x41C64E6D;
    private const uint Increment = 0x3039;
    private const int Shift = 16;

    private uint _state = seed;

    public int Next()
    {
        _state = _state * Multiplier + Increment;
        return (int)(_state >> Shift) & Max;
    }
}
