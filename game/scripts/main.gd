extends Node2D

const Kit = preload("res://scripts/ui_kit.gd")
const DayUI = preload("res://scripts/day_ui.gd")
const State = preload("res://core/day_state.gd")
const World = preload("res://scripts/world.gd")
const Night = preload("res://scripts/night_view.gd")
const AUTO := "user://day1-auto.json"
const MANUAL := "user://day1-manual.json"
const WORLD_RESOLUTION := Vector2i(640,360)
var model = State.new()
var world = World.new()
var world_viewport := SubViewport.new()
var world_display := TextureRect.new()
var layer := CanvasLayer.new()
var ui := Control.new()
var hud := Control.new()
var modal := Control.new()
var night: Control
var time_text: Label
var gold_text: Label
var objective: Label
var hint: Label
var toast_label: Label
var toast_timer := 0.0
var started := false
var modal_kind := ""
var activity_open := false
var font: Font
var theme_ui := Theme.new()
var presentation: RefCounted
var place_text: Label
var next_text: Label
var objective_caption: Label

class SpatialMap extends Control:
	var snapshot: Dictionary = {}
	var target_id := ""
	var map_font: Font
	var padding := Vector2(28, 24)

	func map_point(point: Vector2) -> Vector2:
		var bounds: Rect2 = snapshot.get("bounds", Rect2(0, 0, 1, 1))
		var available := size - padding * 2.0
		var ratio := minf(available.x / bounds.size.x, available.y / bounds.size.y)
		return (size - bounds.size * ratio) * 0.5 + (point - bounds.position) * ratio

	func _draw() -> void:
		draw_rect(Rect2(Vector2.ZERO, size), Color("101f2c"))
		for region in snapshot.get("walk_regions", []):
			var outline := PackedVector2Array()
			for point in region: outline.append(map_point(point))
			if outline.size() >= 3: draw_colored_polygon(outline, Color("455d67"))
		for block in snapshot.get("blocks", []):
			var footprint := Rect2(map_point(block.position), map_point(block.end) - map_point(block.position))
			draw_rect(footprint, Color("213747"))
			draw_rect(footprint, Color("748a99"), false, 1.0)
		for spot in snapshot.get("spots", []):
			var point := map_point(spot.position)
			var is_target: bool = spot.id == target_id
			var color := Color("edca76") if is_target else Color("c4d9d6")
			if is_target: draw_arc(point, 12, 0, TAU, 24, color, 2.0)
			draw_circle(point, 4.5, color)
			var caption := String(spot.title).split(" · ")[0].replace("进入", "").replace("返回码头小街", "出口")
			var width := map_font.get_string_size(caption, HORIZONTAL_ALIGNMENT_LEFT, -1, 16).x
			var offset := Vector2(clampf(point.x - width * 0.5, 6, size.x - width - 6), point.y - 15)
			draw_string_outline(map_font, offset, caption, HORIZONTAL_ALIGNMENT_LEFT, -1, 16, 5, Color("10212a"))
			draw_string(map_font, offset, caption, HORIZONTAL_ALIGNMENT_LEFT, -1, 16, color)
		var player := map_point(snapshot.get("player", Vector2.ZERO))
		draw_circle(player, 10.0, Color("152a33"))
		draw_circle(player, 7.0, Color("7bebdc"))
		draw_circle(player, 3.0, Color("eafffb"))
		draw_rect(Rect2(Vector2.ZERO, size), Color("45626b"), false, 1.0)

