namespace Mdk.Game.Mods;

/// <summary>What a level took from the mods: the images (textures, Kurt's frames, the HUD's) and
/// the models replaced, by name (the console's <c>mods</c>, the F3 overlay).</summary>
public sealed record ModReport(int Mods, IReadOnlyList<string> Images, IReadOnlyList<string> Models)
{
    public static ModReport None { get; } = new(0, [], []);

    /// <summary>"mods 2: 14 images, 1 model".</summary>
    public string Summary => $"mods {Mods}: {Images.Count} images, {Models.Count} models";

    /// <summary>The summary, then what was replaced.</summary>
    public IEnumerable<string> Lines()
    {
        yield return Summary;
        if (Images.Count > 0)
        {
            yield return $"images: {string.Join(' ', Images)}";
        }

        if (Models.Count > 0)
        {
            yield return $"models: {string.Join(' ', Models)}";
        }
    }
}
