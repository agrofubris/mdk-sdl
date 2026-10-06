using System.Numerics;
using Mdk.Engine.Audio;
using Mdk.Formats;
using Mdk.Game.Audio;
using Mdk.Game.Collision;
using Mdk.Game.Level;
using Mdk.Game.Scripts;

namespace Mdk.Game.Tests;

/// <summary>The cutscene camera (0x477d94, get_cutscene_camera) and camera_track's pitch.</summary>
public class CutsceneCameraTests
{
    private const int Level = 7;
    private const string Arena = "DANT_1";
    private const string Model = "SW_H150";
    /// <summary>special_event 93: the boss shot, a fixed camera looking 3 units above the target.</summary>
    private const int BossEvent = 93;
    private static readonly Vector3 BossCamera = new(1158f, 5006f, 315f);
    private const float LookAbove = 3f;
    private const float Precision = 1e-3f;

    [Theory]
    [InlineData(0f, 0f, 0f, 1f, 0f)]
    [InlineData(90f, 0f, 1f, 0f, 0f)]
    [InlineData(0f, 90f, 0f, 0f, -1f)]
    public void YawIsNinetyMinusHeadingAndPitchLooksDown(float yaw, float pitch, float x, float y, float z)
    {
        var forward = CutsceneCamera.ForwardOf(yaw, pitch);
        Assert.Equal(x, forward.X, Precision);
        Assert.Equal(y, forward.Y, Precision);
        Assert.Equal(z, forward.Z, Precision);
    }

    [DataFact]
    public void BossShotLooksAtTheTarget()
    {
        var data = MdkData.Find()!;
        var level = new LevelData(data, Level);
        var cmi = Cmi.Load(data.PathOf($"TRAVERSE/LEVEL{Level}/LEVEL{Level}.CMI"));
        var sprites = Bni.Load(data.PathOf("TRAVERSE/TRAVSPRT.BNI"));
        var space = new ArenaSpace();
        using var device = new AudioDevice(Output.Muted);
        var mixer = new SoundMixer(device, _ => null);
        var runtime = new ScriptRuntime(level, cmi, sprites, space, new TriangleGroups(), mixer, new Kurt.Kurt(space, mixer, _ => 1));
        var target = runtime.Spawn(runtime.GetArenaState(Arena).Controller, Model, new Vector3(1100f, 4900f, 300f), 0f, -1, 0,
            ScriptRuntime.Spawning.Plain)!;

        runtime.SpecialEvent(target, BossEvent);
        Assert.False(CutsceneCamera.Active(runtime));
        runtime.Update(ScriptRuntime.Tick);
        Assert.True(CutsceneCamera.Active(runtime));

        var camera = new CutsceneCamera();
        camera.Update(runtime);
        var expected = Vector3.Normalize(target.Position + Vector3.UnitZ * LookAbove - BossCamera);
        Assert.Equal(BossCamera, camera.Position);
        Assert.Equal(expected.X, camera.Forward.X, Precision);
        Assert.Equal(expected.Y, camera.Forward.Y, Precision);
        Assert.Equal(expected.Z, camera.Forward.Z, Precision);
    }
}
