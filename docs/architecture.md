# Architecture

C# (.NET 10) and SDL3 (ppy.SDL3-CS) port of MDK (1997), following the Godot port
(`../godot-mdk`), whose `docs/` hold the reverse-engineering notes (formats, engine, script
opcodes). Requires the original game data.

## Layers

Each layer talks only to the one below it.

```
 Mdk.App      command line ──► Game.Run
    │
 Mdk.Game     Flow: Game (screens), GameState, Settings, SaveGames, LevelFlow
    │         Menu: main menu, pause menu, options, loading, statistics, save prompt, videos
    │         Stream: the tube between levels (generator, Kurt's flight, drawing)
    │         Fall: the fall before a level (FallSim, FallView, FallHud)
    │         Viewer (a level): level, camera, collision (BSP), sound mixer, scripts and objects, Kurt
    │         DevTools: the console, its commands, the debug overlay
    │   └──────────────► Mdk.Formats  (parsers: DTI, MTO, MTI, SNI, FTI, BNI, models; the 1996 demo's)
 Mdk.Engine   Render (Renderer: meshes, index textures, palettes, panorama)
    │         Audio (AudioDevice: software mixer of voices, music and effects buses, streamed voices, master limiter)
    │         Platform (Window, Input: game keys (rebindable), menu keys, raw keys, pointer, text, cursor, SDL3)
    │         Diagnostics (Profiler: frame sections; LogRing, LogWriter: the output's last lines)
 SDL3         SDL_GPU (Direct3D 12 / Vulkan / Metal), events
```

- `Mdk.Formats` has no dependencies: bytes in, data out.
- `Mdk.Engine` hides SDL: the game sees meshes, materials, keys.
- Shaders (`shaders/*.hlsl`) are compiled at build time to each format whose tool is found, and
  embedded; the renderer creates its device with the first embedded format a GPU driver takes.
