# Builds mdk_leia.dll (the MDK port's Simulated Reality host, native/mdk_leia).
#
#   .\build.ps1                          the stub: the Leia SR mode presents the plain pair
#   .\build.ps1 -WithSrSdk -ClArgs "..." the real weaver: adds MDK_LEIA_SR and your SR-lib /
#                                        SR SDK include and link flags (delay-load its DLLs,
#                                        e.g. /link /DELAYLOAD:SRPlatform64.dll ...)
#
# The SDK is proprietary and stays on your machine; it is never part of this repository.
# Copy the built mdk_leia.dll next to mdk.exe.

param(
    [switch]$WithSrSdk,
    [string]$ClArgs = "",
    [string]$OutDir = ""
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $OutDir) { $OutDir = Join-Path $here "build" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { throw "vswhere not found: install the Visual Studio C++ tools" }
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "no Visual Studio C++ tools found" }
$vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"

$define = if ($WithSrSdk) { "/DMDK_LEIA_SR" } else { "" }
$command = "call `"$vcvars`" >nul && cl /nologo /LD /O2 /EHsc /std:c++17 $define $ClArgs " +
    "`"$here\mdk_leia.cpp`" /Fe:`"$OutDir\mdk_leia.dll`" /Fo:`"$OutDir\mdk_leia.obj`""
cmd /c $command
if ($LASTEXITCODE -ne 0) { throw "cl failed with $LASTEXITCODE" }

Write-Host "Built $OutDir\mdk_leia.dll" -ForegroundColor Green
if (-not $WithSrSdk) {
    Write-Host "Stub build: the Leia SR mode presents the plain side-by-side pair."
}
