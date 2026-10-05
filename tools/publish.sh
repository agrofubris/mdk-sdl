#!/bin/sh
# Builds the single native executable publish/mdk.exe (Native AOT; SDL3 and the shaders are
# embedded). Needs the Visual Studio C++ tools; vswhere is put on the PATH for the AOT linker.
# Run from the project folder: sh tools/publish.sh
PATH="$PATH:/c/Program Files (x86)/Microsoft Visual Studio/Installer"
export PATH
rm -rf publish
dotnet publish src/Mdk.App -c Release -r win-x64 -o publish || exit 1
ls -la publish/mdk.exe
