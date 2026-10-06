namespace Mdk.Formats.Tests;

/// <summary><c>mdk_paths.cfg</c> (the Godot port's ConfigFile): the folders of the game and the demo.</summary>
public class LocalPathsTests
{
    private const string Config = """
        ; my folders
        [other]
        mdk="C:/Wrong"

        [paths]
        mdk="C:/GOG Games/MDK"
        beta = "C:/Games/MDK (1996-08-06) (beta demo)"
        plain=D:/MDK
        escaped="C:\\Games\\\"MDK\""
        """;

    [Theory]
    [InlineData(LocalPaths.Game, "C:/GOG Games/MDK")]
    [InlineData(LocalPaths.Beta, "C:/Games/MDK (1996-08-06) (beta demo)")]
    [InlineData("plain", "D:/MDK")]
    [InlineData("escaped", "C:\\Games\\\"MDK\"")]
    [InlineData("missing", "")]
    public void KeysOfThePathsSectionAreRead(string key, string expected) =>
        Assert.Equal(expected, LocalPaths.Parse(Config, key));

    [Fact]
    public void WindowsLineEndingsAreRead() =>
        Assert.Equal("C:/MDK", LocalPaths.Parse("[paths]\r\nmdk=\"C:/MDK\"\r\n", LocalPaths.Game));

    [Fact]
    public void NoFileNoPath() => Assert.Equal("", LocalPaths.Parse("", LocalPaths.Game));
}
