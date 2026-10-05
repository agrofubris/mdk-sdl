# Architecture

C# (.NET 10) and SDL3 (ppy.SDL3-CS) port of MDK (1997), following the Godot port
(`../godot-mdk`), whose `docs/` hold the reverse-engineering notes (formats, engine, script
opcodes). Requires the original game data.

## Layers

Each layer talks only to the one below it.

```
 Mdk.App      command line ──► Viewer.Run
    │
 Mdk.Game     level, camera, collision (BSP), sound mixer, scripts and objects, Kurt
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
- Insets (`Renderer.Insets.cs`): 3D views in canvas rectangles, under or over the canvas (sniper
  mode's round cameras and clip).
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
   `damp_gravity`), solid objects and platforms (`Collision/Solids.cs`), follow camera, sprite,
   chain gun, muzzle flash, hits, knock-down, death, items (`Kurt/Kurt.Combat.cs`,
   `Kurt/Inventory.cs`), the fans' updrafts (`Kurt/Kurt.Updraft.cs`), sniper mode
   (`Kurt/Kurt.Sniper.cs`, `Kurt/Scope.cs`: look, zoom, clip, the scope's off-centre projection).
   Still to do: ledges, sliding, camera clearance.
7. 🟡 Script VM and objects (`Scripts/`: ports of `script_vm.gd`, `script_runtime.gd`,
   `object_motion.gd`, `object_behaviors.gd`; 30 ticks per second, object moves are BSP box sweeps).
   Kurt's items and blasts are ported (`Items.cs`, `Twister.cs`; twisters aren't drawn), so are the
   effects (`Effects.cs`: wounds, slime drops, bubbles, smoke trails), the flying pieces
   (`Debris.cs`: sparks, shattered groups, break-ups) and the fans (`Fans.cs`), drawn by
   `Objects/EffectsView.cs`, the sniper rounds, their target lock and `bomb_follow_path`
   (`SniperRounds.cs`), Bones' air strike (`AirStrike.cs`) and the full-screen strike
   (`StrikeScene.cs`). Rides and the end of a level are stubs (`Scripts/Stubs/`, marked
   `TODO port`).
8. 🟡 Weapons, items, HUD, sniper mode: the chain gun, the items and the HUD (`Hud/`: health panel,
   inventory, messages in the original fonts, health bar, flashes, drawn on the renderer's 2D
   canvas) are done, and sniper mode's screen (`Hud/SniperOverlay.cs`; the round cameras and the
   loaded rounds are renderer insets, `SniperView.cs`). Bullet holes on textures (`special_130`)
   are not.
9. Menus, fonts, saves, videos (MVE, FLC).
10. Fall, stream, bomber, snowboard sequences.
11. Enhanced look.
