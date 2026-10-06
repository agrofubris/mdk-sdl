using System.Numerics;
using Mdk.Engine.Audio;
using Mdk.Formats;
using Mdk.Game.Audio;
using Mdk.Game.Collision;
using Mdk.Game.Level;
using Mdk.Game.Scripts;

namespace Mdk.Game.Tests;

/// <summary>Bullet holes stamped into textures (special_130, stamp_bullet_hole).</summary>
public class BulletHoleTests
{
    private const int Size = 8;
    private const byte Wall = 7;
    private const byte Dark = 3;

    private static Texture Plain(int width, int height, byte index) =>
        new() { Width = width, Height = height, Indices = Enumerable.Repeat(index, width * height).ToArray() };

    [Fact]
    public void HoleIsCentredOnTheTexelAndKeepsItsTransparentPixels()
    {
        var texture = Plain(Size, Size, Wall);
        // A 3 x 3 hole whose corners are transparent.
        var hole = new Texture { Width = 3, Height = 3, Indices = [0, Dark, 0, Dark, Dark, Dark, 0, Dark, 0] };
        BulletHoles.Stamp(texture, new Vector2(4f, 4f), hole);
        Assert.Equal(Dark, texture.Indices[4 * Size + 4]);
        Assert.Equal(Dark, texture.Indices[3 * Size + 4]);
        Assert.Equal(Wall, texture.Indices[3 * Size + 3]);
        Assert.Equal(5, texture.Indices.Count(i => i == Dark));
    }

    [Fact]
    public void HoleWrapsAroundTheEdges()
    {
        var texture = Plain(Size, Size, Wall);
        var hole = Plain(2, 2, Dark);
        BulletHoles.Stamp(texture, Vector2.Zero, hole);
        // Centred on (0, 0): from (-1, -1) to (0, 0).
        Assert.Equal(Dark, texture.Indices[(Size - 1) * Size + Size - 1]);
        Assert.Equal(Dark, texture.Indices[0]);
    }

    [Fact]
    public void NearestTexturedFaceGivesTheWeights()
    {
        var part = new Model.Part
        {
            TriangleIndices = [0, 1, 2, 3, 4, 5],
            TriangleMaterials = [0, 1],
        };
        Vector3[] vertices = [new(0f, 0f, 0f), new(1f, 0f, 0f), new(0f, 1f, 0f), new(0f, 0f, 5f), new(1f, 0f, 5f), new(0f, 1f, 5f)];
        var point = new Vector3(0.5f, 0.25f, 4.5f);
        var face = BulletHoles.Nearest(part, vertices, point, _ => true)!.Value;
        Assert.Equal(1, face.Triangle);
        Assert.Equal(0.25f, face.Weights.X, 1e-4f);
        Assert.Equal(0.5f, face.Weights.Y, 1e-4f);
        Assert.Equal(0.25f, face.Weights.Z, 1e-4f);

        // The upper face isn't textured: the lower one is taken.
        Assert.Equal(0, BulletHoles.Nearest(part, vertices, point, m => m == 0)!.Value.Triangle);
    }

    [DataFact]
    public void SniperHitStampsTheObjectsTexture()
    {
        var data = MdkData.Find()!;
        var level = new LevelData(data, 7);
        var cmi = Cmi.Load(data.PathOf("TRAVERSE/LEVEL7/LEVEL7.CMI"));
        var sprites = Bni.Load(data.PathOf("TRAVERSE/TRAVSPRT.BNI"));
        var space = new ArenaSpace();
        using var device = new AudioDevice(Output.Muted);
        var mixer = new SoundMixer(device, _ => null);
        var runtime = new ScriptRuntime(level, cmi, sprites, space, new TriangleGroups(), mixer, new Kurt.Kurt(space, mixer, _ => 1));
        var obj = runtime.Spawn(runtime.GetArenaState("DANT_1").Controller, "SW_H150", new Vector3(-11f, -13f, 0.5f), 0f, -1, 0,
            ScriptRuntime.Spawning.Plain)!;
        Texture? stamped = null;
        runtime.TextureStamped += texture => stamped = texture;

        obj.ShotPart = 1;
        obj.ShotPoint = obj.Position + obj.Pose()[0][0] * obj.Scale;
        runtime.StampBulletHole(obj);

        Assert.NotNull(stamped);
        var hole = sprites.GetImage(BulletHoles.Hole);
        Assert.Contains(hole.Indices.First(i => i != 0), stamped.Indices);
    }
}
