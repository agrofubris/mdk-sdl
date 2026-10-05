#!/bin/sh
# Smoke test: every level renders a frame that isn't one flat colour.
# Run from the project folder after `dotnet build`: sh tests/screenshot_test.sh
MDK=src/Mdk.App/bin/Debug/net10.0/mdk.exe
OUT=${TMPDIR:-/tmp}
FAILED=0
for LEVEL in 3 4 5 6 7 8; do
	FILE="$OUT/mdk_level$LEVEL.bmp"
	rm -f "$FILE"
	timeout 60 "$MDK" --level=$LEVEL --mute --screenshot="$FILE" > /dev/null 2>&1
	# Distinct byte values in the pixel data (after the 54-byte header).
	VALUES=$(tail -c +55 "$FILE" 2>/dev/null | od -An -v -tu1 | tr -s ' ' '\n' | sort -u | wc -l)
	if [ "$VALUES" -lt 16 ]; then
		echo "level $LEVEL: FAILED ($VALUES distinct values)"
		FAILED=1
	else
		echo "level $LEVEL: ok"
	fi
done
[ $FAILED = 0 ] && echo PASSED && exit 0
echo FAILED
exit 1
