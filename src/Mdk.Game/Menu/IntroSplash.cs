using System.Numerics;
using Mdk.Engine.Platform;
using Mdk.Formats;

namespace Mdk.Game.Menu;

/// <summary>The <c>INTRO1A</c> splash (0x426edc) before the main menu: at start, after Kurt dies and
/// after the end movies. A 600 x 360 still (<c>MISC/OPTIONS.BNI</c>: two 768-byte palettes, then
/// run-length rows, 0x426e04) whose palette fades; any key skips it.
/// <code>
///   black ──1 s──► palette 1 ──3 s──► ──2 s──► palette 2 ──3 s──► ──1 s──► black
/// </code></summary>
public sealed class IntroSplash
{
    private const string Entry = "INTRO1A";
    private const int Width = 600;
    private const int Height = 360;
    private const int PaletteSize = 768;
    private const int FullWeight = 256;
    private const int WeightShift = 8;
    private const int CopyLimit = 127;
    private const int ByteRange = 256;
    /// <summary>Each step: seconds, palette at its start, palette at its end (0 black, 1, 2).</summary>
    private static readonly (float Seconds, int From, int To)[] Steps = [(1f, 0, 1), (3f, 1, 1), (2f, 1, 2), (3f, 2, 2), (1f, 2, 0)];

    private readonly Ui _ui;
    private readonly byte[][] _palettes = [];
    private readonly PalettedImage? _image;
    private int _step;
    private float _time;

    public bool Done { get; private set; }

    public IntroSplash(Ui ui, Bni options)
    {
        _ui = ui;
        if (!options.Has(Entry))
        {
            Done = true;
            return;
        }

        var bytes = Ui.BytesOf(options, Entry);
        _palettes = [new byte[PaletteSize], bytes[..PaletteSize], bytes[PaletteSize..(PaletteSize * 2)]];
        _image = new PalettedImage(ui.Renderer, Width, Height, Unpack(bytes, PaletteSize * 2, Width * Height), _palettes[0]);
    }

    public void Update(Input input, float delta)
    {
        if (Done)
        {
            return;
        }

        if (input.AnyPressed)
        {
            Done = true;
            return;
        }

        _time += delta;
        if (_time >= Steps[_step].Seconds)
        {
            _time = 0f;
            _step++;
            Done = _step >= Steps.Length;
        }
    }

    public void Draw()
    {
        if (Done || _image == null)
        {
            return;
        }

        var (seconds, from, to) = Steps[_step];
        _image.SetPalette(Blend(_palettes[from], _palettes[to], Math.Clamp(_time / seconds, 0f, 1f)));
        _image.Draw(_ui.View, Vector2.Zero);
    }

    /// <summary>The palette <paramref name="t"/> of the way between two (0x417040, 0x4170ec).</summary>
    public static byte[] Blend(byte[] from, byte[] to, float t)
    {
        var weight = (int)MathF.Round(t * FullWeight);
        var palette = new byte[from.Length];
        for (var i = 0; i < palette.Length; i++)
        {
            palette[i] = (byte)((from[i] * (FullWeight - weight) + to[i] * weight) >> WeightShift);
        }

        return palette;
    }

    /// <summary>Runs: a count above 127 copies 256 - count bytes, a smaller one repeats the next byte,
    /// 0 ends.</summary>
    public static byte[] Unpack(byte[] bytes, int p, int size)
    {
        var output = new byte[size];
        var n = 0;
        while (p < bytes.Length && n < size)
        {
            int count = bytes[p++];
            if (count == 0)
            {
                break;
            }

            if (count > CopyLimit)
            {
                var length = Math.Min(ByteRange - count, size - n);
                bytes.AsSpan(p, length).CopyTo(output.AsSpan(n));
                p += ByteRange - count;
                n += length;
                continue;
            }

            output.AsSpan(n, Math.Min(count, size - n)).Fill(bytes[p++]);
            n += count;
        }

        return output;
    }
}
