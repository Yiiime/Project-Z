extends Control
## C.02 night view. The caller owns rules, transactions and persistence.
## No UI animation, typing event or elapsed real time mutates the world clock.

signal action_requested(action: String, payload: Dictionary)
signal exit_requested

const INK := Color("101c2b")
const WHITE := Color("f2f3f5")
const MUTED := Color("b5c1d0")
const PALE := Color("c5d9ec")
const LINE := Color(0.847, 0.898, 0.965, 0.16)
const SEGMENTS := ["开场 · 今晚，听见小岛", "主节目 · 把生活带上舞台", "收尾 · 把话留给明天", "今晚的直播已结束"]
const WebInputBridge = preload("res://scripts/web_input_bridge.gd")
const Kit = preload("res://scripts/ui_kit.gd")

var model: Object
var reduced_motion := false
var _built := false
var _background: TextureRect
var _portrait: TextureRect
var _expression_atlas: AtlasTexture
var _expression_sheet: Texture2D
var _clock: Label
var _gold: Label
var _live: Label
var _chat_layer: Control
var _segment_label: Label
var _subtitle: Label
var _subtitle_hint: Label
var _chat_scroll: ScrollContainer
var _chat_rows: VBoxContainer
var _input: LineEdit
var _send: Button
var _topic: Button
var _work: Button
var _gift: Button
var _call: Button
var _advance: Button
var _notice: Label
var _drawer: PanelContainer
var _drawer_content: VBoxContainer
var _drawer_kind := ""
var _scrim: ColorRect
var _confirm: PanelContainer
var _confirm_text: Label
var _confirm_times: HBoxContainer
var _time_from: Label
var _time_to: Label
var _confirm_button: Button
var _confirm_action := ""
var _confirm_payload: Dictionary = {}
var _messages_fingerprint := ""
var _message_ids: Dictionary = {}
var _chat_scroll_serial := 0
var _chat_scroll_pending := false
var _chat_follow_bottom := false
var _chat_restore_position := 0
var _pending_text := ""
var _draft_topic := ""
var _last_reply_id := ""
var _breath_time := 0.0
var _expression_name := "neutral"
var _expression_hold := 0.0
var _blink_clock := 0.0
var _portrait_base_position := Vector2.ZERO
var _portrait_base_size := Vector2.ZERO
var _focus_before_modal: Control
var _web_input: RefCounted
var _web_composing := false
var _web_synced_text := ""


func setup(state_model: Object) -> void:
	model = state_model
	if is_node_ready():
		refresh()


func _ready() -> void:
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	mouse_filter = Control.MOUSE_FILTER_STOP
	_build()
	resized.connect(_layout)
	_layout()
	refresh()
	_mount_web_input()
	visibility_changed.connect(_sync_web_input)


