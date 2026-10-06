namespace Mdk.Formats.Tests;

/// <summary>Data paths match the files whatever their case: the game asks for MISC/MDKFONT.FTI,
/// the installation has MISC/mdkfont.fti (case-sensitive file systems).</summary>
public sealed class PathTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mdk-paths").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void PartsMatchWhateverTheirCase()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_dir, "Misc", "FLIC")).FullName;
        File.WriteAllText(Path.Combine(_dir, "Misc", "mdkfont.fti"), "");
        File.WriteAllText(Path.Combine(folder, "Mdk12.flc"), "");

        Assert.Equal(Path.Combine(_dir, "Misc", "mdkfont.fti"), CaseInsensitivePath.Resolve(_dir, "MISC/MDKFONT.FTI"));
        Assert.Equal(Path.Combine(folder, "Mdk12.flc"), CaseInsensitivePath.Resolve(_dir, "MISC/FLIC/MDK12.FLC"));
    }

    [Fact]
    public void MissingFilesKeepTheirName()
    {
        Assert.Equal(Path.Combine(_dir, "MISC", "NONE.GIF"), CaseInsensitivePath.Resolve(_dir, "MISC/NONE.GIF"));
    }
}
