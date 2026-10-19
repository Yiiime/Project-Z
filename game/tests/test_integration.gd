extends SceneTree

## Real main.tscn, real UI handlers/signals, real save/load. No direct core apply.
## Position fixtures use the current world's collision/interaction contract;
## they do not claim continuous keyboard pathfinding QA.
const MainScene = preload("res://main.tscn")
var app
var checks := 0
var failures: Array[String] = []
var finished := false
var isolated_dir := ""


func _initialize() -> void:
	var original_dir := OS.get_user_data_dir()
	ProjectSettings.set_setting("application/config/use_custom_user_dir", true)
	ProjectSettings.set_setting("application/config/custom_user_dir_name", "MoonZ Integration Tests/%d" % OS.get_process_id())
	isolated_dir = OS.get_user_data_dir()
	if isolated_dir == original_dir or not isolated_dir.contains("MoonZ Integration Tests"):
		printerr("FAIL: integration save isolation unavailable; no game was started.")
		quit(1)
		return
	_run.call_deferred()
	_watchdog.call_deferred()


func _watchdog() -> void:
	await create_timer(45.0).timeout
	if not finished:
		failures.append("Integration run did not complete within 45 seconds.")
		_finish()


func _expect(condition: bool, detail: String) -> void:
	checks += 1
	if not condition:
		failures.append(detail)
		printerr("CHECK FAILED: " + detail)


func _frame() -> void:
	await physics_frame
	await process_frame
	await process_frame


func _button_in(node: Node, caption: String) -> Button:
	if node is Button and not node.is_queued_for_deletion() and node.is_visible_in_tree() and node.text == caption:
		return node
	for child in node.get_children():
		var found := _button_in(child, caption)
		if found != null: return found
	return null


func _press(caption: String, scope: Node = null) -> bool:
	var found := _button_in(scope if scope != null else app, caption)
	_expect(found != null, "Visible button exists: " + caption)
	if found == null: return false
	_expect(not found.disabled, "Button enabled: " + caption)
	if found.disabled: return false
	found.pressed.emit()
	await _frame()
	return true


func _key(code: Key) -> void:
	var key := InputEventKey.new()
	key.keycode = code
	key.physical_keycode = code
	key.pressed = true
	app._unhandled_key_input(key)
	await _frame()


func _position_at(target: Vector2) -> void:
	_expect(app.world.can_stand(target), "Position fixture is standable: " + str(target))
	app.world.hero.position = target
	app.world.hero.velocity = Vector2.ZERO
	app.world.moved.emit(target)
	# Let the runtime physics tick update collision, activity props and proximity.
	await physics_frame
	await _frame()


func _interaction_position(id: String) -> Vector2:
	var anchor := Vector2.ZERO
	var exists := false
	var radius: float = app.world.INTERACTION_RADIUS
	if id in ["crate", "target"]:
		anchor = app.world.stage_anchor(id)
		exists = app.world.place == "props"
	else:
		for spot in app.world.spots:
			if spot.id == id:
				anchor = spot.position
				radius = float(spot.get("radius", radius))
				exists = true
				break
	_expect(exists, "Current scene has interaction: " + id)
	if not exists: return Vector2.INF
	var candidate := anchor
	var found := false
	# The runtime itself must reject overlap with the box after placement. Do
	# not bias this fixture away from its centre and hide a placement defect.
	for distance in range(0, int(ceil(radius)) + 1, 2):
		for angle in range(0, 360, 30):
			candidate = anchor + Vector2.from_angle(deg_to_rad(angle)) * distance
			if app.world.can_interact_with(id, candidate):
				found = true
				break
		if found: break
	_expect(found, "Interaction has a point satisfying collision, range, side and sight rules: " + id)
	return candidate if found else Vector2.INF


func _interact(id: String) -> void:
	var candidate := _interaction_position(id)
	if not candidate.is_finite(): return
	await _position_at(candidate)
	_expect(app.world.nearby == id, "World proximity selects requested interaction: " + id)
	await _key(KEY_E)


func _confirm(caption: String) -> void:
	await _press(caption, app.modal)
	await _press("确认", app.modal)


