# Builds mdk_leia.dll (the MDK port's Simulated Reality host, native/mdk_leia).
#
#   .\build.ps1                                     the stub: the Leia SR mode presents the plain pair
#   .\build.ps1 -SrLibDir ..\..\third_party\SR-lib  the real weaver, against bo3b/SR-lib's
#                                                   api_expansion branch (CreateSRInterfaceDX12)
#                                                   with its vendored SDK
#
# The SDK is proprietary and stays on your machine; it is never part of this repository.
# Every SR DLL is delay-loaded, so a machine without the SR Platform still runs the game
# (the weaver's probe fails and the mode presents the pair). Copy the built mdk_leia.dll
# next to mdk.exe.

param(
    [string]$SrLibDir = "",
    [string]$OutDir = ""
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $OutDir) { $OutDir = Join-Path $here "build" }
$build = Join-Path $here "build-cmake"

$cmake = (Get-Command cmake -ErrorAction SilentlyContinue).Source
if (-not $cmake) { throw "cmake not found: install CMake (or add it to the PATH)" }

$sdk = ""
if ($SrLibDir) {
    $SrLibDir = (Resolve-Path $SrLibDir).Path
    $sdk = "-DMDK_LEIA_SRLIB_DIR=$SrLibDir"
}

& $cmake -S $here -B $build -G "Visual Studio 17 2022" -A x64 $sdk
if ($LASTEXITCODE -ne 0) { throw "cmake configure failed" }
& $cmake --build $build --config Release
if ($LASTEXITCODE -ne 0) { throw "cmake build failed" }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$dll = Get-ChildItem -Path $build -Recurse -Filter "mdk_leia.dll" | Select-Object -First 1
Copy-Item $dll.FullName (Join-Path $OutDir "mdk_leia.dll") -Force
Write-Host "Built $(Join-Path $OutDir 'mdk_leia.dll')" -ForegroundColor Green
if (-not $SrLibDir) {
    Write-Host "Stub build: the Leia SR mode presents the plain side-by-side pair."
}