func _build() -> void:
	if _built:
		return
	_built = true
	theme = Kit.make_theme()
	var input_style := Kit.style(Color.TRANSPARENT, Color.TRANSPARENT, 4)
	input_style.content_margin_left = 16
	input_style.content_margin_right = 12
	input_style.content_margin_top = 0
	input_style.content_margin_bottom = 0
	theme.set_stylebox("normal", "LineEdit", input_style)
	var input_focus := input_style.duplicate() as StyleBoxFlat
	input_focus.border_color = Color(PALE, 0.45)
	input_focus.set_border_width_all(1)
	theme.set_stylebox("focus", "LineEdit", input_focus)
	theme.set_color("font_color", "LineEdit", WHITE)
	theme.set_color("font_placeholder_color", "LineEdit", MUTED)
	theme.set_color("caret_color", "LineEdit", PALE)

	var base := ColorRect.new()
	base.color = Color("08131e")
	base.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(base)
	base.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_background = TextureRect.new()
	_background.texture = _texture("res://assets/z-moonlight-clean.png")
	_background.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_background.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED
	_background.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_background)
	_background.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)

	# A transparent portrait sheet requires a separately supplied empty backdrop.
	# Until that exists the complete C.02 still remains static, including the room.
	_portrait = TextureRect.new()
	_portrait.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_portrait.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	_portrait.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_portrait.visible = false
	add_child(_portrait)
	_load_expression_assets()
	add_child(_gradient_overlay(true))
	add_child(_gradient_overlay(false))

	var moon := Kit.picture(Kit.icon("moon"), Vector2.ZERO)
	add_child(moon)
	_place(moon, 0.034, 0.064, 0.057, 0.111)
	var title := _label("Z", 25, WHITE)
	title.autowrap_mode = TextServer.AUTOWRAP_OFF
	title.add_theme_font_override("font", Kit.font(550, true))
	title.name = "NightTitle"
	add_child(title)
	_place(title, 0.071, 0.053, 0.091, 0.092)
	_live = _label("LIVE", 11, Color("c8d2df"))
	_live.autowrap_mode = TextServer.AUTOWRAP_OFF
	_live.add_theme_font_override("font", Kit.font(650, true))
	add_child(_live)
	_place(_live, 0.098, 0.067, 0.15, 0.092)
	var live_dot := Control.new()
	live_dot.mouse_filter = Control.MOUSE_FILTER_IGNORE
	live_dot.draw.connect(func(): live_dot.draw_circle(Vector2(3, 3), 2.5, Color("e59b91")))
	add_child(live_dot)
	_place(live_dot, 0.091, 0.076, 0.096, 0.084)
	_segment_label = _label(SEGMENTS[0], 16, MUTED)
	add_child(_segment_label)
	_place(_segment_label, 0.071, 0.10, 0.45, 0.14)
	var status := HBoxContainer.new()
	status.alignment = BoxContainer.ALIGNMENT_END
	status.add_theme_constant_override("separation", 16)
	add_child(status)
	_place(status, 0.72, 0.067, 0.965, 0.10)
	status.add_child(Kit.picture(Kit.icon("coin"), Vector2(20, 20)))
	_gold = _label("", 19, WHITE)
	_gold.autowrap_mode = TextServer.AUTOWRAP_OFF
	_gold.size_flags_horizontal = Control.SIZE_SHRINK_END
	_gold.add_theme_font_override("font", Kit.font(550, true))
	status.add_child(_gold)
	var status_rule := Kit.divider(true)
	status_rule.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	status_rule.custom_minimum_size.y = 20
	status.add_child(status_rule)
	_clock = _label("", 21, WHITE)
	_clock.autowrap_mode = TextServer.AUTOWRAP_OFF
	_clock.size_flags_horizontal = Control.SIZE_SHRINK_END
	_clock.add_theme_font_override("font", Kit.font(550, true))
	status.add_child(_clock)
	var mode := _label("离线规则回应 · 观众为本地模拟", 12, Color("a0b1c7"))
	mode.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	add_child(mode)
	_place(mode, 0.65, 0.115, 0.965, 0.15)

	_chat_layer = Control.new()
	_chat_layer.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_chat_layer)
	_chat_layer.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	var room := _label("直播间", 14, MUTED)
	_chat_layer.add_child(room)
	_place(room, 0.034, 0.407, 0.08, 0.44)
	var chat_rule := Kit.divider()
	_chat_layer.add_child(chat_rule)
	_place(chat_rule, 0.088, 0.426, 0.319, 0.426)
	var chat_icon := Kit.picture(Kit.icon("chat"), Vector2.ZERO)
	_chat_layer.add_child(chat_icon)
	_place(chat_icon, 0.319, 0.413, 0.332, 0.435)
	_chat_scroll = ScrollContainer.new()
	_chat_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	_chat_scroll.follow_focus = true
	_chat_layer.add_child(_chat_scroll)
	_place(_chat_scroll, 0.034, 0.454, 0.334, 0.753)
	_chat_rows = VBoxContainer.new()
	_chat_rows.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_chat_rows.add_theme_constant_override("separation", 20)
	_chat_scroll.add_child(_chat_rows)
	_chat_scroll.get_v_scroll_bar().changed.connect(_apply_chat_scroll.call_deferred)
	_chat_scroll.gui_input.connect(_cancel_chat_follow_on_input)
	_chat_scroll.get_v_scroll_bar().gui_input.connect(_cancel_chat_follow_on_input)

	_topic = _button("聊聊今天听到的码头铃声", func(): _fill_bell_topic())
	_topic.icon = Kit.icon("wave")
	_topic.alignment = HORIZONTAL_ALIGNMENT_LEFT
	_topic.add_theme_font_size_override("font_size", 14)
	_topic.tooltip_text = "使用白天已经观察到的事实；先填入输入框，可修改后发送。"
	_chat_layer.add_child(_topic)
	_place(_topic, 0.034, 0.77, 0.334, 0.814)
	_work = _button("提交今天做好的节目作品", func(): _request("submit_work"))
	_work.icon = Kit.icon("box")
	_work.alignment = HORIZONTAL_ALIGNMENT_LEFT
	_work.add_theme_font_size_override("font_size", 14)
	_chat_layer.add_child(_work)
	_place(_work, 0.034, 0.818, 0.334, 0.858)

	var subtitle_box := HBoxContainer.new()
	subtitle_box.add_theme_constant_override("separation", 20)
	add_child(subtitle_box)
	_place(subtitle_box, 0.574, 0.741, 0.94, 0.836)
	_subtitle_hint = _label("Z", 15, Color("d2e2f3"))
	_subtitle_hint.autowrap_mode = TextServer.AUTOWRAP_OFF
	_subtitle_hint.add_theme_font_override("font", Kit.font(500, true))
	_subtitle_hint.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
	_subtitle_hint.custom_minimum_size.x = 16
	subtitle_box.add_child(_subtitle_hint)
	var subtitle_rule := Kit.divider(true)
	subtitle_rule.size_flags_vertical = Control.SIZE_SHRINK_BEGIN
	subtitle_rule.custom_minimum_size.y = 28
	subtitle_box.add_child(subtitle_rule)
	_subtitle = _label("", 22, WHITE)
	_subtitle.add_theme_constant_override("outline_size", 3)
	_subtitle.add_theme_color_override("font_outline_color", Color(0.015, 0.025, 0.04, 0.75))
	subtitle_box.add_child(_subtitle)

	var footer_rule := Kit.divider()
	add_child(footer_rule)
	_place(footer_rule, 0.034, 0.859, 0.965, 0.859)
	var composer_panel := PanelContainer.new()
	var composer_style := Kit.style(Color(0.83, 0.88, 0.95, 0.045), LINE, 6)
	composer_style.content_margin_left = 0
	composer_style.content_margin_right = 6
	composer_style.content_margin_top = 6
	composer_style.content_margin_bottom = 6
	composer_panel.add_theme_stylebox_override("panel", composer_style)
	add_child(composer_panel)
	_place(composer_panel, 0.034, 0.887, 0.455, 0.951)
	var composer := HBoxContainer.new()
	composer.add_theme_constant_override("separation", 0)
	composer_panel.add_child(composer)
	_input = LineEdit.new()
	_input.name = "ChatInput"
	_input.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_input.placeholder_text = "说点什么，让她认识你…"
	_input.max_length = 180
	_input.keep_editing_on_text_submit = true
	_input.caret_blink = true
	_input.add_theme_font_size_override("font_size", 17)
	_input.text_submitted.connect(func(_text: String): _send_chat())
	_input.text_changed.connect(func(_text: String): _update_send())
	composer.add_child(_input)
	_send = _button("", _send_chat, true)
	_send.icon = Kit.icon("send")
	_send.tooltip_text = "发送弹幕"
	_send.custom_minimum_size = Vector2(42, 42)
	_send.add_theme_constant_override("icon_max_width", 22)
	for state in ["normal", "hover", "pressed", "focus", "disabled"]:
		var send_style := _send.get_theme_stylebox(state).duplicate() as StyleBoxFlat
		send_style.content_margin_left = 10
		send_style.content_margin_right = 10
		send_style.content_margin_top = 8
		send_style.content_margin_bottom = 8
		_send.add_theme_stylebox_override(state, send_style)
	_send.add_theme_color_override("icon_normal_color", INK)
	_send.add_theme_color_override("icon_hover_color", INK)
	_send.add_theme_color_override("icon_focus_color", INK)
	_send.add_theme_color_override("icon_pressed_color", INK)
	composer.add_child(_send)
	_gift = _button("礼物", func(): _open_drawer("gift"))
	_gift.icon = Kit.icon("gift")
	add_child(_gift)
	_place(_gift, 0.469, 0.887, 0.535, 0.951)
	_call = _button("连麦", func(): _open_drawer("call"))
	_call.icon = Kit.icon("mic")
	add_child(_call)
	_place(_call, 0.54, 0.887, 0.614, 0.951)
	_advance = _button("继续节目", _open_advance)
	_advance.icon = Kit.icon("arrow")
	_advance.icon_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	_advance.alignment = HORIZONTAL_ALIGNMENT_RIGHT
	add_child(_advance)
	_place(_advance, 0.815, 0.887, 0.965, 0.951)
	_notice = _label("阅读、输入与等待不消耗游戏时间", 12, Color("9fb0c5"))
	add_child(_notice)
	_place(_notice, 0.035, 0.956, 0.85, 0.991)
	var back := LinkButton.new()
	back.text = "返回"
	back.pressed.connect(func(): exit_requested.emit())
	back.add_theme_font_size_override("font_size", 13)
	add_child(back)
	_place(back, 0.91, 0.956, 0.965, 0.991)

	_scrim = ColorRect.new()
	_scrim.color = Color(0.015, 0.025, 0.05, 0.12)
	_scrim.visible = false
	_scrim.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(_scrim)
	_scrim.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_scrim.gui_input.connect(func(event: InputEvent):
		if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
			_close_modal())
	_drawer = PanelContainer.new()
	_drawer.add_theme_stylebox_override("panel", _surface_style())
	_drawer.visible = false
	add_child(_drawer)
	_drawer.minimum_size_changed.connect(_fit_modal_heights.call_deferred)
	_drawer_content = _inset_column(_drawer, 28)
	_confirm = PanelContainer.new()
	_confirm.add_theme_stylebox_override("panel", _surface_style())
	_confirm.visible = false
	add_child(_confirm)
	_confirm.minimum_size_changed.connect(_fit_modal_heights.call_deferred)
	var confirm_col := _inset_column(_confirm, 30)
	confirm_col.add_child(_label("继续这一晚", 26, WHITE))
	_confirm_times = HBoxContainer.new()
	_confirm_times.add_theme_constant_override("separation", 20)
	confirm_col.add_child(_confirm_times)
	_time_from = _label("20:00", 31, MUTED)
	_time_from.autowrap_mode = TextServer.AUTOWRAP_OFF
	_time_from.add_theme_font_override("font", Kit.font(500, true))
	_confirm_times.add_child(_time_from)
	_confirm_times.add_child(Kit.picture(Kit.icon("arrow"), Vector2(24, 24)))
	_time_to = _label("20:40", 31, WHITE)
	_time_to.autowrap_mode = TextServer.AUTOWRAP_OFF
	_time_to.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	_time_to.add_theme_font_override("font", Kit.font(550, true))
	_confirm_times.add_child(_time_to)
	_confirm_text = _label("", 17, MUTED)
	confirm_col.add_child(_confirm_text)
	_confirm_button = _button("确认继续", _commit_confirm, true)
	confirm_col.add_child(_confirm_button)
	confirm_col.add_child(_button("留在这里", _close_modal))


