#!/bin/sh
# The console on level 3 (--console opens it and runs commands): pos prints options that start
# there, god survives --die, tp moves Kurt to an arena, save and load a full save, map loads
# another level, kill and unknown commands answer; the game's own lines still print.
# Run from the project folder after `dotnet build`: sh tests/console_test.sh
# No window: the game draws off screen and never takes the focus.
export MDK_HIDDEN=1
# Default settings and saves, not the player's.
export MDK_USER_DIR="$(mktemp -d)"
MDK=src/Mdk.App/bin/Debug/net10.0/mdk
SHOT=${TMPDIR:-/tmp}/mdk_console.bmp
FAILED=0

run() {
	timeout 60 "$MDK" --level=3 --mute --screenshot="$SHOT" "$@" 2>&1
}

# expect <name> <pattern>: $OUT has a line matching the pattern.
expect() {
	echo "$OUT" | grep -q -- "$2" && echo "  $1: ok" || { echo "  $1: FAILED"; FAILED=1; }
}

OUT=$(run --wait=1 --console="pos;bogus;kill")
expect "game lines still print" "^Level 3: 17 arenas"
expect "command echoed" "^\] pos;bogus;kill"
expect "pos" "^level 3 arena HMO_1 at "
expect "pos as --at" "^--level=3 --at=-4.00,0.00,"
expect "pos as --teleport" "^--level=3 --teleport=HMO_1,-4.00,0.00,"
expect "unknown command" "^Unknown command: bogus"
expect "kill" "^Killed [0-9]* enemies"

OUT=$(run --wait=2 --die --console="god")
expect "god survives" "^Kurt at .* health 100"
echo "$OUT" | grep -q "^Kurt died" && { echo "  god: FAILED (died)"; FAILED=1; }

OUT=$(run --wait=1.5 --console="tp HMO_4;health 40;save console_t")
expect "tp to an arena" "^Kurt at .* arena HMO_4 health 40"
expect "save" "^Saved console_t"

OUT=$(run --wait=1 --console="load console_t")
expect "load" "^restored hash"

OUT=$(run --wait=1 --console="map 6")
expect "map" "^Level 6: "

[ $FAILED = 0 ] && echo PASSED && exit 0
echo FAILED
exit 1
