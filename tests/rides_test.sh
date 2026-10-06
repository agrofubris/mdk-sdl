#!/bin/sh
# The rides (rides.gd, snowboard.gd, bomber.gd), compared with the Godot port's runs of the same
# options (tests/walker_ride_test.sh, snowboard_test.sh, bomber_test.sh there):
# - LEVEL7's XD2: Kurt rides it for 12 s and stays on the floor (he doesn't collide with the walker
#   he sits in). Godot: riding XD2, Kurt at (71, 4903, -27).
# - LEVEL4's snowboard of CMEAT_3: Kurt lands on it, rides it and breaks through the ice wall
#   (group 3): past y 4500 after 12 s. Godot: riding XSNOWB, Kurt at (239, 4858, -654).
# - LEVEL7's XE: the comm device calls it, Kurt drops onto it, the view sinks into it, the script
#   unlocks the controls and Kurt drops a bomb. Godot: "bomber view 0.0, locked false, bombs 9",
#   XBN_BOMB at (121, 2394, -53).
# Run from the project folder after `dotnet build`: sh tests/rides_test.sh
# No window: the game draws off screen and never takes the focus.
export MDK_HIDDEN=1
# Default settings, not the player's (their difficulty changes damage).
export MDK_USER_DIR="$(mktemp -d)"
MDK=src/Mdk.App/bin/Debug/net10.0/mdk
SHOT=${TMPDIR:-/tmp}/mdk_rides.bmp
FAILED=0

run() {
	timeout 120 "$MDK" --mute --profile --screenshot="$SHOT" "$@" 2>&1
}

check() {
	if [ "$2" = 0 ]; then
		echo "  $1: ok"
	else
		echo "  $1: FAILED"
		FAILED=1
	fi
}

# The last "Kurt at x y z" coordinate n (1-3).
kurt() {
	echo "$1" | grep "^Kurt at" | tail -1 | cut -d' ' -f$(($2 + 2))
}

OUT=$(run --level=7 --delay=0.5 --at=46,4807,-25,270 --ride=XD2 --walk=12 --wait=13)
echo "$OUT" | grep "^riding" | tail -1
echo "$OUT" | grep "^Kurt at"
echo "$OUT" | grep -q "^riding XD2"; check "rides the XD2" $?
awk "BEGIN { exit !($(kurt "$OUT" 3) > -40 && $(kurt "$OUT" 3) < 0) }"; check "stays on the floor" $?

OUT=$(run --level=4 --delay=0.5 --teleport=CMEAT_3,420,4285,-570 --wait=12.5)
echo "$OUT" | grep "^riding" | tail -1
echo "$OUT" | grep "^Kurt at"
echo "$OUT" | grep -q "^riding XSNOWB"; check "rides the snowboard" $?
awk "BEGIN { exit !($(kurt "$OUT" 2) > 4500) }"; check "breaks through the ice wall" $?

OUT=$(run --level=7 --delay=0.5 --teleport=DANT_5,100,2320,-60 --bomber=drop --wait=13.5)
echo "$OUT" | grep "XBN_BOMB" | head -1
echo "$OUT" | grep -q "^riding XE"; check "rides the XE" $?
echo "$OUT" | grep -q "^bomber view 0.0, locked false, bombs 9"; check "view in the XE, a bomb dropped" $?
echo "$OUT" | grep -q "XBN_BOMB.* DANT_5"; check "the bomb falls" $?

[ $FAILED = 0 ] && echo PASSED && exit 0
echo FAILED
exit 1
