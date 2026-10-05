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
using Mdk.Game.Objects;
using Mdk.Game.Scripts;

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
    /// <summary>Print the scripted objects once per second of game time.</summary>
    public bool Profile { get; init; }
}

public enum SoundMode { On, Muted }

/// <summary>Plays a level: Kurt walks it (F1 switches to a flying camera), the scripts run.
/// <code>
///   input ──► Kurt (60 steps/s) ──► scripts (30 ticks/s) ──► camera ──► level, objects, sky, Kurt ──► frame
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
        var groups = new TriangleGroups();
        var view = new LevelView(renderer, level, groups);
        var mixer = new SoundMixer(audio, SoundBank.ForLevel(data, options.Level).Get);
        var cmi = Cmi.Load(data.PathOf($"TRAVERSE/LEVEL{level.Number}/LEVEL{level.Number}.CMI"));
        PlayMusic(mixer, cmi, level);
        renderer.Panorama = CreatePanorama(renderer, level.Dti);

        var space = new ArenaSpace();
        foreach (var arena in level.Arenas.Where(a => level.IsReachable(a.Name)))
        {
            space.Add(arena);
        }

        var sprites = Bni.Load(data.PathOf("TRAVERSE/TRAVSPRT.BNI"));
        var sprite = new KurtSprite(renderer, sprites, level.Dti.Palette);
        var kurt = new Kurt.Kurt(space, mixer, sprite.FrameCount)
        {
            Feet = options.Position ?? level.Dti.StartPosition + new Vector3(0f, 0f, StartDrop),
            Yaw = options.Yaw ?? level.Dti.StartAngle,
        };
        var scripts = new ScriptRuntime(level, cmi, sprites, space, groups, mixer);
        scripts.Kurt.Teleported += (feet, yaw) => (kurt.Feet, kurt.Yaw) = (feet, yaw);
        scripts.ArenaEntered += view.Enter;
        var objects = new ObjectView(renderer, new MaterialResolver(renderer, level.Dti));
        var looks = new Dictionary<string, ObjectView.Look>();
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
                Feed(scripts.Kurt, kurt);
                scripts.Update(Step);
                if (options.Profile && MathF.Floor(time) > MathF.Floor(time - Step))
                {
                    Profile(scripts, time);
                }
            }

            var camera = flying ? fly.View(renderer.AspectRatio) : follow.View(renderer.AspectRatio);
            mixer.ListenerPosition = camera.Position;
            mixer.ListenerRight = flying ? fly.Right : Vector3.Normalize(Vector3.Cross(follow.Forward, Vector3.UnitZ));
            mixer.Update(elapsed);
            audio.Update(elapsed);
            scripts.Eye = camera.Position;
            view.Draw();
            DrawObjects(objects, scripts, level, looks);
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

    /// <summary>What the scripts see of Kurt this step.</summary>
    private static void Feed(KurtLink link, Kurt.Kurt kurt)
    {
        link.Position = kurt.Feet;
        link.Yaw = kurt.Yaw;
        link.OnFloor = kurt.OnFloor;
        link.Velocity = kurt.Facing * kurt.ForwardSpeed + kurt.Right * kurt.StrafeSpeed + Vector3.UnitZ * kurt.VerticalSpeed;
    }

    /// <summary>The visible objects, each with its arena's palette and textures.</summary>
    private static void DrawObjects(ObjectView view, ScriptRuntime scripts, LevelData level, Dictionary<string, ObjectView.Look> looks)
    {
        foreach (var obj in scripts.Objects)
        {
            if (!obj.Visible)
            {
                continue;
            }

            if (!looks.TryGetValue(obj.Arena, out var look))
            {
                var arena = level.Arenas.Find(a => a.Name == obj.Arena);
                look = looks[obj.Arena] = arena != null
                    ? new ObjectView.Look(level.PaletteOf(arena), level.ArchivesOf(arena))
                    : new ObjectView.Look(level.Dti.Palette, [level.LevelTextures]);
            }

            view.Draw(obj, look);
        }
    }

    /// <summary>For tests: the objects of Kurt's arena (like the Godot port's --profile).</summary>
    private static void Profile(ScriptRuntime scripts, float time)
    {
        var second = scripts.SecondArena + (scripts.SecondActive ? " (active)" : "");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Profile {time:0}s: objects {scripts.Objects.Count}, arena {scripts.CurrentArena}, second {second}"));
        foreach (var obj in scripts.Objects.Where(o => o.Arena == scripts.CurrentArena))
        {
            var p = obj.Position;
            var door = (obj.Flags & MdkObject.FlagDoor) != 0 ? $" door {obj.DoorState:x}" : "";
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {obj.TypeName}_{obj.InstanceId} {obj.Arena} ({Rounded(p.X)}, {Rounded(p.Y)}, {Rounded(p.Z)}) yaw {(int)obj.Yaw} " +
                $"move {obj.MoveCommand} path {obj.Path} anim {obj.Animation?.Name ?? "-"} frame {obj.AnimationFrame} " +
                $"speed {obj.Speed:0.0} health {obj.Health} flags {obj.Flags:x}{door}"));
        }

        if (scripts.Vm.Unimplemented.Count != 0)
        {
            Console.WriteLine($"  unimplemented opcodes: {string.Join(", ", scripts.Vm.Unimplemented.Select(u => $"{u.Key}x{u.Value}"))}");
        }
    }

    /// <summary>A coordinate rounded like Godot's Vector3.round().</summary>
    private static string Rounded(float value) =>
        (MathF.Round(value, MidpointRounding.AwayFromZero) + 0f).ToString("0.0", CultureInfo.InvariantCulture);

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
    private static void PlayMusic(SoundMixer mixer, Cmi cmi, LevelData level)
    {
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
