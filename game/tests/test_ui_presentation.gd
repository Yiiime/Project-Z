extends SceneTree

## Real main.tscn presentation regression. All saves use an isolated directory.
## Headless: checks only. Native: the same checks plus 8 screenshots at each size.
## Inventory/history fixtures are explicit static states, not a claimed playthrough.
## Run: godot --path game --script res://tests/test_ui_presentation.gd
const MainScene = preload("res://main.tscn")
const OUTPUT := "res://evidence/ui-v2"
const SIZES := [Vector2i(1600, 900), Vector2i(1280, 720)]
var app
var isolated_dir := ""
var checks := 0
var failures: Array[String] = []
var captures: Array[Dictionary] = []
var finished := false
var native := false


func _initialize() -> void:
	native = DisplayServer.get_name() != "headless"
	var original_dir := OS.get_user_data_dir()
	ProjectSettings.set_setting("application/config/use_custom_user_dir", true)
	ProjectSettings.set_setting("application/config/custom_user_dir_name", "MoonZ Presentation Tests/%d" % OS.get_process_id())
	isolated_dir = OS.get_user_data_dir()
	if isolated_dir == original_dir or not isolated_dir.contains("MoonZ Presentation Tests"):
		printerr("FAIL: presentation save isolation unavailable; scene not started.")
		quit(1)
		return
	_run.call_deferred()
	_watchdog.call_deferred()


func _watchdog() -> void:
	await create_timer(90.0).timeout
	if not finished:
		failures.append("Presentation checks exceeded 90 seconds.")
		_finish()


func _expect(condition: bool, detail: String) -> void:
	checks += 1
	if not condition:
		failures.append(detail)
		printerr("CHECK FAILED: " + detail)


func _frame() -> void:
	await physics_frame
	for _index in range(4): await process_frame


func _state() -> String:
	return JSON.stringify(app.model.data, "", true, true)


func _nodes(node: Node) -> Array[Node]:
	var result: Array[Node] = [node]
	for child in node.get_children(): result.append_array(_nodes(child))
	return result


func _button(caption: String) -> Button:
	for node in _nodes(app.modal):
		if node is Button and node.is_visible_in_tree() and node.text == caption:
			return node
	return null


func _has_text(value: String) -> bool:
	for node in _nodes(app.modal):
		if node is Label and node.is_visible_in_tree() and node.text.contains(value): return true
	return false


func _press(caption: String) -> void:
	var button := _button(caption)
	_expect(button != null, "Existing public button: " + caption)
	if button == null: return
	_expect(not button.disabled, "Enabled public button: " + caption)
	if button.disabled: return
	button.pressed.emit()
	await _frame()


func _key(code: Key) -> void:
	var event := InputEventKey.new()
	event.keycode = code; event.physical_keycode = code; event.pressed = true
	app._unhandled_key_input(event)
	await _frame()


func _mouse_click(button: Button) -> void:
	if button == null: return
	# Viewport-local coordinates exercise GUI hit testing, including disabled state.
	var point := button.get_global_rect().get_center()
	for down in [true, false]:
		var event := InputEventMouseButton.new()
		event.button_index = MOUSE_BUTTON_LEFT
		event.pressed = down
		event.position = point; event.global_position = point
		event.button_mask = MOUSE_BUTTON_MASK_LEFT if down else 0
		root.push_input(event, true)
		await process_frame
	await _frame()


func _clean_fixture(place: String = "cafe") -> void:
	app.model.new_game()
	app.started = true
	app.world.change_place(place, app.world.default_pos(place))
	app.model.data.location = place
	app.model.data.position = [app.world.hero.position.x, app.world.hero.position.y]
	# The fixture begins a fresh visual state; do not carry an earlier test's toast.
	app.toast_timer = 0; app.toast_label.hide()
	app.modal.hide(); app.hud.show(); app.refresh()
	await _frame()


func _owned_fixture() -> void:
	await _clean_fixture()
	# Deliberate presentation fixture: three inventory kinds and their source receipts.
	# No core action is claimed to have been played to construct this screenshot.
	app.model.data.time = 610
	app.model.data.gold = 28
	app.model.data.inventory = {"gift": 1, "material": 2, "work": 1}
	app.model.data.flags = {"checked_in": true, "stage_done": true, "craft_done": true, "bell_observed": true}
	app.model.data.events = []
	app.model.data.receipts = {}
	for entry in [["buy_gift", 515, "在咖啡馆买下一份小礼物。"], ["buy_material", 520, "在咖啡馆准备材料。"], ["craft", 580, "亲手做好一份节目作品。"], ["buy_material", 585, "再次购买材料，留在背包。"]]:
		var receipt := {"request_id": "ui-fixture-" + str(entry[1]), "action": entry[0], "end_time": entry[1], "message": entry[2]}
		app.model.data.events.append(receipt.duplicate(true))
		app.model.data.receipts[receipt.request_id] = receipt.duplicate(true)
	app.refresh()
	await _frame()