func _ready() -> void:
	var font_variation:=FontVariation.new()
	font_variation.base_font=load("res://assets/fonts/NotoSansSC-Variable.ttf")
	font_variation.variation_opentype={TextServerManager.get_primary_interface().name_to_tag("wght"):450.0}
	font=font_variation
	make_theme()
	presentation=DayUI.new(self)
	# Sample the world once at a fixed pixel grid. UI stays on the root canvas.
	world_viewport.name="PixelWorldViewport"
	world_viewport.size=WORLD_RESOLUTION
	world_viewport.disable_3d=true
	world_viewport.snap_2d_transforms_to_pixel=true
	world_viewport.render_target_update_mode=SubViewport.UPDATE_WHEN_VISIBLE
	add_child(world_viewport)
	world_viewport.add_child(world)
	world.interaction_requested.connect(interact)
	world.moved.connect(func(pos: Vector2): model.data.position=[pos.x,pos.y])
	add_child(layer)
	world_display.name="PixelWorldDisplay"
	world_display.texture=world_viewport.get_texture()
	world_display.texture_filter=CanvasItem.TEXTURE_FILTER_NEAREST
	world_display.expand_mode=TextureRect.EXPAND_IGNORE_SIZE
	world_display.stretch_mode=TextureRect.STRETCH_SCALE
	world_display.mouse_filter=Control.MOUSE_FILTER_IGNORE
	layer.add_child(world_display)
	world_display.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	layer.add_child(ui)
	ui.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	ui.theme=theme_ui
	ui.mouse_filter=Control.MOUSE_FILTER_IGNORE
	ui.add_child(hud)
	hud.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	hud.mouse_filter=Control.MOUSE_FILTER_IGNORE
	build_hud()
	ui.add_child(modal)
	modal.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	modal.mouse_filter=Control.MOUSE_FILTER_STOP
	modal.hide()
	toast_label=label("",19,Color("e4edf5"))
	ui.add_child(toast_label)
	toast_label.position=Vector2(510,150)
	toast_label.size=Vector2(650,70)
	toast_label.horizontal_alignment=HORIZONTAL_ALIGNMENT_CENTER
	toast_label.add_theme_stylebox_override("normal",Kit.style(Color(0.045,0.08,0.13,0.96),Kit.LINE,5))
	toast_label.mouse_filter=Control.MOUSE_FILTER_IGNORE
	toast_label.hide()
	model.new_game()
	start_menu()

func make_theme() -> void:
	theme_ui=Kit.make_theme()
	font=Kit.font()

func label(value: String, size: int=20, color: Color=Kit.WHITE) -> Label:
	var l:=Label.new()
	l.text=value; l.add_theme_font_size_override("font_size",size)
	l.add_theme_color_override("font_color",color)
	l.autowrap_mode=TextServer.AUTOWRAP_WORD_SMART
	l.mouse_filter=Control.MOUSE_FILTER_IGNORE
	return l

func button(value: String, fn: Callable, primary: bool=false) -> Button:
	var b:=Button.new(); b.text=value; b.custom_minimum_size=Vector2(0,50)
	b.alignment=HORIZONTAL_ALIGNMENT_LEFT; b.pressed.connect(fn)
	b.add_theme_font_size_override("font_size",18)
	if primary: Kit.primary(b)
	return b

func panel(title: String, subtitle: String="", wide: bool=false) -> VBoxContainer:
	clear_modal(); modal.show()
	var shade:=ColorRect.new(); shade.name="SurfaceShade"
	shade.color=Color(0.01,0.035,0.07,0.60 if wide else 0.14)
	modal.add_child(shade); shade.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	var p:=PanelContainer.new(); p.name="UISurface"
	p.position=Vector2(144,126) if wide else Vector2(1020,162)
	p.size=Vector2(1312,686) if wide else Vector2(528,650)
	var box:=Kit.style(Color(0.059,0.106,0.169,0.985),Kit.LINE,8)
	box.content_margin_left=32; box.content_margin_right=32; box.content_margin_top=26; box.content_margin_bottom=28
	p.add_theme_stylebox_override("panel",box); modal.add_child(p)
	var structure:=VBoxContainer.new(); structure.add_theme_constant_override("separation",16); p.add_child(structure)
	var h:=HBoxContainer.new(); h.add_theme_constant_override("separation",14); structure.add_child(h)
	if wide:
		var symbol:=Kit.picture(Kit.icon("bag" if title=="背包" else ("map" if title.contains("地图") else "journal")),Vector2(25,25)); symbol.modulate=Kit.PALE; h.add_child(symbol)
	var heading:=label(title,28); heading.add_theme_font_override("font",Kit.font(550)); heading.size_flags_horizontal=Control.SIZE_EXPAND_FILL; h.add_child(heading)
	var close:=button("",close_modal); close.icon=Kit.icon("close"); close.tooltip_text="关闭 · Esc"; close.custom_minimum_size=Vector2(34,34); Kit.quiet(close); h.add_child(close)
	Kit.compact(close)
	if subtitle!="": structure.add_child(label(subtitle,16,Kit.MUTED))
	structure.add_child(Kit.divider())
	var scroll:=ScrollContainer.new(); scroll.name="SurfaceScroll"; scroll.size_flags_vertical=Control.SIZE_EXPAND_FILL; scroll.horizontal_scroll_mode=ScrollContainer.SCROLL_MODE_DISABLED; structure.add_child(scroll)
	var v:=VBoxContainer.new(); v.name="SurfaceContent"; v.size_flags_horizontal=Control.SIZE_EXPAND_FILL; v.add_theme_constant_override("separation",16); scroll.add_child(v)
	modal_footer("Esc  返回","查看与阅读不推进时间")
	return v

