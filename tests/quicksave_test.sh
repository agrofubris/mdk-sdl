#!/bin/sh
# Quick save (F2, the original's full save with its name prompt) and quick load (F9): keys pressed
# by --press at set times. Save then load gives the same level state (same hash); loading with no
# save, saving in sniper mode and the keys outside a level say so or do nothing; the pause menu
# names the bound keys (a rebound key from settings.cfg too). Saves go to a temporary user folder.
# Run from the project folder after `dotnet build`: sh tests/quicksave_test.sh
# No window: the game draws off screen and never takes the focus.
export MDK_HIDDEN=1
MDK=src/Mdk.App/bin/Debug/net10.0/mdk
OUT_DIR=${TMPDIR:-/tmp}
SHOT=$OUT_DIR/mdk_quicksave.bmp
MDK_USER_DIR=$OUT_DIR/mdk_quicksave_user
export MDK_USER_DIR
rm -rf "$MDK_USER_DIR"
FAILED=0

check() {
	if [ "$2" = 0 ]; then
		echo "  $1: ok"
	else
		echo "  $1: FAILED"
		FAILED=1
	fi
}

run() {
	timeout 120 "$MDK" --mute --screenshot="$SHOT" "$@" 2>&1
}

OUT=$(run --level=3 --press=QuickLoad@1 --wait=1.5)
echo "$OUT" | grep -q "^Nothing to load"; check "nothing to load" $?

OUT=$(run --level=3 --press=QuickSave@1,Menu.Accept@1.3,QuickLoad@2 --wait=3)
SAVED=$(echo "$OUT" | grep "^snapshot hash" | sed 's/^snapshot //')
LOADED=$(echo "$OUT" | grep "^restored hash" | sed 's/^restored //')
echo "$OUT" | grep -q "^Game saved" && echo "$OUT" | grep -q "^Game loaded"; check "saved and loaded messages" $?
[ -n "$SAVED" ] && [ "$SAVED" = "$LOADED" ]; check "quick load restores the level ($SAVED)" $?

OUT=$(run --level=3 --sniper --press=QuickSave@2.5 --wait=2.8)
echo "$OUT" | grep -q "^Can't save now"; check "no save in sniper mode" $?

OUT=$(run --menu --press=QuickLoad@0.5,QuickSave@0.6 --wait=1)
! echo "$OUT" | grep -q "Loaded game\|Nothing to load\|Game saved\|Can't save"; check "no keys outside a level" $?

# The pause menu opens on a level that isn't the first (its screenshot by wall time).
OUT=$(run --level=3 --press=QuickLoad@0.5,Menu.Back@1.5 --wait=2.5)
echo "$OUT" | grep -q "^Hint: Quick save: F2   Quick load: F9"; check "pause menu names the keys" $?

printf 'bind.QuickLoad=F5\n' > "$MDK_USER_DIR/settings.cfg"
OUT=$(run --level=3 --press=QuickLoad@0.5,Menu.Back@1.5 --wait=2.5)
echo "$OUT" | grep -q "^Hint: Quick save: F2   Quick load: F5"; check "rebound key from settings.cfg" $?

rm -rf "$MDK_USER_DIR"
[ $FAILED = 0 ] && echo PASSED && exit 0
echo FAILED
exit 1