func _read_only(open: Callable, context: String) -> void:
	var before := _state()
	open.call()
	await _frame()
	_expect(_state() == before, context + " preserves all authority state, not only time")
	await _layout_checks(context)


func _layout_checks(context: String) -> void:
	var viewport := root.get_visible_rect()
	_expect(viewport.size.is_equal_approx(Vector2(1600, 900)), context + " retains the 1600x900 logical viewport")
	var surface_count := 0
	for node in _nodes(app.modal):
		if not node is Control or not node.is_visible_in_tree(): continue
		if str(node.name) in ["UISurface", "DialogueSurface", "DialoguePortrait", "SpatialMap"]:
			surface_count += 1
			var rect: Rect2 = node.get_global_rect()
			_expect(rect.size.x > 0 and rect.size.y > 0 and viewport.grow(1).encloses(rect), context + " " + str(node.name) + " is inside logical viewport: " + str(rect))
		if node is ScrollContainer:
			var scroll := node as ScrollContainer
			_expect(scroll.size.x > 0 and scroll.size.y > 0 and viewport.grow(1).encloses(scroll.get_global_rect()), context + " scroll viewport is usable and onscreen")
			_expect(scroll.get_child_count() > 0, context + " scroll has content")
			var bar := scroll.get_v_scroll_bar()
			if bar.max_value > bar.page + 1:
				var previous := scroll.scroll_vertical
				scroll.scroll_vertical = int(ceil(bar.max_value))
				await _frame()
				_expect(scroll.scroll_vertical > 0 and is_equal_approx(bar.value, bar.max_value - bar.page), context + " overflowing content can scroll to its end")
				scroll.scroll_vertical = previous
				await _frame()
	_expect(surface_count > 0, context + " has a real visible presentation surface")
	# Container bounds alone miss narrow HBox labels wrapping one glyph per line.
	# Keep this targeted to names, compact prices/statuses and actual HUD key hints.
	var compact := ["许舟", "林乔", "陈叔", "陆岑", "12", "8", "5 分钟", "已完成", "可进行", "已错过", "待开始", "WASD", "移动", "E", "交互", "T", "日程", "今晚 20:00 · Z 的直播"]
	var label_nodes := _nodes(app.modal)
	if app.hud.visible: label_nodes.append_array(_nodes(app.hud))
	for node in label_nodes:
		if not node is Label or not node.is_visible_in_tree() or not node.text in compact: continue
		var measured: Vector2 = node.get_theme_font("font").get_string_size(node.text, HORIZONTAL_ALIGNMENT_LEFT, -1, node.get_theme_font_size("font_size"))
		_expect(node.get_line_count() == 1 and node.size.x + 2 >= measured.x, context + " compact label stays on one line: " + node.text + " width=" + str(node.size.x))
	if app.modal_kind == "shop":
		var purchase := _button("购买材料包 · 8 金币")
		if purchase == null: purchase = _button("购买小礼物 · 12 金币")
		var dock := app.modal.find_child("ShopPurchaseDock", true, false) as Control
		_expect(purchase != null and dock != null, context + " has a fixed shop purchase dock")
		if purchase != null and dock != null:
			var before: Rect2 = purchase.get_global_rect()
			_expect(viewport.encloses(before) and dock.get_global_rect().encloses(before), context + " main purchase CTA is fully visible in its dock")
			var ancestor := purchase.get_parent()
			var scroll_ancestor := false
			while ancestor != null:
				if ancestor is ScrollContainer: scroll_ancestor = true
				ancestor = ancestor.get_parent()
			_expect(not scroll_ancestor, context + " purchase CTA does not live inside scroll content")
			var shop_scroll := app.modal.find_child("SurfaceScroll", true, false) as ScrollContainer
			var previous := shop_scroll.scroll_vertical
			shop_scroll.scroll_vertical = int(ceil(shop_scroll.get_v_scroll_bar().max_value))
			await _frame()
			_expect(purchase.get_global_rect().is_equal_approx(before), context + " scrolling products never moves the purchase CTA")
			shop_scroll.scroll_vertical = previous
			await _frame()