func refresh() -> void:
	if not _built or not is_instance_valid(model):
		return
	var data: Dictionary = _data()
	var segment := clampi(int(data.get("segment", 0)), 0, 3)
	var phase := str(data.get("phase", "day"))
	var night := phase in ["live", "night"]
	_clock.text = _time_text(int(data.get("time", 1200)))
	_gold.text = str(int(data.get("gold", 0)))
	_gold.tooltip_text = "金币"
	_live.text = "LIVE" if night else "REPLAY"
	_segment_label.text = SEGMENTS[segment]
	_input.editable = night
	_input.placeholder_text = "说点什么，让她认识你…" if night else "今晚的直播已结束"
	_gift.disabled = not night
	var flags: Dictionary = data.get("flags", {})
	var inventory: Dictionary = data.get("inventory", {})
	_topic.visible = bool(flags.get("bell_observed", false)) and night
	_topic.disabled = bool(flags.get("bell_replied", false)) or segment != 0
	_topic.text = "码头的铃声 · 已得到回应" if bool(flags.get("bell_replied", false)) else ("聊聊今天听到的码头铃声" if segment == 0 else "开场话题已经结束")
	_work.visible = (bool(flags.get("craft_done", false)) or int(inventory.get("work", 0)) > 0) and night
	var work_preview := _preview("submit_work")
	_work.disabled = not bool(work_preview.get("ok", false))
	_work.tooltip_text = str(work_preview.get("reason", ""))
	_work.text = "节目作品 · 已得到回应" if bool(flags.get("work_responded", false)) else "提交今天做好的节目作品"
	_call.text = "连麦中" if str(data.get("call_state", "idle")) == "connected" else "连麦"
	_call.disabled = not night
	_advance.text = "回看今晚" if phase == "review" else ("返回小岛" if phase == "done" else "继续节目")
	_advance.disabled = phase == "day"
	_update_messages(data.get("messages", []))
	_update_send()
	if _drawer.visible:
		_render_drawer()
	if _confirm.visible and not _confirm_action.is_empty():
		var current := _preview(_confirm_action, _confirm_payload)
		_confirm_button.disabled = not bool(current.get("ok", false))
		if _confirm_button.disabled:
			_confirm_text.text = str(current.get("reason", "当前状态已变化，请返回后重新选择。"))
	_chat_layer.visible = not _scrim.visible
	_sync_web_input()


