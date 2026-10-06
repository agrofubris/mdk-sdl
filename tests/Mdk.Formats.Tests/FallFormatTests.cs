using Mdk.Formats;

namespace Mdk.Formats.Tests;

/// <summary>The fall's files (<c>FALL3D/</c>; godot-mdk docs/formats.md, "Fall files").</summary>
public class FallFormatTests
{
    private const int Falls = 5;
    private const int HazeFrames = 16;
    private const int MaxHazeOffset = 8;

    private static readonly MdkData Data = MdkData.Find() ?? throw new InvalidOperationException("MDK data not found");
    private static readonly Bni Bni = Bni.Load(Data.PathOf("FALL3D/FALL3D.BNI"));

    [DataFact]
    public void PickupsOfEachFall()
    {
        Assert.Equal(["SW_HOME", "SW_GATT", "SW_HOME"], Fall3d.Pickups(Bni, "FALLPU_1"));
        for (var n = 2; n <= Falls; n++)
        {
            Assert.Equal(["SW_GATT", "SW_HBOMB", "SW_SGREN", "SW_HOME"], Fall3d.Pickups(Bni, $"FALLPU_{n}"));
        }
    }

    /// <summary>A clear middle and blend offsets 1-8 around it.</summary>
    [DataFact]
    public void HazeFrames600By180()
    {
        for (var i = 0; i < HazeFrames; i++)
        {
            var haze = Fall3d.Haze(Bni, $"ZOOM{i:0000}");
            Assert.Equal(Fall3d.HazeWidth * Fall3d.HazeRows, haze.Length);
            Assert.All(haze, b => Assert.InRange(b, 0, MaxHazeOffset));
            Assert.Equal(0, haze[Fall3d.HazeRows / 2 * Fall3d.HazeWidth + Fall3d.HazeWidth / 2]);
            Assert.Contains(haze, b => b > 0);
        }
    }

    /// <summary>Every model's textures and colours are in each fall's MTI (or <c>PEN_n</c> colours).</summary>
    [DataFact]
    public void ModelMaterialsResolve()
    {
        string[] named = ["KURT", "MISSILE", "CHUTE", "BONES", "SW_DUMMY", "SW_H150", "SW_THUMP", "SW_TWIST", "SW_INTER"];
        string[] single = ["EXPLODE", "SW_GATT", "SW_HBOMB", "SW_HOME", "SW_SGREN"];
        for (var n = 1; n <= Falls; n++)
        {
            var mti = TextureArchive.Load(Data.PathOf($"FALL3D/FALL3D_{n}.MTI"));
            Assert.Equal(1024, mti.Textures[$"LEVEL{n}"].Width);
            Assert.Equal(64, mti.Textures[$"POD{n}"].Width);
            foreach (var name in named.Concat(single).Where(Bni.Has))
            {
                var parts = named.Contains(name) ? Model.Parts.Named : Model.Parts.Single;
                var model = Model.Parse(name, Bni.Bytes, Bni.Entries[name].Offset, Model.Header.NoFlags, parts);
                var used = model.PartList.SelectMany(p => p.TriangleMaterials).Where(m => m >= 0).Distinct();
                Assert.All(used, m => Assert.True(mti.Textures.ContainsKey(model.Materials[m]) || mti.Colors.ContainsKey(model.Materials[m]) || model.Materials[m].StartsWith("PEN_"), $"{name}: {model.Materials[m]}"));
            }
        }
    }
}
