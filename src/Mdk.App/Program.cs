using System.Globalization;
using System.Numerics;
using Mdk.Formats;
using Mdk.Game;

// MDK in C# and SDL3: Kurt walks a level (WASD/arrows, mouse, Space jumps, Shift runs, F1 flying
// camera with E/Q up and down, F12 screenshot, Esc quits).
//
//   --level=N               level 3-8 (default 3)
//   --at=x,y,z[,yaw]        Kurt's feet (MDK coordinates) and yaw in degrees
//   --pitch=degrees         flying camera pitch (positive looks up)
//   --fly                   start with the flying camera
//   --mute                  no sound (tests)
//   --screenshot=file.bmp   save a frame after --wait seconds of game time, print Kurt, quit (tests)
//   --wait=seconds          game time before the screenshot
//   --walk=seconds          hold "forward" for this long (tests)
//   --jump                  hold "jump" (tests)
//   --profile               print the scripted objects once per second of game time (tests)

const int DefaultLevel = 3;

var options = args.Where(a => a.StartsWith("--"))
    .Select(a => a[2..].Split('=', 2))
    .ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "");

var data = MdkData.Find();
if (data == null)
{
    Console.Error.WriteLine("MDK data not found: install MDK (GOG/Steam), put this folder in it, or set MDK_DATA_DIR.");
    return 1;
}

Vector3? position = null;
float? yaw = null;
if (options.TryGetValue("at", out var at))
{
    var v = at.Split(',').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
    position = new Vector3(v[0], v[1], v[2]);
    yaw = v.Length > 3 ? v[3] : null;
}

var level = options.TryGetValue("level", out var levelText) ? int.Parse(levelText) : DefaultLevel;
var pitch = options.TryGetValue("pitch", out var pitchText) ? float.Parse(pitchText, CultureInfo.InvariantCulture) : 0f;
options.TryGetValue("screenshot", out var screenshot);
var sound = options.ContainsKey("mute") ? SoundMode.Muted : SoundMode.On;
float Seconds(string name) => options.TryGetValue(name, out var text) ? float.Parse(text, CultureInfo.InvariantCulture) : 0f;
Viewer.Run(data, new ViewerOptions(level, position, yaw, pitch, sound)
{
    Screenshot = screenshot,
    Wait = Seconds("wait"),
    Walk = Seconds("walk"),
    Jump = options.ContainsKey("jump"),
    Fly = options.ContainsKey("fly"),
    Profile = options.ContainsKey("profile"),
});
return 0;