func _read_only_checks() -> void:
	await _clean_fixture()
	await _read_only(app.show_map, "Map")
	await _read_only(app.show_inventory, "Empty inventory")
	_expect(_has_text("背包还是空的"), "Empty bag explicitly communicates emptiness")
	_expect(_button("小礼物   ×0") == null and not _has_text("已拥有"), "Empty bag does not advertise owned inventory")
	var gift_images := 0
	for node in _nodes(app.modal):
		if node is TextureRect and node.texture != null and node.texture.resource_path == "res://assets/gift.png": gift_images += 1
	_expect(gift_images == 0, "Empty bag does not render a large owned-gift image")
	for caption in ["记录", "许可", "物品"]:
		var before := _state()
		await _press(caption)
		_expect(_state() == before, "Inventory tab " + caption + " is read-only")
	await _read_only(app.show_schedule, "Schedule")
	await _read_only(app.show_journal, "Journal")
	await _read_only(app.talk_lin, "Lin dialogue")
	await _read_only(app.talk_chen, "Chen dialogue")
	await _read_only(app.show_shop, "Gift shop")
	var before := _state()
	await _press("材料包")
	_expect(_state() == before and _button("购买材料包 · 8 金币") != null, "Shop selection is read-only and changes purchase target")
	await _owned_fixture()
	await _read_only(app.show_inventory, "Owned inventory")
	for entry in [["gift", "小礼物   ×1", "咖啡馆购买 · 08:35"], ["material", "材料包   ×2", "咖啡馆购买 · 09:45"], ["work", "节目作品   ×1", "工作台制作 · 09:40"]]:
		before = _state()
		await _press(entry[1])
		_expect(_state() == before, "Selecting owned " + entry[0] + " does not mutate authority state")
		_expect(_has_text(entry[2]), "Selected " + entry[0] + " source comes from the matching latest receipt")
		_expect(_has_text("已拥有 · ×" + str(app.model.data.inventory[entry[0]])), "Selected " + entry[0] + " displays its own quantity")
		await _layout_checks("Inventory " + entry[0])
	await _press("记录")
	_expect(_has_text("已记下 · 海风、铜铃与船靠岸的水声"), "Records read the actual observed flag")
	await _press("许可")
	_expect(_has_text("未取得许可") and _has_text("暂不开放"), "Public work does not imply maintenance or private access")
	# Exercise a genuinely overflowing journal; do not demand that all long text fits.
	for index in range(16):
		app.model.data.events.append({"end_time": 590 + index, "message": "排版夹具记录 %d：" % index + "长内容应可滚动查看，浏览不能结算新的行动。".repeat(3)})
	await _read_only(app.show_journal, "Long journal")
	var scroll := app.modal.find_child("SurfaceScroll", true, false) as ScrollContainer
	_expect(scroll != null and scroll.get_v_scroll_bar().max_value > scroll.get_v_scroll_bar().page, "Long journal actually exercises overflow")


func _input_and_purchase_checks() -> void:
	await _clean_fixture()
	app.talk_lin(); await _frame()
	var before := _state()
	await _key(KEY_1)
	_expect(app.modal_kind == "shop" and _state() == before, "NPC number 1 follows the shop choice without changing state")
	app.talk_lin(); await _frame()
	await _mouse_click(_button("看看小店"))
	_expect(app.modal_kind == "shop" and _state() == before, "Actual mouse hit follows the same shop choice as number 1")
	app.talk_lin(); await _frame()
	var unavailable := _button("上午咖啡班 · 08:40—10:10")
	_expect(unavailable != null and unavailable.disabled, "Early coffee choice is visibly disabled")
	await _key(KEY_2)
	_expect(app.modal_kind == "dialogue" and _state() == before and app.model.data.pending.is_empty(), "Disabled numbered choice cannot start an activity")
	await _mouse_click(unavailable)
	_expect(app.modal_kind == "dialogue" and _state() == before, "Disabled mouse choice cannot execute either")
	app.show_shop(); await _frame()
	await _press("材料包")
	await _press("购买材料包 · 8 金币")
	_expect(app.modal_kind == "confirmation" and _state() == before, "Material purchase still requires a nonmutating preview")
	await _press("确认")
	_expect(app.model.data.inventory.material == 1 and app.model.data.inventory.gift == 0, "Material CTA purchases material, never a gift")
	_expect(app.model.data.gold == 32 and app.model.data.time == 425, "Actual material purchase commits exactly 8 gold and 5 minutes")
	var receipt: Dictionary = app.model.data.events.back()
	_expect(receipt.action == "buy_material" and receipt.inventory_delta.material == 1 and receipt.inventory_delta.gift == 0, "Authoritative receipt identifies the selected material")
	app.show_inventory(); await _frame()
	_expect(_has_text("咖啡馆购买 · 07:05") and _button("材料包   ×1") != null, "Inventory renders the actual purchase receipt, not a concept timestamp")
	_expect(_button("小礼物   ×1") == null, "Real material-only inventory does not invent a gift row")


