# Architecture

C# (.NET 10) and SDL3 (ppy.SDL3-CS) port of MDK (1997), following the Godot port
(`../godot-mdk`), whose `docs/` hold the reverse-engineering notes (formats, engine, script
opcodes). Requires the original game data.

## Layers

Each layer talks only to the one below it.

```
 Mdk.App      command line ──► Viewer.Run
    │
 Mdk.Game     level, camera, collision (BSP), sound mixer, (later) scripts, Kurt
    │   └──────────────► Mdk.Formats  (parsers: DTI, MTO, MTI, SNI, FTI, BNI, models)
 Mdk.Engine   Render (Renderer: meshes, index textures, palettes, panorama)
    │         Audio (AudioDevice: software mixer of voices)
    │         Platform (Window, Input, embedded native SDL3)
 SDL3         SDL_GPU (Direct3D 12, DXIL shaders), events
```

- `Mdk.Formats` has no dependencies: bytes in, data out.
- `Mdk.Engine` hides SDL: the game sees meshes, materials, keys.
- Shaders (`shaders/*.hlsl`) are compiled by the Windows SDK's `dxc` at build time and embedded.

## Rendering (original look)

```
 Arena triangles ──Layers (coplanar lift)──► batches per surface ──► Renderer
                                                                     │
 surface = index texture (R8) + palette (256x1) │ flat colour        ▼
                                                                offscreen colour + depth
                                                                     │ blit
                                                                  swapchain
```

- Palette index 0 is transparent.
- Glass (`GLASS1-4`) blends after the opaque surfaces; mirrors show the panorama where the sky
  behind them would be (shifted by MIRRLOW...MIRRHIGH).
- The sky is a screen-filling triangle drawn first, scrolled by yaw and pitch like the original's
  2D backdrop.

## Roadmap

1. ✅ Formats, level viewer (arenas, corridors, textures, glass).
2. ✅ Sky panorama, mirrors, triangle groups (state kept in the triangle flags, as the original).
   Animated textures wait for the script VM (opcode 133 drives them).
3. ✅ Collision: the original's BSP (`Collision/Bsp.cs`, from godot-mdk `docs/bsp.md`).
4. ✅ Sound mixer (SDL audio stream, the original's volume, distance, Doppler and pan laws).
5. ✅ Script decoder (every script of the game decodes like the reference disassembler).
6. 🟡 Kurt: walking, turning, jumping, chute, falls through the BSP (`damp_collide_move`,
   `damp_gravity`), follow camera, sprite. Still to do: sniper mode, ledges, sliding, firing,
   knock-down, camera clearance, updrafts.
7. Script VM and objects (port of `script_vm.gd`, `script_runtime.gd`).
8. Weapons, items, HUD, sniper mode.
9. Menus, fonts, saves, videos (MVE, FLC).
10. Fall, stream, bomber, snowboard sequences.
11. Enhanced look.