- `Mdk.Formats` finds the game: `MDK_DATA_DIR`, `mdk` in `mdk_paths.cfg` next to the program
  (`LocalPaths`, the Godot port's file), the folders above the program, GOG and Steam folders.
- `Mdk.Formats` finds data files whatever their case (`CaseInsensitivePath`: the GOG installation
  has `MISC/mdkfont.fti`, the game asks for `MISC/MDKFONT.FTI`).

## Platforms

One native executable per platform (Native AOT) with the shaders embedded, and SDL3's library
for its runtime next to it (`SDL3.dll`, `libSDL3.so`, `libSDL3.dylib`).

```
 shaders/*.hlsl ──dxc──────────► DXIL ───► Direct3D 12 (Windows)
        │
        └──────dxc -spirv──────► SPIR-V ─► Vulkan (Linux; Windows with SDL_GPU_DRIVER=vulkan)
                                   │
                                   └──spirv-cross──► MSL ─► Metal (macOS)
```

- Bindings (SDL_GPU's rules, `shaders/bindings.hlsli`): HLSL registers in spaces 1-3 for DXIL;
  SPIR-V combines each texture with its sampler at set 2, binding = register; spirv-cross keeps
  the SPIR-V bindings as Metal indices (`--msl-decoration-binding`).
- A missing tool skips its format; CI requires its platform's (`-p:RequiredShaders`).
- CI (`.github/workflows/build.yml`): Windows (DXIL, SPIR-V), Linux (SPIR-V), macOS (SPIR-V,
  MSL) build, test without the game data (`[DataFact]` tests are skipped) and publish; a `v*` tag
  releases the archives. Shader tools: the DirectXShaderCompiler release (Windows, Linux), the
  Vulkan SDK (macOS).

## Rendering (original look)

```
 Arena triangles ──Layers (depth layers)───► batches per surface ──► Renderer
                                                                     │
 surface = index texture (R8) + palette (256x1) │ flat colour        ▼
                                                                offscreen colour + depth
                                                                     │ blit
                                                                  swapchain
```

- Palette index 0 is transparent.
- Coplanar details (`Level/Layers.cs`): a triangle overlapping bigger ones of its plane is a layer
  above them; layers and outlines are drawn pulled towards the eye (`DepthPull`), the same place on
  screen, nearer in depth: no flicker, no cracks. The original draws back to front, without depth.
- Vertex colours (white unless given) multiply the surface, Gouraud-blended: the stream's tube.
- Glass (`GLASS1-4`) blends after the opaque surfaces; mirrors show the panorama where the sky
  behind them would be (shifted by MIRRLOW...MIRRHIGH).
- Insets (`Renderer.Insets.cs`): 3D views in canvas rectangles, under or over the canvas (sniper
  mode's round cameras and clip).
- The sky is a screen-filling triangle drawn first, scrolled by yaw and pitch like the original's
  2D backdrop.
- Anti-aliasing (`Renderer.AntiAliasing`, 2x or 4x MSAA): the scene is drawn into a multisampled
  target resolved into the frame; off, nothing changes.

## Rendering (enhanced look)

The game picks the look (`Settings.Graphics`, `Level/EnhancedLook.cs`, the Godot port's
`Level._enhance`): materials carry a `Shading` (arenas and objects `Lit`, Kurt and effects
`Sprite`, glass and mirrors `Original`), the sky a `Sampling`, and the renderer gets a `Lighting`
(sun, ambient, shadow distance, glow, haze in the colour below the sky panorama). The 2D canvas is
filtered too (`Renderer.CanvasSampling`).

```
 sun's depth (Lit casters) ──► shadow map 2048² ─────────┐
 camera's depth (opaque) ───────────────────────────────┐ │
 scene: filtered sky, enhanced.hlsl (MSAA) ──► scene ───┼─┴─mips─► post.hlsl ─► frame ─► canvas, insets
                                                        └ ambient occlusion    (occlusion × colour, glow)
```

- Colour textures (`Renderer.Colours.cs`, `ColourMips`): surfaces and sprites sample each index
  texture expanded through its palette to RGBA8 (premultiplied, index 0 clear), with box-filtered
  mips and a 2D array layer per animated frame, trilinear and 16x anisotropic. Made before the
  first frame that draws them, again when their indices or palette change (bullet holes); about
  5.3 times the index textures' memory (20-41 MB a level).
- `palette_filtered.hlsli` (the canvas): bilinear by hand, each of the four texels through the
  palette; index 0 transparent; animated textures keep to their frame.
- `enhanced.hlsl`: flat normals from the world position's screen derivatives (the triangle's
  plane, turned to the camera); light in linear colour: albedo × (ambient + sun × N·L × shadow),
  then the haze (1 − e^(−density × distance)). Sprites: filtered, unlit, edges cut at half cover.
- Shadows (`SunShadow`): an orthographic view along the sunlight, centred on the camera and
  snapped to whole texels; 2 × 2 compared texels, slope and normal offsets against acne.
- `occlusion.hlsl`: Alchemy ambient occlusion from the camera's depth, fading in the haze, 12
  samples turned in a 4 x 4 ordered pattern, into its own target (`screen.hlsli` shared).
- `post.hlsl`: the occlusion blurred over 4 x 4 pixels of the same plane (no grain, no shade
  across edges); glow from the scene's blurred mips, screen-blended.

## Game flow

`Flow/Game.cs` runs one screen (`IScreen`) at a time; each frame returns an `Event` and the game
picks the next screen. A screen's textures and meshes are freed when it ends (`Renderer.Mark`,
`Renderer.Release`); the 2D screens draw the original's 600 x 360 view fitted on the canvas
(`Menu/Ui.cs`, `ScreenView`).

```
 splash ─► main menu ─new game─► briefing ──fall──► loading ─► level (Viewer)
              ▲   └─continue / saved game──────────► loading      │ tornado (EndLevel)
              │                                                    ▼
              │                           index 0-3: stream ─► statistics ─► save prompt ─► briefing ──fall──► next level
              │                           index 4:   Gunter stream ─► save prompt ─► LEVEL5
              ├── Kurt died (LASTGAME) ◄──────────────────────────┘
              └── end movies (event 81) ◄── LEVEL5
```

## Developer tools

The console (Grave, the key left of 1 by its scancode) and the debug overlay (F3) of a level
(`DevTools/`, `LevelDevTools` in the `Viewer`):

```
 Console.WriteLine ──► LogWriter ──► stdout / stderr (unchanged; tests parse them)
                               └──► LogRing (500 lines) ──► ConsoleView
 Input (RawKey, typed text) ──► DevConsole ──Enter──► CommandRegistry ──► ConsoleCommands (parse, check)
                                   │ Tab, Up/Down        (help, clear)          │
                                   ▼                                            ▼
                             CommandHistory                     ICommandTarget: LevelCommands (Kurt, scripts,
                                                                settings, saves; Next: map, load, quit)
 Profiler (render, physics, scripts, audio), Renderer.Stats, GC ──► OverlayText ──► OverlayView
```

- The game runs on while the console is open; the level gets an idle `Input` (the tests' held
  keys still apply), so typing reaches neither Kurt nor the cheats.
- The engine's parts are generic: `RawKey` (fixed keys by place, auto-repeat included),
  `Profiler` sections, `RenderStats` (GPU draw calls and triangles of the last frame), the log.
- `Kurt.Mortality` (god: no damage, no death by falling out) and `Kurt.Clipping` (noclip: the
  walking keys move him freely). `ArenaStops` finds a floor of an arena (the soak tour, `tp`,
  `map`); `GameState.StartArena` carries `map`'s arena to the next `Viewer`.
- `--console="..."` opens the console once Kurt's arena is known (after `--delay`) and runs the
  lines (`tests/console_test.sh`).

## The 1996 beta demo

The three levels of `MDKDEMO.EXE` (6 August 1996; godot-mdk `docs/beta96.md`, `mdk_beta.gd`,
`beta_script_decoder.gd`) are numbered 961, 963 and 966. `BetaDemo` (found by `MDK_BETA_DIR`, `beta`
in `mdk_paths.cfg` or a `BETA96` folder) reads their loose files into the retail loaders' objects,
so the game runs them unchanged:

```
 LEVELn.SET, .CON, ARENAS/*.HOT ──► Dti         ARENAS/*.BSP (16-char names, 36-byte nodes) ──► Arena
 LEVELnO.MTO, LEVELnS.MTI, *.LBA ──► TextureArchive (no header, index 0 made black)
 LEVELn.CMI ──► Cmi (Dialect Beta1996: paths ──► retail splines, animations ──► ParseBeta)
 *.SNI ──► Sni         SPRITES/*.ABB, HUD/*.LBB ──► over TRAVSPRT.BNI's entries
```

- Scripts: `ScriptDecoder.For(cmi)` decodes the demo's bytecode into retail instructions
  (`ScriptDecoder.Beta.cs`); its own opcodes (`BetaOpcodes`: follow_path, fire, the alarm-ended
  condition) run in `ScriptVm`.
- Triangles flagged 2 (`Arena.ClipFlag`) aren't drawn and stop Kurt, not the scripts' rays
  (`Bsp.Clip`).
- `ScriptRuntime.Beta.cs`: no town timer, Kurt's arena from the arenas' boxes, the part the chain gun
  hits. `Kurt.Beta.cs`: rolls (Z, C), the helmet, backing up. `Viewer`: the teleports (T or Alt and a
  digit), the demo's sprites, `LevelMusic.Ambience`. `MainMenu`: the "Beta Levels" page.
- Not checked against the demo (it isn't on the development machine): the BSP nodes' last four
  `s16` are taken as padding after the retail six (`BetaDemo.CheckNodes` warns when nodes don't hold
  their triangles); the rest follows the Godot port, tested on synthetic files.

## Open questions

None open. Answered (Ghidra): which objects Kurt walks into or stands on
(`MdkObject.Footing`).

- Wall: neither 0x10 nor 0x800 (`damp_collide_move` 0x465e34 skips `flags & 0x810`; XY only).
- Floor: 0x100 and not 0x10 (`damp_platform_floor` 0x41d2c4).
- 0x800000 decides neither: landed on such a platform, Kurt keeps it without the floor scan
  (`damp_gravity` 0x469efc sets 0x573b8c).

  | flags             | walls | floor | e.g. (level)                               |
  |-------------------|-------|-------|--------------------------------------------|
  | none              | yes   | no    | grunts, doors, turrets                     |
  | 0x100 (+0x800000) | yes   | yes   | XPGUN (3), XBGUN, XTR (6), XTANK (7)       |
  | 0x800 + 0x100     | no    | yes   | XWINCH (3), XSNOWB 0x800900 (4)            |
  | 0x10, or 0x800    | no    | no    | pickups, bolts, X4_TOWER (5), XFORK (8)    |

## Roadmap

1. ✅ Formats, level viewer (arenas, corridors, textures, glass).
2. ✅ Sky panorama, mirrors, triangle groups (state kept in the triangle flags, as the original).
   Animated textures (`Scripts/AnimatedTextures.cs`, opcode 133), the scripts' sky modes
   (`Level/SkyModes.cs`, the renderer's `Backdrop`), the glass panes' outlines (`Level/Outlines.cs`,
   line primitives; the enhanced look skips flagged edges inside a flat pane), ropes (`Objects/RopeView.cs`), bullet holes (`Scripts/BulletHoles.cs`,
   re-uploaded textures).
3. ✅ Collision: the original's BSP (`Collision/Bsp.cs`, from godot-mdk `docs/bsp.md`).
4. ✅ Sound mixer (SDL audio stream, the original's volume, distance, Doppler and pan laws), the
   arenas' music and its fades (`Audio/LevelMusic.cs`).
5. ✅ Script decoder (every script of the game decodes like the reference disassembler).
6. 🟡 Kurt: walking, turning, jumping, chute, falls through the BSP (`damp_collide_move`,
   `damp_gravity`), solid objects and platforms (`Collision/Solids.cs`), follow camera, sprite,
   chain gun, muzzle flash, hits, knock-down, death, items (`Kurt/Kurt.Combat.cs`,
   `Kurt/Inventory.cs`), the fans' updrafts (`Kurt/Kurt.Updraft.cs`), sniper mode
   (`Kurt/Kurt.Sniper.cs`, `Kurt/Scope.cs`: look, zoom, clip, the scope's off-centre projection).
   The camera's climb term (−40 × the smoothed rise per tick), the rides (`Scripts/Rides.cs`,
   `Snowboard.cs`, `Bomber.cs`, `Kurt/Kurt.Ride.cs`, `Hud/BomberOverlay.cs`; Kurt's moves report
   the objects and triangle groups they touch; the board steers with the turn or strafe keys or
   the mouse, and goes into the arena Kurt rides into, flag 0x80000). Ledges (`Kurt/Kurt.Ledge.cs`), sliding in the
   wind tunnels (`Kurt/Kurt.Slide.cs`), hard landings and falling out of the arena, the camera's
   roll (`Kurt/CameraRoll.cs`), clearance and shake (`Kurt/FollowCamera.cs`,
   `Collision/ArenaSpace.Probes.cs`). Each step the camera follows Kurt after the scripts (a
   platform has carried him: `game_frame`'s order), and the XD2 and the XE are put with Kurt
   (`Rides.Follow`), so nothing is drawn a step apart (`tests/carry_test.sh`, `--trace`).
7. 🟡 Script VM and objects (`Scripts/`: ports of `script_vm.gd`, `script_runtime.gd`,
   `object_motion.gd`, `object_behaviors.gd`; 30 ticks per second, object moves are BSP box sweeps).
   Kurt's items and blasts are ported (`Items.cs`, `Twister.cs`; twisters drawn as ribbons, `Ribbon.cs`), so are the
   effects (`Effects.cs`: wounds, slime drops, bubbles, smoke trails), the flying pieces
   (`Debris.cs`: sparks, shattered groups, break-ups) and the fans (`Fans.cs`), drawn by
   `Objects/EffectsView.cs`, the sniper rounds, their target lock and `bomb_follow_path`
   (`SniperRounds.cs`), Bones' air strike (`AirStrike.cs`) and the full-screen strike
   (`StrikeScene.cs`), the end of a level (`EndLevel.cs`: the torn triangles are drawn by
   `LevelView.DrawEnd`), full saves (`Snapshot.cs`, `ScriptRuntime.Snapshot.cs`, `Kurt.Snapshot.cs`:
   JSON in the save, objects' public fields by reflection).
8. 🟡 Weapons, items, HUD, sniper mode: the chain gun, the items and the HUD (`Hud/`: health panel,
   inventory, messages in the original fonts, health bar, flashes, drawn on the renderer's 2D
   canvas) are done, and sniper mode's screen (`Hud/SniperOverlay.cs`; the round cameras and the
   loaded rounds are renderer insets, `SniperView.cs`), bullet holes on textures (`special_130`).
   Cutscenes look through `CutsceneCamera.cs`.
9. 🟡 Menus, saves, level flow, videos (`Flow/`, `Menu/`; ports of `main_menu.gd`,
   `menu_items.gd`, `pause_menu.gd`, `save_prompt.gd`, `stats_screen.gd`, `slideshow.gd`,
   `intro_splash.gd`, `loading_screen.gd`, `game_state.gd`, `settings.gd`, `video.gd`,
   `end_movie.gd`): the splash, the main menu over `MDK12.FLC` and the slideshow, options and key
   bindings, saved games (the Godot port's JSON), "Continue" after a death, the pause menu, the
   loading screen, the end of a level (`EndLevel`: Kurt rises, the white flash), the
   intermission, debriefing, Score-O-matic (its heads: `Menu/HeadsView.cs`), save prompt and
   briefing, the end movies, F2's full saves, the original's mouse cursor (`Menu/MenuCursor.cs`,
   `Window.SetCursor`), the `SEETHEWHOLEGAME` debug keys.
10. 🟡 Fall, stream, bomber, snowboard sequences. The rides are done (see 6). The stream is done (`Stream/`; ports of
    `stream.gd`, `stream_tube.gd`): the tube from Watcom's `rand()` (`--stream` uses seed 1, as the
    Godot reference test), Kurt's steering, drift and walls, the lights, the bonus, Bones' rescue,
    the Gunter tube and the planet, the fades. Unknown: the lights' blend (added). The fall is done
    (`Fall/`; ports of `fall.gd`, `fall_missile.gd`): the intro in space, steering, the camera, the ground with the crawler's
    track and the haze (quads far below the camera), radars, missiles, pickups, Bones, the palette
    effects, the HUD; health and pickups go on to the level (`GameState.Carry`). Pickups are only
    tested under their chute, as the original (0x41275c, as fall.gd).
    Approximations: the radar's colours, the smoke trails.
11. ✅ Enhanced look (`Renderer.Enhanced.cs`, `shaders/enhanced.hlsl`, `post.hlsl`, `depth.hlsl`):
    filtered textures, sky, sprites and 2D screens, a sun with shadows, white ambient light,
    ambient occlusion, glow, haze; mipmapped, anisotropic textures; anti-aliasing in both looks.
    Still to do: a sun per level, lights for muzzle flashes and explosions, occlusion and haze in the
    insets (sniper mode).
