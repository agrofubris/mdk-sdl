#!/bin/sh
# Builds the native executable publish/mdk (mdk.exe on Windows; Native AOT, the shaders embedded)
# and SDL3's library next to it for a runtime, this machine's by default. Other options go to dotnet publish.
# Needs the C/C++ linker: the Visual Studio C++ tools on Windows (vswhere is put on the PATH for
# the AOT linker), clang on Linux, Xcode on macOS.
# Run from the project folder: sh tools/publish.sh [win-x64|linux-x64|osx-arm64|osx-x64] [options]
case "$(uname -s)" in
	MINGW* | MSYS* | CYGWIN*) RID=win-x64 ;;
	Darwin) [ "$(uname -m)" = arm64 ] && RID=osx-arm64 || RID=osx-x64 ;;
	*) RID=linux-x64 ;;
esac
case "$1" in
	win-* | linux-* | osx-*) RID=$1; shift ;;
esac
PATH="$PATH:/c/Program Files (x86)/Microsoft Visual Studio/Installer"
export PATH
# Only the program is replaced: settings.cfg and saves/ next to it stay.
rm -f publish/mdk publish/mdk.exe
dotnet publish src/Mdk.App -c Release -r "$RID" -o publish "$@" || exit 1
ls -la publish/mdk*
