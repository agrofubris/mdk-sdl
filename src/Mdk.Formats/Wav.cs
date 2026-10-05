namespace Mdk.Formats;

/// <summary>PCM sound from a RIFF WAV (stand-alone like <c>MAINSONG</c>, or in SNI archives). Reads
/// only the <c>fmt </c> and <c>data</c> chunks: some files miscount a pad byte (<c>GATTFIRE</c>) or
/// truncate <c>LIST</c> chunks.</summary>
public sealed record Wav(int Channels, int SampleRate, int BitsPerSample, byte[] Data)
{
    private const int RiffHeader = 12;
    private const int ChunkHeader = 8;

    public static Wav? Parse(byte[] wav)
    {
        byte[]? format = null;
        byte[]? data = null;
        var pos = RiffHeader;
        while (pos + ChunkHeader <= wav.Length)
        {
            var id = Bin.Ascii(wav, pos, 4);
            // Truncated chunks can claim more than the file holds.
            var size = (int)Math.Min(Bin.U32(wav, pos + 4), (uint)(wav.Length - pos - ChunkHeader));
            var end = Math.Min(pos + ChunkHeader + size, wav.Length);
            var body = wav.AsSpan(pos + ChunkHeader, end - pos - ChunkHeader).ToArray();
            if (id == "fmt ")
            {
                format = body;
            }
            else if (id == "data")
            {
                data = body;
            }

            // Chunks are padded to an even size.
            pos += ChunkHeader + size + (size & 1);
        }

        if (format == null || data == null)
        {
            return null;
        }

        // fmt: u16 format, u16 channels, u32 rate, u32 byte rate, u16 align, u16 bits.
        return new Wav(Bin.U16(format, 2), (int)Bin.U32(format, 4), Bin.U16(format, 14), data);
    }
}
