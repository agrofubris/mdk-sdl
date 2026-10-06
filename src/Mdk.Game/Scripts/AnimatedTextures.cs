namespace Mdk.Game.Scripts;

/// <summary>The frames of arena textures that scripts animate (<c>arena_texture_frame</c>, opcode
/// 133 → <c>texture_set_frame</c> 0x42d9c0; godot-mdk docs/animated_textures.md). No clock drives
/// them: level 4's MEAT_3 and level 7's DANT_5 add 6 (12 once hit) frames per second to
/// <c>M_COMM</c> each tick, and stop when the device is destroyed.
/// <code>
///   mode 0: frame = value - 1      mode 1: frame += value · dt      else: frame += value
///   frame wraps into [0, frames); drawn frame = trunc(frame)
/// </code></summary>
public sealed class AnimatedTextures(Func<string, string, int> frameCount)
{
    /// <summary>How opcode 133's value sets the frame.</summary>
    public enum Mode { Absolute, PerSecond, Frames }

    private readonly Dictionary<(string Arena, string Texture), float> _frames = [];

    /// <summary>The script's mode number as a <see cref="Mode"/>.</summary>
    public static Mode ModeOf(int mode) => mode switch
    {
        0 => Mode.Absolute,
        1 => Mode.PerSecond,
        _ => Mode.Frames,
    };

    /// <summary>Opcode 133 for an arena's texture, <paramref name="dt"/> seconds a tick.</summary>
    public void Set(string arena, string texture, Mode mode, float value, float dt)
    {
        var count = frameCount(arena, texture);
        if (count < 1)
        {
            return;
        }

        var current = _frames.GetValueOrDefault((arena, texture));
        var target = mode switch
        {
            Mode.Absolute => value - 1f,
            Mode.PerSecond => current + value * dt,
            _ => current + value,
        };

        // Into [0, count): a forward loop, never ping-pong.
        target %= count;
        if (target < 0f)
        {
            target += count;
        }

        _frames[(arena, texture)] = target;
    }

    /// <summary>The frame drawn for an arena's texture (0 until a script sets it).</summary>
    public int FrameOf(string arena, string texture) => (int)_frames.GetValueOrDefault((arena, texture));

    /// <summary>The textures animated so far, by arena and name, and their frames.</summary>
    public IReadOnlyDictionary<(string Arena, string Texture), float> Frames => _frames;
}
