using System.Numerics;
using Mdk.Engine.Audio;
using Mdk.Formats;
using Mdk.Game.Audio;
using Mdk.Game.Collision;
using Mdk.Game.Level;
using Mdk.Game.Objects;
using Mdk.Game.Scripts;

namespace Mdk.Game.Tests;

/// <summary>Object moves through level 7's start arena (DANT_1, whose floor is at z 0).</summary>
public class ObjectMotionTests
{
    private const int Level = 7;
    private const string Arena = "DANT_1";
    /// <summary>A model of the level, on the floor where the runner pickup waits.</summary>
    private const string Model = "SW_H150";
    private static readonly Vector3 OnTheFloor = new(-11f, -13f, 0.5f);
    private const int Ticks = 5;

    private static readonly MdkData Data = MdkData.Find() ?? throw new InvalidOperationException("MDK data not found");
    private static readonly AudioDevice Device = new(Output.Muted);

    private static ScriptRuntime CreateRuntime()
    {
        var level = new LevelData(Data, Level);
        var cmi = Cmi.Load(Data.PathOf($"TRAVERSE/LEVEL{Level}/LEVEL{Level}.CMI"));
        var sprites = Bni.Load(Data.PathOf("TRAVERSE/TRAVSPRT.BNI"));
        var space = new ArenaSpace();
        var mixer = new SoundMixer(Device, _ => null);
        return new ScriptRuntime(level, cmi, sprites, space, new TriangleGroups(), mixer, new Kurt.Kurt(space, mixer, _ => 1));
    }

    /// <summary>An object thrown along the floor slides on it and touches it (a grenade lands).</summary>
    [Fact]
    public void ObjectSlidingAlongTheFloorTouchesIt()
    {
        var runtime = CreateRuntime();
        var obj = runtime.Spawn(runtime.GetArenaState(Arena).Controller, Model, OnTheFloor, 0f, -1, 0, ScriptRuntime.Spawning.Plain);
        Assert.NotNull(obj);
        obj.Flags = MdkObject.FlagGravity | MdkObject.FlagCollides;
        obj.Friction = 0f;
        obj.Velocity = new Vector3(20f, 0f, -5f);

        // It lands on the third tick, then slides on.
        for (var i = 0; i < Ticks; i++)
        {
            runtime.Motion.Update(obj);
        }

        Assert.True(obj.Position.X > OnTheFloor.X);
        Assert.NotEqual(0, obj.ContactFlags & MdkObject.ContactFloor);
    }
}
