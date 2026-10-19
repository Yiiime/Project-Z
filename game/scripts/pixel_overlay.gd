extends Node2D
var owner_world: Node2D
var debug_mode := false

func _draw() -> void:
	if not is_instance_valid(owner_world): return
	var w = owner_world
	if debug_mode:
		_draw_interaction_key(w)
		if not w.debug_visible: return
		for polygon in w.walk_regions:
			var outline: PackedVector2Array = polygon.duplicate()
			outline.append(outline[0])
			draw_polyline(outline, Color(0.2, 0.9, 1.0, 0.8), 1.0)
		for object in w.objects:
			var rect: Rect2 = object.footprint
			draw_rect(rect, Color(1.0, 0.32, 0.24, 0.28))
			draw_rect(rect, Color(1.0, 0.42, 0.3, 0.9), false, 0.8)
			draw_circle(object.position, 2.0, Color.WHITE)
		for spot in w.map_snapshot().spots:
			draw_arc(spot.position, float(spot.get("radius", 42.0)), 0.0, TAU, 40, Color(0.45, 1.0, 0.6, 0.7), 0.7)
			var normal: Vector2 = spot.get("normal", Vector2.ZERO)
			draw_line(spot.position, spot.position + normal * 22.0, Color.YELLOW, 1.5)
		draw_rect(Rect2(w.hero.position - Vector2(6, 4), Vector2(12, 8)), Color.YELLOW, false, 1.0)
		return
	# Ground shadows do not inherit the alpha of independently fading objects.
	for object in w.objects:
		var rect: Rect2 = object.footprint
		_draw_contact_shadow(rect)
	if w.place == "props" and w.is_stage:
		var target: Vector2 = w.stage_anchor("target")
		draw_rect(Rect2(target - Vector2(17, 11), Vector2(34, 22)), Color(0.93, 0.79, 0.38, 0.16))
		draw_rect(Rect2(target - Vector2(17, 11), Vector2(34, 22)), Color("e5c866"), false, 1.0)
		if not w.carrying:
			var crate_at: Vector2 = target if w.crate_placed else w.stage_anchor("crate")
			_draw_contact_shadow(Rect2(crate_at - Vector2(10, 5), Vector2(20, 10)))
	if not w.nearby.is_empty():
		draw_arc(w.spot_position(w.nearby), 8.0, 0.0, TAU, 24, Color("ecdbaa"), 1.0)
	_draw_contact_shadow(Rect2(w.hero.position - Vector2(6, 3), Vector2(12, 6)))

func _draw_contact_shadow(footprint: Rect2) -> void:
	if not footprint.has_area(): return
	var center := footprint.get_center() + Vector2(2, 2)
	var width := maxf(footprint.size.x + 5.0, 13.0)
	var height := maxf(footprint.size.y + 4.0, 6.0)
	draw_set_transform(center, 0.0, Vector2(1.0, height / width))
	draw_circle(Vector2.ZERO, width / 2.0 + 2, Color(0.06, 0.12, 0.12, 0.08))
	draw_circle(Vector2.ZERO, width / 2.0, Color(0.06, 0.12, 0.12, 0.13))
	draw_set_transform(Vector2.ZERO)

func _draw_interaction_key(w: Node2D) -> void:
	var id: String = w.nearby
	if id.is_empty() or not w.can_interact_with(id, w.hero.position): return
	var center: Vector2 = (w.spot_position(id) - Vector2(0, 44)).round()
	if id == "lin": center.x += 20.0
	for object in w.objects:
		if id == "lin" or str(object.id) != id + "_npc": continue
		# A service spot is in front of its NPC; use a head-side callout instead.
		var feet: Vector2 = object.position
		center = (feet + Vector2(23, -43)).round()
		var canvas := get_global_transform_with_canvas()
		var screen: Vector2 = canvas * center
		var safe_top := get_viewport_rect().size.y * (122.0 / 900.0) + 12.0
		if screen.y < safe_top:
			# Keep the side callout below the HUD without moving it onto the face.
			screen.y = safe_top
			center = (canvas.affine_inverse() * screen).round()
		break
	var box := Rect2(center - Vector2(9, 9), Vector2(18, 18))
	draw_rect(Rect2(box.position + Vector2(1, 2), box.size), Color(0.05, 0.10, 0.13, 0.4))
	draw_rect(box, Color("20383f"))
	draw_rect(box, Color("e6d5a8"), false, 1.0)
	draw_colored_polygon(PackedVector2Array([center + Vector2(-3, 9), center + Vector2(3, 9), center + Vector2(0, 12)]), Color("e6d5a8"))
	var font := ThemeDB.fallback_font
	var letter_width := font.get_string_size("E", HORIZONTAL_ALIGNMENT_LEFT, -1, 11).x
	draw_string(font, Vector2(roundf(center.x - letter_width / 2.0), center.y + 4), "E", HORIZONTAL_ALIGNMENT_LEFT, -1, 11, Color("fff0c5"))
