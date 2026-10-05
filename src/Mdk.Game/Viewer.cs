using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Mdk.Engine.Audio;
using Mdk.Engine.Platform;
using Mdk.Engine.Render;
using Mdk.Formats;
using Mdk.Game.Audio;
using Mdk.Game.Collision;
using Mdk.Game.Kurt;
using Mdk.Game.Level;

namespace Mdk.Game;

/// <summary>How a session starts, and what tests make it do: hold keys for some seconds, then save
/// a frame and quit.</summary>
public sealed record ViewerOptions(int Level, Vector3? Position, float? Yaw, float Pitch, SoundMode Sound)
{
    /// <summary>Save a frame after <see cref="Wait"/> seconds of game time, print Kurt's state, and quit.</summary>
    public string? Screenshot { get; init; }
    public float Wait { get; init; }
    /// <summary>Hold "forward" this many seconds from the start.</summary>
    public float Walk { get; init; }
    public bool Jump { get; init; }
    public bool Fly { get; init; }
}

public enum SoundMode { On, Muted }

/// <summary>Plays a level: Kurt walks it (F1 switches to a flying camera).
/// <code>
///   input ──► Kurt (60 steps/s) ──► camera ──► level, sky, Kurt's sprite ──► frame
///                └── mixer (listener at the camera)
/// </code></summary>
public static class Viewer
{
    private const int WindowWidth = 1280;
    private const int WindowHeight = 960;
    /// <summary>Kurt moves in fixed steps; the tests' frames are steps too.</summary>
    private const float Step = 1f / 60f;
    /// <summary>Steps caught up at most after a slow frame.</summary>
    private const int MaxSteps = 10;
    /// <summary>The start is slightly below the landing pad (the original lands Kurt by chute).</summary>
    private const float StartDrop = 3f;

    public static void Run(MdkData data, ViewerOptions options)
    {
        var loading = Stopwatch.StartNew();
        var level = new LevelData(data, options.Level);
        using var window = new Window($"MDK - level {options.Level}", WindowWidth, WindowHeight);
        using var renderer = new Renderer(window);
        using var audio = new AudioDevice(options.Sound == SoundMode.Muted ? Output.Muted : Output.Speakers);
        var view = new LevelView(renderer, level, new TriangleGroups());
        var mixer = new SoundMixer(audio, SoundBank.ForLevel(data, options.Level).Get);
        PlayMusic(mixer, data, level);
        renderer.Panorama = CreatePanorama(renderer, level.Dti);

        var space = new ArenaSpace();
        foreach (var arena in level.Arenas.Where(a => level.IsReachable(a.Name)))
        {
            space.Add(arena);
        }

        var sprite = new KurtSprite(renderer, Bni.Load(data.PathOf("TRAVERSE/TRAVSPRT.BNI")), level.Dti.Palette);
        var kurt = new Kurt.Kurt(space, mixer, sprite.FrameCount)
        {
            Feet = options.Position ?? level.Dti.StartPosition + new Vector3(0f, 0f, StartDrop),
            Yaw = options.Yaw ?? level.Dti.StartAngle,
        };
        var follow = new FollowCamera();
        var fly = new FreeCamera { Pitch = options.Pitch };
        var flying = options.Fly;
        Console.WriteLine($"Level {options.Level}: {level.Arenas.Count} arenas, {view.TriangleCount} triangles, loaded in {loading.ElapsedMilliseconds} ms");

        var input = new Input();
        var test = options.Screenshot != null;
        var clock = Stopwatch.StartNew();
        var time = 0f;
        var pending = 0f;
        window.CaptureMouse(test ? Capture.Off : Capture.On);
        while (window.PumpEvents(input) && !input.WasPressed(Key.Escape))
        {
            var elapsed = test ? Step : (float)clock.Elapsed.TotalSeconds;
            clock.Restart();
            if (input.WasPressed(Key.Fly))
            {
                flying = !flying;
                fly.Position = follow.Position;
                fly.Yaw = kurt.Yaw;
            }

            // Tests ignore the real mouse.
            if (test)
            {
                input.ClearMouse();
            }

            // Kurt in fixed steps; the tests' keys by game time.
            pending = MathF.Min(pending + elapsed, Step * MaxSteps);
            while (pending >= Step)
            {
                pending -= Step;
                time += Step;
                input.Hold(Key.Forward, time <= options.Walk ? Input.State.Down : Input.State.Up);
                input.Hold(Key.Jump, options.Jump ? Input.State.Down : Input.State.Up);
                if (flying)
                {
                    fly.Update(input, Step);
                }
                else
                {
                    kurt.Update(input, Step);
                    follow.Update(kurt, ArenaPitch(level, space, kurt.Feet), input, Step);
                }

                input.ClearMouse();
            }

            var camera = flying ? fly.View(renderer.AspectRatio) : follow.View(renderer.AspectRatio);
            mixer.ListenerPosition = camera.Position;
            mixer.ListenerRight = flying ? fly.Right : Vector3.Normalize(Vector3.Cross(follow.Forward, Vector3.UnitZ));
            mixer.Update(elapsed);
            audio.Update(elapsed);
            view.Draw();
            sprite.Draw(kurt, camera.Position, flying ? fly.Forward : follow.Forward, flying ? Vector3.UnitZ : follow.Up,
                flying ? FreeCamera.FieldOfView : FollowCamera.FieldOfView);

            var save = SavePath(options, input, time);
            renderer.Present(camera, SkyColour(level.Dti), save);
            if (save == null)
            {
                continue;
            }

            Console.WriteLine($"Saved {save}");
            if (test)
            {
                Report(kurt, space);
                return;
            }
        }
    }