func modal_footer(left: String, right: String) -> void:
	var back:=label(left,16,Kit.MUTED); back.position=Vector2(52,846); back.size=Vector2(450,32); modal.add_child(back)
	var note:=label(right,15,Kit.MUTED); note.horizontal_alignment=HORIZONTAL_ALIGNMENT_RIGHT; note.position=Vector2(1040,846); note.size=Vector2(508,32); modal.add_child(note)

func clear_modal() -> void:
	for child in modal.get_children(): modal.remove_child(child); child.queue_free()

func close_modal() -> void:
	modal.hide()
	modal_kind=""
	if not started: start_menu(); return
	if not model.data.get("pending",{}).is_empty() and model.data.pending.action != "stage_work":
		activity_open=false
	refresh()

func build_hud() -> void:
	hud.add_child(Kit.fade(Color(0.015,0.045,0.08,0.78),Color.TRANSPARENT,Rect2(0,0,1600,210)))
	hud.add_child(Kit.fade(Color.TRANSPARENT,Color(0.02,0.045,0.08,0.94),Rect2(0,697,1600,203)))
	var sun:=Kit.picture(Kit.icon("sun"),Vector2.ZERO); sun.position=Vector2(53,45); sun.size=Vector2(22,22); sun.modulate=Kit.PALE; hud.add_child(sun)
	place_text=label("第 1 天   ·   码头小街",17); place_text.position=Vector2(88,41); place_text.size=Vector2(390,32); hud.add_child(place_text)
	time_text=label("07:00",38); time_text.add_theme_font_override("font",Kit.font(500,true)); time_text.position=Vector2(52,78); time_text.size=Vector2(230,51); hud.add_child(time_text)
	next_text=label("下一日程   12:00 午间",15,Kit.MUTED); next_text.position=Vector2(55,135); next_text.size=Vector2(380,28); hud.add_child(next_text)
	var coin:=Kit.picture(Kit.icon("coin"),Vector2.ZERO); coin.position=Vector2(1040,49); coin.size=Vector2(23,23); coin.modulate=Color("e0d2b7"); hud.add_child(coin)
	gold_text=label("40",23); gold_text.add_theme_font_override("font",Kit.font(550,true)); gold_text.position=Vector2(1076,43); gold_text.size=Vector2(95,35); hud.add_child(gold_text)
	var nav:=HBoxContainer.new(); nav.position=Vector2(1168,36); nav.add_theme_constant_override("separation",14); hud.add_child(nav)
	for data in [["map","M",show_map,"地图"],["bag","I",show_inventory,"背包"],["journal","J",show_journal,"日记"],["menu","Esc",pause_menu,"菜单"]]:
		var b:=button(data[1],data[2]); b.icon=Kit.icon(data[0]); b.tooltip_text=data[3]; b.custom_minimum_size=Vector2(80,48); b.add_theme_font_size_override("font_size",14); b.add_theme_font_override("font",Kit.font(500,true)); Kit.quiet(b); nav.add_child(b)
	var rule:=ColorRect.new(); rule.color=Kit.PALE; rule.position=Vector2(52,746); rule.size=Vector2(1,63); rule.mouse_filter=Control.MOUSE_FILTER_IGNORE; hud.add_child(rule)
	objective_caption=label("初来乍到",15,Kit.MUTED); objective_caption.position=Vector2(70,741); objective_caption.size=Vector2(500,26); hud.add_child(objective_caption)
	objective=label("",20); objective.position=Vector2(70,775); objective.size=Vector2(660,54); hud.add_child(objective)
	hint=label("",18); hint.position=Vector2(820,775); hint.size=Vector2(700,52); hint.horizontal_alignment=HORIZONTAL_ALIGNMENT_RIGHT; hud.add_child(hint)
	var keys:=HBoxContainer.new(); keys.name="HUDKeys"; keys.position=Vector2(52,846); keys.add_theme_constant_override("separation",12); hud.add_child(keys)
	for data in [["WASD","移动"],["E","交互"],["T","日程"]]:
		var cap:=label(data[0],12); cap.autowrap_mode=TextServer.AUTOWRAP_OFF; cap.add_theme_font_override("font",Kit.font(500,true))
		var cap_style:=Kit.style(Color(0.12,0.20,0.30,0.45),Kit.LINE,2); cap_style.content_margin_left=7; cap_style.content_margin_right=7; cap_style.content_margin_top=3; cap_style.content_margin_bottom=3
		cap.add_theme_stylebox_override("normal",cap_style); keys.add_child(cap)
		var caption:=label(data[1],15,Kit.MUTED); caption.autowrap_mode=TextServer.AUTOWRAP_OFF; keys.add_child(caption)
		var gap:=Control.new(); gap.custom_minimum_size.x=10; keys.add_child(gap)
	var help:=label("F5 保存   /   Esc 菜单",15,Kit.MUTED); help.name="HUDHelp"; help.position=Vector2(1170,846); help.size=Vector2(378,32); help.horizontal_alignment=HORIZONTAL_ALIGNMENT_RIGHT; hud.add_child(help)

