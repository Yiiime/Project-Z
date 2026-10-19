extends SceneTree

## Native captures of the real main.tscn and its real UI handlers.
## Run: godot --path game --script res://tests/capture_day.gd
## Uses isolated saves and standable position fixtures, not keyboard traversal.
## These pictures are layout evidence, not an assertion of manual play testing.
const MainScene = preload("res://main.tscn")
const CAPTURE_NAMES := ["explore", "map", "dialogue", "cafe", "work", "inventory", "night", "summary"]
var app
var isolated_dir := ""
var checks := 0
var captures := 0
var failures: Array[String] = []
var finished := false


func _initialize() -> void:
	if DisplayServer.get_name() == "headless":
		printerr("FAIL: capture_day requires a native rendering driver; do not pass --headless.")
		quit(1)
		return
	var original_dir := OS.get_user_data_dir()
	ProjectSettings.set_setting("application/config/use_custom_user_dir", true)
	ProjectSettings.set_setting("application/config/custom_user_dir_name", "MoonZ Capture Tests/%d" % OS.get_process_id())
	isolated_dir = OS.get_user_data_dir()
	if isolated_dir == original_dir or not isolated_dir.contains("MoonZ Capture Tests"):
		printerr("FAIL: save isolation unavailable; no game was started.")
		quit(1)
		return
	_run.call_deferred()
	_watchdog.call_deferred()


func _watchdog() -> void:
	await create_timer(100.0).timeout
	if not finished:
		failures.append("Native capture run exceeded 100 seconds.")
		_finish()


func _expect(condition: bool, detail: String) -> void:
	checks += 1
	if not condition:
		failures.append(detail)
		printerr("CHECK FAILED: " + detail)
		_finish()


func _frame() -> void:
	await process_frame
	await process_frame


func _button_in(node: Node, caption: String) -> Button:
	if node is Button and not node.is_queued_for_deletion() and node.is_visible_in_tree() and node.text == caption:
		return node
	for child in node.get_children():
		var found := _button_in(child, caption)
		if found != null: return found
	return null


func _press(caption: String, scope: Node = null) -> void:
	var found := _button_in(scope if scope != null else app, caption)
	_expect(found != null, "Visible button exists: " + caption)
	if found == null: return
	_expect(not found.disabled, "Button enabled: " + caption)
	if found.disabled: return
	found.pressed.emit()
	await _frame()


func _key(code: Key) -> void:
	var key := InputEventKey.new()
	key.keycode = code
	key.physical_keycode = code
	key.pressed = true
	app._unhandled_key_input(key)
	await _frame()


func _position_at(target: Vector2) -> void:
	_expect(app.world.can_stand(target), "Position fixture is standable: " + str(target))
	if not app.world.can_stand(target): return
	app.world.hero.position = target
	app.world.hero.velocity = Vector2.ZERO
	app.world.moved.emit(target)
	app.world._physics_process(0.0)
	await _frame()


func _interaction_point(id: String, stage: bool = false) -> Vector2:
	var anchor := Vector2.ZERO
	var normal := Vector2.DOWN
	var exists := stage
	if stage:
		anchor = app.world.stage_anchor(id)
	else:
		for spot in app.world.spots:
			if spot.id == id:
				anchor = spot.position
				normal = spot.get("normal", Vector2.DOWN)
				exists = true
				break
	_expect(exists, "Scene has interaction: " + id)
	if not exists: return Vector2.INF
	# Prefer an approach point instead of standing in a crate's occupied centre.
	var preferred := anchor + (normal if normal != Vector2.ZERO else Vector2.DOWN) * 24.0
	if app.world.can_interact_with(id, preferred) and (stage or app.world.interaction_at(preferred) == id):
		return preferred
	for radius in range(0, 101, 4):
		for angle in range(0, 360, 15):
			var candidate := anchor + Vector2.from_angle(deg_to_rad(angle)) * radius
			if app.world.can_interact_with(id, candidate) and (stage or app.world.interaction_at(candidate) == id):
				return candidate
	_expect(false, "No unobstructed, standable interaction fixture: " + id)
	return Vector2.INF


func _interact(id: String, stage: bool = false) -> void:
	var point := _interaction_point(id, stage)
	if not point.is_finite(): return
	await _position_at(point)
	_expect(app.world.can_interact_with(id, app.world.hero.position), "Actual foot position can interact: " + id)
	if not stage:
		_expect(app.world.nearby == id, "Proximity selects interaction: " + id)
	await _key(KEY_E)


func _confirm(caption: String) -> void:
	await _press(caption, app.modal)
	await _press("确认", app.modal)


func _capture(id: String) -> void:
	_expect(id in CAPTURE_NAMES, "Known screenshot id: " + id)
	# Let real toast expiration, camera smoothing, and container layout finish.
	# This is real UI elapsed time; the game clock remains action based.
	await create_timer(4.25).timeout
	await _frame()
	if id == "map":
		var spatial_map := app.modal.find_child("SpatialMap", true, false) as Control
		_expect(spatial_map != null, "Actual SpatialMap exists in the main UI tree")
		if spatial_map != null:
			_expect(spatial_map.size.is_equal_approx(Vector2(890, 495)), "SpatialMap remains constrained to 890 x 495")
			_expect(Rect2(Vector2.ZERO, Vector2(1600, 900)).encloses(spatial_map.get_global_rect()), "SpatialMap stays inside native viewport")
			_expect(spatial_map.snapshot.player.is_equal_approx(app.world.hero.position), "Map player marker comes from the current world position")
	await RenderingServer.frame_post_draw
	var pixels := root.get_texture().get_image()
	_expect(not pixels.is_empty(), "Native viewport contains pixels: " + id)
	_expect(pixels.get_size() == Vector2i(1600, 900), "Native viewport is 1600 x 900: " + id)
	var output := ProjectSettings.globalize_path("res://evidence/screens-" + id + ".png")
	DirAccess.make_dir_recursive_absolute(output.get_base_dir())
	if not pixels.is_empty():
		_expect(pixels.save_png(output) == OK, "Saved screenshot: " + id)
		captures += 1
		print("CAPTURE: %s | phase=%s time=%d gold=%d" % [output, app.model.data.phase, app.model.data.time, app.model.data.gold])


