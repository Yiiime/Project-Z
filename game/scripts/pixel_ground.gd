extends Node2D
## Static art only. Every coast edge uses the same polygon as the walking boundary.
var bounds := Rect2(0, 0, 1672, 941)
var regions: Array = []
var textures: Array[Texture2D] = []
var place := "island"
const TILE := 32

func configure(scene_id: String, scene_bounds: Rect2, walkable: Array, tiles: Array[Texture2D]) -> void:
	place = scene_id
	bounds = scene_bounds
	regions = walkable
	textures = tiles
	texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	queue_redraw()

func _rect_polygon(rect: Rect2) -> PackedVector2Array:
	return PackedVector2Array([rect.position, Vector2(rect.end.x, rect.position.y), rect.end, Vector2(rect.position.x, rect.end.y)])

func _polygon_bounds(polygon: PackedVector2Array) -> Rect2:
	var result := Rect2(polygon[0], Vector2.ZERO)
	for point in polygon: result = result.expand(point)
	return result

func _paint_surface(polygon: PackedVector2Array, index: int, base: Color, detail: float, span: int = 64) -> void:
	if polygon.size() < 3: return
	draw_colored_polygon(polygon, base)
	if index >= textures.size() or textures[index] == null: return
	var source: Texture2D = textures[index]
	var source_region := Rect2(Vector2.ZERO, source.get_size())
	if source is AtlasTexture:
		source_region = source.region
		source = source.atlas
	# Original texture over quiet ground color: no PNG is altered.
	var extent := _polygon_bounds(polygon)
	for y in range(int(floor(extent.position.y / span)) * span, int(ceil(extent.end.y)), span):
		for x in range(int(floor(extent.position.x / span)) * span, int(ceil(extent.end.x)), span):
			var tile_rect := Rect2(x, y, span, span)
			for clipped in Geometry2D.intersect_polygons(polygon, _rect_polygon(tile_rect)):
				var uv := PackedVector2Array()
				for point in clipped:
					var local: Vector2 = (point - tile_rect.position) / float(span)
					uv.append((source_region.position + local * source_region.size) / source.get_size())
				draw_colored_polygon(clipped, Color(1, 1, 1, detail), uv, source)

func _land_intersections(rect: Rect2) -> Array[PackedVector2Array]:
	var result: Array[PackedVector2Array] = []
	for polygon in regions:
		for clipped in Geometry2D.intersect_polygons(polygon, _rect_polygon(rect)):
			result.append(clipped)
	return result

func _draw() -> void:
	if place == "island": _draw_island()
	else: _draw_room()

func _draw_island() -> void:
	_paint_surface(_rect_polygon(bounds), 4, Color("326675"), 0.26, 96)
	# Quay thickness goes outward from legal land, never into its walk area.
	for polygon in regions:
		for outer in Geometry2D.offset_polygon(polygon, 11.0): draw_colored_polygon(outer, Color("254e5a"))
		for outer in Geometry2D.offset_polygon(polygon, 8.0): draw_colored_polygon(outer, Color("616b63"))
		_paint_surface(polygon, 0, Color("718451"), 0.27, 64)
	var paths: Array[Rect2] = [Rect2(140, 348, 1440, 80), Rect2(700, 528, 875, 80), Rect2(976, 110, 96, 530), Rect2(1416, 180, 88, 460), Rect2(95, 325, 465, 80)]
	for path in paths:
		for polygon in _land_intersections(path.grow(2.0)): draw_colored_polygon(polygon, Color("626f4c"))
	for path in paths:
		for polygon in _land_intersections(path): _paint_surface(polygon, 1, Color("a7a38b"), 0.44, 64)
	for polygon in _land_intersections(Rect2(bounds.position.x, 640, bounds.size.x, bounds.end.y - 640)):
		_paint_surface(polygon, 2, Color("8d7558"), 0.38, 64)
	for polygon in regions:
		var closed: PackedVector2Array = polygon.duplicate()
		closed.append(polygon[0])
		draw_polyline(closed, Color("aaa78d"), 2.0)
		for outer in Geometry2D.offset_polygon(polygon, 10.0):
			var waterline: PackedVector2Array = outer.duplicate()
			waterline.append(outer[0])
			draw_polyline(waterline, Color(0.58, 0.77, 0.75, 0.5), 1.0)