    /// <summary>The test's screenshot once its wait is over, or F12's.</summary>
    private static string? SavePath(ViewerOptions options, Input input, float time)
    {
        if (options.Screenshot != null)
        {
            return time >= options.Wait ? options.Screenshot : null;
        }

        return input.WasPressed(Key.Screenshot) ? $"mdk-{DateTime.Now:yyyyMMdd-HHmmss}.bmp" : null;
    }

    /// <summary>For tests: where Kurt is and what he does.</summary>
    private static void Report(Kurt.Kurt kurt, ArenaSpace space)
    {
        var f = kurt.Feet;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Kurt at {f.X:0.00} {f.Y:0.00} {f.Z:0.00} yaw {kurt.Yaw:0} {kurt.Current} floor {kurt.OnFloor} arena {space.ArenaAt(f)}"));
    }

    /// <summary>The camera pitch of the arena around the feet (DTI, degrees).</summary>
    private static float ArenaPitch(LevelData level, ArenaSpace space, Vector3 feet)
    {
        const float DefaultPitch = 4f;
        var name = space.ArenaAt(feet);
        return level.Dti.Arenas.FirstOrDefault(a => a.Name == name)?.Pitch ?? DefaultPitch;
    }

    /// <summary>The starting arena's music (named by the level's CMI, in <c>LEVELnO.SNI</c>).</summary>
    private static void PlayMusic(SoundMixer mixer, MdkData data, LevelData level)
    {
        var cmi = Cmi.Load(data.PathOf($"TRAVERSE/LEVEL{level.Number}/LEVEL{level.Number}.CMI"));
        var arena = level.Dti.Arenas[level.Dti.StartArena].Name;
        if (cmi.ArenaMusic.TryGetValue(arena, out var music) && mixer.Play(music) != 0)
        {
            Console.WriteLine($"Music: {music}");
        }
    }

    /// <summary>The level's sky and the panorama its mirrors show, through the level's palette.</summary>
    private static Panorama CreatePanorama(Renderer renderer, Dti dti)
    {
        var sky = renderer.CreateIndexTexture(dti.Sky.Width, dti.Sky.Height, dti.Sky.Indices);
        var mirrorSky = dti.MirrorSky == dti.Sky ? sky : renderer.CreateIndexTexture(dti.MirrorSky.Width, dti.MirrorSky.Height, dti.MirrorSky.Indices);
        var palette = renderer.CreatePalette(dti.Palette.Rgba);
        return new Panorama(sky, mirrorSky, palette, dti.SkyWrapWidth, dti.SkyHorizonRow, dti.SkyOffset, dti.Sky.Height,
            dti.SkyTopColor, dti.SkyBottomColor);
    }

    /// <summary>The colour above the sky panorama.</summary>
    private static Vector4 SkyColour(Dti dti)
    {
        var at = dti.SkyTopColor * 4;
        var rgba = dti.Palette.Rgba;
        return new Vector4(rgba[at], rgba[at + 1], rgba[at + 2], byte.MaxValue) / byte.MaxValue;
    }
}