func _mount_web_input() -> void:
	if not OS.has_feature("web"):
		return
	var bridge = WebInputBridge.new()
	if not bridge.mount(_on_web_input_event, "moonz-chat-input-%d" % get_instance_id()):
		return
	_web_input = bridge
	# Preserve the layout and authoritative draft mirror without invoking Godot IME.
	_input.self_modulate.a = 0.0
	_input.focus_mode = Control.FOCUS_NONE
	_input.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_sync_web_input()


func _on_web_input_event(arguments: Array) -> void:
	if arguments.size() < 3 or not _built:
		return
	var kind := str(arguments[0])
	_web_composing = bool(arguments[2])
	_input.text = str(arguments[1])
	_web_synced_text = _input.text
	_update_send()
	if kind == "submit" and not _web_composing:
		_send_chat()
	elif kind == "escape" and not _web_composing:
		if _scrim.visible:
			_close_modal()
		else:
			exit_requested.emit()


func _web_input_visible() -> bool:
	if not _built or not is_visible_in_tree() or _scrim.visible:
		return false
	# The host owns its pause/menu overlay. Detect an interactive higher sibling,
	# while allowing noninteractive toast labels to remain above the input.
	var parent := get_parent()
	if parent != null:
		for sibling in parent.get_children():
			if sibling is Control and sibling != self and not sibling is Label and sibling.is_visible_in_tree() and sibling.z_index > z_index and sibling.mouse_filter != Control.MOUSE_FILTER_IGNORE:
				if sibling.get_global_rect().intersects(_input.get_global_rect()):
					return false
	return true


func _sync_web_input() -> void:
	if _web_input == null or not _built:
		return
	_web_input.update(_input.get_global_rect(), _web_input_visible(), _input.editable, _input.placeholder_text)
	# Only explicit Godot draft changes (shortcut or acknowledged send) write back.
	# Browser editing and caret/composition state are never reset each frame.
	if not _web_composing and _input.text != _web_synced_text:
		_web_input.set_value(_input.text)
		_web_synced_text = _input.text


func _exit_tree() -> void:
	if _web_input != null:
		_web_input.destroy()
		_web_input = null


func _data() -> Dictionary:
	if not is_instance_valid(model):
		return {}
	var value: Variant = model.get("data")
	return value if value is Dictionary else {}


func _preview(action: String, payload: Dictionary = {}) -> Dictionary:
	if not is_instance_valid(model) or not model.has_method("preview"):
		return {"ok": false, "reason": "尚未连接规则状态。"}
	var result: Variant = model.call("preview", action, payload)
	return result if result is Dictionary else {"ok": false, "reason": "规则没有返回有效预览。"}


func _request(action: String, payload: Dictionary = {}) -> bool:
	var check := _preview(action, payload)
	if not bool(check.get("ok", false)):
		show_notice(str(check.get("reason", "目前不能执行这项操作。")))
		return false
	action_requested.emit(action, payload.duplicate(true))
	return true


func show_notice(message: String) -> void:
	if is_instance_valid(_notice):
		_notice.text = message


