using Mdk.Game.Scripts;

namespace Mdk.Game.Tests;

/// <summary>Arena textures animated by opcode 133 (texture_set_frame 0x42d9c0).</summary>
public class AnimatedTextureTests
{
    private const string Arena = "DANT_5";
    private const string Comm = "M_COMM";
    private const int CommFrames = 6;
    private const float Tick = ScriptRuntime.Tick;

    private static AnimatedTextures Create() => new((arena, texture) => texture == Comm ? CommFrames : 0);

    [Fact]
    public void FramesPerSecondAddUpEachTick()
    {
        var textures = Create();
        // DANT_5's 6 frames a second: frame 3 after half a second.
        for (var tick = 0; tick < 16; tick++)
        {
            textures.Set(Arena, Comm, AnimatedTextures.Mode.PerSecond, 6f, Tick);
        }

        Assert.Equal(3, textures.FrameOf(Arena, Comm));
        Assert.Equal(0, textures.FrameOf("MEAT_3", Comm));
    }

    [Fact]
    public void FramesWrapForward()
    {
        var textures = Create();
        textures.Set(Arena, Comm, AnimatedTextures.Mode.Frames, 5f, Tick);
        textures.Set(Arena, Comm, AnimatedTextures.Mode.Frames, 2f, Tick);
        Assert.Equal(1, textures.FrameOf(Arena, Comm));
        textures.Set(Arena, Comm, AnimatedTextures.Mode.Frames, -2f, Tick);
        Assert.Equal(5, textures.FrameOf(Arena, Comm));
    }

    [Fact]
    public void AbsoluteFramesCountFromOne()
    {
        var textures = Create();
        textures.Set(Arena, Comm, AnimatedTextures.ModeOf(0), 4f, Tick);
        Assert.Equal(3, textures.FrameOf(Arena, Comm));
    }

    [Fact]
    public void TexturesWithoutFramesStay()
    {
        var textures = Create();
        textures.Set(Arena, "D5_WALL", AnimatedTextures.Mode.Frames, 2f, Tick);
        Assert.Empty(textures.Frames);
    }
}