func _draw_room() -> void:
	var warm := place == "cafe"
	var floor_color := Color("9a805e") if warm else Color("777d78")
	draw_rect(bounds, Color("283a3d"))
	for polygon in regions:
		_paint_surface(polygon, 7 if warm else 8, floor_color, 0.32 if warm else 0.22, 64)
	var north := bounds.position.y + 8.0
	var south := bounds.end.y - 8.0
	var left := bounds.position.x + 8.0
	var right := bounds.end.x - 8.0
	var plaster := Color("b3b29c") if warm else Color("929f9b")
	var panel := Color("496862") if warm else Color("4b6064")
	# Back wall: visible face, top ledge, low dado and a floor seam.
	draw_rect(Rect2(bounds.position.x, north - 52, bounds.size.x, 52), plaster)
	draw_rect(Rect2(bounds.position.x, north - 52, bounds.size.x, 3), Color("d3c9ae") if warm else Color("b1bfba"))
	draw_rect(Rect2(bounds.position.x, north - 16, bounds.size.x, 16), panel)
	draw_rect(Rect2(bounds.position.x, north - 17, bounds.size.x, 2), Color("708b7a") if warm else Color("7e9390"))
	for x in range(int(left + 24), int(right), 48):
		draw_line(Vector2(x, north - 14), Vector2(x, north - 2), Color(0.12, 0.23, 0.23, 0.27), 1)
	for center_x in [left + 80.0, left + 258.0, right - 82.0]: _draw_window(Vector2(center_x, north - 35), warm)
	# Side walls remain entirely outside the existing walk rectangle.
	draw_rect(Rect2(bounds.position.x, north - 49, 8, south - north + 57), panel.darkened(0.12))
	draw_rect(Rect2(right, north - 49, 8, south - north + 57), panel.darkened(0.24))
	draw_line(Vector2(left - 1, north), Vector2(left - 1, south), Color("9aa68a"), 1)
	draw_line(Vector2(right, north), Vector2(right, south), Color("3c514e"), 1)
	# Floor contact shadows do not introduce new blocking.
	draw_rect(Rect2(left, north, right - left, 3), Color(0.10, 0.16, 0.15, 0.22))
	draw_rect(Rect2(left, north + 3, right - left, 5), Color(0.10, 0.16, 0.15, 0.08))
	draw_rect(Rect2(left, north, 3, south - north), Color(0.10, 0.16, 0.15, 0.13))
	draw_rect(Rect2(right - 3, north, 3, south - north), Color(0.10, 0.16, 0.15, 0.13))
	var exit_x := 720.0 if warm else 775.0
	for edge in [Rect2(bounds.position.x, south, exit_x - 26.0 - bounds.position.x, 8), Rect2(exit_x + 26.0, south, bounds.end.x - exit_x - 26.0, 8)]:
		draw_rect(edge, panel.darkened(0.32))
		draw_rect(Rect2(edge.position, Vector2(edge.size.x, 2)), Color("adb08f") if warm else Color("a0aaa0"))
	# Independent door and threshold sprites are supplied by world.gd at this gap.
	draw_rect(Rect2(exit_x - 26.0, south, 52, 8), floor_color.darkened(0.12))

func _draw_window(center: Vector2, warm: bool) -> void:
	var frame := Rect2(center - Vector2(25, 13), Vector2(50, 26))
	draw_rect(frame.grow(2), Color("62694f") if warm else Color("526b6c"))
	draw_rect(frame, Color("a8c4bb") if warm else Color("8faeb5"))
	draw_rect(Rect2(frame.position + Vector2(2, 2), Vector2(21, 9)), Color("cbddd0") if warm else Color("b2c8c6"))
	draw_line(Vector2(center.x, frame.position.y), Vector2(center.x, frame.end.y), Color("727e67"), 2)
	draw_line(Vector2(frame.position.x, center.y), Vector2(frame.end.x, center.y), Color("727e67"), 2)
	draw_rect(Rect2(frame.position.x - 3, frame.end.y, 56, 3), Color("c0b89b") if warm else Color("a1b2a9"))
