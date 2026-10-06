# Godot reference scripts

Scripts run against the Godot port (`../godot-mdk`) to produce the numbers some tests compare
with. They swap the Godot port's random numbers for the original's Watcom `rand()` (seed 1) so
both ports draw the same values.

They use the Godot port's classes, so copy a script into that project first:

```
godot --headless --audio-driver Dummy --fixed-fps 60 --path ../godot-mdk -s <script>
```

- `stream_ref.gd`: the stream tube after levels (rings, points, colours, lights) for `StreamTests`.
- `stream_run.gd`: Kurt's flight through the stream until the first wall hit, for
  `tests/stream_test.sh`.