func _run() -> void:
	root.size = Vector2i(1600, 900)
	app = MainScene.instantiate()
	root.add_child(app)
	await _frame()
	_expect(not app.started and app.modal_kind == "title", "Real main scene opens title")
	await _press("开始新的一天")
	await _press("开始探索")
	_expect(app.started and app.model.data.time == 420 and app.model.data.gold == 40, "New-game UI creates initial state")
	_expect(FileAccess.file_exists(app.AUTO), "New-game UI writes automatic save")
	_expect(app.world.hero is CharacterBody2D, "Actual world uses a physics character body")
	_expect(app.world.get_viewport() == app.world_viewport and app.world_viewport.size == Vector2i(640, 360), "World renders inside a fixed 640x360 viewport")
	_expect(app.world_display.texture_filter == CanvasItem.TEXTURE_FILTER_NEAREST and app.world_display.texture == app.world_viewport.get_texture(), "World display scales the actual viewport texture with nearest sampling")
	_expect(app.hud.get_viewport() == app.get_viewport() and app.hud.get_viewport() != app.world.get_viewport(), "HUD keeps root resolution outside the pixel world")
	_expect(app.world.camera.zoom.is_equal_approx(Vector2.ONE), "Pixel camera preserves a 640x360 world view at 1x internal zoom")
	_expect(app.get_viewport().get_camera_2d() == null, "World camera cannot transform the outer UI canvas")
	var before_walk: Vector2 = app.world.hero.position
	var before_walk_time: int = app.model.data.time
	var walk_key := InputEventKey.new()
	walk_key.physical_keycode = KEY_D
	walk_key.keycode = KEY_D
	walk_key.pressed = true
	Input.parse_input_event(walk_key)
	await physics_frame
	await _frame()
	walk_key.pressed = false
	Input.parse_input_event(walk_key)
	await physics_frame
	await _frame()
	_expect(app.world.hero.position.x > before_walk.x, "Physical key movement still reaches the isolated viewport world")
	_expect(app.model.data.time == before_walk_time, "Physical walking inside the pixel viewport remains free")
	var original_debug: bool = app.world.debug_visible
	await _key(KEY_F3)
	_expect(app.world.debug_visible != original_debug, "Root F3 forwards one toggle to the isolated world")
	await _key(KEY_F3)
	_expect(app.world.debug_visible == original_debug, "Second F3 restores the world debug overlay")

	await _interact("xu")
	await _confirm("办理报到")
	_expect(app.model.data.flags.get("checked_in", false) and app.model.data.time == 450, "Check-in button commits 30 minutes")
	await _interact("props")
	_expect(app.world.place == "props" and app.model.data.location == "props", "Door signal synchronizes scene and authority location")
	await _interact("chen")
	await _press("协助场务 · 60 分钟 / +16 金币", app.modal)
	await _press("开始", app.modal)
	_expect(app.model.data.pending.get("action", "") == "stage_work" and app.world.is_stage, "Stage confirmation enters actual world activity")
	_expect(app.model.data.time == 450 and app.model.data.gold == 40, "Beginning stage activity does not pay early")
	app.enter_place("island")
	_expect(app.world.place == "props", "Pending stage blocks scene exit")
	await _position_at(_interaction_position("crate"))
	await _key(KEY_E)
	_expect(app.model.data.activity.get("carrying", false) and app.world.carrying, "E picks up the real activity crate")
	var saved_crate_position: Vector2 = app.world.hero.position
	await _key(KEY_F5)
	_expect(app.model.last_error.is_empty(), "Manual save accepts carried crate")
	await _position_at(app.world.default_pos(app.world.place))
	await _key(KEY_F9)
	_expect(app.model.data.activity.get("carrying", false) and app.world.carrying and app.world.hero.position.is_equal_approx(saved_crate_position), "UI reload restores crate and player position")
	await _position_at(_interaction_position("target"))
	await _key(KEY_E)
	_expect(app.model.data.activity.get("placed", false) and app.world.crate_placed, "E places crate in target")
	_expect(app.world.can_stand(app.world.hero.position), "Placing crate leaves player's feet clear of solid box")
	await _position_at(_interaction_position("chen"))
	await _key(KEY_E)
	await _press("核对箱号和位置，交接灯具配件", app.modal)
	_expect(app.model.data.pending.is_empty() and app.model.data.flags.get("stage_done", false) and app.model.data.time == 510 and app.model.data.gold == 56, "Actual crate handover settles one wage")

	await _interact("exit")
	await _interact("cafe")
	await _interact("lin")
	await _press("看看小店", app.modal)
	await _confirm("购买小礼物 · 12 金币")
	_expect(app.model.data.time == 515 and app.model.data.gold == 44 and app.model.data.inventory.gift == 1, "Shop UI buys real inventory with five-minute cost")
	await _interact("lin")
	await _confirm("等到上午班开始 · 08:40")
	await _interact("lin")
	await _press("上午咖啡班 · 08:40—10:10", app.modal)
	await _press("开始", app.modal)
	_expect(app.model.data.pending.get("action", "") == "coffee_am", "Coffee UI enters pending shift")
	await _press("热茶", app.modal)
	_expect(app.model.data.activity.get("step", -1) == 0 and app.model.data.time == 520, "Wrong first order cannot complete a coffee step")
	await _press("黑咖啡", app.modal)
	_expect(app.model.data.activity.get("step", -1) == 1, "Correct first order advances actual UI step")
	await _key(KEY_F5)
	await _press("拿铁", app.modal)
	_expect(app.model.data.activity.get("step", -1) == 2, "Second correct order reaches step two")
	await _key(KEY_F9)
	_expect(app.model.data.activity.get("step", -1) == 1 and app.modal.visible and app.modal_kind == "activity", "Reload opens coffee UI at saved step one")
	await _press("拿铁", app.modal)
	await _press("热茶", app.modal)
	_expect(app.model.data.pending.is_empty() and app.model.data.time == 610 and app.model.data.gold == 74 and app.model.data.flags.get("coffee_am_done", false), "Three actual UI orders settle one 90-minute wage")
	var coffee_events := 0
	for event in app.model.data.events:
		if event.action == "coffee_am": coffee_events += 1
	_expect(coffee_events == 1, "Interrupted and restored coffee work has one receipt")

	await _interact("exit")
	await _interact("bell")
	await _press("记下这段声音 · 不耗时", app.modal)
	_expect(app.model.data.flags.get("bell_observed", false) and app.model.data.time == 610, "World bell observation supplies real free topic")
	await _key(KEY_M)
	_expect(app.modal_kind == "map" and app.model.data.time == 610, "M map is free and read-only")
	await _key(KEY_ESCAPE)
	await _key(KEY_I)
	_expect(app.model.data.inventory.gift == 1 and app.model.data.time == 610, "Inventory view preserves item and clock")
	await _key(KEY_ESCAPE)

	await _interact("dorm")
	await _confirm("等到午间 · 12:00")
	await _interact("dorm")
	await _confirm("午间用餐与休息 · 到 14:00")
	_expect(app.model.data.time == 840, "UI lunch respects mandatory 120-minute schedule")
	await _interact("dorm")
	await _confirm("等到傍晚 · 18:00")
	await _interact("dorm")
	await _confirm("晚间用餐与休息 · 30 分钟")
	await _interact("dorm")
	await _confirm("等到 Z 开播 · 20:00")
	await _interact("dorm")
	await _confirm("打开手机 · 进入 Z 的直播间")
	_expect(app.model.data.phase == "live" and is_instance_valid(app.night) and app.night.visible and not app.world.visible, "UI changes from island to phone stream")

	await _press("聊聊今天听到的码头铃声", app.night)
	_expect(app.night._input.text.contains("海浪") and not app.night._send.disabled, "Observed topic fills an editable enabled composer")
	app.night._send.pressed.emit()
	await _frame()
	_expect(app.model.data.flags.get("bell_replied", false) and app.model.data.recognition == 1 and app.model.data.time == 1200, "Night send signal reaches fact-grounded response without time cost")
	_expect(app.night._input.text.is_empty(), "Successful chat clears the composer")
	app.night._gift.pressed.emit()
	await _frame()
	await _press("赠送给 Z", app.night)
	_expect(app.model.data.inventory.gift == 0 and app.model.data.gold == 74 and app.model.data.recognition == 3, "Gift drawer sends preowned inventory without second purchase")
	var empty_gift := _button_in(app.night, "赠送给 Z")
	_expect(empty_gift != null and empty_gift.disabled, "Gift UI prevents sending empty stock")
	app.night._close_modal()
	await _key(KEY_F5)
	var night_messages: int = app.model.data.messages.size()
	await _key(KEY_F9)
	_expect(app.model.data.messages.size() == night_messages and app.night.visible and app.model.data.recognition == 3, "UI restores night transcript and contribution")
	app.night._input.text = "  晚上好，这条消息只是普通问候。  "
	app.night._input.text_changed.emit(app.night._input.text)
	app.night._send.pressed.emit()
	await _frame()
	_expect(app.night._input.text.is_empty() and app.model.data.time == 1200 and app.model.data.recognition == 3, "Whitespace chat clears only after successful send; no extra contribution")
	for segment in range(3):
		app.night._advance.pressed.emit()
		await _frame()
		_expect(app.night._confirm.visible, "Program advance presents actual confirmation")
		app.night._confirm_button.pressed.emit()
		await _frame()
		_expect(app.model.data.segment == segment + 1 and app.model.data.time == 1200 + (segment + 1) * 40, "One confirmed program transition consumes 40 minutes")
	_expect(app.model.data.phase == "review", "Last segment opens review")
	app.night._advance.pressed.emit()
	await _frame()
	await _press("结束这一天", app.night)
	_expect(app.model.data.phase == "done" and app.model.data.time == 1380 and app.model.data.gold == 74 and app.model.data.recognition == 3, "UI completes whole D1 A plus coffee route")
	_expect(app.modal_kind == "summary" and app.modal.visible and not app.night.visible, "Real main UI presents day summary")
	await _press("保存这一天", app.modal)
	_expect(app.model.last_error.is_empty(), "Day summary saves valid final state")
	await _key(KEY_F9)
	_expect(app.model.data.phase == "done" and app.modal_kind == "summary", "Reloading final save restores summary rather than opening D2")
	# Exercise the UI's recovery disclosure, beyond the core-only save tests.
	var recovery_path := "user://integration-recovery.json"
	app.save_game(recovery_path, false)
	app.save_game(recovery_path, false)
	_corrupt_file(recovery_path)
	app.load_game(recovery_path)
	await _frame()
	_expect(app.model.last_load_used_backup and app.model.data.phase == "done", "Main load handler recovers backup through the real save API")
	_expect(app.toast_label.text.contains("备份"), "UI explicitly discloses loading a backup rather than claiming ordinary success")
	var before_bad_load := JSON.stringify(app.model.data, "", true, true)
	_corrupt_file(recovery_path + ".bak")
	app.load_game(recovery_path)
	await _frame()
	_expect(JSON.stringify(app.model.data, "", true, true) == before_bad_load and app.modal_kind == "summary", "Both corrupt save files retain current UI and game state")
	_finish()


func _finish() -> void:
	if finished: return
	finished = true
	if is_instance_valid(app): app.queue_free()
	for filename in ["day1-auto.json", "day1-manual.json", "integration-recovery.json"]:
		for suffix in ["", ".bak", ".tmp", ".bak.tmp"]:
			var path := isolated_dir.path_join(filename + suffix)
			if FileAccess.file_exists(path): DirAccess.remove_absolute(path)
	if failures.is_empty():
		print("PASS: real main.tscn full-day UI integration, %d checks; stage, coffee, save/resume, inventory, night chat/gift, three segments and summary." % checks)
		quit(0)
	else:
		printerr("FAIL: %d failures / %d UI integration checks." % [failures.size(), checks])
		quit(1)


func _corrupt_file(path: String) -> void:
	var file := FileAccess.open(path, FileAccess.WRITE)
	file.store_string("{ deliberately incomplete integration fixture")
	file.close()
