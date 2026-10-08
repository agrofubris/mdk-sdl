namespace Mdk.Game.HdTextures;

/// <summary>An image replacing a texture for the renderer: <see cref="Frames"/> frames of
/// <see cref="Width"/> x <see cref="Height"/> stacked downwards, RGBA8 premultiplied (as its colour
/// textures). Made from a mod's files (<see cref="Mods.ModImages"/>).</summary>
public sealed record HdImage(int Width, int Height, int Frames, byte[] Rgba);
