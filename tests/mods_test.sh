#!/bin/sh
# Mods (docs/modding.md): --export-assets writes the game's textures, 2D images and models; a mod
# made of exported files (LEVEL3's H1_ textures all one other texture, the HUD panel as the skull,
# the grunt's model as exported) changes the enhanced look, never the original; the console lists
# it and what it replaced; switched off (settings or --mod) it changes nothing.
# Run from the project folder after `dotnet build`: sh tests/mods_test.sh
# No window: the game draws off screen and never takes the focus.
export MDK_HIDDEN=1
MDK=src/Mdk.App/bin/Debug/net10.0/mdk
OUT_DIR=${TMPDIR:-/tmp}/mdk_mods
EXPORT=$OUT_DIR/export
PLAIN=$OUT_DIR/plain
MODDED=$OUT_DIR/modded
MOD=$MODDED/mods/swap
FAILED=0

check() {
	if [ "$2" = 0 ]; then
		echo "  $1: ok"
	else
		echo "  $1: FAILED"
		FAILED=1
	fi
}

# expect <name> <pattern>: $OUT has a line matching the pattern.
expect() {
	echo "$OUT" | grep -q -- "$2"; check "$1" $?
}

# shot <user folder> <file> <options>: a screenshot of LEVEL3 by the grunts.
shot() {
	USER_DIR=$1
	FILE=$2
	shift 2
	OUT=$(MDK_USER_DIR="$USER_DIR" timeout 60 "$MDK" --mute --level=3 --at=-15,140,135,90 --wait=0.5 --screenshot="$FILE" "$@" 2>&1)
	[ -s "$FILE" ]
}

rm -rf "$OUT_DIR"
mkdir -p "$PLAIN" "$MOD/textures/LEVEL3" "$MOD/images" "$MOD/models/LEVEL3"

echo "export"
OUT=$(timeout 120 "$MDK" --export-assets="$EXPORT" 2>&1)
expect "summary" "^Exported to .*: [0-9]* textures, [0-9]* images, [0-9]* models"
[ -s "$EXPORT/textures/LEVEL3/H1_FLR.png" ]; check "a texture" $?
[ -s "$EXPORT/textures/LEVEL3/K_RUN_0.png" ]; check "Kurt's frame" $?
[ -s "$EXPORT/images/LEVEL3/SC_STAT.png" ]; check "the HUD's panel" $?
[ -s "$EXPORT/images/FONTBIG.png" ]; check "a font" $?
[ -s "$EXPORT/models/LEVEL3/XG.glb" ]; check "the grunt's model" $?

for FILE in "$EXPORT"/textures/LEVEL3/H1_*.png; do
	cp "$EXPORT/textures/LEVEL3/H4_MUD.png" "$MOD/textures/LEVEL3/$(basename "$FILE")"
done
cp "$EXPORT/images/LEVEL3/SKULL.png" "$MOD/images/SC_STAT.png"
cp "$EXPORT/models/LEVEL3/XG.glb" "$MOD/models/LEVEL3/"
printf 'name=Swap\npriority=0\n' > "$MOD/mod.txt"

echo "enhanced look"
shot "$PLAIN" "$OUT_DIR/plain.bmp" --enhanced; check "screenshot" $?
shot "$MODDED" "$OUT_DIR/modded.bmp" --enhanced --console="mods"; check "modded screenshot" $?
! cmp -s "$OUT_DIR/plain.bmp" "$OUT_DIR/modded.bmp"; check "the mod changes it" $?
expect "loaded" "^Mods: mods 1: [0-9]* images, 1 models"
expect "console: the mod" "^Swap (swap) on, priority 0"
expect "console: the model" "^models: XG"
expect "console: the panel" "^images: .*SC_STAT"

echo "original look"
shot "$PLAIN" "$OUT_DIR/plain_original.bmp" --original; check "screenshot" $?
shot "$MODDED" "$OUT_DIR/modded_original.bmp" --original; check "modded screenshot" $?
cmp -s "$OUT_DIR/plain_original.bmp" "$OUT_DIR/modded_original.bmp"; check "the mod changes nothing" $?

echo "switched off"
shot "$MODDED" "$OUT_DIR/none.bmp" --enhanced --mod=nothing; check "screenshot" $?
expect "unknown mod named" "^No mod nothing in "
cmp -s "$OUT_DIR/plain.bmp" "$OUT_DIR/none.bmp"; check "--mod: as without" $?
echo "mod.swap=Off" > "$MODDED/settings.cfg"
shot "$MODDED" "$OUT_DIR/off.bmp" --enhanced; check "screenshot" $?
cmp -s "$OUT_DIR/plain.bmp" "$OUT_DIR/off.bmp"; check "settings: as without" $?
# The console draws over the frame: a shot of its own.
shot "$MODDED" "$OUT_DIR/console.bmp" --enhanced --console="mods"; check "screenshot" $?
expect "console: off" "^Swap (swap) off"

[ $FAILED = 0 ] && echo PASSED && exit 0
echo FAILED
exit 1
