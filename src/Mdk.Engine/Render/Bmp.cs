using System.Buffers.Binary;

namespace Mdk.Engine.Render;

/// <summary>Writes RGBA8 pixels as an uncompressed 32-bit BMP (top-down rows).</summary>
internal static class Bmp
{
    private const int FileHeaderSize = 14;
    private const int InfoHeaderSize = 40;
    private const int BitsPerPixel = 32;

    public static void Write(string path, int width, int height, byte[] rgba)
    {
        var dataSize = width * height * 4;
        var file = new byte[FileHeaderSize + InfoHeaderSize + dataSize];
        var span = file.AsSpan();

        // File header: "BM", size, reserved, pixel data offset.
        span[0] = (byte)'B';
        span[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(span[2..], file.Length);
        BinaryPrimitives.WriteInt32LittleEndian(span[10..], FileHeaderSize + InfoHeaderSize);

        // Info header: a negative height stores rows top-down.
        BinaryPrimitives.WriteInt32LittleEndian(span[14..], InfoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(span[18..], width);
        BinaryPrimitives.WriteInt32LittleEndian(span[22..], -height);
        BinaryPrimitives.WriteInt16LittleEndian(span[26..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[28..], BitsPerPixel);

        // BMP pixels are BGRA.
        var pixels = span[(FileHeaderSize + InfoHeaderSize)..];
        for (var i = 0; i < dataSize; i += 4)
        {
            pixels[i] = rgba[i + 2];
            pixels[i + 1] = rgba[i + 1];
            pixels[i + 2] = rgba[i];
            pixels[i + 3] = rgba[i + 3];
        }

        File.WriteAllBytes(path, file);
    }
}
