using System.Globalization;
using System.Numerics;
using Mdk.Formats;
using Mdk.Game;
using Mdk.Game.Flow;
using Mdk.Game.Menu;
using Mdk.Game.Scripts;

// MDK in C# and SDL3: the splash, the main menu, then the levels in the order 7, 6, 3, 4, 8, 5 with
// their loading screens, briefings, statistics and saves. In a level: WASD/arrows, mouse, Space
// jumps, Shift runs, Ctrl or the left mouse button fires, Enter uses the item, Tab/[/] and 1-5
// select it, the right mouse button toggles sniper mode (wheel or PageUp/PageDown zoom, Tab/[/]
// select the ammo), F1 flying camera with E/Q up and down, F12 screenshot, Esc the pause menu.
//
//   --level=N               play level 3-8 at once (no menu); 961, 963, 966: the 1996 demo's
//   --menu                  the main menu without the splash (--splash: with it)
//   --options, --controls   the menu's options or controls page (--beta-levels: the demo's levels)
//   --stats=N               the screens after level N (--phase=1-4 starts at a page,
//                           --counts=shots,hits,sniper,sniper hits,kills,enemies,heads, --towns=bits)
//   --briefing=N            the briefing of level N
//   --fall=N                the fall before level N, then the level (--profile prints it every second,
//                           --walk=seconds holds "forward" from --delay=seconds of fall)
//   --end                   the end movies
//   --stream=N              the stream after level N (--health=N: Kurt's health)
//   --load=NAME             load a saved game
//   --save=NAME             save the first level when it starts (tests)
//   --at=x,y,z[,yaw]        Kurt's feet (MDK coordinates) and yaw in degrees
//   --pitch=degrees         flying camera pitch (positive looks up)
//   --fly                   start with the flying camera
//   --mute                  no sound (tests)
//   --enhanced, --original  the enhanced or the original look instead of the settings' (tests)
//   --hidden                no window, frames drawn off screen (tests; also MDK_HIDDEN=1)
//   --screenshot=file.bmp   save a frame after --wait seconds of game time, print Kurt, quit (tests)
//   --wait=seconds          game time before the screenshot
//   --walk=seconds          hold "forward" for this long (tests)
//   --delay=seconds         start the held keys this late (tests; as the Godot port's --delay)
//   --jump                  hold "jump" (tests)
//   --fire                  hold "fire" (tests)
//   --give=SW_HBOMB,...     pickups Kurt starts with (tests)
//   --use                   press "use" after 1 second (tests)
//   --die                   Kurt is hurt to death after 1 second (tests)
//   --event=N               a special_event after 1 second: 1 ends the level (tests)
//   --profile               print the scripted objects once per second of game time (tests)
//   --sniper[=zoom[,pitch]] sniper mode once Kurt stands (zoom 1 to 0.25, pitch positive down; tests)
//   --zoom=seconds          hold "zoom in" in sniper mode (tests)
//   --sniper-fire           fire one sniper round once the clip is loaded (tests)
//   --strike[=dive]         Bones' full-screen strike after 1 second (dive: the plane only; tests)
//   --teleport=ARENA,x,y,z  teleport Kurt there after the delay (tests)
//   --kill=TYPE             kill the first object of that type after the delay (tests)
//   --ride=TYPE             put Kurt on the first walker of that type after the delay (tests)
//   --bomber[=drop]         LEVEL7: call the XE of DANT_5 after the delay, board it (drop: and drop a bomb; tests)
//   --snapshot=NAME         at the screenshot, a full save as F2 makes, its hash printed (tests)
//   --beta-teleport=N       the 1996 demo's teleport N after the delay (tests)
//   --roll=left|right       hold a roll of the 1996 demo's levels after the delay (tests)
//   --soak[=seed]           random seeded keys on every screen, 6 steps a frame, checks in a level
//                           (tests; --tour: every arena in turn during --wait; tests/soak_test.sh)

const int DefaultLevel = 7;

var options = args.Where(a => a.StartsWith("--"))
    .Select(a => a[2..].Split('=', 2))
    .ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "");

