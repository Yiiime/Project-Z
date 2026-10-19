extends SceneTree

# Uses the runtime collision function itself. This checks graph reachability,
# not rendered occlusion, full character-body collision, or continuous steering.
const WorldScript = preload("res://scripts/world.gd")
const STEP := 8
var failures := 0
var geometry_failures := 0

func _initialize() -> void:
	call_deferred("_run")

func _run() -> void:
	var world = WorldScript.new()
	root.add_child(world)
	await process_frame
	world.set_process(false)
	world.set_physics_process(false)
	for place in ["island", "cafe", "props"]:
		var start: Vector2 = world.default_pos(place)
		world.change_place(place, start)
		world.is_stage = place == "props"
		if not world.can_stand(start):
			failures += 1
			print("FAIL spawn ", place, " ", start)
			continue
		var points := _flood(world, start)
		print("WORLD ", place, " reachable samples=", points.size(), " step=", STEP)
		for spot in world.spots:
			_check_interaction_reachable(world, place + "/" + String(spot.id), points, spot.id)
		if place == "island":
			for destination in ["cafe", "props"]:
				var return_point: Vector2 = world.return_pos(destination)
				_check_reachable("island/" + destination + "_return", points, return_point, 16.0)
				if not world.can_stand(return_point):
					failures += 1
					print("FAIL return point ", destination, " ", return_point)
			_probe_object(world, "harbor_kiosk")
			_probe_object(world, "bench_695")
			_probe_object(world, "bench_1200")
			_probe_object(world, "props_building")
			_probe_solid(world, "island/eastern_water", Vector2(1600,800))
			_probe_solid(world, "island/western_water", Vector2(400,650))
		elif place == "cafe":
			_probe_object(world, "cafe_table_1")
			_probe_object(world, "cafe_table_2")
			_probe_object(world, "cafe_counter")
		else:
			_check_interaction_reachable(world, "props/crate", points, "crate")
			_check_interaction_reachable(world, "props/target", points, "target")
			_check_interaction_reachable(world, "props/checklist", points, "chen")
			_probe_object(world, "props_stool")
			_probe_solid(world, "props/active_stage_crate", world.stage_anchor("crate"))
	world.queue_free()
	print("WORLD RESULT unreachable=", failures, " art-collision failures=", geometry_failures)
	quit(1 if failures > 0 or geometry_failures > 0 else 0)

func _flood(world: Node2D, start: Vector2) -> Array[Vector2]:
	var bounds: Rect2 = world.map_snapshot().bounds
	var queue: Array[Vector2i] = [Vector2i.ZERO]
	var seen := {Vector2i.ZERO: true}
	var points: Array[Vector2] = []
	var head := 0
	var directions: Array[Vector2i] = [Vector2i.LEFT, Vector2i.RIGHT, Vector2i.UP, Vector2i.DOWN]
	while head < queue.size():
		var key: Vector2i = queue[head]
		head += 1
		points.append(start + Vector2(key) * STEP)
		for direction in directions:
			var next: Vector2i = key + direction
			if seen.has(next): continue
			seen[next] = true
			var p := start + Vector2(next) * STEP
			if not bounds.has_point(p): continue
			# Every sample is evaluated by the live World.can_stand implementation.
			if world.can_stand(p) and world.can_stand(start + (Vector2(key) + Vector2(next)) * STEP * 0.5):
				queue.append(next)
	return points

func _check_interaction_reachable(world: Node2D, title: String, points: Array[Vector2], id: String) -> void:
	for point in points:
		if world.can_interact_with(id, point):
			print("PASS ", title, " legal contact=", point)
			return
	failures += 1
	print("FAIL ", title, " has no reachable contact on the permitted side")

func _probe_object(world: Node2D, id: String) -> void:
	for object in world.objects:
		if object.id == id:
			_probe_solid(world, world.place + "/" + id, object.footprint.get_center())
			return
	geometry_failures += 1
	print("FAIL missing collision object ", world.place, "/", id)

func _check_reachable(label: String, points: Array[Vector2], target: Vector2, radius: float) -> void:
	var best := INF
	var closest := Vector2.ZERO
	for p in points:
		var distance := p.distance_to(target)
		if distance < best:
			best = distance
			closest = p
	if best > radius:
		failures += 1
		print("FAIL ", label, " nearest=", closest, " distance=", snappedf(best,0.1), " allowed=", radius)
	else:
		print("PASS ", label, " nearest=", closest, " distance=", snappedf(best,0.1))

func _probe_solid(world: Node2D, label: String, position: Vector2) -> void:
	if world.can_stand(position):
		geometry_failures += 1
		print("FAIL ART-COLLISION ", label, " admits foot position=", position)
	else:
		print("SOLID ", label, " ", position)
