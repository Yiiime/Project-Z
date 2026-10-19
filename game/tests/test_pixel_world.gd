extends SceneTree

# Runtime geometry and physics checks for the rebuilt pixel scenes. Reachability
# samples the real foot collision function; occlusion checks inspect the scene
# graph, not rendered pixels. Visual overlap still needs graphical review.
const WorldScript = preload("res://scripts/world.gd")
const MainScene = preload("res://main.tscn")
const STEP := 8.0
var checks := 0
var failures: Array[String] = []
var samples := {}
var isolated_dir := ""

func _initialize() -> void:
	var original_dir := OS.get_user_data_dir()
	ProjectSettings.set_setting("application/config/use_custom_user_dir", true)
	ProjectSettings.set_setting("application/config/custom_user_dir_name", "MoonZ Pixel World Tests/%d" % OS.get_process_id())
	isolated_dir = OS.get_user_data_dir()
	if original_dir == isolated_dir or not isolated_dir.contains("MoonZ Pixel World Tests"):
		printerr("FAIL: could not isolate saves; pixel world test did not start")
		quit(1)
		return
	_run.call_deferred()

func expect(ok: bool, detail: String) -> void:
	checks += 1
	if not ok:
		failures.append(detail)
		printerr("FAIL: " + detail)

func _run() -> void:
	var world = WorldScript.new()
	root.add_child(world)
	await process_frame
	world.set_process(false)
	world.set_physics_process(false)
	for place in ["island", "cafe", "props"]:
		world.change_place(place, world.default_pos(place))
		await physics_frame
		var snapshot: Dictionary = world.map_snapshot()
		expect(snapshot.place == place and snapshot.player == world.hero.position, place + ": map uses real scene and player")
		expect(world.can_stand(world.hero.position), place + ": spawn has a valid foot position")
		var points: Array[Vector2] = flood(world)
		samples[place] = points
		expect(points.size() > 100, place + ": connected walkable area exists")
		for spot in world.spots:
			var contact: Dictionary = find_contact(world, spot.id, points)
			expect(not contact.is_empty(), place + "/" + spot.id + ": reachable same-side interaction")
			expect(not world.can_interact_with(spot.id, spot.position + Vector2(500, 500)), place + "/" + spot.id + ": no remote interaction")
		for block in snapshot.blocks:
			expect(not world.can_stand(block.get_center()), place + ": solid footprint rejects foot center " + str(block))
		var sorted: Node2D = world.get_node_or_null("DepthSorted")
		expect(sorted != null and sorted.y_sort_enabled, place + ": depth layer enables foot Y sorting")
		if sorted != null:
			expect(world.hero.get_parent() == sorted and world.hero.z_index == 0, place + ": player shares the object depth plane")
		var foreground := world.get_node_or_null("Foreground")
		var ground := world.get_node_or_null("Ground")
		expect(foreground != null and ground != null, place + ": foreground and ground are separate layers")
		await check_real_collision(world, snapshot.blocks, place)
		print("PIXEL SCENE ", place, ": reachable samples=", points.size(), ", interactions=", world.spots.size(), ", footprints=", snapshot.blocks.size())
	world.change_place("island", world.default_pos("island"))
	for place in ["cafe", "props"]:
		var return_point: Vector2 = world.return_pos(place)
		expect(world.can_stand(return_point), place + ": returning doorway spawns on public path")
		expect(return_point.distance_to(world.spot_position(place)) <= 64.0, place + ": return stays beside matching doorway")
	world.change_place("cafe", world.default_pos("cafe"))
	var behind_counter := Vector2(792, 541)
	expect(world.can_stand(behind_counter), "counter back-side rejection fixture stands on actual floor")
	expect(behind_counter.distance_to(world.spot_position("lin")) < world.INTERACTION_RADIUS, "back-side fixture is inside interaction distance")
	expect(not world.can_interact_with("lin", behind_counter), "nearby cafe interaction rejects the wrong approach side")
	var opposite_counter := Vector2(792, 478)
	expect(world.can_stand(opposite_counter), "counter occlusion fixture stands on actual floor")
	expect(world._line_blocked(opposite_counter, world.spot_position("lin")), "counter footprint blocks line of interaction across its body")
	world.change_place("props", world.default_pos("props"))
	world.is_stage = true
	for id in ["crate", "target", "chen"]:
		expect(not find_contact(world, id, samples.props).is_empty(), "stage/" + id + ": reachable from room entrance")
	var stage_snapshot: Dictionary = world.map_snapshot()
	for id in ["crate", "target"]:
		var present := false
		for spot in stage_snapshot.spots:
			if spot.id == id: present = true
		expect(present, "map includes active stage anchor " + id)
	world.queue_free()
	await process_frame
	await check_main_spatial_flow()
	for filename in ["day1-auto.json", "day1-manual.json", "pixel-spatial.json"]:
		for suffix in ["", ".bak", ".tmp", ".bak.tmp"]:
			var path := isolated_dir.path_join(filename + suffix)
			if FileAccess.file_exists(path): DirAccess.remove_absolute(path)
	print("PIXEL WORLD RESULT checks=", checks, " failures=", failures.size())
	quit(0 if failures.is_empty() else 1)

