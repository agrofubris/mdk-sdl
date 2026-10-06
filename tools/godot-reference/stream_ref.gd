## Reference dump of the Godot port's StreamTube with Watcom's rand() (seed 1) instead of randi().
extends SceneTree


class WatcomTube extends StreamTube:
	static var state := 1

	func _rand() -> int:
		state = (state * 0x41C64E6D + 0x3039) & 0xFFFFFFFF
		return (state >> 16) & 0x7FFF


func v(p: Vector3) -> String:
	return "%.3f %.3f %.3f" % [p.x, p.y, p.z]


func dump(index: int, difficulty: int, kind: int, advances: int) -> void:
	WatcomTube.state = 1
	var tube := WatcomTube.new(index, difficulty, kind)
	print("tube %d %d %d: turn %.1f radius %.1f %.1f" % [index, difficulty, kind, tube._max_turn, tube._min_radius, tube._max_radius])
	print("  ramp 0 %s 10 %s 63 %s" % [tube._ramp[0].to_html(false), tube._ramp[10].to_html(false), tube._ramp[63].to_html(false)])
	var spawns := tube.take_spawns()
	print("  init head %d tail %d lights %d" % [tube.head, tube.tail, spawns.size()])
	var s: StreamTube.Spawn = spawns[0]
	print("  light0 t %.1f x %.4f z %.4f size %.4f speed %.4f" % [s.t, s.x, s.z, s.size, s.speed])
	for n in [1, 10, 30]:
		print("  ring %d origin %s point0 %s point5 %s" % [n, v(tube._origins[n & 31]), v(tube._points[n & 31][0]), v(tube._points[n & 31][5])])
	print("  segment 5 colours %s" % [str(Array(tube._colours[5]))])
	print("  segment 5 plane0 %s %.3f plane7 %s %.3f" % [v(tube._normals[5][0]), tube._distances[5][0], v(tube._normals[5][7]), tube._distances[5][7]])
	print("  centre 10.5 %s place %s" % [v(tube.centre(10.5)), v(tube.place(10.5, 1.0, 2.0))])
	var f := tube.frame(10.5, Vector3(0, 0, 1))
	print("  frame 10.5 x %s y %s z %s" % [v(f.x), v(f.y), v(f.z)])
	var c := tube.centre(10.5)
	print("  wall axis %.3f wall far %.4f" % [tube.wall_hit(c, 10.5, 1.5), tube.wall_hit(c + f.x * 9.5, 10.5, 1.5)])
	var lights := 0
	var planet := -1.0
	for i in advances:
		tube.advance()
		for spawn in tube.take_spawns():
			if spawn.kind == StreamTube.SpawnKind.PLANET:
				planet = spawn.t
			else:
				lights += 1
	print("  after %d: head %d tail %d lights %d planet %.1f" % [advances, tube.head, tube.tail, lights, planet])
	var last: int = mini(tube.head - 1, tube.tail + 30)
	print("  ring %d origin %s point3 %s" % [last, v(tube._origins[last & 31]), v(tube._points[last & 31][3])])
	print("  angles %s radius %.4f" % [v(tube._angles), tube._radius])


func _init() -> void:
	dump(0, 1, StreamTube.Kind.NORMAL, 200)
	dump(2, 2, StreamTube.Kind.NORMAL, 50)
	dump(4, 1, StreamTube.Kind.GUNTER, 200)
	quit()