func start_menu() -> void:
	presentation.title()

func new_game() -> void:
	model.new_game(); started=true
	world.change_place("island",world.default_pos("island"))
	model.data.position=[world.hero.position.x,world.hero.position.y]
	modal.hide(); hud.show(); save_game(AUTO,false); refresh()
	show_arrival()

func show_arrival() -> void:
	var v:=panel("第一天 · 到岛上去","07:00 / 21 天倒计时")
	v.add_child(label("你给自己的直播昵称起名为「听潮」。\n先去码头登记亭找许舟报到，了解工作和今晚的节目。",22))
	v.add_child(label("靠近人物或入口时，按 E 交互。\nM 查看全岛地图；I 查看背包。",18))
	v.add_child(button("开始探索",close_modal,true))

func refresh() -> void:
	if not started: return
	time_text.text=model.clock()
	place_text.text="第 1 天   ·   "+place_name()
	next_text.text="下一日程   "+("12:00 午间" if model.data.time<720 else ("14:00 下午自由活动" if model.data.time<840 else ("20:00 Z 的直播" if model.data.time<1200 else "今晚，听见小岛")))
	gold_text.text=str(model.data.gold)
	var f:Dictionary=model.data.flags
	if not f.get("checked_in",false): objective.text="初来乍到\n去登记亭找许舟报到"
	elif not f.get("stage_done",false): objective.text="认识小岛的人\n前往道具间，向陈叔询问场务工作"
	else: objective.text="给今晚准备一点心意\n购物、试做作品，或听听码头的铃声"
	var pending:Dictionary=model.data.get("pending",{})
	world.is_stage=not pending.is_empty() and pending.action=="stage_work"
	world.carrying=model.data.get("activity",{}).get("carrying",false)
	world.crate_placed=model.data.get("activity",{}).get("placed",false)
	if world.is_stage:
		objective.text="场务交接 · 活动进行中\n"+("箱子已放好，回到陈叔身边核对清单" if world.crate_placed else "将灰蓝场务箱搬到黄色标记内 · E 拿起 / 放下")
	elif not pending.is_empty(): objective.text="当班进行中\n按 E 继续完成手头的工作"
	var objective_lines:PackedStringArray=objective.text.split("\n")
	objective_caption.text=objective_lines[0]
	if objective_lines.size()>1: objective.text=objective_lines[1]
	world.visible=model.data.phase=="day"
	world_display.visible=world.visible
	hud.visible=model.data.phase=="day"
	if model.data.phase=="live" or model.data.phase=="review": show_night()
	elif model.data.phase=="done": show_summary()
	world.frozen=modal.visible or model.data.phase!="day"