func flood(world: Node2D) -> Array[Vector2]:
	var start: Vector2 = world.hero.position
	var bounds: Rect2 = world.map_snapshot().bounds
	var queue: Array[Vector2i] = [Vector2i.ZERO]
	var seen := {Vector2i.ZERO: true}
	var points: Array[Vector2] = []
	var head := 0
	while head < queue.size():
		var key: Vector2i = queue[head]
		head += 1
		var current := start + Vector2(key) * STEP
		points.append(current)
		for direction in [Vector2i.LEFT, Vector2i.RIGHT, Vector2i.UP, Vector2i.DOWN]:
			var next: Vector2i = key + direction
			if seen.has(next): continue
			seen[next] = true
			var candidate := start + Vector2(next) * STEP
			if bounds.has_point(candidate) and world.can_stand(candidate) and world.can_stand(current.lerp(candidate, 0.5)):
				queue.append(next)
	return points

func find_contact(world: Node2D, id: String, points: Array) -> Dictionary:
	var best := INF
	var result := {}
	var anchor: Vector2 = world.stage_anchor(id) if id in ["crate", "target"] else world.spot_position(id)
	for point in points:
		var distance: float = point.distance_to(anchor)
		if distance < best and world.can_interact_with(id, point):
			best = distance
			result = {"position": point}
	return result

func check_real_collision(world: Node2D, blocks: Array, place: String) -> void:
	var spawn: Vector2 = world.hero.position
	world.frozen = false
	for direction in [Vector2.LEFT, Vector2.RIGHT, Vector2.UP, Vector2.DOWN]:
		if not world.can_stand(spawn + direction * 8.0): continue
		await physics_frame
		world.move_character(direction)
		expect(world.hero.position.distance_to(spawn) > 0.1, place + ": actual physics moves on clear floor")
		world.hero.position = spawn
		break
	var probes := 0
	for block in blocks:
		var center: Vector2 = block.get_center()
		for start in [Vector2(block.position.x - 12, center.y), Vector2(block.end.x + 12, center.y), Vector2(center.x, block.position.y - 10), Vector2(center.x, block.end.y + 10)]:
			if not world.can_stand(start): continue
			world.hero.position = start
			world.frozen = false
			var direction: Vector2 = start.direction_to(center)
			for frame in range(12):
				await physics_frame
				world.move_character(direction)
			var finish: Vector2 = world.hero.position
			expect(not block.has_point(finish) and world.can_stand(finish), place + ": real CharacterBody2D stops at footprint")
			expect(finish.distance_to(start) < 20.0, place + ": movement cannot tunnel through obstacle")
			probes += 1
			break
		if probes >= 2: break
	expect(probes > 0, place + ": exercised real physics against accessible furniture/building")

func position_near(app: Node, id: String) -> bool:
	var contact := find_contact(app.world, id, samples[app.world.place])
	expect(not contact.is_empty(), "main flow reaches " + app.world.place + "/" + id)
	if contact.is_empty(): return false
	app.world.hero.position = contact.position
	app.world.moved.emit(contact.position)
	app.world.nearby = app.world.interaction_at(contact.position)
	return true

