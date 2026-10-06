@echo off
rem Builds publish\mdk.exe: one native executable (Native AOT; SDL3 and the shaders embedded).
rem Needs the .NET 10 SDK and the Visual Studio C++ tools (the AOT linker).
rem Usage: publish.bat [dotnet publish options]
setlocal
cd /d "%~dp0"

rem The AOT linker finds the C++ tools through vswhere.
set "PATH=%PATH%;%ProgramFiles(x86)%\Microsoft Visual Studio\Installer"

if exist publish rmdir /s /q publish
dotnet publish src\Mdk.App -c Release -r win-x64 -o publish %*
if errorlevel 1 exit /b 1

dir publish\mdk.exe
