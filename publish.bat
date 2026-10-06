@echo off
rem Builds publish\mdk.exe (Native AOT, the shaders embedded) and SDL3.dll next to it.
rem Needs the .NET 10 SDK and the Visual Studio C++ tools (the AOT linker).
rem Usage: publish.bat [dotnet publish options]
setlocal
cd /d "%~dp0"

rem The AOT linker finds the C++ tools through vswhere.
set "PATH=%PATH%;%ProgramFiles(x86)%\Microsoft Visual Studio\Installer"

rem Only the program is replaced: settings.cfg and saves\ next to it stay.
if exist publish\mdk.exe del publish\mdk.exe
dotnet publish src\Mdk.App -c Release -r win-x64 -o publish %*
if errorlevel 1 exit /b 1

dir publish\mdk.exe
