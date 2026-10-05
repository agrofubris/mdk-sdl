# MDK (C# / SDL3)

Port of MDK (Shiny Entertainment, 1997) to C# and SDL3. Requires the original game data (GOG or
Steam). Work in progress: Kurt walks the levels (no enemies or scripts yet). See
`docs/architecture.md`.

## Build and run

Needs the .NET 10 SDK and the Windows SDK (for `dxc`).

```
dotnet build
src/Mdk.App/bin/Debug/net10.0/mdk.exe --level=3
```

One native executable (Native AOT, needs the Visual Studio C++ tools): `sh tools/publish.sh`
gives `publish/mdk.exe`. SDL3 is embedded and unpacked once to `%LOCALAPPDATA%/mdk-sdl`.

The game data is found in `MDK_DATA_DIR`, in a folder above the program (put this folder inside
the MDK installation), or in the default GOG and Steam folders.

Controls: W/S or arrows to walk, A/D strafe, arrows or mouse turn, Space jump (hold: chute),
Shift run, F1 flying camera (E/Q up and down), F12 screenshot, Esc quits.

## Tests

```
dotnet test
sh tests/screenshot_test.sh
sh tests/kurt_test.sh
```