func place_name() -> String:
	return {"island":"码头小街","cafe":"咖啡馆","props":"道具间"}.get(model.data.location,"码头小街")

func _process(delta: float) -> void:
	world.frozen=not started or modal.visible or model.data.phase!="day"
	hud.get_node("HUDKeys").visible=not modal.visible
	hud.get_node("HUDHelp").visible=not modal.visible
	if is_instance_valid(hint): hint.text="[ E ]  "+world.near_title() if world.nearby!="" else ""
	if world.is_stage: hint.text=stage_hint()
	elif started and not model.data.get("pending",{}).is_empty(): hint.text="[ E ]  继续手头的工作"
	if toast_timer>0:
		toast_timer-=delta
		toast_label.visible=toast_timer>0

func _unhandled_key_input(event: InputEvent) -> void:
	if not event is InputEventKey or not event.pressed or event.echo: return
	var k:int=event.physical_keycode
	if modal.visible and modal_kind=="dialogue" and k>=KEY_1 and k<=KEY_9:
		var index:int=k-KEY_1
		if index<presentation.choice_buttons.size():
			var option:Button=presentation.choice_buttons[index]
			if is_instance_valid(option) and not option.disabled: option.pressed.emit()
		get_viewport().set_input_as_handled()
		return
	if k==KEY_ESCAPE:
		if modal.visible: close_modal()
		elif started: pause_menu()
	elif k==KEY_F5 and started: save_game(MANUAL)
	elif k==KEY_F9 and started: load_game(MANUAL)
	elif model.data.phase=="day" and started:
		if k==KEY_F3:
			# A standalone SubViewport has no automatic input propagation.
			world._unhandled_key_input(event)
		elif k==KEY_M:
			if modal_kind=="map": close_modal()
			else: show_map()
		elif k==KEY_I:
			if modal_kind=="inventory": close_modal()
			else: show_inventory()
		elif k==KEY_J:
			if modal_kind=="journal": close_modal()
			else: show_journal()
		elif k==KEY_T: show_schedule()
		elif k==KEY_E and not modal.visible:
			if not model.data.get("pending",{}).is_empty(): resume_activity()
			else: world.interact()

func interact(id: String) -> void:
	match id:
		"cafe": enter_place("cafe")
		"props": enter_place("props")
		"exit": enter_place("island")
		"xu": talk_xu()
		"lin": talk_lin()
		"chen": talk_chen()
		"lu": talk_lu()
		"dorm": show_schedule()
		"bell":
			var v:=panel("海风里的节拍","码头 · 公开观察")
			v.add_child(label("一阵海风过来，绳索轻撞桅杆。登记亭旁的铃响了两下，随后是船靠岸的水声。\n今晚 Z 的节目主题正是「听见小岛」。",22))
			v.add_child(button("记下这段声音 · 不耗时",func():do_action("observe_bell"),true))
		"gate":
			var v:=panel("需要维护工作许可","灯塔 · 维护区")
			v.add_child(label("临时场务证不包含这里的权限。\n公共步道可以自由通行，维护区暂不开放。",22))
			v.add_child(button("返回公共步道",close_modal,true))

func enter_place(id: String) -> void:
	if not model.data.get("pending",{}).is_empty(): toast("先完成手头的工作，再离开这里。"); return
	var pos:Vector2=world.default_pos(id)
	if id=="island": pos=world.return_pos(model.data.location)
	world.change_place(id,pos)
	model.data.location=id; model.data.position=[world.hero.position.x,world.hero.position.y]
	modal.hide(); refresh(); save_game(AUTO,false)

func talk_xu() -> void:
	presentation.talk_xu()

func talk_lin() -> void:
	presentation.talk_lin()

func talk_chen() -> void:
	presentation.talk_chen()

func talk_lu() -> void:
	presentation.talk_lu()

