namespace Mdk.Formats.Tests;

/// <summary>The animations and stills of <c>MISC</c> decode.</summary>
public class MediaTests
{
    private static readonly MdkData Data = MdkData.Find() ?? throw new InvalidOperationException("MDK data not found");
    private const int SlideCount = 8;

    [DataTheory]
    [InlineData("MISC/FLIC/MDKEND.FLC")]
    [InlineData("MISC/FLIC/Mdk12.flc")]
    [InlineData("MISC/FLIC/pie.flc")]
    [InlineData("MISC/FLIC/shiny.flc")]
    public void FlcFramesDecode(string path)
    {
        var flc = Flc.Load(Data.PathOf(path));
        Assert.NotNull(flc);
        while (flc.NextFrame())
        {
        }

        Assert.Equal(flc.FrameCount, flc.Frame);
        Assert.Contains(flc.Indices, i => i != 0);
        Assert.Contains(flc.Palette, c => c != 0);
    }

    [DataFact]
    public void EndMovieDecodes()
    {
        var mve = Mve.Load(Data.PathOf("MISC/FLIC/MDKBZK.MVE"));
        Assert.NotNull(mve);
        var frames = 0;
        var audio = 0;
        while (mve.NextFrame())
        {
            frames++;
            audio += mve.Audio.Count;
        }

        Assert.True(frames > 1);
        Assert.True(audio > 0);
        Assert.True(mve.SampleRate > 0);
        Assert.Contains(mve.Indices, i => i != 0);
    }

    [DataFact]
    public void SlidesDecode()
    {
        for (var i = 1; i <= SlideCount; i++)
        {
            var gif = Gif.Load(Data.PathOf($"MISC/MDKS_{i:000}.GIF"));
            Assert.NotNull(gif);
            Assert.Equal(gif.Width * gif.Height, gif.Indices.Length);

            // A real picture uses many colours.
            Assert.True(gif.Indices.Distinct().Count() > 16, $"slide {i}");
        }
    }
}