func _send_chat() -> void:
	if not is_instance_valid(_input) or _input.has_ime_text() or _web_composing:
		return
	var text := _input.text
	if text.strip_edges().is_empty():
		return
	_pending_text = text.strip_edges()
	var payload: Dictionary = {"text": text}
	if _draft_topic == "bell" and text.contains("铃"):
		payload["topic"] = "bell"
	if not _request("chat", payload):
		_pending_text = ""


func _update_send() -> void:
	if is_instance_valid(_send) and is_instance_valid(_input):
		_send.disabled = not _input.editable or _input.text.strip_edges().is_empty() or _web_composing


func _fill_bell_topic() -> void:
	_draft_topic = "bell"
	_input.text = "我今天在码头听过那阵铃声，可以把它和海浪接成今晚的开场节拍。"
	_input.caret_column = _input.text.length()
	_update_send()
	if _web_input != null:
		_sync_web_input()
		_web_input.focus()
	else:
		_input.grab_focus()


func _update_messages(messages: Array) -> void:
	var fingerprint := str(hash(var_to_str(messages)))
	if fingerprint == _messages_fingerprint:
		return
	_messages_fingerprint = fingerprint
	var scroll_bar := _chat_scroll.get_v_scroll_bar()
	var follow_bottom := _chat_follow_bottom if _chat_scroll_pending else scroll_bar.value >= scroll_bar.max_value - scroll_bar.page - 48.0
	var previous_position := _chat_restore_position if _chat_scroll_pending else int(scroll_bar.value)
	var next_ids: Dictionary = {}
	for child in _chat_rows.get_children():
		_chat_rows.remove_child(child)
		child.queue_free()
	var last_z: Dictionary = {}
	for index in range(maxi(0, messages.size() - 80), messages.size()):
		if not messages[index] is Dictionary:
			continue
		var message: Dictionary = messages[index]
		var speaker := str(message.get("speaker", "system"))
		var body := str(message.get("text", ""))
		var message_id := str(message.get("id", "%s:%d:%s" % [speaker, index, body]))
		next_ids[message_id] = true
		if speaker == "player" and not _message_ids.has(message_id):
			follow_bottom = true
		if speaker == "z":
			last_z = message
		if speaker == "player" and not _pending_text.is_empty() and body == _pending_text:
			if _input.text.strip_edges() == _pending_text:
				_input.clear()
			_pending_text = ""
			_draft_topic = ""
		var row := VBoxContainer.new()
		row.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		row.add_theme_constant_override("separation", 5)
		_chat_rows.add_child(row)
		if speaker == "player" and str(message.get("kind", "")) == "gift":
			var gift_line := HBoxContainer.new()
			gift_line.add_theme_constant_override("separation", 12)
			row.add_child(gift_line)
			gift_line.add_child(Kit.picture(Kit.icon("gift"), Vector2(21, 24)))
			var record := _label("听潮  ·  " + body, 16, Color("d1dbea"))
			gift_line.add_child(record)
			continue
		var who := "节目记录"
		match speaker:
			"player": who = "听潮 · 你"
			"z": who = "Z"
			"audience": who = "岛上来信 · 模拟观众"
		var delivery := ""
		if speaker == "player":
			match str(message.get("state", "")):
				"responded": delivery = " · 已回应"
				"delivered": delivery = " · 已送达"
				_: delivery = " · 已发送"
		var author := _label(who + delivery, 13, PALE if speaker == "player" else Color("aebed3"))
		var message_body := _label(body, 19 if speaker != "system" else 15, WHITE if speaker != "system" else MUTED)
		message_body.add_theme_constant_override("line_spacing", 8)
		if speaker == "player":
			var inset := StyleBoxEmpty.new()
			inset.content_margin_left = 17
			author.add_theme_stylebox_override("normal", inset)
			message_body.add_theme_stylebox_override("normal", inset)
			row.draw.connect(func(): row.draw_line(Vector2.ZERO, Vector2(0, row.size.y), Color("b8cce1"), 1.0))
			row.resized.connect(row.queue_redraw)
		row.add_child(author)
		row.add_child(message_body)
	if messages.is_empty():
		_chat_rows.add_child(_label("直播记录会保留在这里。先读读今晚的话题，再决定要说什么。", 18, MUTED))
	if not last_z.is_empty():
		_subtitle.text = str(last_z.get("text", ""))
		_subtitle_hint.text = "Z"
		var reply_id := str(last_z.get("id", ""))
		if reply_id != _last_reply_id:
			_last_reply_id = reply_id
			_set_expression(str(last_z.get("expression", "neutral")))
	else:
		_subtitle.text = ""
	_message_ids = next_ids
	_chat_scroll_serial += 1
	_chat_scroll_pending = true
	_chat_follow_bottom = follow_bottom
	_chat_restore_position = previous_position
	_settle_chat_scroll(_chat_scroll_serial)


func _apply_chat_scroll() -> void:
	if not _chat_scroll_pending or not is_instance_valid(_chat_scroll):
		return
	var bar := _chat_scroll.get_v_scroll_bar()
	_chat_scroll.scroll_vertical = int(bar.max_value - bar.page) if _chat_follow_bottom else _chat_restore_position