func show_shop() -> void:
	presentation.shop()

func action_button(v: VBoxContainer, action: String, payload: Dictionary={}, caption: String="") -> void:
	var q:Dictionary=model.preview(action,payload)
	var b:=button(caption if caption!="" else q.get("label",action),func():confirm_action(action,payload),q.ok)
	b.disabled=not q.ok
	v.add_child(b)
	if not q.ok: v.add_child(label(q.reason,16,Color("b6a993")))

func confirm_action(action: String, payload: Dictionary={}) -> void:
	presentation.confirmation(action,payload)

func do_action(action: String, payload: Dictionary={}) -> Dictionary:
	var request_id: String="ui-%d-%d" %[Time.get_ticks_usec(),randi()]
	var result:Dictionary=model.apply(action,payload,request_id)
	if result.ok:
		modal.hide(); save_game(AUTO,false); refresh()
	else: toast(result.reason)
	if result.ok: toast(result.get("message","已完成"))
	if is_instance_valid(night): night.refresh()
	return result

func begin_activity(action: String,payload: Dictionary) -> void:
	var result:Dictionary=model.begin_action(action,payload,"activity-%d"%Time.get_ticks_usec())
	if not result.ok: toast(result.reason); return
	model.data.activity={"step":0,"carrying":false,"placed":false,"recipe":0,"correct":true}
	save_game(AUTO,false); modal.hide(); refresh()
	if action!="stage_work": show_activity()
	else: toast("靠近箱子按 E 拿起，搬到黄色标记内。")

func resume_activity() -> void:
	if model.data.pending.action!="stage_work": show_activity(); return
	var a:Dictionary=model.data.activity
	if a.get("placed",false):
		if world.can_interact_with("chen",world.hero.position): show_stage_check()
		else: toast("回到陈叔身边，核对交接清单。")
	elif a.get("carrying",false):
		if world.can_interact_with("target",world.hero.position):
			model.data.activity.carrying=false; model.data.activity.placed=true
			toast("箱子已放到标记里。回去找陈叔核对。")
		else: toast("站到黄色标记旁，把箱子放进框里。")
	elif world.can_interact_with("crate",world.hero.position):
		model.data.activity.carrying=true; toast("已拿起场务箱。")
	else: toast("先靠近灰蓝色场务箱，再按 E。")
	save_game(AUTO,false); refresh()

func stage_hint() -> String:
	var target := "chen" if world.crate_placed else ("target" if world.carrying else "crate")
	if not world.can_interact_with(target,world.hero.position): return ""
	return "[ E ]  " + {"chen":"核对场务交接","target":"放下场务箱","crate":"拿起场务箱"}[target]

func show_stage_check() -> void:
	var v:=panel("核对交接清单","陈叔 · 清单 A-03")
	v.add_child(label("箱号 A-03\n位置：黄色边框内\n内容：灯具配件\n\n陈叔：放好了？最后这一项，一起确认一下。",22))
	v.add_child(button("核对箱号和位置，交接灯具配件",func():complete_activity({"correct":true}),true))
	v.add_child(button("没核对清单，直接说交接完成",func():complete_activity({"correct":false})))

func show_activity() -> void:
	presentation.activity()

func activity_step() -> void:
	model.data.activity.step=int(model.data.activity.get("step",0))+1
	save_game(AUTO,false)
	if model.data.activity.step>=3: complete_activity({"correct":true})
	else: show_activity()

func complete_activity(payload: Dictionary) -> void:
	var pending:Dictionary=model.data.pending.duplicate(true)
	var r:Dictionary=model.apply(pending.action,payload,pending.request_id)
	if r.ok:
		modal.hide(); save_game(AUTO,false); refresh(); toast(r.get("message","活动完成"))
	else: toast(r.reason)

func show_schedule() -> void:
	if not started or model.data.phase!="day": return
	presentation.schedule()

