#!/bin/sh
# Sniper mode on level 3: Kurt lands facing the grunt XG_1006 (19, 181, 127), enters sniper mode
# looking 9° down and fires one round once the clip is loaded: a bullet takes 8 of its 60 health, a
# sniper grenade blows it up. The screenshot shows the scope: the dark SNIPERS1 frame around a lit
# view. The Godot port, from the same spot (--level=3 --at=0,120,138.2,75.5 --delay=0.3
# --sniper=1,9 --fire --profile=2): XG_1006 at 36 health after three bullets, as here with --fire.
# Run from the project folder after `dotnet build`: sh tests/sniper_test.sh
MDK=src/Mdk.App/bin/Debug/net10.0/mdk
SHOT=${TMPDIR:-/tmp}/mdk_sniper.bmp
SPOT=--at=0,120,150,75.5
# Godot's comparison starts just above the floor.
LOW_SPOT=--at=0,120,138.2,75.5
FAILED=0

# run <spot> <options>
run() {
	timeout 60 "$MDK" --level=3 --mute --profile --screenshot="$SHOT" --sniper=1,9 "$@" 2>&1
}

# The line of an object at a profile second: profile <output> <second> <object>.
profile() {
	echo "$1" | sed -n "/^Profile $2s:/,/^Profile/p" | grep "^  $3 "
}

check() {
	if [ "$2" = 0 ]; then
		echo "  $1: ok"
	else
		echo "  $1: FAILED"
		FAILED=1
	fi
}

OUT=$(run "$SPOT" --sniper-fire --wait=2.5)
profile "$OUT" 1 XG_1006
profile "$OUT" 2 XG_1006
profile "$OUT" 1 XG_1006 | grep -q "health 60 "; check "grunt at full health" $?
profile "$OUT" 2 XG_1006 | grep -q "health 52 "; check "a bullet takes 8" $?

# The scope's screen: the frame's left edge is darker than the view in the scope.
python - "$SHOT" <<'EOF'
import sys
from PIL import Image, ImageStat
image = Image.open(sys.argv[1]).convert("L")
w, h = image.size
frame = ImageStat.Stat(image.crop((0, 0, w // 40, h))).mean[0]
scope = ImageStat.Stat(image.crop((w * 3 // 8, h * 3 // 8, w * 5 // 8, h * 5 // 8))).mean[0]
print("  frame %.0f, scope %.0f" % (frame, scope))
sys.exit(0 if frame < 40 and scope > 2 * frame else 1)
EOF
check "scope overlay" $?

OUT=$(run "$SPOT" --sniper-fire --give=SW_SGREN --wait=3.5)
profile "$OUT" 3 "EXPLODE_[0-9]*" | head -1
[ -z "$(profile "$OUT" 3 XG_1006)" ]; check "a sniper grenade kills the grunt" $?

OUT=$(run "$LOW_SPOT" --fire --wait=2.5)
profile "$OUT" 2 XG_1006
profile "$OUT" 2 XG_1006 | grep -q "health 36 "; check "held fire: three hits (Godot: 36)" $?

[ $FAILED = 0 ] && echo PASSED && exit 0
echo FAILED
exit 1
