#!/bin/sh
# The inventory stays on screen outside sniper mode (0x46cce4 resets its timer 0x574328 to 60
# every frame): 3 seconds after the start, with Kurt still and firing, the super chain gun's
# slot and its ticks are still drawn at the bottom left. Each run is compared with the same run
# without the pickup.
# Run from the project folder after `dotnet build`: sh tests/hud_test.sh
# No window: the game draws off screen and never takes the focus.
export MDK_HIDDEN=1
# Default settings, not the player's (their difficulty changes damage).
export MDK_USER_DIR="$(mktemp -d)"
MDK=src/Mdk.App/bin/Debug/net10.0/mdk
DIR=${TMPDIR:-/tmp}
FAILED=0

# shot <file> <options>
shot() {
	FILE="$DIR/$1"
	shift
	rm -f "$FILE"
	timeout 60 "$MDK" --level=3 --mute --wait=3 --screenshot="$FILE" "$@" > /dev/null 2>&1
}

check() {
	if [ "$2" = 0 ]; then
		echo "  $1: ok"
	else
		echo "  $1: FAILED"
		FAILED=1
	fi
}

# inventory_shown <with> <without>: the bottom left (the first two slots) differs.
inventory_shown() {
	python - "$DIR/$1" "$DIR/$2" <<'EOF'
import sys
from PIL import Image, ImageChops, ImageStat
with_item = Image.open(sys.argv[1]).convert("L")
without = Image.open(sys.argv[2]).convert("L")
w, h = with_item.size
box = (0, h * 300 // 360, w * 80 // 600, h)
diff = ImageStat.Stat(ImageChops.difference(with_item.crop(box), without.crop(box))).mean[0]
print("  bottom left difference %.1f" % diff)
sys.exit(0 if diff > 5 else 1)
EOF
}

shot mdk_hud_gatt.bmp --give=SW_GATT
shot mdk_hud_none.bmp
inventory_shown mdk_hud_gatt.bmp mdk_hud_none.bmp; check "inventory shown while still" $?

shot mdk_hud_gatt_fire.bmp --give=SW_GATT --fire
shot mdk_hud_none_fire.bmp --fire
inventory_shown mdk_hud_gatt_fire.bmp mdk_hud_none_fire.bmp; check "inventory shown while firing" $?

[ $FAILED = 0 ] && echo PASSED && exit 0
echo FAILED
exit 1