func _run() -> void:
	root.size = Vector2i(1600, 900)
	app = MainScene.instantiate()
	root.add_child(app)
	await _frame()
	for method in ["stage_anchor", "can_interact_with", "interaction_at", "map_snapshot", "_physics_process"]:
		_expect(app.world.has_method(method), "Current world exposes required capture API: " + method)
	if not failures.is_empty(): return
	await _press("开始新的一天")
	await _press("开始探索")
	_expect(app.model.data.time == 420 and app.model.data.gold == 40, "Actual new-game initial state")
	await _capture("explore")
	await _key(KEY_M)
	await _capture("map")
	await _key(KEY_ESCAPE)

	await _interact("xu")
	await _confirm("办理报到")
	await _interact("props")
	await _interact("chen")
	await _capture("dialogue")
	await _press("协助场务 · 60 分钟 / +16 金币", app.modal)
	await _press("开始", app.modal)
	await _interact("crate", true)
	_expect(app.model.data.activity.get("carrying", false), "Actual stage pickup")
	await _interact("target", true)
	_expect(app.model.data.activity.get("placed", false), "Actual stage placement")
	await _interact("chen", true)
	await _press("核对箱号和位置，交接灯具配件", app.modal)
	_expect(app.model.data.flags.get("stage_done", false) and app.model.data.time == 510, "Actual stage handover")

	await _interact("exit")
	await _interact("cafe")
	await _interact("lin")
	await _press("看看小店", app.modal)
	await _capture("cafe")
	await _confirm("购买小礼物 · 12 金币")
	await _interact("lin")
	await _confirm("等到上午班开始 · 08:40")
	await _interact("lin")
	await _press("上午咖啡班 · 08:40—10:10", app.modal)
	await _press("开始", app.modal)
	await _capture("work")
	await _press("黑咖啡", app.modal)
	await _press("拿铁", app.modal)
	await _press("热茶", app.modal)
	_expect(app.model.data.time == 610 and app.model.data.gold == 74 and app.model.data.inventory.gift == 1, "Actual work and purchase produce inventory")
	await _key(KEY_I)
	await _capture("inventory")
	await _key(KEY_ESCAPE)

	await _interact("exit")
	await _interact("bell")
	await _press("记下这段声音 · 不耗时", app.modal)
	await _key(KEY_T)
	await _confirm("等到午间 · 12:00")
	await _key(KEY_T)
	await _confirm("午间用餐与休息 · 到 14:00")
	await _key(KEY_T)
	await _confirm("等到傍晚 · 18:00")
	await _key(KEY_T)
	await _confirm("晚间用餐与休息 · 30 分钟")
	await _key(KEY_T)
	await _confirm("等到 Z 开播 · 20:00")
	await _key(KEY_T)
	await _confirm("打开手机 · 进入 Z 的直播间")
	_expect(app.model.data.phase == "live", "Actual schedule opens phone stream")
	await _press("聊聊今天听到的码头铃声", app.night)
	app.night._send.pressed.emit()
	await _frame()
	app.night._gift.pressed.emit()
	await _frame()
	await _press("赠送给 Z", app.night)
	app.night._close_modal()
	await _frame()
	_expect(app.model.data.recognition == 3 and app.model.data.inventory.gift == 0 and app.model.data.gold == 74, "Fact-grounded response and inventory-only gift")
	await _capture("night")

	for segment in range(3):
		app.night._advance.pressed.emit()
		await _frame()
		app.night._confirm_button.pressed.emit()
		await _frame()
		_expect(app.model.data.segment == segment + 1, "Actual program confirmation advances once")
	app.night._advance.pressed.emit()
	await _frame()
	await _press("结束这一天", app.night)
	_expect(app.model.data.phase == "done" and app.model.data.time == 1380 and app.modal_kind == "summary", "Actual sleep opens day summary")
	await _capture("summary")
	_expect(captures == CAPTURE_NAMES.size(), "All eight requested screenshots captured")
	_finish()


func _finish() -> void:
	if finished: return
	finished = true
	if is_instance_valid(app): app.queue_free()
	for filename in ["day1-auto.json", "day1-manual.json"]:
		for suffix in ["", ".bak", ".tmp", ".bak.tmp"]:
			var path := isolated_dir.path_join(filename + suffix)
			if FileAccess.file_exists(path): DirAccess.remove_absolute(path)
	DirAccess.remove_absolute(isolated_dir)
	if failures.is_empty():
		print("PASS: %d real main.tscn native screenshots, %d flow checks; isolated saves cleaned. Not manual keyboard play testing." % [captures, checks])
		quit(0)
	else:
		printerr("FAIL: %d failures / %d flow checks; %d screenshots." % [failures.size(), checks, captures])
		quit(1)
