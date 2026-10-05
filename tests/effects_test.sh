#!/bin/sh
# Effects. Level 3: Kurt holds fire on the first grunts; his hits throw sparks, the grunt that dies
# blows up (EXPLODE) into its break-up pieces and slime drops. The Godot port, from the same spot
# (--level=3 --at=0,120,150,90 --fire --profile=N): debris pieces 19 at 7 s, 34 at 8 s; effects 41 at 10 s.
# Level 8's GUNT_3 fan: Kurt put in its box rises with his chute open (Godot: z 49 at 3 s, K_CHUTEC).
# Run from the project folder after `dotnet build`: sh tests/effects_test.sh
MDK=src/Mdk.App/bin/Debug/net10.0/mdk.exe
SHOT=${TMPDIR:-/tmp}/mdk_effects.bmp
FAILED=0

check() {
	if [ "$2" = 0 ]; then
		echo "  $1: ok"
	else
		echo "  $1: FAILED"
		FAILED=1
	fi
}

# The largest count of a profile line's field: most <output> <field>.
most() {
	echo "$1" | grep "^  effects " | sed "s/.*$2 \([0-9]*\).*/\1/" | sort -n | tail -1
}

OUT=$(timeout 60 "$MDK" --level=3 --mute --profile --screenshot="$SHOT" --at=0,120,150,90 --fire --wait=15 2>&1)
PIECES=$(most "$OUT" "debris pieces")
EFFECTS=$(most "$OUT" "effects")
echo "  most debris pieces $PIECES, most effects $EFFECTS"
[ "${PIECES:-0}" -gt 0 ]; check "sparks and pieces fly" $?
[ "${EFFECTS:-0}" -gt 0 ]; check "slime drops" $?
echo "$OUT" | grep -q "^  EXPLODE_"; check "a grunt explodes" $?

OUT=$(timeout 60 "$MDK" --level=8 --mute --profile --screenshot="$SHOT" --at=-505,858,-45,90 --wait=3 2>&1)
echo "$OUT" | grep "^Kurt at"
echo "$OUT" | grep -q "fans 1"; check "the fan is created" $?
Z=$(echo "$OUT" | grep "^Kurt at" | awk '{print int($5)}')
[ "${Z:--45}" -gt 30 ]; check "Kurt rises in the updraft" $?
echo "$OUT" | grep "^Kurt at" | grep -q " Chute "; check "his chute opens" $?

[ $FAILED = 0 ] && echo PASSED && exit 0
echo FAILED
exit 1