func _settle_chat_scroll(serial: int) -> void:
	# New wrapped rows can change the range after the first deferred call. Follow
	# range changes and wait for two stable layout frames before releasing the plan.
	var previous_range := Vector2(-1, -1)
	var stable_frames := 0
	for frame in range(8):
		await get_tree().process_frame
		if serial != _chat_scroll_serial or not is_inside_tree():
			return
		var bar := _chat_scroll.get_v_scroll_bar()
		var current_range := Vector2(bar.max_value, bar.page)
		stable_frames = stable_frames + 1 if current_range.is_equal_approx(previous_range) else 0
		previous_range = current_range
		_apply_chat_scroll()
		if stable_frames >= 2 and frame >= 2:
			break
	if serial == _chat_scroll_serial:
		_chat_scroll_pending = false


func _cancel_chat_follow_on_input(event: InputEvent) -> void:
	if event is InputEventPanGesture or (event is InputEventMouseButton and event.pressed) or (event is InputEventScreenTouch and event.pressed):
		_chat_scroll_serial += 1
		_chat_scroll_pending = false


func _open_drawer(kind: String) -> void:
	_focus_before_modal = get_viewport().gui_get_focus_owner()
	_drawer_kind = kind
	_confirm.hide()
	_scrim.show()
	_drawer.show()
	_chat_layer.hide()
	_render_drawer()
	_layout()


func _render_drawer() -> void:
	for child in _drawer_content.get_children():
		_drawer_content.remove_child(child)
		child.queue_free()
	var header := HBoxContainer.new()
	_drawer_content.add_child(header)
	var heading := _label("一点心意" if _drawer_kind == "gift" else "来聊一会儿", 27, WHITE)
	heading.add_theme_font_override("font", Kit.font(500))
	heading.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	header.add_child(heading)
	var close := _button("", _close_modal)
	close.icon = Kit.icon("close")
	close.tooltip_text = "关闭"
	close.custom_minimum_size = Vector2(36, 36)
	for state in ["normal", "hover", "pressed", "focus", "disabled"]:
		var close_style := close.get_theme_stylebox(state).duplicate() as StyleBoxFlat
		close_style.content_margin_left = 8
		close_style.content_margin_right = 8
		close_style.content_margin_top = 8
		close_style.content_margin_bottom = 8
		close.add_theme_stylebox_override(state, close_style)
	close.add_theme_constant_override("icon_max_width", 20)
	header.add_child(close)
	if _drawer_kind == "gift":
		_render_gift()
	else:
		_render_call()
	close.grab_focus()


func _render_gift() -> void:
	var image := TextureRect.new()
	image.texture = _texture("res://assets/gift.png")
	image.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	image.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	image.custom_minimum_size = Vector2(0, clampf(size.x * 0.128, 160, 205))
	_drawer_content.add_child(image)
	var inventory: Dictionary = _data().get("inventory", {})
	var item_heading := HBoxContainer.new()
	var item_name := _label("小礼物", 19, WHITE)
	item_name.autowrap_mode = TextServer.AUTOWRAP_OFF
	item_name.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	item_heading.add_child(item_name)
	var count := _label("已拥有  %d" % int(inventory.get("gift", 0)), 14, MUTED)
	count.autowrap_mode = TextServer.AUTOWRAP_OFF
	count.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	item_heading.add_child(count)
	_drawer_content.add_child(item_heading)
	_drawer_content.add_child(_label("把白天挑好的心意，送到她面前。\n礼物会留下记录，不会替你换来私下的承诺。", 16, MUTED))
	_add_drawer_action("赠送给 Z", "gift", {}, true)
	var gift_note := _label("使用 1 份库存 · 金币不变 · 不推进节目", 12, Color("9eafc5"))
	gift_note.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_drawer_content.add_child(gift_note)


func _render_call() -> void:
	var data := _data()
	var state := str(data.get("call_state", "idle"))
	var segment := int(data.get("segment", 0))
	var call_symbol := Kit.picture(Kit.icon("mic"), Vector2(0, 76))
	call_symbol.self_modulate = PALE
	_drawer_content.add_child(call_symbol)
	var description := "自愿报名，主节目随机抽选。\n连麦采用文字交流，不会访问麦克风。"
	match state:
		"joined": description = "你已进入报名名单。\n抽选只在主节目进行，取消报名不会记为失约。"
		"selected": description = "这一轮抽到了你。\n愿意现在接通文字连麦吗？拒绝不会扣资源，也不算失约。"
		"connected": description = "文字连麦已接通。\n关闭此面板后在下方输入框发言；结束连麦后仍可发送弹幕。"
		"not_selected": description = "这轮没有抽到你。\n弹幕和已经准备好的节目作品仍然可以得到有效回应。"
		"declined": description = "你已婉拒这次连麦。\n可以继续发弹幕，不会被记作失约。"
		"cancelled": description = "你已取消报名。\n不扣金币，也不影响弹幕或作品路径。"
		"ended": description = "这次连麦已经结束。\n今晚的真实交流仍会保留在记录中。"
	_drawer_content.add_child(_label(description, 19, WHITE))
	if state == "idle" or state == "cancelled":
		_add_drawer_action("报名随机连麦", "call_join", {}, true)
	elif state == "joined":
		_add_drawer_action("参与本轮抽选" if segment == 1 else "主节目才开始抽选", "call_draw", {}, true)
		_add_drawer_action("取消报名", "call_cancel")
	elif state == "selected":
		_add_drawer_action("接受 · 文字连麦", "call_accept", {}, true)
		_add_drawer_action("这次先不了", "call_decline")
	elif state == "connected":
		_drawer_content.add_child(_button("回到输入框", func():
			_close_modal()
			if _web_input != null: _web_input.focus()
			else: _input.grab_focus(), true))
		_add_drawer_action("结束连麦", "call_end")
	_drawer_content.add_child(_label("随机结果由规则保存，重新打开面板不会重抽。\n不赠送物品也能报名；抽中不代表获得私人邀约。", 14, MUTED))


