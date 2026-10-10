# SDL3 D3D12 interop (the Leia SR weave bridge)

The private, versioned ABI that lets this port weave the Leia SR pair on SDL_GPU's own D3D12
command list. SDL_GPU keeps its `ID3D12Device` and command lists internal; this injection
exposes exactly what the SR weaver needs and nothing else.

Vendored from [agrofubris/starfox-enhanced-stereo](https://github.com/agrofubris/starfox-enhanced-stereo)
(GPL-3.0), which follows [oneup03](https://github.com/oneup03/rt64-3D)'s RT64 3D design and
[bo3b](https://github.com/bo3b/SR-lib)'s SR-lib. Only the include path in `sdl_d3d12_bridge.inc`
was adapted (`#include "sdl_d3d12_bridge.h"`, the file sitting next to the backend source).

- `sdl_d3d12_bridge.h` — the ABI: property names, versions, and the callback signatures
  (device, buffer/geometry/texture/compute/present bridges, present hooks, and the
  `starfox.gpu.d3d12.present-weave.v1` weave used here).
- `sdl_d3d12_bridge.inc` — the implementation, `#include`d inside SDL's
  `src/gpu/d3d12/SDL_gpu_d3d12.c`; it sets the bridge properties on the GPU device's properties
  and installs the swapchain hooks.

`tools/build_sdl3.ps1` fetches SDL, injects both files (guarded anchors: a mismatched SDL fails
the build loudly instead of half-patching), builds `SDL3.dll`, and copies it next to the program.
Ship that DLL with the game: a stock SDL3.dll has no bridge and the Leia SR mode presents the
plain side-by-side pair.

This is not an SDL public API and is pinned to the SDL revision it is built against.
