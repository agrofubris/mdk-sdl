## Runs the Godot port's stream with a Watcom-rand tube (seed 1) at a fixed 60 fps, printing Kurt
## until the first wall hit (the hurt rolls use randi() there, so later frames differ).
extends SceneTree


class WatcomTube extends StreamTube:
	static var state := 1

	func _rand() -> int:
		state = (state * 0x41C64E6D + 0x3039) & 0xFFFFFFFF
		return (state >> 16) & 0x7FFF


var stream: Node
var frames := 0
var last_health := -1


func _initialize() -> void:
	var level := int(OS.get_environment("STREAM_LEVEL")) if OS.get_environment("STREAM_LEVEL") != "" else 7
	root.get_node("GameState").level = level
	stream = load("res://game/stream/stream.tscn").instantiate()
	root.add_child(stream)


## The stream is ready once the first frame starts: its tube is replaced before its first update.
func _swap() -> void:
	for s in stream._sprites:
		s.node.queue_free()
	stream._sprites.clear()
	WatcomTube.state = 1
	stream._tube = WatcomTube.new(stream._index, stream._difficulty, stream._kind)
	stream._add_spawns()
	stream._screen_up = Vector3(0, 0, 1)
	stream._view_rows.clear()
	stream._update_camera()
	print("difficulty %d health %d lights %d t %.2f" % [stream._difficulty, stream._health, stream._sprites.size(), stream._kurt.t])
	last_health = stream._health


func _process(_delta: float) -> bool:
	frames += 1
	if frames == 1:
		_swap()
	var k = stream._kurt
	if frames % 30 == 0 or stream._health != last_health:
		print("frame %d t %.3f x %.3f z %.3f yaw %.2f speed %.3f tail %d health %d pos %.3f %.3f %.3f eye %.3f %.3f %.3f" % [frames, k.t, k.x, k.z, k.yaw, k.speed, stream._tube.tail, stream._health,
				stream._kurt_position.x, stream._kurt_position.y, stream._kurt_position.z, stream._camera_eye.x, stream._camera_eye.y, stream._camera_eye.z])
	if stream._health != last_health:
		return true
	return frames > 2400
