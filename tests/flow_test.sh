#!/bin/sh
# The menus, saves and level flow: the main menu and its options, the statistics and the briefing,
# the end of a level leading to the statistics, Kurt's death leading to the menu with "Continue",
# and a saved game written and loaded again. Settings and saves go to a temporary user folder.
# Run from the project folder after `dotnet build`: sh tests/flow_test.sh
# No window: the game draws off screen and never takes the focus.
export MDK_HIDDEN=1
MDK=src/Mdk.App/bin/Debug/net10.0/mdk
OUT_DIR=${TMPDIR:-/tmp}
SHOT=$OUT_DIR/mdk_flow.bmp
MDK_USER_DIR=$OUT_DIR/mdk_flow_user
export MDK_USER_DIR
rm -rf "$MDK_USER_DIR"
FAILED=0

run() {
	timeout 120 "$MDK" --mute --screenshot="$SHOT" "$@" 2>&1
}

check() {
	if [ "$2" = 0 ]; then
		echo "  $1: ok"
	else
		echo "  $1: FAILED"
		FAILED=1
	fi
}

OUT=$(run --menu --wait=1)
echo "$OUT" | grep "^Menu"
echo "$OUT" | grep -q "^Menu: New Game | Level: 1 | Saved Game | Options | Quit$"; check "main menu" $?
[ -f "$SHOT" ]; check "menu screenshot" $?

OUT=$(run --menu --options --wait=1)
echo "$OUT" | grep -q "^Menu: Master volume: 80 | .* | Gore: On | Controls | Back$"; check "options page" $?

OUT=$(run --stats=7 --counts=120,60,10,5,8,20,3 --phase=1 --wait=2)
echo "$OUT" | grep "^Score"
echo "$OUT" | grep -q "^Score-O-matic: 120/120, 50/100, 10/10, 50/100, 8/20, heads 3$"; check "Score-O-matic counts" $?

OUT=$(run --briefing=6 --wait=2)
echo "$OUT" | grep -q "^Statistics: Briefing$"; check "briefing" $?

# special_event 1 ends level 7: the tornado (about 7 s), then the stream (tests/stream_test.sh
# follows it to the statistics).
OUT=$(run --level=7 --event=1 --wait=12)
echo "$OUT" | grep "^Stream"
echo "$OUT" | grep -q "^Stream after LEVEL7: Normal,"; check "level end leads to the stream" $?

# Kurt dies: LASTGAME is written and the menu offers "Continue".
OUT=$(run --level=3 --die --wait=10)
echo "$OUT" | grep "^Kurt died\|^Menu"
echo "$OUT" | grep -q "^Menu: Continue | New Game | Level: 3 |"; check "death leads to the menu with Continue" $?

OUT=$(run --level=6 --save=FLOW1 --wait=0.5)
echo "$OUT" | grep "^Saved game"
grep -q '"type":3,"level":6' "$MDK_USER_DIR/saves/FLOW1.sav"; check "game saved" $?
OUT=$(run --load=FLOW1 --wait=0.5)
echo "$OUT" | grep "^Loaded\|^Level"
echo "$OUT" | grep -q "^Level 6:"; check "saved game loads level 6" $?

rm -rf "$MDK_USER_DIR"
[ $FAILED = 0 ] && echo PASSED && exit 0
echo FAILED
exit 1
