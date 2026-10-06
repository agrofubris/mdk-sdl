using System.Globalization;
using Mdk.Engine.Audio;
using Mdk.Engine.Platform;
using Mdk.Game.Kurt;

namespace Mdk.Game.Flow;

/// <summary>The player's settings (settings.gd): volumes, the music filter, the mouse, the window,
/// the difficulty, gore and the key bindings (one key or mouse button per action, by name). Saved as
/// <c>name=value</c> lines in the user's folder; applied at start and whenever they change.
/// <code>
///   master_volume=80
///   bind.Forward=W
/// </code></summary>
public sealed class Settings
{
    public const int MaxVolume = 100;
    private const string FileName = "settings.cfg";
    private const string BindPrefix = "bind.";

    /// <summary>The game's actions in the order the controls screen lists them, with their names.</summary>
    public static readonly IReadOnlyList<(Key Key, string Name)> Actions =
    [
        (Key.Forward, "Forward"), (Key.Back, "Back"), (Key.TurnLeft, "Turn left"), (Key.TurnRight, "Turn right"),
        (Key.StrafeLeft, "Strafe left"), (Key.StrafeRight, "Strafe right"), (Key.Jump, "Jump"), (Key.Turbo, "Run"),
        (Key.Fire, "Fire"), (Key.Sniper, "Sniper mode"), (Key.ZoomIn, "Zoom in"), (Key.ZoomOut, "Zoom out"),
        (Key.UseItem, "Use item"), (Key.ItemNext, "Next item"), (Key.ItemPrevious, "Previous item"),
    ];

    /// <summary>Volumes from 0 to 100.</summary>
    public int MasterVolume = 80;
    public int MusicVolume = 50;
    public int EffectsVolume = 70;
    public bool MusicFilter = true;
    /// <summary>Mouse sensitivity multiplier (0.25-3) and inverted vertical look.</summary>
    public float MouseSensitivity = 1f;
    public bool InvertMouse;
    public bool Fullscreen;
    public Difficulty Difficulty = Difficulty.Normal;
    /// <summary>Gore (0x5742dc, on by default): green sparks, slime, blown off parts, the head shots row.</summary>
    public bool Gore = true;
    /// <summary>Rebound actions: action → key or mouse button name (see <see cref="Input.Bind"/>).</summary>
    public readonly Dictionary<Key, string> Bindings = [];

    /// <summary>The settings file in a user folder.</summary>
    public static string PathIn(string folder) => Path.Combine(folder, FileName);

    public static Settings Load(string path) => File.Exists(path) ? Parse(File.ReadAllText(path)) : new Settings();

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Format());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Settings not saved: {e.Message}");
        }
    }

    /// <summary>Settings from their lines; unknown or bad lines keep the defaults.</summary>
    public static Settings Parse(string text)
    {
        var settings = new Settings();
        foreach (var line in text.Split('\n'))
        {
            var parts = line.Trim().Split('=', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            settings.Set(parts[0].Trim(), parts[1].Trim());
        }

        return settings;
    }

    public string Format()
    {
        var lines = new List<string>
        {
            $"master_volume={MasterVolume}",
            $"music_volume={MusicVolume}",
            $"effects_volume={EffectsVolume}",
            $"music_filter={MusicFilter}",
            $"mouse_sensitivity={MouseSensitivity.ToString(CultureInfo.InvariantCulture)}",
            $"invert_mouse={InvertMouse}",
            $"fullscreen={Fullscreen}",
            $"difficulty={Difficulty}",
            $"gore={Gore}",
        };
        lines.AddRange(Bindings.Select(b => $"{BindPrefix}{b.Key}={b.Value}"));
        return string.Join('\n', lines) + "\n";
    }

    private void Set(string name, string value)
    {
        if (name.StartsWith(BindPrefix, StringComparison.Ordinal))
        {
            if (Enum.TryParse<Key>(name[BindPrefix.Length..], out var key))
            {
                Bindings[key] = value;
            }

            return;
        }

        switch (name)
        {
            case "master_volume":
                MasterVolume = Volume(value, MasterVolume);
                break;
            case "music_volume":
                MusicVolume = Volume(value, MusicVolume);
                break;
            case "effects_volume":
                EffectsVolume = Volume(value, EffectsVolume);
                break;
            case "music_filter":
                MusicFilter = bool.TryParse(value, out var filter) ? filter : MusicFilter;
                break;
            case "mouse_sensitivity":
                MouseSensitivity = float.TryParse(value, CultureInfo.InvariantCulture, out var sensitivity) ? sensitivity : MouseSensitivity;
                break;
            case "invert_mouse":
                InvertMouse = bool.TryParse(value, out var invert) ? invert : InvertMouse;
                break;
            case "fullscreen":
                Fullscreen = bool.TryParse(value, out var fullscreen) ? fullscreen : Fullscreen;
                break;
            case "difficulty":
                Difficulty = Enum.TryParse<Difficulty>(value, out var difficulty) ? difficulty : Difficulty;
                break;
            case "gore":
                Gore = bool.TryParse(value, out var gore) ? gore : Gore;
                break;
        }
    }

    private static int Volume(string value, int fallback) =>
        int.TryParse(value, out var volume) ? Math.Clamp(volume, 0, MaxVolume) : fallback;

    /// <summary>The volumes and filter, the window, the mouse and the bindings.</summary>
    public void Apply(AudioDevice audio, Window window, Input input)
    {
        audio.MasterGain = Gain(MasterVolume);
        audio.SetBusGain(Bus.Music, Gain(MusicVolume));
        audio.SetBusGain(Bus.Effects, Gain(EffectsVolume));
        audio.MusicFilter = MusicFilter ? Filter.On : Filter.Off;
        window.SetFullscreen(Fullscreen ? Engine.Platform.Fullscreen.On : Engine.Platform.Fullscreen.Off);
        input.MouseScale = MouseSensitivity;
        input.InvertMouse = InvertMouse;
        input.ResetBindings();
        foreach (var (key, control) in Bindings)
        {
            input.Bind(key, control);
        }
    }

    /// <summary>0-100 to a linear gain (the volume as a fraction, silent at 0).</summary>
    private static float Gain(int volume) => (float)volume / MaxVolume;
}