func _add_drawer_action(label: String, action: String, payload: Dictionary = {}, primary: bool = false) -> void:
	var check := _preview(action, payload)
	var button := _button(label, func(): _request(action, payload), primary)
	if primary:
		button.alignment = HORIZONTAL_ALIGNMENT_LEFT
		button.icon = Kit.icon("arrow")
		button.icon_alignment = HORIZONTAL_ALIGNMENT_RIGHT
		button.add_theme_color_override("icon_normal_color", INK)
		button.add_theme_color_override("icon_hover_color", INK)
		button.add_theme_color_override("icon_focus_color", INK)
		button.add_theme_color_override("icon_pressed_color", INK)
	button.disabled = not bool(check.get("ok", false))
	button.tooltip_text = str(check.get("reason", ""))
	_drawer_content.add_child(button)
	if button.disabled and not button.tooltip_text.is_empty():
		_drawer_content.add_child(_label(button.tooltip_text, 14, MUTED))


func _open_advance() -> void:
	var data := _data()
	var phase := str(data.get("phase", "night"))
	if phase == "done":
		exit_requested.emit()
		return
	_confirm_action = "sleep" if phase == "review" else "advance_segment"
	_confirm_payload = {} if phase == "review" else {"expected_segment": int(data.get("segment", 0))}
	var check := _preview(_confirm_action, _confirm_payload)
	if not bool(check.get("ok", false)):
		show_notice(str(check.get("reason", "现在不能推进节目。")))
		return
	_focus_before_modal = get_viewport().gui_get_focus_owner()
	_drawer.hide()
	_scrim.show()
	_confirm.show()
	_chat_layer.hide()
	_confirm_times.visible = phase != "review"
	_time_from.text = _time_text(int(data.get("time", 1200)))
	_time_to.text = _time_text(int(check.get("end_time", 1240)))
	if phase == "review":
		var flags: Dictionary = data.get("flags", {})
		var facts: Array[String] = []
		if bool(flags.get("bell_replied", false)): facts.append("她回应了你今天听到的码头铃声")
		if bool(flags.get("gift_responded", false)): facts.append("她收到了你白天准备的小礼物")
		if bool(flags.get("work_responded", false)): facts.append("你完成的节目作品被实际采用")
		if facts.is_empty(): facts.append("你看完了今晚的直播；尚未形成被回应的准备记录")
		_confirm_text.text = "今晚留下的事实：\n\n• " + "\n• ".join(facts) + "\n\n认识需要时间。这些记录不等于信任、私下邀请或合作承诺。\n确认后结束这一天，时间到 " + _time_text(int(check.get("end_time", 1380))) + "。"
		_confirm_button.text = "结束这一天"
	else:
		_confirm_text.text = "节目推进 %d 分钟。\n\n输入、阅读与赠礼不额外扣时，也不会再次扣除礼物。\n\n还想留在这个话题里，可以返回继续聊。" % int(check.get("minutes", 40))
		_confirm_button.text = "确认推进节目"
	_confirm_button.disabled = false
	_layout()
	_confirm_button.grab_focus()


func _commit_confirm() -> void:
	var action := _confirm_action
	var payload := _confirm_payload.duplicate(true)
	_close_modal()
	_request(action, payload)


func _close_modal() -> void:
	_drawer.hide()
	_confirm.hide()
	_scrim.hide()
	_chat_layer.show()
	_drawer_kind = ""
	_confirm_action = ""
	_confirm_payload = {}
	if is_instance_valid(_focus_before_modal) and _focus_before_modal.is_visible_in_tree():
		_focus_before_modal.grab_focus()
	_sync_web_input()


func _unhandled_key_input(event: InputEvent) -> void:
	if not is_visible_in_tree():
		return
	if event is InputEventKey and event.pressed and not event.echo and event.keycode == KEY_ESCAPE:
		if _input.has_ime_text():
			return
		if _scrim.visible:
			_close_modal()
		else:
			exit_requested.emit()
		get_viewport().set_input_as_handled()


func _layout() -> void:
	if not _built:
		return
	var viewport_size := size
	if viewport_size.x < 100.0:
		viewport_size = Vector2(1600, 900)
	_drawer.size = Vector2(clampf(viewport_size.x * 0.30, 380.0, 480.0), 0)
	_confirm.size = Vector2(clampf(viewport_size.x * 0.30, 380.0, 480.0), 0)
	_portrait_base_position = Vector2(viewport_size.x * 0.36, viewport_size.y * 0.07)
	_portrait_base_size = Vector2(viewport_size.x * 0.61, viewport_size.y * 0.87)
	_portrait.position = _portrait_base_position
	_portrait.size = _portrait_base_size
	_portrait.pivot_offset = Vector2(_portrait_base_size.x * 0.5, _portrait_base_size.y)
	_fit_modal_heights.call_deferred()
	_sync_web_input()


