using Mdk.Formats;
using Mdk.Game.Level;

namespace Mdk.Game.Tests;

/// <summary>A level keeps the system colours (<c>SYS_PAL</c>) at palette 0-63: its load copies only
/// the DTI's 64-255 (0x41ba68: base palette + 0xc0 to 0x5736a4). LEVEL8's DTI has magenta and purple
/// at 10-12, which its aliens, shots and the grenade icon use.</summary>
public class SystemColoursTests
{
    private const int Level = 8;
    private const int Channels = 3;
    private static readonly int[] Changed = [10, 11, 12];

    private static readonly MdkData Data = MdkData.Find() ?? throw new InvalidOperationException("MDK data not found");

    [DataFact]
    public void LevelKeepsSystemColours()
    {
        var system = Fti.Load(Data.PathOf("MISC/MDKFONT.FTI")).GetBytes("SYS_PAL");
        var level = new LevelData(Data, Level);

        foreach (var index in Changed)
        {
            var rgba = level.Dti.Palette.Rgba.AsSpan(index * 4, Channels);
            Assert.True(rgba.SequenceEqual(system.AsSpan(index * Channels, Channels)), $"colour {index}");
        }
    }
}
