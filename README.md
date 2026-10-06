# MDK in C# and SDL3

![Status: work in progress](https://img.shields.io/badge/status-work%20in%20progress-orange)
![Progress: about 60%](https://img.shields.io/badge/progress-~60%25-yellow)
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

**Status: early work in progress.** Every level loads, Kurt walks it and the level scripts run:
aliens spawn, walk and fly their paths, doors open, pickups fall on their chutes. Kurt fires his
chain gun, throws his items, gets hurt, knocked down and dies, and the HUD shows his health,
inventory, messages and the target's health bar. Aliens blow up into pieces, sparks fly, slime
bleeds and the fans lift Kurt. He snipes through the scope with every kind of round and calls
Bones' air strike. Only Kurt's arena and the one behind an open door are drawn and solid, as in
the original. The game starts with the splash and the main menu (options, key bindings, saved
games) and plays the levels in order with their loading screens, briefings, the end of each level,
the statistics and the save prompt; when Kurt dies, "Continue" starts the level again. The fall
before each level, the stream after it, the rides and full saves (F2) aren't ported yet. The
[Godot port](https://github.com/nemo22/mdk-godot) is more complete for now.

## Progress

| Area | Done |
| --- | --- |
| Data formats: levels, textures, models, sprites, sounds, fonts, scripts, videos | █████████░ 90% |
| Rendering: arenas, glass, sky, mirrors, Kurt's sprite, effects | ████████░░ 80% |
| Collisions: the original's BSP | ████████░░ 80% |
| Kurt: walking, turning, jumping, chute, camera, damage, death | ██████░░░░ 60% |
| Sound mixer (the original's laws) and music | ██████░░░░ 60% |
| Script VM, aliens, objects, doors, effects, fans | ███████░░░ 70% |
| Weapons, items, sniper mode | ████████░░ 80% |
| HUD (health, inventory, messages, health bar) | ███████░░░ 70% |
| Menus, saves, level flow | ███████░░░ 70% |
| The fall and the stream between levels, rides | ░░░░░░░░░░ 0% |
| Videos: the menu's FLC and slideshow, the end movies | ████████░░ 80% |
| **Overall** | **about 60%** |

The plan is in [docs/architecture.md](docs/architecture.md#roadmap).

## Running

Windows x64 for now. Put the program (or this folder) inside the MDK installation folder (for
example `C:\GOG Games\MDK\mdk-sdl`), install MDK in the default GOG or Steam location, or set the
`MDK_DATA_DIR` environment variable.

```
mdk.exe
```

- No option: the splash, then the main menu.
- `--level=N`: play level N (3 to 8) at once, without the menu (the game plays them in the order
  7, 6, 3, 4, 8, 5).
- `--menu`: the main menu without the splash (`--splash` with it); `--options`, `--controls` open
  those pages.
- `--stats=N`: the screens after level N (`--phase=1-4` starts at a page: 1 the Score-O-matic, 2
  the intermission, 3 the briefing, 4 the debriefing; `--counts=shots,hits,sniper,sniper
  hits,kills,enemies,heads`, `--towns=bits`); `--briefing=N` its briefing alone; `--end` the end
  movies.
- `--load=NAME`: load a saved game; `--save=NAME`: save the first level when it starts (tests).
- `--at=x,y,z[,yaw]`: where Kurt starts (MDK coordinates).
- `--fly`: start with the flying camera.
- `--mute`: no sound.
- Tests: `--screenshot=file.bmp` (after `--wait=seconds` of game time, then quit),
  `--walk=seconds`, `--delay=seconds` (held keys start later), `--jump`, `--fire`, `--give=SW_HBOMB,...` (pickups to start with), `--use`
  (uses the item after 1 second), `--profile` (prints the objects of Kurt's arena every second),
  `--sniper[=zoom[,pitch]]` (sniper mode once Kurt stands), `--zoom=seconds` (zooms in),
  `--sniper-fire` (one sniper round), `--strike[=dive]` (Bones' full-screen strike), `--die` (Kurt
  dies after 1 second), `--event=N` (a `special_event` after 1 second: 1 ends the level). With
  `--screenshot` but without `--level`, the screen shown after `--wait` seconds (menu,
  statistics...) is saved.

Settings (volumes, music filter, mouse, fullscreen, difficulty, gore, key bindings) and saved games
are kept in `%LOCALAPPDATA%/mdk-sdl` (`settings.cfg`, `saves/*.sav`; `MDK_USER_DIR` overrides the
folder). `LASTGAME` (written when Kurt dies) is deleted at start, as in the original.

Controls: W/S or Up/Down to run, A/D to strafe, the mouse or Left/Right to turn, Space to jump
(hold it while falling to open the chute), Shift for turbo, Ctrl or the left mouse button to fire,
Enter to use the item, Tab or [ ] to select it (or 1-5), the right mouse button for sniper mode
(the mouse wheel or PageUp/PageDown zoom, Tab or [ ] select the ammo), F1 for the flying camera (E/Q to go up and
down), F12 for a screenshot, Esc for the pause menu (resume, options, main menu, quit). The bindings
can be changed in Options, Controls. In the menus: the arrows or the mouse, Enter or a click, Esc
back. Typing `TOOSCARYFORME` in a level turns gore on or off, `SEETHEWHOLEGAME` the main menu's
debug keys (3-8 start that level, D the statistics with random counts).

## Building

Needs the .NET 10 SDK and the Windows SDK (its `dxc` compiles the shaders).

```
dotnet build
src/Mdk.App/bin/Debug/net10.0/mdk.exe --level=3
```

One native executable (Native AOT, also needs the Visual Studio C++ tools): `sh tools/publish.sh`
gives `publish/mdk.exe` (about 5 MB). SDL3 is embedded in it and unpacked once to
`%LOCALAPPDATA%/mdk-sdl`.

## Tests

With the game data installed:

```
dotnet test
sh tests/screenshot_test.sh
sh tests/kurt_test.sh
sh tests/combat_test.sh
sh tests/scripts_test.sh
sh tests/sniper_test.sh
sh tests/effects_test.sh
sh tests/flow_test.sh
```

## Layout

- `src/Mdk.Formats`: MDK's file formats, no dependencies.
- `src/Mdk.Engine`: SDL3 behind a small API: the renderer (SDL_GPU), the audio mixer, the window
  and input.
- `src/Mdk.Game`: the game: levels, collisions, Kurt, the camera, the sound mixer's laws, scripts,
  the game's flow (`Flow/`: screens, settings, saves) and its menus (`Menu/`).
- `src/Mdk.App`: the program and its command line.
- `shaders/`: HLSL shaders, compiled to DXIL at build time.
- `docs/`: [the architecture and roadmap](docs/architecture.md). The knowledge base about MDK
  itself (formats, engine, scripts, BSP) is in the
  [Godot port's docs](https://github.com/nemo22/mdk-godot/tree/main/docs).
- `tests/`, `tools/`: tests, the opcode table generator and the publish script.

## Legal

This is an unofficial fan project, not affiliated with Shiny Entertainment or the current rights
holders of MDK. MDK and its data are the property of their respective owners.

This repository contains only original code, documentation and tools. It doesn't include or
distribute any data from the game. To run the port you need a legally obtained copy of the
original MDK, whose installed data files the port loads.