func _fit_modal_heights() -> void:
	# Wrapped text first reports its minimum at the old width; shrink after layout.
	if is_instance_valid(_drawer):
		_drawer.size.y = _drawer.get_combined_minimum_size().y
		_drawer.position = Vector2(size.x * 0.034, maxf(32.0, size.y * 0.842 - _drawer.size.y))
	if is_instance_valid(_confirm):
		_confirm.size.y = _confirm.get_combined_minimum_size().y
		_confirm.position = Vector2(size.x * 0.034, maxf(32.0, size.y * 0.842 - _confirm.size.y))


func _load_expression_assets() -> void:
	_expression_sheet = _texture("res://assets/z-expressions.png")
	if _expression_sheet == null:
		return
	_expression_atlas = AtlasTexture.new()
	_expression_atlas.atlas = _expression_sheet
	var clean_backdrop := _texture("res://assets/z-background.png")
	if clean_backdrop != null:
		_background.texture = clean_backdrop
		_portrait.texture = _expression_atlas
		_portrait.visible = true
	_set_expression("neutral")


func _set_expression(expression: String) -> void:
	_expression_name = expression
	_expression_hold = 3.5 if expression != "neutral" else 0.0
	_show_expression(expression)


func _show_expression(expression: String) -> void:
	if _expression_atlas == null or _expression_sheet == null:
		return
	var index := 0
	match expression:
		"warm": index = 1
		"blink": index = 2
		"curious", "thoughtful", "thinking": index = 3
	var cell := Vector2(_expression_sheet.get_width() / 2.0, _expression_sheet.get_height() / 2.0)
	_expression_atlas.region = Rect2(Vector2(index % 2, floori(index / 2.0)) * cell, cell)


func _process(delta: float) -> void:
	_sync_web_input()
	if not _built or not _portrait.visible or not is_visible_in_tree():
		return
	_breath_time += delta
	if reduced_motion:
		_portrait.scale = Vector2.ONE
		_show_expression(_expression_name)
		return
	var breath := sin(_breath_time * 1.35) * 0.0022
	_portrait.scale = Vector2(1.0 + breath * 0.35, 1.0 + breath)
	if _expression_hold > 0.0:
		_expression_hold -= delta
		if _expression_hold <= 0.0:
			_expression_name = "neutral"
	_blink_clock = fmod(_blink_clock + delta, 4.8)
	_show_expression("blink" if _blink_clock > 4.64 else _expression_name)


func _texture(path: String) -> Texture2D:
	if ResourceLoader.exists(path):
		return load(path) as Texture2D
	return null


func _surface_style() -> StyleBoxFlat:
	var box := Kit.style(Color(0.059, 0.106, 0.165, 0.975), Color(0.80, 0.86, 0.93, 0.22), 10)
	box.content_margin_left = 0
	box.content_margin_right = 0
	box.content_margin_top = 0
	box.content_margin_bottom = 0
	return box


func _label(text: String, font_size: int = 19, color: Color = WHITE) -> Label:
	var label := Label.new()
	label.text = text
	label.add_theme_font_size_override("font_size", font_size)
	label.add_theme_color_override("font_color", color)
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	return label


func _button(text: String, callback: Callable, primary: bool = false) -> Button:
	var button := Button.new()
	button.text = text
	button.custom_minimum_size.y = 52 if primary else 42
	button.add_theme_font_size_override("font_size", 17)
	button.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	button.pressed.connect(callback)
	if primary:
		Kit.primary(button)
	else:
		Kit.quiet(button)
	return button


func _inset_column(panel: Control, margin: int) -> VBoxContainer:
	var inset := MarginContainer.new()
	for side in ["left", "top", "right", "bottom"]:
		inset.add_theme_constant_override("margin_" + side, margin)
	panel.add_child(inset)
	var column := VBoxContainer.new()
	column.add_theme_constant_override("separation", 16)
	inset.add_child(column)
	return column


func _place(control: Control, left: float, top: float, right: float, bottom: float) -> void:
	control.anchor_left = left
	control.anchor_top = top
	control.anchor_right = right
	control.anchor_bottom = bottom
	control.offset_left = 0
	control.offset_top = 0
	control.offset_right = 0
	control.offset_bottom = 0


func _gradient_overlay(horizontal: bool) -> TextureRect:
	var gradient := Gradient.new()
	gradient.offsets = PackedFloat32Array([0.0, 0.48, 1.0])
	gradient.colors = PackedColorArray([Color(0.025, 0.045, 0.075, 0.8), Color(0.025, 0.045, 0.075, 0.12), Color(0.025, 0.045, 0.075, 0.01)]) if horizontal else PackedColorArray([Color(0.025, 0.045, 0.075, 0.01), Color(0.025, 0.045, 0.075, 0.08), Color(0.025, 0.045, 0.075, 0.95)])
	var texture := GradientTexture2D.new()
	texture.gradient = gradient
	texture.fill_from = Vector2.ZERO
	texture.fill_to = Vector2.RIGHT if horizontal else Vector2.DOWN
	var rect := TextureRect.new()
	rect.texture = texture
	rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	rect.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	return rect


func _time_text(minutes: int) -> String:
	return "%02d:%02d" % [minutes / 60, minutes % 60]
