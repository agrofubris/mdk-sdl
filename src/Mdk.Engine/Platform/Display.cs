using System.Globalization;

namespace Mdk.Engine.Platform;

/// <summary>The operating system, for what the game offers on it (GPU backends, window options).</summary>
public enum Os { Windows, Linux, MacOs, Android }

public static class OsInfo
{
    public static Os Current =>
        OperatingSystem.IsAndroid() ? Os.Android
        : OperatingSystem.IsWindows() ? Os.Windows
        : OperatingSystem.IsMacOS() ? Os.MacOs
        : Os.Linux;
}

/// <summary>A size in pixels, written <c>1280x960</c>.</summary>
public readonly record struct Resolution(int Width, int Height)
{
    private const char Separator = 'x';

    public int Area => Width * Height;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Width}{Separator}{Height}");

    /// <summary>"1280x960"; both sides positive.</summary>
    public static bool TryParse(string text, out Resolution size)
    {
        size = default;
        var parts = text.Split(Separator);
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var width)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var height)
            || width <= 0 || height <= 0)
        {
            return false;
        }

        size = new Resolution(width, height);
        return true;
    }
}

/// <summary>A mode of a display: its size and refresh rate (Hz).</summary>
public readonly record struct ScreenMode(int Width, int Height, float Refresh)
{
    public Resolution Size => new(Width, Height);
}

/// <summary>How the window shows: its fullscreen mode, its size when windowed, and the display's
/// mode in exclusive fullscreen (null: the desktop's).</summary>
public readonly record struct DisplaySetup(Fullscreen Fullscreen, Resolution Window, Resolution? Exclusive);

/// <summary>The sizes the Display options step through.
/// <code>
///   windowed:  presets that fit the display's usable area (no task bar), plus the window's size
///   exclusive: the display's modes, one per size (its highest refresh rate)
/// </code></summary>
public static class Resolutions
{
    public static readonly Resolution[] Presets =
    [
        new(640, 480), new(800, 600), new(1024, 768), new(1280, 720), new(1280, 960), new(1600, 900),
        new(1600, 1200), new(1920, 1080), new(1920, 1440), new(2560, 1440), new(3840, 2160),
    ];

    /// <summary>The presets fitting <paramref name="usable"/>, and <paramref name="current"/>, by area.</summary>
    public static IReadOnlyList<Resolution> Windowed(Resolution usable, Resolution current) =>
        Presets.Where(p => p.Width <= usable.Width && p.Height <= usable.Height)
            .Append(current)
            .Distinct()
            .OrderBy(s => s.Area).ThenBy(s => s.Width)
            .ToList();

    /// <summary>One mode per size, the highest refresh rate, by area.</summary>
    public static IReadOnlyList<ScreenMode> Exclusive(IEnumerable<ScreenMode> modes) =>
        modes.GroupBy(m => m.Size)
            .Select(g => g.MaxBy(m => m.Refresh))
            .OrderBy(m => m.Size.Area).ThenBy(m => m.Width)
            .ToList();

    /// <summary>The size <paramref name="step"/> places from <paramref name="current"/> (wrapping);
    /// from outside the list, the first.</summary>
    public static Resolution Step(IReadOnlyList<Resolution> list, Resolution current, int step)
    {
        if (list.Count == 0)
        {
            return current;
        }

        var index = list.ToList().IndexOf(current);
        if (index < 0)
        {
            return list[0];
        }

        return list[((index + step) % list.Count + list.Count) % list.Count];
    }
}

/// <summary>Keeps frames at most <c>limit</c> a second (VSync off).</summary>
public static class FrameLimiter
{
    public const int Off = 0;
    public static readonly int[] Limits = [Off, 60, 120, 144];

    /// <summary>What's left of a frame's time after <paramref name="elapsed"/>.</summary>
    public static TimeSpan Remaining(TimeSpan elapsed, int limit)
    {
        if (limit <= Off)
        {
            return TimeSpan.Zero;
        }

        var frame = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / limit);
        return elapsed >= frame ? TimeSpan.Zero : frame - elapsed;
    }

    /// <summary>Sleeps the rest of the frame (precisely: SDL_DelayPrecise).</summary>
    public static void Wait(TimeSpan elapsed, int limit)
    {
        const long NanosecondsPerTick = 100;
        var rest = Remaining(elapsed, limit);
        if (rest > TimeSpan.Zero)
        {
            SDL.SDL3.SDL_DelayPrecise((ulong)(rest.Ticks * NanosecondsPerTick));
        }
    }
}
