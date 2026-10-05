# MDK in C# and SDL3

![Status: work in progress](https://img.shields.io/badge/status-work%20in%20progress-orange)
![Progress: about 25%](https://img.shields.io/badge/progress-~35%25-orange)
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
aliens spawn, walk and fly their paths, doors open, pickups fall on their chutes. There are no
weapons, HUD or menus yet, and Kurt can't be hurt: it's not a game yet. The [Godot port](https://github.com/nemo22/mdk-godot)
is far more complete for now.

## Progress

| Area | Done |
| --- | --- |
| Data formats: levels, textures, models, sprites, sounds, fonts, scripts, videos | █████████░ 90% |
| Rendering: arenas, glass, sky, mirrors, Kurt's sprite | ██████░░░░ 60% |
| Collisions: the original's BSP | ████████░░ 80% |
| Kurt: walking, turning, jumping, chute, camera | █████░░░░░ 50% |
| Sound mixer (the original's laws) and music | ██████░░░░ 60% |
| Script VM, aliens, objects, doors | ██████░░░░ 60% |
| Weapons, items, sniper mode | ░░░░░░░░░░ 0% |
| HUD, menus, saves, level flow | ░░░░░░░░░░ 0% |
| The fall and the stream between levels, rides | ░░░░░░░░░░ 0% |
| Videos (decoders done, no player yet) | ███░░░░░░░ 30% |
| **Overall** | **about 35%** |

The plan is in [docs/architecture.md](docs/architecture.md#roadmap).

## Running

Windows x64 for now. Put the program (or this folder) inside the MDK installation folder (for
example `C:\GOG Games\MDK\mdk-sdl`), install MDK in the default GOG or Steam location, or set the
`MDK_DATA_DIR` environment variable.

```
mdk.exe --level=3
```

- `--level=N`: the level, 3 to 8 (the game plays them in the order 7, 6, 3, 4, 8, 5).
- `--at=x,y,z[,yaw]`: where Kurt starts (MDK coordinates).
- `--fly`: start with the flying camera.
- `--mute`: no sound.
- Tests: `--screenshot=file.bmp` (after `--wait=seconds` of game time, then quit),
  `--walk=seconds`, `--jump`, `--profile` (prints the objects of Kurt's arena every second).

Controls: W/S or Up/Down to run, A/D to strafe, the mouse or Left/Right to turn, Space to jump
(hold it while falling to open the chute), Shift for turbo, F1 for the flying camera (E/Q to go
up and down), F12 for a screenshot, Esc to quit.

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
sh tests/scripts_test.sh
```

## Layout

- `src/Mdk.Formats`: MDK's file formats, no dependencies.
- `src/Mdk.Engine`: SDL3 behind a small API: the renderer (SDL_GPU), the audio mixer, the window
  and input.
- `src/Mdk.Game`: the game: levels, collisions, Kurt, the camera, the sound mixer's laws, scripts.
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
