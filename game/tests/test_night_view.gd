extends SceneTree
## Run: godot --headless --path game --script res://tests/test_night_view.gd
## Optional native render: omit --headless and add -- --capture=/absolute/output
## This checks UI requests and real-core integration, not native IME composition.

const Night = preload("res://scripts/night_view.gd")
const State = preload("res://core/day_state.gd")

class StubState extends RefCounted:
	var data: Dictionary = {
		"day": 1, "time": 1200, "gold": 74,
		"inventory": {"gift": 1, "material": 0, "work": 1},
		"flags": {"bell_observed": true, "craft_done": true},
		"events": [], "messages": [{"id": "z0", "speaker": "z", "text": "今晚，听见小岛。", "kind": "reply", "segment": 0, "state": "info", "expression": "neutral"}],
		"recognition": 0, "segment": 0, "phase": "live", "call_state": "idle"
	}
	func preview(action: String, payload: Dictionary = {}) -> Dictionary:
		if action == "advance_segment" and payload.get("expected_segment", -1) != data.segment:
			return {"ok": false, "reason": "节目已经切换。"}
		if action == "gift" and data.inventory.gift < 1:
			return {"ok": false, "reason": "没有小礼物。"}
		return {"ok": true, "reason": "", "minutes": 40 if action == "advance_segment" else 0, "end_time": data.time + 40, "gold_delta": 0}

var requests: Array[Dictionary] = []
var failures: Array[String] = []
var capture_directory := ""
var checks := 0


func _initialize() -> void:
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--capture="):
			capture_directory = argument.trim_prefix("--capture=")
	_run.call_deferred()


func _run() -> void:
	root.size = Vector2i(1600, 900)
	await _stub_checks()
	await _web_callback_checks()
	await _chat_scroll_checks()
	await _real_core_checks()
	if not capture_directory.is_empty():
		await _visual_capture_checks()
	if failures.is_empty():
		print("NIGHT_VIEW_OK: %d checks; exact text/Web callback payload, composition gating, overlay visibility, gifts, calls, real-core live/review/done. Browser DOM/IME requires Web verification." % checks)
		quit(0)
	else:
		for failure in failures:
			push_error(failure)
		quit(1)


func _stub_checks() -> void:
	requests.clear()
	var state := StubState.new()
	var screen = Night.new()
	screen.setup(state)
	screen.action_requested.connect(func(action: String, payload: Dictionary): requests.append({"action": action, "payload": payload.duplicate(true)}))
	root.add_child(screen)
	await process_frame
	await process_frame
	_check(screen._input.editable, "Actual live phase permits input")
	_check(screen._topic.visible and screen._work.visible, "Prepared flags expose real daytime topics")
	var snapshot := var_to_str(state.data)
	var original := "  码头铃声与海浪，英文 bell；[不解析标记]  "
	screen._input.text = original
	screen._send_chat()
	_check(requests.size() == 1 and requests[0].action == "chat", "Chat emits one request")
	_check(requests[0].payload.text == original, "Text payload preserves exact typed content")
	_check(var_to_str(state.data) == snapshot, "Typing and sending do not write model")
	_check(screen._input.text == original, "Unacknowledged input is not discarded")
	state.data.messages.append({"id": "p1", "speaker": "player", "text": original.strip_edges(), "kind": "chat", "segment": 0, "state": "sent"})
	screen.refresh()
	_check(screen._input.text.is_empty(), "Committed message acknowledges and clears input")
	screen._fill_bell_topic()
	screen._send_chat()
	_check(requests[-1].payload.get("topic", "") == "bell", "Known bell shortcut supplies bounded topic hint")
	screen._input.clear()
	screen._draft_topic = ""
	screen._open_drawer("gift")
	await process_frame
	await process_frame
	var count := requests.size()
	_check(screen._drawer.visible, "Gift drawer opens")
	_check(screen._drawer.get_rect().end.y <= screen.size.y - 20.0, "Gift drawer fits the 1600x900 viewport after text wrapping")
	_check(requests.size() == count, "Opening a drawer does not spend")
	screen._request("gift")
	_check(requests[-1].action == "gift" and requests[-1].payload.is_empty(), "Gift confirmation sends gift action only")
	_check(state.data.gold == 74 and state.data.inventory.gift == 1, "UI does not deduct currency or inventory")
	await _capture("night-gift", screen)
	screen._close_modal()
	state.data.inventory.gift = 0
	count = requests.size()
	_check(not screen._request("gift"), "Invalid gift is blocked by preview")
	_check(requests.size() == count, "Blocked gift emits no request")
	state.data.inventory.gift = 1
	for call_state in ["idle", "joined", "selected", "connected", "not_selected", "cancelled", "declined", "ended"]:
		state.data.call_state = call_state
		count = requests.size()
		screen._open_drawer("call")
		await process_frame
		_check(requests.size() == count, "Displaying call state %s never draws or accepts automatically" % call_state)
		screen._close_modal()
	state.data.call_state = "idle"
	screen._request("call_join")
	_check(requests[-1].action == "call_join", "Voluntary call entry is an explicit request")
	_check(state.data.call_state == "idle", "Call UI does not change authoritative call state")
	count = requests.size()
	screen._open_advance()
	_check(requests.size() == count, "Opening advance confirmation does not advance")
	_check(screen._confirm_payload.expected_segment == 0, "Advance confirmation captures the current segment")
	state.data.segment = 1
	screen.refresh()
	_check(screen._confirm_button.disabled, "Refresh disables stale segment confirmation")
	screen._commit_confirm()
	_check(requests.size() == count, "Stale confirmation cannot emit an advance")
	state.data.segment = 0
	screen.refresh()
	screen._open_advance()
	screen._commit_confirm()
	_check(requests[-1].action == "advance_segment" and requests[-1].payload.expected_segment == 0, "Confirmed advance includes expected segment")
	var clock_before := int(state.data.time)
	screen._process(9.0)
	_check(state.data.time == clock_before, "Visual breathing never changes world clock")
	state.data.flags = {}
	state.data.inventory.work = 0
	screen.refresh()
	_check(not screen._topic.visible and not screen._work.visible, "Missing preparation does not invent shortcut experiences")
	await _capture("night-watching", screen)
	screen.queue_free()
	await process_frame