var data = MdkData.Find();
if (data == null)
{
    Console.Error.WriteLine("MDK data not found: install MDK (GOG/Steam), put this folder in it, set MDK_DATA_DIR, or name it as mdk in mdk_paths.cfg next to the program.");
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

int? Number(string name) => options.TryGetValue(name, out var text) ? int.Parse(text, CultureInfo.InvariantCulture) : null;
var level = Number("level") ?? Number("stats") ?? Number("briefing") ?? Number("stream") ?? Number("fall") ?? DefaultLevel;
if (BetaDemo.IsBeta(level) && BetaDemo.Find(data) == null)
{
    Console.Error.WriteLine("The 1996 beta demo not found: set MDK_BETA_DIR, or name it as beta in mdk_paths.cfg next to the program.");
    return 1;
}

var pitch = options.TryGetValue("pitch", out var pitchText) ? float.Parse(pitchText, CultureInfo.InvariantCulture) : 0f;
options.TryGetValue("screenshot", out var screenshot);
var sound = options.ContainsKey("mute") ? SoundMode.Muted : SoundMode.On;
float Seconds(string name) => options.TryGetValue(name, out var text) ? float.Parse(text, CultureInfo.InvariantCulture) : 0f;
var viewer = new ViewerOptions(level, position, yaw, pitch, sound)
{
    Screenshot = screenshot,
    Wait = Seconds("wait"),
    Walk = Seconds("walk"),
    Delay = Seconds("delay"),
    Jump = options.ContainsKey("jump"),
    Fire = options.ContainsKey("fire"),
    Give = options.TryGetValue("give", out var give) ? give.Split(',') : [],
    Use = options.ContainsKey("use"),
    Fly = options.ContainsKey("fly"),
    Profile = options.ContainsKey("profile"),
    Sniper = options.TryGetValue("sniper", out var sniper) ? SniperTest.Setup(sniper) : null,
    Zoom = Seconds("zoom"),
    SniperFire = options.ContainsKey("sniper-fire"),
    Strike = options.TryGetValue("strike", out var strike) ? (strike == "dive" ? StrikeScene.Plane.Only : StrikeScene.Plane.WithPilot) : null,
    Die = options.ContainsKey("die"),
    Event = Number("event"),
    Teleport = options.TryGetValue("teleport", out var teleport) ? TeleportTarget.Parse(teleport) : null,
    Kill = options.GetValueOrDefault("kill"),
    Ride = options.GetValueOrDefault("ride"),
    Bomber = options.TryGetValue("bomber", out var bomber) ? (bomber == "drop" ? BomberTest.Drop : BomberTest.Ride) : null,
    Snapshot = options.GetValueOrDefault("snapshot"),
    Soak = options.TryGetValue("soak", out var soak) ? (soak.Length != 0 ? int.Parse(soak, CultureInfo.InvariantCulture) : 1) : null,
    Route = options.ContainsKey("tour") ? SoakRoute.Tour : SoakRoute.Stay,
    BetaTeleport = Number("beta-teleport"),
    Roll = options.TryGetValue("roll", out var roll) ? (roll == "left" ? BetaRoll.Left : BetaRoll.Right) : null,
};

// Test options of a level (and --screenshot without --menu) skip the menu, as in the Godot port.
var start = options.ContainsKey("end") ? Start.EndMovie
    : options.ContainsKey("stats") ? Start.Statistics
    : options.ContainsKey("briefing") ? Start.Briefing
    : options.ContainsKey("stream") ? Start.Stream
    : options.ContainsKey("fall") ? Start.Fall
    : options.ContainsKey("level") || (screenshot != null && !options.ContainsKey("menu")) ? Start.Level
    : Start.Menu;
var page = options.ContainsKey("controls") ? MenuPage.Controls
    : options.ContainsKey("options") ? MenuPage.Options
    : options.ContainsKey("beta-levels") ? MenuPage.BetaLevels
    : MenuPage.Main;
// Tests run without a window: --hidden, or MDK_HIDDEN set (so nothing takes the focus).
var hidden = options.ContainsKey("hidden") || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MDK_HIDDEN"));
using var game = new Game(data, new GameOptions(start, viewer)
{
    Display = hidden ? Display.Hidden : Display.Window,
    Page = page,
    Splash = !options.ContainsKey("menu") || options.ContainsKey("splash"),
    Phase = Number("phase") is { } phase ? (StatsScreen.Phase)phase : null,
    Counts = options.TryGetValue("counts", out var counts) ? counts.Split(',').Select(c => int.Parse(c, CultureInfo.InvariantCulture)).ToList() : [],
    Towns = Number("towns"),
    Load = options.GetValueOrDefault("load"),
    Save = options.GetValueOrDefault("save"),
    Screenshot = screenshot,
    Wait = Seconds("wait"),
    Health = Number("health"),
    Graphics = options.ContainsKey("enhanced") ? Graphics.Enhanced : options.ContainsKey("original") ? Graphics.Original : null,
});
game.Run();
return 0;