func use_spot(app: Node, id: String) -> void:
	app.close_modal()
	if not position_near(app, id): return
	expect(app.world.nearby == id, "nearby selection matches " + id)
	app.world.frozen = false
	app.world.interact()
	await process_frame

func check_main_spatial_flow() -> void:
	var app = MainScene.instantiate()
	root.add_child(app)
	await process_frame
	app.new_game()
	app.close_modal()
	app.world.set_physics_process(false)
	app.world.set_process(false)
	expect(app.world.hero.position == app.world.default_pos("island"), "main starts at world-owned spawn")
	await use_spot(app, "xu")
	expect(app.modal.visible, "registration interaction opens actual main panel")
	app.do_action("check_in")
	expect(app.model.data.flags.get("checked_in", false), "reachable registration still settles rule")
	await use_spot(app, "props")
	expect(app.world.place == "props" and app.model.data.location == "props", "door keeps runtime and model location synchronized")
	await use_spot(app, "chen")
	app.begin_activity("stage_work", {})
	expect(app.world.is_stage, "main enters spatial crate activity")
	var original_time: int = app.model.data.time
	app.world.hero.position = app.world.default_pos("props")
	if not app.world.can_interact_with("crate", app.world.hero.position):
		expect(app.stage_hint().is_empty(), "distant crate has no E prompt")
		app.resume_activity()
		expect(not app.model.data.activity.carrying, "distant E cannot pick up crate")
	if position_near(app, "crate"):
		expect(app.stage_hint().contains("拿起"), "same-side crate contact shows pickup prompt")
		app.resume_activity()
		expect(app.model.data.activity.carrying and app.world.carrying, "main picks up crate through world anchor")
	var carried_map: Dictionary = app.world.map_snapshot()
	var source_still_visible := false
	for spot in carried_map.spots:
		if spot.id == "crate": source_still_visible = true
	expect(not source_still_visible, "map removes crate from original position after pickup")
	app.save_game("user://pixel-spatial.json", false)
	app.load_game("user://pixel-spatial.json")
	expect(app.world.carrying and app.world.can_stand(app.world.hero.position), "save/resume restores carried crate on valid floor")
	if position_near(app, "target"):
		app.resume_activity()
		expect(app.model.data.activity.placed and app.world.crate_placed, "main places crate through world anchor")
		expect(app.world.can_stand(app.world.hero.position), "placing crate preserves clear space for player's feet")
	var placed_map: Dictionary = app.world.map_snapshot()
	var placed_marker := false
	for spot in placed_map.spots:
		if spot.id == "target" and spot.position == app.world.stage_anchor("target") and spot.title.contains("已放好"):
			placed_marker = true
	expect(placed_marker, "map reports crate at actual placed destination")
	if position_near(app, "chen"):
		app.resume_activity()
		expect(app.modal.visible, "returning to Chen opens actual handover panel")
		app.complete_activity({"correct": true})
	expect(app.model.data.flags.get("stage_done", false) and app.model.data.time == original_time + 60, "reachable handover settles one 60-minute action")
	await use_spot(app, "exit")
	expect(app.world.place == "island" and app.world.can_stand(app.world.hero.position), "props exit returns to valid public ground")
	await use_spot(app, "cafe")
	await use_spot(app, "lin")
	app.do_action("buy_gift")
	expect(app.model.data.inventory.gift == 1, "reachable cafe service still supplies nightly gift")
	await use_spot(app, "exit")
	await use_spot(app, "bell")
	app.do_action("observe_bell")
	expect(app.model.data.flags.get("bell_observed", false), "reachable harbor bell still supplies nightly topic")
	app.close_modal()
	var before_map: int = app.model.data.time
	app.show_map()
	var map = app.modal.find_child("SpatialMap", true, false)
	expect(map != null, "M menu uses spatial map widget")
	if map != null:
		expect(map.snapshot.player == app.world.hero.position, "M map draws exact player position")
		expect(map.snapshot.blocks == app.world.blocks, "M map collision outlines share runtime data")
	expect(app.model.data.time == before_map, "map review does not consume game time")
	app.queue_free()
	await process_frame