func _web_callback_checks() -> void:
	requests.clear()
	var state := StubState.new()
	var host := Control.new()
	root.add_child(host)
	host.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	var screen = Night.new()
	screen.setup(state)
	screen.action_requested.connect(func(action: String, payload: Dictionary): requests.append({"action": action, "payload": payload.duplicate(true)}))
	host.add_child(screen)
	await process_frame
	await process_frame
	var snapshot := var_to_str(state.data)
	var text := "  我今天听见完整的中文铃声，海浪也在回应。English 123 🌙  "
	screen._on_web_input_event(["input", text, true])
	_check(screen._input.text == text, "Web input callback preserves a complete multilingual draft")
	_check(screen._send.disabled, "Composition disables the send button")
	screen._on_web_input_event(["submit", text, true])
	_check(requests.is_empty(), "IME candidate confirmation cannot send a message")
	screen._on_web_input_event(["input", text, false])
	_check(not screen._send.disabled, "Committed composition restores sending")
	screen._on_web_input_event(["submit", text, false])
	_check(requests.size() == 1 and requests[0].payload.text == text, "Noncomposing Enter emits the complete original payload exactly once")
	_check(var_to_str(state.data) == snapshot, "Web callbacks leave all authoritative state unchanged")
	_check(screen._input.text == text, "Unacknowledged Web input stays available")
	state.data.messages.append({"id": "web-p1", "speaker": "player", "text": text.strip_edges(), "kind": "chat", "segment": 0, "state": "sent"})
	screen.refresh()
	_check(screen._input.text.is_empty(), "Acknowledged Web draft clears through existing message reconciliation")
	screen._on_web_input_event(["submit", "  ", false])
	_check(requests.size() == 1, "Blank native-input submission creates no request")
	_check(screen._web_input_visible(), "Unobstructed night composer is eligible for the Web overlay")
	screen._open_drawer("gift")
	_check(not screen._web_input_visible(), "Gift drawer hides the Web overlay")
	screen._close_modal()
	screen._open_advance()
	_check(not screen._web_input_visible(), "Program confirmation hides the Web overlay")
	_check(screen._confirm_button.get_theme_color("font_focus_color") == Color("101c2b"), "Focused primary confirmation keeps dark text on pale background")
	screen._close_modal()
	var pause := Control.new()
	pause.z_index = 5
	host.add_child(pause)
	pause.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_check(not screen._web_input_visible(), "Host pause overlay hides the native input")
	pause.hide()
	_check(screen._web_input_visible(), "Closing the host pause overlay restores input eligibility")
	screen.hide()
	_check(not screen._web_input_visible(), "Hidden night scene cannot leave a DOM input visible")
	screen.show()
	_check(screen._web_input == null, "Headless/native runtime does not mount a browser bridge")
	host.queue_free()
	await process_frame


