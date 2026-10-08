using System.Buffers.Binary;
using System.Security.Cryptography;
using Mdk.Formats;

namespace Mdk.Game.HdTextures;

/// <summary>The name of an HD image: a hash of what the texture shows through a palette (its size,
/// frames, indices, and the colours of the indices it uses). Arenas whose palettes differ only in
/// colours a texture doesn't use share its image; a changed texture or colour makes another key, so
/// a cache made from other game files is never used (stale).
/// <code>
///   SHA-256(width, height, frames, indices, RGB of each used index 1-255) ─► first 8 bytes, hex
/// </code></summary>
public static class HdKey
{
    private const int KeyBytes = 8;
    private const int HeaderInts = 3;
    private const int Channels = 4;
    private const int Rgb = 3;

    public static string Of(Texture texture, Palette palette)
    {
        Span<bool> used = stackalloc bool[Palette.Size];
        foreach (var index in texture.Indices)
        {
            used[index] = true;
        }

        // Index 0 is always clear: its colour shows nowhere.
        used[0] = false;
        var colours = new List<byte>();
        for (var i = 0; i < Palette.Size; i++)
        {
            if (used[i])
            {
                colours.Add((byte)i);
                colours.AddRange(palette.Rgba.AsSpan(i * Channels, Rgb));
            }
        }

        var header = new byte[HeaderInts * sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, texture.Width);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(sizeof(int)), texture.Height);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(2 * sizeof(int)), texture.FrameCount);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(header);
        hash.AppendData(texture.Indices);
        hash.AppendData(colours.ToArray());
        return Convert.ToHexStringLower(hash.GetHashAndReset().AsSpan(0, KeyBytes));
    }
}