func _capture(id: String, physical_size: Vector2i) -> void:
	await _frame()
	await _layout_checks(str(physical_size) + " " + id)
	if not native: return
	# Explicit redraw avoids awaiting a post-draw signal from an occluded window.
	# https://docs.godotengine.org/en/stable/classes/class_renderingserver.html#class-renderingserver-method-force-draw
	RenderingServer.force_draw()
	var pixels := root.get_texture().get_image()
	_expect(not pixels.is_empty() and pixels.get_size() == physical_size, "Native capture size is " + str(physical_size) + ": " + id)
	if pixels.is_empty(): return
	var name := "%dx%d-%s.png" % [physical_size.x, physical_size.y, id]
	var path := ProjectSettings.globalize_path(OUTPUT.path_join(name))
	DirAccess.make_dir_recursive_absolute(path.get_base_dir())
	_expect(pixels.save_png(path) == OK, "Saved native screenshot: " + name)
	captures.append({"file": name, "size": [pixels.get_width(), pixels.get_height()], "state": id, "fixture": true})


func _presentation_at_size(physical_size: Vector2i) -> void:
	root.size = physical_size
	await _frame()
	await _clean_fixture("island")
	app.start_menu(); await _capture("01-title", physical_size)
	await _clean_fixture()
	app.show_inventory(); await _capture("02-empty-bag", physical_size)
	await _owned_fixture()
	app.show_inventory(); await _capture("03-inventory-gift", physical_size)
	app.show_shop(); await _frame(); await _press("材料包")
	await _capture("04-material-shop", physical_size)
	app.talk_lin(); await _capture("05-lin-dialogue", physical_size)
	app.show_schedule(); await _capture("06-schedule", physical_size)
	app.show_journal(); await _capture("07-journal", physical_size)
	app.confirm_action("buy_material"); await _capture("08-action-preview", physical_size)


func _run() -> void:
	root.size = Vector2i(1600, 900)
	app = MainScene.instantiate(); root.add_child(app)
	await _frame()
	_expect(app.modal_kind == "title", "Real main scene starts at title")
	await _press("开始新的一天")
	await _press("开始探索")
	_expect(FileAccess.file_exists(app.AUTO), "Initial UI save is written inside isolated user storage")
	# Focused visual confirmation after a presentation-only fix; default remains full.
	if not "--captures-only" in OS.get_cmdline_user_args():
		await _read_only_checks()
		await _input_and_purchase_checks()
	for physical_size in SIZES: await _presentation_at_size(physical_size)
	if native: _expect(captures.size() == 16, "All eight representative states captured at both native sizes")
	_finish()


func _finish() -> void:
	if finished: return
	finished = true
	var capture_only := "--captures-only" in OS.get_cmdline_user_args()
	var run_scope := "Native layout/capture confirmation only; explicit state fixtures. No purchase or input regression rerun." if capture_only else "Real main.tscn and UI events; explicit inventory/history/position fixtures; not manual keyboard play or full-day completion."
	if native:
		var output := ProjectSettings.globalize_path(OUTPUT.path_join("presentation-report.json"))
		DirAccess.make_dir_recursive_absolute(output.get_base_dir())
		var file := FileAccess.open(output, FileAccess.WRITE)
		if file != null:
			file.store_string(JSON.stringify({"checks": checks, "failures": failures, "captures": captures, "mode": "captures_only" if capture_only else "full", "scope": run_scope}, "\t"))
	if is_instance_valid(app): app.queue_free()
	for filename in ["day1-auto.json", "day1-manual.json"]:
		for suffix in ["", ".bak", ".tmp", ".bak.tmp"]:
			var path := isolated_dir.path_join(filename + suffix)
			if FileAccess.file_exists(path): DirAccess.remove_absolute(path)
	DirAccess.remove_absolute(isolated_dir)
	if failures.is_empty():
		print("UI_PRESENTATION_OK: %d checks; %d native screenshots; isolated saves; mode=%s. %s" % [checks, captures.size(), "captures_only" if capture_only else "full", run_scope])
		quit(0)
	else:
		printerr("UI_PRESENTATION_FAIL: %d failures / %d checks; %d screenshots." % [failures.size(), checks, captures.size()])
		quit(1)