func _chat_scroll_checks() -> void:
	var state := StubState.new()
	state.data.messages.clear()
	for index in range(12):
		state.data.messages.append({"id": "history-%d" % index, "speaker": "z", "text": "第 %d 段岛上见闻。" % index + "今天码头的铃声和海浪接在一起，有人停下来听了一会儿。".repeat(3), "kind": "reply", "segment": 0, "state": "info"})
	var screen = Night.new()
	screen.setup(state)
	root.add_child(screen)
	for frame in range(10): await process_frame
	var bar: VScrollBar = screen._chat_scroll.get_v_scroll_bar()
	_check(bar.max_value > bar.page * 2, "Real wrapped message scene exceeds two scroll pages")
	_check(_last_message_visible(screen), "Initial history finishes layout with its latest row visible")
	bar.value = 0
	await process_frame
	state.data.messages.append({"id": "ordinary-update", "speaker": "z", "text": "普通新消息不会把正在阅读旧记录的人拉回底部。".repeat(3), "kind": "reply", "segment": 0, "state": "info"})
	screen.refresh()
	for frame in range(10): await process_frame
	_check(bar.value < 2, "Ordinary update preserves the actual scroll position while reading history")
	_check(not _last_message_visible(screen), "History reading remains separated from the newest row")
	screen.action_requested.connect(func(action: String, payload: Dictionary):
		if action == "chat":
			state.data.messages.append({"id": "own-sent", "speaker": "player", "text": payload.text.strip_edges(), "kind": "chat", "segment": 0, "state": "sent"})
			screen.refresh())
	screen._on_web_input_event(["input", "这条自己发送的完整中文消息必须立即出现在可视区。", false])
	screen._on_web_input_event(["submit", "这条自己发送的完整中文消息必须立即出现在可视区。", false])
	for frame in range(10): await process_frame
	_check(_last_message_visible(screen), "Submitting from history brings the actual new player row into view")
	var last_row: VBoxContainer = screen._chat_rows.get_child(screen._chat_rows.get_child_count() - 1)
	_check(last_row.get_child(1).text == "这条自己发送的完整中文消息必须立即出现在可视区。", "The visible newest body is the original submitted Chinese message")
	for index in range(3):
		state.data.messages.append({"id": "burst-%d" % index, "speaker": "z", "text": "接连到达的消息会在自动换行完成后留在底部。".repeat(4), "kind": "reply", "segment": 0, "state": "info"})
		screen.refresh()
	for frame in range(10): await process_frame
	_check(_last_message_visible(screen), "Consecutive refreshes follow growing wrapped content when already at bottom")
	_check(absf(bar.value - (bar.max_value - bar.page)) < 2, "Actual scrollbar reaches the new content bottom")
	screen.queue_free()
	await process_frame


func _last_message_visible(screen: Control) -> bool:
	var viewport_rect: Rect2 = screen._chat_scroll.get_global_rect()
	var row: Control = screen._chat_rows.get_child(screen._chat_rows.get_child_count() - 1)
	var row_rect := row.get_global_rect()
	return row_rect.end.y <= viewport_rect.end.y + 2 and row_rect.position.y >= viewport_rect.position.y - 2


func _real_core_checks() -> void:
	var state = State.new()
	# Fix the fixture to the selected branch; production randomness remains in core.
	for candidate in range(64):
		var rng := RandomNumberGenerator.new()
		rng.seed = candidate
		if rng.randi_range(0, 1) == 0:
			state.data.seed = candidate
			break
	for item in [
		["check_in", {}], ["stage_work", {"correct": true}], ["buy_gift", {}],
		["observe_bell", {}], ["wait", {"target": 720}], ["lunch", {}],
		["wait", {"target": 1200}], ["enter_stream", {}]
	]:
		var preparation: Dictionary = state.apply(item[0], item[1])
		_check(preparation.get("ok", false), "Real preparation: " + str(item[0]) + " " + str(preparation.get("reason", "")))
	var screen = Night.new()
	screen.setup(state)
	screen.action_requested.connect(func(action: String, payload: Dictionary):
		var receipt: Dictionary = state.apply(action, payload)
		_check(receipt.get("ok", false), "Core applies UI request: " + action)
		screen.refresh())
	root.add_child(screen)
	await process_frame
	_check(screen._input.editable, "Real core live phase is recognised")
	screen._fill_bell_topic()
	screen._send_chat()
	_check(state.data.flags.get("bell_replied", false), "Real bell shortcut receives a qualified response")
	_check(state.data.time == 1200 and screen._input.text.is_empty(), "Real chat keeps time and acknowledges input")
	var gold_before := int(state.data.gold)
	screen._open_drawer("gift")
	screen._request("gift")
	_check(state.data.inventory.gift == 0 and state.data.gold == gold_before, "Real gift costs inventory only")
	_check(state.data.time == 1200, "Real gift does not advance program")
	screen._close_modal()
	screen._request("call_join")
	_check(state.data.call_state == "joined", "Real call requires explicit registration")
	screen._open_advance()
	screen._commit_confirm()
	_check(state.data.segment == 1 and state.data.time == 1240, "Real advance commits exactly 40 minutes")
	screen._open_drawer("call")
	screen._request("call_draw")
	_check(state.data.call_state in ["selected", "not_selected"], "Real draw outcome is stored")
	var draw_state := str(state.data.call_state)
	screen._close_modal()
	screen._open_drawer("call")
	_check(state.data.call_state == draw_state, "Reopening call drawer does not reroll")
	if draw_state == "selected":
		screen._request("call_accept")
		_check(state.data.call_state == "connected", "Selected call still needs acceptance")
		_check(not screen._request("advance_segment", {"expected_segment": 1}), "Connected call blocks advance until ended")
		screen._request("call_end")
	else:
		_check(not screen._request("call_accept"), "Unselected call cannot be accepted")
	screen._close_modal()
	for expected in [1, 2]:
		screen._open_advance()
		_check(screen._confirm_payload.expected_segment == expected, "UI carries current real segment")
		screen._commit_confirm()
	_check(state.data.phase == "review" and state.data.time == 1320, "Three confirmed segments lead to review at 22:00")
	_check(not screen._input.editable and screen._gift.disabled, "Review does not permit more live interactions")
	screen._open_advance()
	_check(screen._confirm_action == "sleep", "Review opens explicit end-of-day confirmation")
	screen._commit_confirm()
	_check(state.data.phase == "done" and state.data.time == 1380, "Confirmed sleep ends day at 23:00")
	screen.queue_free()
	await process_frame