func show_map() -> void:
	if not started or model.data.phase!="day": return
	var v:=panel(place_name()+" · 地图","M / Esc 返回 · 查看地图不耗时",true)
	var row:=HBoxContainer.new(); row.add_theme_constant_override("separation",25); v.add_child(row)
	var canvas:=SpatialMap.new(); canvas.name="SpatialMap"; canvas.custom_minimum_size=Vector2(890,495); row.add_child(canvas)
	canvas.snapshot=world.map_snapshot(); canvas.target_id=map_target_id(); canvas.map_font=font
	canvas.mouse_filter=Control.MOUSE_FILTER_IGNORE
	canvas.resized.connect(canvas.queue_redraw)
	var info:=VBoxContainer.new(); info.custom_minimum_size=Vector2(320,0); row.add_child(info)
	info.add_child(label("当前位置",17,Color("8fa7c1"))); info.add_child(label(place_name(),29))
	info.add_child(label("●  你的位置",19,Color("7bebdc")))
	info.add_child(label("◎  当前目标",19,Color("edca76")))
	info.add_child(label(objective.text,21))
	info.add_child(label("浅色区域可通行，深色轮廓为建筑和物体。\n靠近人物或门口，出现 E 提示后交互。",18,Color("a0b8d1")))
	info.add_child(label("门口连接室内与码头小街。\n地图不提供传送。",17,Color("8fa7c1")))
	modal_kind="map"

func map_target_id() -> String:
	if world.is_stage:
		return "chen" if world.crate_placed else ("target" if world.carrying else "crate")
	if not model.data.flags.get("checked_in",false): return "xu" if world.place=="island" else "exit"
	if not model.data.flags.get("stage_done",false):
		return "chen" if world.place=="props" else ("props" if world.place=="island" else "exit")
	if world.place=="cafe": return "lin"
	if world.place=="props": return "lu"
	return "bell" if not model.data.flags.get("bell_observed",false) else "dorm"

func show_inventory() -> void:
	if not started or model.data.phase!="day": return
	presentation.inventory()

func show_journal() -> void:
	presentation.journal()

func pause_menu() -> void:
	if not started: return
	var v:=panel("暂停","阅读、菜单与保存不推进游戏时间")
	v.add_child(button("继续游戏",close_modal,true))
	v.add_child(button("保存到手动槽 · F5",func():save_game(MANUAL)))
	var b:=button("读取手动槽 · F9",func():load_game(MANUAL)); b.disabled=not FileAccess.file_exists(MANUAL); v.add_child(b)
	v.add_child(button("读取最近自动保存",func():load_game(AUTO)))
	v.add_child(button("重新开始这一天",func():
		var p:=panel("重新开始？","当前自动槽会保存新的一天，手动槽保留。")
		p.add_child(button("开始新的一天",func():
			if is_instance_valid(night): night.queue_free(); night=null
			new_game(),true))
		p.add_child(button("返回",pause_menu))))
	modal_kind="pause"

func save_game(path: String, announce: bool=true) -> void:
	if not started: return
	model.data.location=world.place
	model.data.position=[world.hero.position.x,world.hero.position.y]
	var ok:bool=model.save_to(path)
	if announce: toast("进度已保存。" if ok else "保存失败，请保留当前游戏。")

func load_game(path: String) -> void:
	if not model.load_from(path): toast("无法读取这个存档；当前进度未改变。"); return
	started=true; modal.hide()
	if is_instance_valid(night): night.queue_free(); night=null
	var p:Array=model.data.position
	world.change_place(model.data.location,Vector2(p[0],p[1]))
	model.data.position=[world.hero.position.x,world.hero.position.y]
	refresh()
	if not model.data.get("pending",{}).is_empty() and model.data.pending.action!="stage_work": show_activity()
	toast(model.last_notice if model.last_notice!="" else "已读取进度。")

func show_night() -> void:
	modal.hide()
	if not is_instance_valid(night):
		night=Night.new(); ui.add_child(night); night.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
		night.action_requested.connect(func(action: String,payload: Dictionary):do_action(action,payload))
		night.exit_requested.connect(pause_menu)
		night.setup(model)
		night.z_index=2
		modal.z_index=5; toast_label.z_index=9
	night.refresh()

func show_summary() -> void:
	presentation.summary()

func toast(message: String) -> void:
	toast_label.text=message
	toast_label.visible=true
	toast_timer=4.0
