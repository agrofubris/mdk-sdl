# MDK in C# and SDL3

![Status: work in progress](https://img.shields.io/badge/status-work%20in%20progress-orange)
![Progress: about 78%](https://img.shields.io/badge/progress-~78%25-yellow)
![.NET 10](https://img.shields.io/badge/.NET-10-512bd4?logo=dotnet&logoColor=white)
![SDL3](https://img.shields.io/badge/SDL-3-blue)

A port of [MDK](https://en.wikipedia.org/wiki/MDK_(video_game)) (Shiny Entertainment, 1997) to
C# on [SDL3](https://libsdl.org/). It's the successor of the Godot port
[mdk-godot](https://github.com/nemo22/mdk-godot) and reuses its reverse engineering of the
original game's data files and executable (`MDKD3D.EXE`). Instead of Godot's renderer and physics
it renders and collides like the original: Kurt moves through the arenas' BSP trees with the
original's box sweeps.

> [!IMPORTANT]
> **You need the original game to play.** This port contains no data from MDK: no levels,
> textures, models, sounds, music or videos. It reads them at run time from your own installed
> copy of the original game (available for example on GOG). Without the original game data the
> port won't run.

**Status: work in progress, not yet played through.** Every level loads, Kurt walks it and the level scripts run:
aliens spawn, walk and fly their paths, doors open, pickups fall on their chutes. Kurt fires his
chain gun, throws his items, gets hurt, knocked down and dies, and the HUD shows his health,
inventory, messages and the target's health bar. Aliens blow up into pieces, sparks fly, slime
bleeds and the fans lift Kurt. He grabs ledges and climbs them, slides down level 6's wind
tunnels on his back, gets hurt by hard landings and dies falling out of the arena; the camera
rolls, shakes and pushes him away from the walls behind him. He snipes through the scope with every kind of round and calls
Bones' air strike. Only Kurt's arena and the one behind an open door are drawn and solid, as in
the original; doors move into Kurt's arena, so they open from either side, and a teleport into
a corridor loads the arena behind it.
The game starts with the splash and the main menu (options, key bindings, saved
games) and plays the levels in order with their loading screens, briefings, the end of each level,
the statistics and the save prompt; when Kurt dies, "Continue" starts the level again. After each
level Kurt steers down the stream's tube until Bones' crane picks him up (after LEVEL8 he follows
Gunter to the planet). Before each level he falls from orbit onto the minecrawler, dodging the
radars' missiles and catching pickups. Kurt rides level 4's snowboard and level 7's walker
(`XD2`) and bomber (`XE`), the end of a level tears the arena apart around him as he rises, F2
makes a full save of the level, and the Score-O-matic spins its heads. Each arena plays its music with
the original's fades, scripts animate textures, switch the sky and film cutscenes, sniper rounds
leave bullet holes, and ropes, twisters' ribbons and the glass panes' outlines are drawn. An
enhanced look (Options, Graphics) filters the textures, lights the levels with a sun and its
shadows, and adds ambient occlusion, a little glow and a haze; anti-aliasing smooths the edges. The three
levels of the 1996 beta demo play as extras ([below](#the-1996-beta-demo)). The
[Godot port](https://github.com/nemo22/mdk-godot) is more complete for now.

## Progress

| Area | Done |
| --- | --- |
| Data formats: levels, textures, models, sprites, sounds, fonts, scripts, videos | █████████░ 90% |
| Rendering: arenas, glass, sky, mirrors, Kurt's sprite, effects | █████████░ 90% |
| Enhanced look: filtering, sun and shadows, occlusion, glow, haze; anti-aliasing | ████████░░ 80% |
| Collisions: the original's BSP | ████████░░ 80% |
| Kurt: walking, turning, jumping, chute, ledges, slides, camera, damage, death | █████████░ 90% |
| Sound mixer (the original's laws) and music | ████████░░ 80% |
| Script VM, aliens, objects, doors, effects, fans | ███████░░░ 70% |
| Weapons, items, sniper mode | ████████░░ 80% |
| HUD (health, inventory, messages, health bar) | ███████░░░ 70% |
| Menus, saves, level flow | ████████░░ 80% |
| The fall and the stream between levels, rides | ████████░░ 80% |
| Videos: the menu's FLC and slideshow, the end movies | ████████░░ 80% |
| Playtesting and bug fixing | ██░░░░░░░░ 20% |
| **Overall** | **about 78%** |

The plan is in [docs/architecture.md](docs/architecture.md#roadmap).

## Running

Windows x64, Linux x64 and macOS (Apple silicon): one executable each, `mdk.exe` or `mdk` (see
[Building](#building)). It is played on Windows; the Linux and macOS builds are only built and
started on CI so far. Put the program (or this folder) inside the MDK installation folder (for
example `C:\GOG Games\MDK\mdk-sdl`), install MDK in the default GOG or Steam location, or set the
`MDK_DATA_DIR` environment variable, or name the folder in a file `mdk_paths.cfg` next to the
program:

```
[paths]
mdk="C:/GOG Games/MDK"
beta="C:/Games/MDK (1996-08-06) (beta demo)"
```

On Linux and macOS copy the installed game's folder (from
Windows, Wine or the GOG installer); file names match whatever their case. macOS keeps a downloaded
program in quarantine: `xattr -d com.apple.quarantine mdk`.

```
mdk.exe
```

- No option: the splash, then the main menu.
- `--level=N`: play level N (3 to 8) at once, without the menu (the game plays them in the order
  7, 6, 3, 4, 8, 5); 961, 963 and 966 are the 1996 demo's.
- `--menu`: the main menu without the splash (`--splash` with it); `--options`, `--controls`,
  `--beta-levels` open those pages.
- `--stats=N`: the screens after level N (`--phase=1-4` starts at a page: 1 the Score-O-matic, 2
  the intermission, 3 the briefing, 4 the debriefing; `--counts=shots,hits,sniper,sniper
  hits,kills,enemies,heads`, `--towns=bits`); `--briefing=N` its briefing alone; `--end` the end
  movies.
- `--fall=N`: the fall before level N, then the level (`--profile` prints it every second,
  `--walk=seconds` holds "forward" from `--delay=seconds` of fall).
- `--stream=N`: the stream after level N (`--health=N` Kurt's health; its tube is always the same
  one, for tests).
- `--load=NAME`: load a saved game (a full save prints its hash); `--save=NAME`: save the first
  level when it starts; `--snapshot=NAME`: a full save (as F2) at the screenshot, its hash printed
  (tests).
- `--at=x,y,z[,yaw]`: where Kurt starts (MDK coordinates).
- `--fly`: start with the flying camera.
- `--mute`: no sound.
- `--enhanced`, `--original`: the enhanced or the original look instead of the settings' (not
  saved).
- `--bloodyes`, `--nobloodno`: gore on or off instead of the settings' (not saved; the original's
  `-bloodyes`, `-nobloodno`).
- Tests: `--screenshot=file.bmp` (after `--wait=seconds` of game time, then quit),
  `--walk=seconds`, `--delay=seconds` (held keys start later), `--jump`, `--fire`, `--give=SW_HBOMB,...` (pickups to start with), `--use`
  (uses the item after 1 second), `--profile` (prints the objects of Kurt's arena every second),
  `--trace` (prints Kurt, the camera and what carries him every frame),
  `--sniper[=zoom[,pitch]]` (sniper mode once Kurt stands), `--zoom=seconds` (zooms in),
  `--sniper-fire` (one sniper round), `--strike[=dive]` (Bones' full-screen strike), `--die` (Kurt
  dies after 1 second), `--event=N` (a `special_event` after 1 second: 1 ends the level). After
  `--delay`: `--teleport=ARENA,x,y,z`, `--kill=TYPE` (kills the first object of that type),
  `--ride=TYPE` (Kurt on that walker), `--bomber[=drop]` (level 7: calls the `XE`, boards it, and
  with `drop` drops a bomb once unlocked), `--beta-teleport=N` (the 1996 demo's teleport N),
  `--roll=left|right` (holds a roll of the demo's levels). `--console="pos;god"` opens the
  console and runs those commands, on any first screen (`--menu --console="map 6"`). With
  `--screenshot` but without `--level`, the screen shown after `--wait` seconds (menu,
  statistics...) is saved. `--soak[=seed]`: random but seeded keys on every screen (menu keys
  too when it starts in the menus), 0.1 s a frame; in a level Kurt is healed and problems print
  as `Soak problem` (`--tour`: every arena in turn during `--wait`, doors shut before each teleport).
  `--press=QuickSave@1,Menu.Accept@1.5`: presses game keys, or menu keys after `Menu.`, once at
  those times.

Settings (volumes, music filter, mouse, fullscreen, anti-aliasing, difficulty, graphics, gore, key
bindings) and saved games
are kept next to the executable (`settings.cfg`, `saves/*.sav`; `MDK_USER_DIR` overrides the
folder). The first run copies them from where older builds kept them (`%LOCALAPPDATA%/mdk-sdl`). `LASTGAME` (written when Kurt dies) is deleted at start, as in the original.

Controls: W/S or Up/Down to run, A/D to strafe, the mouse or Left/Right to turn, Space to jump
(hold it while falling to open the chute; running into a ledge while falling grabs it), Shift for turbo, Ctrl or the left mouse button to fire,
Enter or E to use the item, Tab, [ ] or the mouse wheel to select it (or 1-5), the right mouse button for sniper mode
(the mouse wheel or PageUp/PageDown zoom, Tab or [ ] select the ammo), F1 for the flying camera (E/Q to go up and
down), F2 for a quick save (the original's full save: the name is asked, the level's number
offered), F9 for a quick load (this session's last F2 save, else the newest save; "Nothing to
load" if none), F3 for the debug overlay, the key left of 1 for the console, F12 for a screenshot, Esc for the pause menu (resume, options, main menu, quit; it names the quick keys). Saving
and loading show "Game saved", "Game loaded" or "Can't save now" (sniper mode, rides) on the HUD;
outside a level the quick keys do nothing. The bindings
can be changed in Options, Controls (to any key, mouse button or wheel direction). In the menus: the arrows or the mouse, Enter or a click, Esc
back; the mouse cursor is the original's arrow. Typing `TOOSCARYFORME` in a level turns gore on or
off for the session, `SEETHEWHOLEGAME` the main menu's debug keys (3-8 start that level, F the fall before LEVEL8,
S the stream after LEVEL7, D the statistics with random counts).

### Console and debug overlay

On every screen (menus, briefing, statistics, videos, loading, the fall, the stream, a level),
F3 shows the debug overlay (its text made 4 times a second): frames per second and the frame's
time (scene, render, physics, scripts, audio), draw calls, triangles and the time the frame waited
for the GPU (in a window, render includes waiting for the display's refresh: vsync), memory (GC
heap, working set, collections), then the screen's
own lines: a menu's name; the fall's time, Kurt's height, health and pickups; the stream's
segment, speed and health; in a level Kurt's position, yaw and state, the level, his arena and
the objects. The key left of 1 (`` ` ``, `;` on a Slovak keyboard) opens the console: the game's
output above a command line (Tab completes a command, Up/Down the history, PageUp/PageDown
scroll, Esc closes). The screen runs on, without the keys. Outside a level `tp`, `pos`, `give`,
`health`, `kill` and `save` answer "Not in a level"; `god`, `noclip` and `onehit` hold from the next level;
`map` starts a level from the menus.

| Command | |
| --- | --- |
| `map <level> [arena]` | play a level, from a floor of that arena |
| `teleport`, `tp <x y z \| arena [x y z]>` | move Kurt (`tp HMO_4`, `tp 1 2 3`, `tp HMO_4,1,2,3`) |
| `pos` | where Kurt is, as `--level`, `--at`, `--teleport` and `--console` options |
| `noclip`, `god` | fly through everything (Space up, Q down); no damage |
| `onehit` | Kurt's hits (guns, sniper rounds, blasts, items, air strike) kill, unless the target's script gives its health back (LEVEL8's forklift) |
| `give all`, `give <SW_...>` | pickups (`give SW_HBOMB`) |
| `health <n>`, `kill` | Kurt's health; kill the enemies of his arena |
| `save <slot>`, `load <slot>` | a full save (as F2), a saved game |
| `difficulty <easy\|normal\|hard>`, `look <original\|enhanced>`, `aa <off\|2x\|4x>` | settings (saved; the look reloads the level) |
| `timescale <x>` | game time faster or slower (0 to 10) |
| `fps`, `help`, `clear`, `quit` | the overlay, the commands, clear the log, quit |

### The 1996 beta demo

`MDKDEMO.EXE` of 6 August 1996, a non-interactive DOS demo, has three levels in earlier versions of
the game's formats: the city (961), `HMO_1` (963) and `OLYM_1` (966). The port plays them (see the
Godot port's [docs/beta96.md](https://github.com/nemo22/mdk-godot/blob/main/docs/beta96.md)). It
isn't part of the port: you need your own unpacked copy, besides the retail game (menus, fonts and
sprites come from it). Tell the port where it is (the folder with `TRAVERSE` and `MDKDEMO.EXE`):
`beta` in `mdk_paths.cfg` (above), the `MDK_BETA_DIR` environment variable, or a folder `BETA96` or
`MDK (1996-08-06) (beta demo)` in or next to the game's folder or the program's. The main menu then
has a "Beta Levels" page; `--level=961` starts one at once (no briefing, fall or stream). They show
the demo's Kurt and health display; Kurt dies as in the retail game, and no "Continue" is saved.
Their own keys: Z and C roll left and right, T or Alt with a digit takes the demo's teleports (the
city needs T + 4 to reach the top of `ARENA_4`); the sniper key puts the helmet on first.

## Building

Needs the .NET 10 SDK and the shader compiler of the platform's GPU API (SDL_GPU):

| Platform | GPU API | Shaders | Tools |
| --- | --- | --- | --- |
| Windows | Direct3D 12 | DXIL | `dxc` of the Windows SDK, or of the [DirectXShaderCompiler release](https://github.com/microsoft/DirectXShaderCompiler/releases) |
| Linux (Windows with `SDL_GPU_DRIVER=vulkan`) | Vulkan | SPIR-V | `dxc` of the DirectXShaderCompiler release or of the [Vulkan SDK](https://vulkan.lunarg.com/sdk/home) |
| macOS | Metal | MSL | `dxc` (SPIR-V) and `spirv-cross` of the Vulkan SDK |

The build compiles every format whose tool runs and skips the others (it prints `Shaders: DXIL
true, SPIR-V false, MSL false`); the program uses the first one its GPU driver takes. The tools are
looked for at the Windows SDK's place (DXIL), in `$VULKAN_SDK/bin`, then on the PATH; or name them:
`-p:Dxc=...` (DXIL), `-p:DxcSpirv=...`, `-p:SpirvCross=...`. `-p:RequiredShaders="spirv msl"` fails
the build without those formats.

```
dotnet build
src/Mdk.App/bin/Debug/net10.0/mdk --level=3
```

One native executable (Native AOT; also needs the Visual Studio C++ tools on Windows, clang and
zlib on Linux, Xcode on macOS): `sh tools/publish.sh [win-x64|linux-x64|osx-arm64|osx-x64]` (this
machine's by default; `publish.bat` on Windows) gives `publish/mdk` or `publish/mdk.exe` (about 5 MB) and SDL3's library next to
it (`SDL3.dll`, `libSDL3.so`, `libSDL3.dylib`).

GitHub Actions (`.github/workflows/build.yml`) builds and tests every push on Windows, Linux and
macOS and publishes the three executables as the run's artifacts; a `v*` tag makes a release of
them.

## Tests

With the game data installed (without it `dotnet test` skips the tests that read it):

```
dotnet test
sh tests/screenshot_test.sh
sh tests/kurt_test.sh
sh tests/kurt_moves_test.sh
sh tests/combat_test.sh
sh tests/scripts_test.sh
sh tests/sniper_test.sh
sh tests/hud_test.sh
sh tests/effects_test.sh
sh tests/flow_test.sh
sh tests/stream_test.sh
sh tests/quicksave_test.sh
sh tests/fall_test.sh
sh tests/rides_test.sh
sh tests/carry_test.sh
sh tests/snapshot_test.sh
sh tests/end_level_test.sh
sh tests/visuals_test.sh
sh tests/enhanced_test.sh
sh tests/beta_test.sh
sh tests/console_test.sh
sh tests/alloc_test.sh
sh tests/soak_test.sh short
```

`tests/soak_test.sh` (about 3 minutes with `short`, 15 without) plays every level, arena, fall,
stream and menu with random keys and reports exceptions, hangs and `Soak problem` lines.
`MDK_REFERENCE=<another build's mdk> sh tests/enhanced_test.sh` also checks that the original look
is that build's, pixel for pixel.
`tests/beta_test.sh` and the demo's unit test are skipped without the 1996 demo.
`tests/alloc_test.sh` plays levels hidden with `--perf[=warmup seconds]` (each frame waits for the
GPU; at the end the frames' average times by section, bytes allocated per frame and garbage
collections are printed) and fails above 512 bytes a frame or on a gen 1-2 collection.

## Layout

- `src/Mdk.Formats`: MDK's file formats, no dependencies.
- `src/Mdk.Engine`: SDL3 behind a small API: the renderer (SDL_GPU), the audio mixer, the window
  and input.
- `src/Mdk.Game`: the game: levels, collisions, Kurt, the camera, the sound mixer's laws, scripts,
  the game's flow (`Flow/`: screens, settings, saves), its menus (`Menu/`), the stream (`Stream/`)
  and the fall (`Fall/`).
- `src/Mdk.App`: the program and its command line.
- `shaders/`: HLSL shaders, compiled to DXIL, SPIR-V and MSL at build time (`bindings.hlsli`:
  SDL_GPU's bindings).
- `docs/`: [the architecture and roadmap](docs/architecture.md). The knowledge base about MDK
  itself (formats, engine, scripts, BSP) is in the
  [Godot port's docs](https://github.com/nemo22/mdk-godot/tree/main/docs).
- `tests/`, `tools/`: tests, the opcode table generator and the publish script.
- `.github/workflows/`: CI builds and releases.

## Legal

This is an unofficial fan project, not affiliated with Shiny Entertainment or the current rights
holders of MDK. MDK and its data are the property of their respective owners.

This repository contains only original code, documentation and tools. It doesn't include or
distribute any data from the game. To run the port you need a legally obtained copy of the
original MDK, whose installed data files the port loads.
