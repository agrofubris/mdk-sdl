# Builds SDL3.dll with the D3D12 interop the Leia SR weave needs (third_party/sdl3-interop):
# fetches SDL, injects the bridge into the D3D12 backend (guarded anchors: a mismatched SDL
# fails loudly, never half-patches), builds SDL3.dll and copies it next to the program.
#
#   .\build_sdl3.ps1 [-OutDir publish] [-SdlDir third_party\SDL] [-SdlRef <tag|commit>]
#
# A stock SDL3.dll has no bridge: the Leia SR mode then presents the plain side-by-side pair.
# Needs git, CMake and the Visual Studio C++ tools.

param(
    [string]$OutDir = "",
    [string]$SdlDir = "",
    [string]$SdlRef = "",
    [string]$BuildDir = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$interop = Join-Path $repo "third_party\sdl3-interop"
if (-not $SdlDir) { $SdlDir = Join-Path $repo "third_party\SDL" }
if (-not $BuildDir) { $BuildDir = Join-Path $repo "third_party\SDL-build" }
if (-not $OutDir) { $OutDir = Join-Path $repo "publish" }

# 1. Fetch SDL (pinned revision when given).
if (-not (Test-Path (Join-Path $SdlDir "CMakeLists.txt"))) {
    Write-Host "Cloning SDL into $SdlDir"
    git clone --depth 1 https://github.com/libsdl-org/SDL $SdlDir
    if ($SdlRef) {
        git -C $SdlDir fetch --depth 1 origin $SdlRef
        git -C $SdlDir checkout --detach FETCH_HEAD
    }
}

$versionHeader = Get-Content (Join-Path $SdlDir "include\SDL3\SDL_version.h") -Raw
$version = ([regex]::Matches($versionHeader, '#define SDL_(?:MAJOR|MINOR|MICRO)_VERSION\s+(\d+)') |
    ForEach-Object { $_.Groups[1].Value }) -join "."
Write-Host "SDL $version at $(git -C $SdlDir rev-parse --short HEAD)"

# 2. The interop files, next to the backend source.
$backendDir = Join-Path $SdlDir "src\gpu\d3d12"
Copy-Item (Join-Path $interop "sdl_d3d12_bridge.h") $backendDir -Force
Copy-Item (Join-Path $interop "sdl_d3d12_bridge.inc") $backendDir -Force

# 3. Inject (the same guarded insertions as starfox's cmake/sdl3-d3d12-interop.cmake).
$backend = Join-Path $backendDir "SDL_gpu_d3d12.c"
$code = Get-Content $backend -Raw
if ($code.Contains("starfox_present_hooks")) {
    Write-Host "SDL backend already patched"
}
else {
    $nl = if ($code.Contains("`r`n")) { "`r`n" } else { "`n" }
    $createAnchor = '    CHECK_D3D12_ERROR_AND_RETURN("Could not create IDXGISwapChain3", false);'
    $destroyAnchor = '    IDXGISwapChain_Release(windowData->swapchain);'
    $destroyFn = 'static void D3D12_INTERNAL_DestroySwapchain('
    $createFn = 'static SDL_GPUDevice *D3D12_CreateDevice(bool debugMode, bool preferLowPower, SDL_PropertiesID props)'
    $tail = "    renderer->sdlGPUDevice = result;$nl$nl    return result;"
    foreach ($anchor in @($createAnchor, $destroyAnchor, $destroyFn, $createFn, $tail)) {
        if (-not $code.Contains($anchor)) {
            throw "Pinned SDL anchors missing (SDL revision differs): update tools/build_sdl3.ps1 and third_party/sdl3-interop"
        }
    }

    $hookLookup = '    const StarfoxSdlD3D12PresentHooksV1 *starfox_present_hooks = (const StarfoxSdlD3D12PresentHooksV1 *)SDL_GetPointerProperty(SDL_GetGlobalProperties(), STARFOX_SDL_D3D12_PRESENT_HOOKS, NULL);'
    $hookCreate = "$hookLookup$nl    if (starfox_present_hooks && starfox_present_hooks->version == 1 && starfox_present_hooks->swapchain)$nl        starfox_present_hooks->swapchain(starfox_present_hooks->user, renderer->device, (void **)&swapchain3, false);"
    $hookDestroy = "$hookLookup$nl    if (starfox_present_hooks && starfox_present_hooks->version == 1 && starfox_present_hooks->swapchain)$nl        starfox_present_hooks->swapchain(starfox_present_hooks->user, renderer->device, (void **)&windowData->swapchain, true);"
    $properties = @(
        '    SDL_SetPointerProperty(renderer->props, STARFOX_SDL_D3D12_DEVICE, renderer->device);'
        '    SDL_SetPointerProperty(renderer->props, STARFOX_SDL_D3D12_BRIDGE, (void *)&starfox_d3d12_bridge);'
        '    SDL_SetPointerProperty(renderer->props, STARFOX_SDL_D3D12_GEOMETRY_BRIDGE, (void *)&starfox_d3d12_geometry_bridge);'
        '    SDL_SetPointerProperty(renderer->props, STARFOX_SDL_D3D12_TEXTURE_BRIDGE, (void *)&starfox_d3d12_texture_bridge);'
        '    SDL_SetPointerProperty(renderer->props, STARFOX_SDL_D3D12_COMPUTE_BRIDGE, (void *)&starfox_d3d12_compute_bridge);'
        '    SDL_SetPointerProperty(renderer->props, STARFOX_SDL_D3D12_PRESENT_BRIDGE, (void *)&starfox_d3d12_present_bridge);'
        '    SDL_SetPointerProperty(renderer->props, STARFOX_SDL_D3D12_PRESENT_WEAVE, (void *)&starfox_d3d12_present_weave_bridge);'
    ) -join $nl

    $code = $code.Replace($destroyFn, "#include `"sdl_d3d12_bridge.h`"$nl$destroyFn")
    $code = $code.Replace($createAnchor, "$createAnchor$nl$hookCreate")
    $code = $code.Replace($destroyAnchor, "$hookDestroy$nl$destroyAnchor")
    $code = $code.Replace($createFn, "#include `"sdl_d3d12_bridge.inc`"$nl$nl$createFn")
    $code = $code.Replace($tail, "    renderer->sdlGPUDevice = result;$nl$nl$properties$nl    return result;")
    Set-Content -Path $backend -Value $code -NoNewline
    Write-Host "Patched $backend"
}

# 4. Build SDL3.dll.
cmake -S $SdlDir -B $BuildDir -G "Visual Studio 17 2022" -A x64 -DSDL_SHARED=ON -DSDL_STATIC=OFF -DSDL_TESTS=OFF -DSDL_EXAMPLES=OFF
if ($LASTEXITCODE -ne 0) { throw "cmake configure failed" }
cmake --build $BuildDir --config Release --target SDL3-shared
if ($LASTEXITCODE -ne 0) { throw "cmake build failed" }

$dll = Get-ChildItem -Path $BuildDir -Recurse -Filter "SDL3.dll" | Select-Object -First 1
if (-not $dll) { throw "SDL3.dll not found under $BuildDir" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Copy-Item $dll.FullName (Join-Path $OutDir "SDL3.dll") -Force
Write-Host "Built $(Join-Path $OutDir 'SDL3.dll') (patched SDL $version)" -ForegroundColor Green
