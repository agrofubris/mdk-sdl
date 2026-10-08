using Mdk.Engine.Render;
using Mdk.Formats;

namespace Mdk.Game.Mods;

/// <summary>The canvas's 2D images (HUD, menus, fonts): an index texture, and a mod's image for it
/// when one has it (<see cref="ModImages.Image"/>, drawn by the enhanced look's canvas at the
/// window's resolution; the original look keeps the index texture).</summary>
public static class CanvasImages
{
    /// <summary>A 2D image's index texture, its mod image given to the renderer.</summary>
    public static int Create(Renderer renderer, ModImages? mods, string name, Texture texture, Palette palette, int paletteId)
    {
        var id = renderer.CreateIndexTexture(texture.Width, texture.Height, texture.Indices);
        Replace(renderer, mods, name, texture, palette, id, paletteId);
        return id;
    }

    /// <summary>Gives an existing index texture its mod image, if any.</summary>
    public static void Replace(Renderer renderer, ModImages? mods, string name, Texture texture, Palette palette, int textureId, int paletteId)
    {
        if (mods?.Image(name, texture, palette) is { } image)
        {
            renderer.ReplaceImage(textureId, paletteId, image.Width, image.Height, image.Rgba);
        }
    }

    /// <summary>An animation's frame's name in mods (PICKUPS_3).</summary>
    public static string FrameName(string animation, int frame) => $"{animation}_{frame}";
}
