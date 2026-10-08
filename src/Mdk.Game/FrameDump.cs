using System.Globalization;

namespace Mdk.Game;

/// <summary>--frames=folder,count[,every]: after the wait, every <paramref name="Every"/>th frame
/// (60 a second) is saved as <c>frame000.bmp</c>... until <paramref name="Count"/> are, then the
/// game quits (the README's animation: 4 gives 15 frames a second).</summary>
public sealed record FrameDump(string Folder, int Count, int Every)
{
    private const int DefaultEvery = 4;

    public static FrameDump Parse(string text)
    {
        var parts = text.Split(',');
        var every = parts.Length > 2 ? int.Parse(parts[2], CultureInfo.InvariantCulture) : DefaultEvery;
        return new FrameDump(parts[0], int.Parse(parts[1], CultureInfo.InvariantCulture), every);
    }

    /// <summary>The file of the frame drawn <paramref name="step"/> frames after the wait, or null
    /// when that one isn't saved.</summary>
    public string? PathOf(int step)
    {
        if (step % Every != 0 || step / Every >= Count)
        {
            return null;
        }

        return Path.Combine(Folder, $"frame{step / Every:000}.bmp");
    }

    /// <summary>The last frame was saved at this step.</summary>
    public bool IsLast(int step) => step / Every >= Count - 1;
}