func _visual_capture_checks() -> void:
	# Authored visual fixtures render the real Night control. No online audience or
	# model is involved, and these captures are not manual-play acceptance.
	for dimensions in [Vector2i(1600, 900), Vector2i(1280, 720)]:
		root.size = dimensions
		var state := StubState.new()
		state.data.flags = {"bell_observed": true, "bell_replied": true}
		state.data.inventory.work = 0
		state.data.messages = [
			{"id": "visual-z0", "speaker": "z", "text": "晚上好。今晚，听见小岛。", "kind": "reply", "segment": 0, "state": "info"},
			{"id": "visual-p0", "speaker": "player", "text": "能把码头的铃声做成今晚的开场节拍吗？", "kind": "chat", "segment": 0, "state": "responded"},
			{"id": "visual-z1", "speaker": "z", "text": "原来你今天听过码头的铜铃。风大的时候，它会抢在海浪前面响一下。", "kind": "reply", "segment": 0, "state": "info"},
			{"id": "visual-gift", "speaker": "player", "text": "送出小礼物 ×1", "kind": "gift", "segment": 0, "state": "delivered"},
			{"id": "visual-z2", "speaker": "z", "text": "礼物收到了，谢谢你。比起礼物，我还想听听你今天在岛上发现了什么。", "kind": "reply", "segment": 0, "state": "info", "expression": "warm"}
		]
		var screen = Night.new()
		screen.reduced_motion = true
		screen.setup(state)
		root.add_child(screen)
		for frame in range(10): await process_frame
		await _capture("c02-%d-watching" % dimensions.x, screen)
		screen._open_drawer("gift")
		for frame in range(6): await process_frame
		# Controls use the stretch canvas's logical coordinates, not window pixels.
		_check(screen._drawer.get_global_rect().end.y <= screen.get_global_rect().end.y - 30, "C02 gift drawer fits %dpx viewport" % dimensions.x)
		await _capture("c02-%d-gift" % dimensions.x, screen)
		screen._close_modal()
		screen._open_drawer("call")
		for frame in range(6): await process_frame
		_check(screen._drawer.get_global_rect().end.y <= screen.get_global_rect().end.y - 30, "C02 call drawer fits %dpx viewport" % dimensions.x)
		await _capture("c02-%d-call" % dimensions.x, screen)
		screen._close_modal()
		screen._open_advance()
		for frame in range(6): await process_frame
		_check(screen._confirm.get_global_rect().end.y <= screen.get_global_rect().end.y - 30, "C02 confirmation fits %dpx viewport" % dimensions.x)
		await _capture("c02-%d-advance" % dimensions.x, screen)
		screen.queue_free()
		await process_frame
	root.size = Vector2i(1600, 900)


func _capture(name: String, screen: Control) -> void:
	if capture_directory.is_empty():
		return
	if DisplayServer.get_name() == "headless":
		_check(false, "Capture requires a real rendering driver; headless checks cannot prove pixels")
		return
	await process_frame
	await RenderingServer.frame_post_draw
	DirAccess.make_dir_recursive_absolute(capture_directory)
	var image := root.get_texture().get_image()
	_check(not image.is_empty(), "Native rendering returns pixels")
	if not image.is_empty():
		_check(image.get_size() == root.size, "Native capture matches requested window resolution")
		_check(image.save_png(capture_directory.path_join(name + ".png")) == OK, "Save native screenshot: " + name)
	_check(screen.size.x > 0 and screen.size.y > 0, "Screen has renderable bounds")


func _check(condition: bool, description: String) -> void:
	checks += 1
	if not condition:
		failures.append(description)
