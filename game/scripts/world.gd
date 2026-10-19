extends Node2D
## Pixel spatial prototype. Project tuning values, not measured original-game parameters.
signal interaction_requested(id: String)
signal moved(pos: Vector2)
const GroundScript = preload("res://scripts/pixel_ground.gd")
const OverlayScript = preload("res://scripts/pixel_overlay.gd")
const SPEED := 110.0
const CHARACTER_HEIGHT := 40.0
const INTERACTION_RADIUS := 42.0
const FOOT_HALF := Vector2(6,4)
const ASSETS := "res://assets/pixel-v1/"
var place := "island"
var hero := CharacterBody2D.new()
var hero_image := Sprite2D.new()
var camera := Camera2D.new()
var background: Node2D
var depth_sorted := Node2D.new()
var ground_overlay: Node2D
var foreground := Node2D.new()
var debug_overlay: Node2D
var boundary_body := StaticBody2D.new()
var spots: Array[Dictionary] = []
var walk_regions: Array[PackedVector2Array] = []
var blocks: Array[Rect2] = []
var objects: Array[Dictionary] = []
var npcs: Array[Node2D] = []
var frozen := false
var nearby := ""
var carrying := false
var crate_placed := false
var is_stage := false
var debug_visible := false
var clock := 0.0
var facing := 0
var hero_atlas: Texture2D
var texture_cache := {}
var scene_bounds := Rect2(0,0,1672,941)
var _frames := {}
var _atlas_metadata: Dictionary = {}
var _visuals: Array[Dictionary] = []
var _stage_node: Node2D
var _stage_collision: CollisionShape2D
var _stage_sprite: Sprite2D
var _last_character_frame := -1

func _ready() -> void:
	texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	if FileAccess.file_exists(ASSETS+"atlas-regions.json"):
		var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(ASSETS+"atlas-regions.json"))
		if parsed is Dictionary: _atlas_metadata = parsed.get("sheets",{})
	background = GroundScript.new(); background.name = "Ground"; background.z_index = -100; add_child(background)
	ground_overlay = OverlayScript.new(); ground_overlay.name = "GroundOverlay"; ground_overlay.owner_world = self; ground_overlay.z_index = -50; add_child(ground_overlay)
	depth_sorted.name = "DepthSorted"; depth_sorted.y_sort_enabled = true; add_child(depth_sorted)
	foreground.name = "Foreground"; foreground.z_index = 20; add_child(foreground)
	boundary_body.name = "SceneBoundary"; add_child(boundary_body)
	hero.name = "Player"; hero.motion_mode = CharacterBody2D.MOTION_MODE_FLOATING; hero.safe_margin = 0.05
	hero.collision_layer = 1; hero.collision_mask = 1; depth_sorted.add_child(hero); hero.add_child(hero_image)
	var foot := CollisionShape2D.new(); var shape := RectangleShape2D.new(); shape.size = FOOT_HALF*2.0; foot.shape = shape; hero.add_child(foot)
	hero.add_child(camera); camera.zoom = get_viewport_rect().size/Vector2(640,360); camera.position_smoothing_enabled = true; camera.position_smoothing_speed = 7.0
	debug_overlay = OverlayScript.new(); debug_overlay.name = "DebugOverlay"; debug_overlay.owner_world = self; debug_overlay.debug_mode = true; debug_overlay.z_index = 50; add_child(debug_overlay)
	hero_atlas = tex(ASSETS+"player.png")
	change_place("island",default_pos("island"))

func tex(path: String) -> Texture2D:
	if not texture_cache.has(path) and ResourceLoader.exists(path): texture_cache[path] = load(path)
	return texture_cache.get(path)

func _frame(sheet: String,index: int,columns: int = 3,rows: int = 3,crop_alpha: bool = true) -> Texture2D:
	var key := "%s:%d:%s" % [sheet,index,crop_alpha]
	if _frames.has(key): return _frames[key]
	var source := tex(ASSETS+sheet+".png")
	if source == null: return null
	var cell := Vector2i(source.get_width()/columns,source.get_height()/rows)
	var region := Rect2i(Vector2i(index%columns,index/columns)*cell,cell)
	var measured: Dictionary = _atlas_metadata.get(sheet,{})
	if measured.has("regions") and index < measured.regions.size():
		var coords: Array = measured.regions[index]
		region = Rect2i(int(coords[0]),int(coords[1]),int(coords[2]),int(coords[3]))
	elif crop_alpha:
		var used: Rect2i = source.get_image().get_region(region).get_used_rect()
		if used.has_area(): region = Rect2i(region.position+used.position,used.size)
	var result := AtlasTexture.new(); result.atlas = source; result.region = Rect2(region); _frames[key] = result
	return result

func _set_art(sprite: Sprite2D,texture: Texture2D,height: float,max_width: float = 0.0) -> void:
	sprite.texture = texture; sprite.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	if texture == null: return
	var factor := height/texture.get_height()
	if max_width > 0: factor = minf(factor,max_width/texture.get_width())
	sprite.scale = Vector2.ONE*factor
	# Alpha-trimmed art uses its bottom centre as the foot/ground anchor.
	sprite.offset = Vector2(0,-texture.get_height()/2.0)

func _set_actor(sprite: Sprite2D,sheet: String,frame_index: int,columns: int,rows: int) -> void:
	var texture := _frame(sheet,frame_index,columns,rows)
	_set_art(sprite,texture,CHARACTER_HEIGHT)
	if texture == null: return
	var measured: Dictionary = _atlas_metadata.get(sheet,{})
	if measured.has("visible_height"):
		sprite.scale = Vector2.ONE*(CHARACTER_HEIGHT/float(measured.visible_height))
		var anchor := Vector2(texture.get_width()/2.0,texture.get_height()-float(measured.get("padding",0)))
		if measured.has("anchors") and frame_index < measured.anchors.size():
			var at: Array = measured.anchors[frame_index]
			anchor = Vector2(float(at[0]),float(at[1]))
		sprite.offset = Vector2(texture.get_width(),texture.get_height())/2.0-anchor

func region(points: Array) -> void:
	var poly := PackedVector2Array()
	for p in points: poly.append(Vector2(p[0],p[1]))
	walk_regions.append(poly)

func change_place(id: String,pos: Vector2) -> void:
	place = id if id in ["island","cafe","props"] else "island"
	for child in depth_sorted.get_children():
		if child != hero: depth_sorted.remove_child(child); child.queue_free()
	for child in boundary_body.get_children(): boundary_body.remove_child(child); child.queue_free()
	npcs.clear(); walk_regions.clear(); blocks.clear(); objects.clear(); spots.clear(); _visuals.clear(); nearby = ""; is_stage = false
	if place == "island": _build_island()
	elif place == "cafe": _build_cafe()
	else: _build_props()
	_build_boundaries(); _build_stage_crate()
	var tiles: Array[Texture2D] = []
	for i in range(9): tiles.append(_frame("terrain",i,3,3,false))
	background.configure(place,scene_bounds,walk_regions,tiles)
	hero.position = pos if can_stand(pos) else default_pos(place); hero.velocity = Vector2.ZERO
	var view_size := get_viewport_rect().size/camera.zoom
	var camera_bounds := scene_bounds
	if place != "island":
		camera_bounds.position.y -= 40.0
		camera_bounds.size.y += 40.0
		var spare := Vector2(maxf(0,view_size.x-camera_bounds.size.x),maxf(0,view_size.y-camera_bounds.size.y))
		camera_bounds.position -= spare/2.0
		camera_bounds.size += spare
	camera.limit_left = int(camera_bounds.position.x); camera.limit_top = int(camera_bounds.position.y)
	camera.limit_right = int(camera_bounds.end.x); camera.limit_bottom = int(camera_bounds.end.y); camera.reset_smoothing()
	_last_character_frame = -1; _update_character(Vector2.ZERO)
	ground_overlay.queue_redraw(); debug_overlay.queue_redraw()

func _build_island() -> void:
	scene_bounds = Rect2(0,0,1672,941)
	region([[95,95],[1640,95],[1640,725],[1540,725],[1540,865],[720,865],[720,705],[625,605],[95,605]])
	_add_object("cafe_building","咖啡馆","outdoor",0,Vector2(430,345),Vector2(190,160),Rect2(-78,-58,156,58),true)
	_add_object("props_building","道具间","outdoor",1,Vector2(1470,402),Vector2(215,165),Rect2(-91,-68,182,68),true)
	_add_object("dorm_building","宿舍","outdoor",2,Vector2(1045,145),Vector2(170,125),Rect2(-70,-50,140,50),true)
	_add_object("lighthouse","灯塔","outdoor",3,Vector2(1570,215),Vector2(94,160),Rect2(-29,-35,58,35),true)
	_add_object("harbor_kiosk","登记亭","outdoor",4,Vector2(888,545),Vector2(120,95),Rect2(-49,-30,98,30),true)
	for p in [Vector2(720,345),Vector2(1200,355),Vector2(1130,580),Vector2(360,535),Vector2(640,190),Vector2(1240,165),Vector2(1380,655),Vector2(180,185),Vector2(535,520),Vector2(240,430),Vector2(310,448),Vector2(565,245),Vector2(850,240),Vector2(1330,275)]:
		_add_object("tree_%d_%d" % [p.x,p.y],"树","outdoor",5,p,Vector2(88,104),Rect2(-9,-8,18,12),true)
	for p in [Vector2(695,385),Vector2(1200,515),Vector2(1460,670)]:
		_add_object("bench_%d" % int(p.x),"长凳","outdoor",6,p,Vector2(66,33),Rect2(-28,-9,56,11))
	for p in [Vector2(565,385),Vector2(1030,470),Vector2(1340,570),Vector2(1000,775)]:
		_add_object("lamp_%d" % int(p.x),"路灯","outdoor",7,p,Vector2(20,66),Rect2(-4,-4,8,7),true)
	for x in [770,834,898,1180,1244,1308]: _add_object("fence_%d" % x,"码头护栏","outdoor",8,Vector2(x,820),Vector2(60,30),Rect2(-28,-5,56,7))
	for p in [Vector2(815,746),Vector2(846,764),Vector2(1415,755),Vector2(1445,778)]:
		_add_object("cargo_%d_%d" % [p.x,p.y],"码头货箱","indoor",5,p,Vector2(31,27),Rect2(-12,-11,24,13))
	_add_npc("xu_npc","许舟",0,Vector2(945,545))
	spots = [_spot("xu","许舟 · 码头问询",Vector2(945,568),Vector2.DOWN),_spot("cafe","进入咖啡馆",Vector2(450,367),Vector2.DOWN),_spot("props","进入道具间",Vector2(1470,435),Vector2.DOWN),_spot("dorm","宿舍 · 日程与直播",Vector2(1045,180),Vector2.DOWN),_spot("bell","听一听码头铃声",Vector2(1260,706)),_spot("gate","灯塔维护区",Vector2(1570,249),Vector2.DOWN)]

func _build_cafe() -> void:
	scene_bounds = Rect2(615,435,550,325); region([[623,443],[1157,443],[1157,752],[623,752]])
	_add_object("cafe_counter","咖啡柜台","indoor",0,Vector2(792,528),Vector2(166,75),Rect2(-77,-29,154,29))
	# Each dining sprite already contains its four chairs; its whole contact area is solid.
	_add_object("cafe_table_1","餐桌与四椅","indoor",1,Vector2(972,635),Vector2(88,60),Rect2(-40,-43,80,45))
	_add_object("cafe_table_2","餐桌与四椅","indoor",1,Vector2(1080,700),Vector2(88,60),Rect2(-40,-43,80,45))
	_add_object("cafe_table_3","餐桌与四椅","indoor",1,Vector2(717,646),Vector2(88,60),Rect2(-40,-43,80,45))
	_add_object("cafe_shelf","咖啡与杯子","indoor",4,Vector2(1080,487),Vector2(104,85),Rect2(-43,-17,86,17))
	_add_object("cafe_shelf_left","储物架","indoor",4,Vector2(664,485),Vector2(68,65),Rect2(-27,-13,54,13))
	_add_object("cafe_plant","盆栽","indoor",6,Vector2(650,610),Vector2(36,56),Rect2(-10,-8,20,12),true)
	_add_object("cafe_plant_right","盆栽","indoor",6,Vector2(1120,558),Vector2(36,56),Rect2(-10,-8,20,12),true)
	_add_floor_art("cafe_mat","indoor",7,Vector2(720,731),Vector2(66,27)); _add_npc("lin_npc","林乔",1,Vector2(792,490))
	_add_exit_door("cafe_door",Vector2(720,752))
	spots = [_spot("lin","林乔 · 工作与小店",Vector2(792,552),Vector2.DOWN),_spot("exit","返回码头小街",Vector2(720,725),Vector2.UP)]

func _build_props() -> void:
	scene_bounds = Rect2(530,470,560,310); region([[538,478],[1082,478],[1082,772],[538,772]])
	_add_object("props_workbench","工作台","indoor",3,Vector2(994,532),Vector2(145,58),Rect2(-62,-27,124,27))
	_add_object("props_shelf","道具架","indoor",4,Vector2(785,524),Vector2(125,85),Rect2(-52,-17,104,17))
	_add_object("props_stool","凳子","indoor",2,Vector2(560,593),Vector2(23,32),Rect2(-9,-9,18,13))
	_add_object("props_storage","库存箱","indoor",5,Vector2(1045,711),Vector2(35,32),Rect2(-14,-13,28,16))
	_add_floor_art("props_mat","indoor",7,Vector2(775,754),Vector2(66,25)); _add_npc("chen_npc","陈叔",2,Vector2(640,511)); _add_npc("lu_npc","陆岑",3,Vector2(1058,545))
	_add_exit_door("props_door",Vector2(775,772))
	spots = [_spot("chen","陈叔 · 场务交接",Vector2(640,540),Vector2.DOWN,48),_spot("lu","陆岑 · 工作台",Vector2(1058,568),Vector2.DOWN),_spot("exit","返回码头小街",Vector2(775,750),Vector2.UP)]

func _add_object(id: String,title: String,sheet: String,frame_index: int,at: Vector2,visual_size: Vector2,local_footprint: Rect2,can_fade: bool = false) -> Node2D:
	var object := Node2D.new(); object.name = id; object.position = at; depth_sorted.add_child(object)
	var sprite := Sprite2D.new(); object.add_child(sprite); _set_art(sprite,_frame(sheet,frame_index),visual_size.y,visual_size.x)
	if sprite.texture == null:
		var placeholder := Polygon2D.new(); placeholder.polygon = PackedVector2Array([Vector2(-visual_size.x/2,-visual_size.y),Vector2(visual_size.x/2,-visual_size.y),Vector2(visual_size.x/2,0),Vector2(-visual_size.x/2,0)]); placeholder.color = Color("596c62"); object.add_child(placeholder)
	var body := StaticBody2D.new(); object.add_child(body)
	var actual_size := visual_size
	if sprite.texture != null: actual_size = sprite.texture.get_size()*sprite.scale
	var proportional_footprint := Rect2(local_footprint.position*actual_size/visual_size,local_footprint.size*actual_size/visual_size)
	var collision := CollisionShape2D.new(); var shape := RectangleShape2D.new(); shape.size = proportional_footprint.size; collision.shape = shape; collision.position = proportional_footprint.get_center(); body.add_child(collision)
	var footprint := Rect2(at+proportional_footprint.position,proportional_footprint.size); blocks.append(footprint)
	objects.append({"id":id,"title":title,"position":at,"footprint":footprint,"visual_size":actual_size,"anchor":"bottom_center","interaction_side":"explicit_spot","sheet":sheet,"frame":frame_index,"node":object})
	var upper := sprite
	if can_fade:
		var fraction := 0.72
		if sheet == "outdoor":
			fraction = {0:0.54,1:0.43,2:0.36,3:0.28,4:0.43,5:0.72,7:0.78}.get(frame_index,0.65)
		upper = _split_upper_art(object,sprite,fraction)
		objects[-1]["upper_fraction"] = fraction
	_visuals.append({"node":object,"sprite":upper,"can_fade":can_fade,"height":actual_size.y,"width":actual_size.x})
	return object

func _split_upper_art(object: Node2D,base: Sprite2D,fraction: float) -> Sprite2D:
	if base.texture == null: return base
	var source := base.texture
	var width := source.get_width()
	var height := source.get_height()
	var cut := clampi(int(round(height*fraction)),1,height-1)
	var art_scale := base.scale
	var upper_texture := AtlasTexture.new(); upper_texture.atlas = source; upper_texture.region = Rect2(0,0,width,cut)
	var lower_texture := AtlasTexture.new(); lower_texture.atlas = source; lower_texture.region = Rect2(0,cut,width,height-cut)
	var upper := Sprite2D.new(); upper.name = "UpperOccluder"; upper.texture = upper_texture; upper.centered = false; upper.scale = art_scale; upper.position = Vector2(-width/2.0,-height)*art_scale; upper.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST; object.add_child(upper)
	base.name = "SolidBase"; base.texture = lower_texture; base.centered = false; base.offset = Vector2.ZERO; base.position = Vector2(-width/2.0,-height+cut)*art_scale
	return upper

func _add_exit_door(id: String,at: Vector2) -> void:
	var door := Node2D.new(); door.name = id; door.position = at; depth_sorted.add_child(door)
	var sprite := Sprite2D.new(); door.add_child(sprite); _set_art(sprite,_frame("indoor",8),52,36)
	var upper := _split_upper_art(door,sprite,0.72)
	_visuals.append({"node":door,"sprite":upper,"can_fade":true,"height":52.0,"width":36.0})

func _add_npc(id: String,title: String,frame_index: int,at: Vector2) -> void:
	var npc := Node2D.new(); npc.name = id; npc.position = at; depth_sorted.add_child(npc)
	var sprite := Sprite2D.new(); npc.add_child(sprite); _set_actor(sprite,"npc",frame_index,2,2)
	var body := StaticBody2D.new(); npc.add_child(body); var collision := CollisionShape2D.new(); var shape := RectangleShape2D.new(); shape.size = Vector2(12,8); collision.shape = shape; body.add_child(collision)
	var footprint := Rect2(at-Vector2(6,4),Vector2(12,8)); blocks.append(footprint)
	objects.append({"id":id,"title":title,"position":at,"footprint":footprint,"visual_size":Vector2(24,40),"anchor":"feet","interaction_side":"explicit_spot","sheet":"npc","frame":frame_index,"node":npc}); npcs.append(npc)

func _add_floor_art(id: String,sheet: String,index: int,at: Vector2,size: Vector2) -> void:
	var sprite := Sprite2D.new(); sprite.name = id; sprite.position = at; sprite.z_index = -1; depth_sorted.add_child(sprite); _set_art(sprite,_frame(sheet,index),size.y,size.x)

func _build_boundaries() -> void:
	for polygon in walk_regions:
		for i in range(polygon.size()):
			var shape := SegmentShape2D.new(); shape.a = polygon[i]; shape.b = polygon[(i+1)%polygon.size()]
			var collision := CollisionShape2D.new(); collision.shape = shape; boundary_body.add_child(collision)

func _build_stage_crate() -> void:
	_stage_node = Node2D.new(); _stage_node.name = "StageCrate"; depth_sorted.add_child(_stage_node)
	_stage_sprite = Sprite2D.new(); _stage_node.add_child(_stage_sprite); _set_art(_stage_sprite,_frame("indoor",5),25,30)
	var body := StaticBody2D.new(); _stage_node.add_child(body); _stage_collision = CollisionShape2D.new(); var shape := RectangleShape2D.new(); shape.size = Vector2(20,10); _stage_collision.shape = shape; body.add_child(_stage_collision); _update_stage()

func _spot(id: String,title: String,at: Vector2,normal: Vector2 = Vector2.ZERO,radius: float = INTERACTION_RADIUS) -> Dictionary:
	return {"id":id,"title":title,"position":at,"normal":normal,"radius":radius}

func spot(id: String,title: String,at: Vector2) -> Dictionary:
	return _spot(id,title,at)

func default_pos(id: String) -> Vector2:
	if id == "cafe": return Vector2(720,691)
	if id == "props": return Vector2(775,733)
	return Vector2(1000,650)

func return_pos(from_place: String) -> Vector2:
	return Vector2(455,389) if from_place == "cafe" else Vector2(1470,480)

func stage_anchor(id: String) -> Vector2:
	return {"crate":Vector2(708,653),"target":Vector2(885,717),"chen":Vector2(640,540)}.get(id,Vector2.ZERO)

func spot_position(id: String) -> Vector2:
	if id in ["crate","target"]: return stage_anchor(id)
	for item in spots:
		if item.id == id: return item.position
	return Vector2.ZERO

func can_stand(pos: Vector2) -> bool:
	for shift in [Vector2(-6,-4),Vector2(6,-4),Vector2(6,4),Vector2(-6,4)]:
		var inside := false
		for polygon in walk_regions:
			if Geometry2D.is_point_in_polygon(pos+shift,polygon): inside = true; break
		if not inside: return false
	var foot := Rect2(pos-FOOT_HALF,FOOT_HALF*2.0)
	for footprint in blocks:
		if foot.intersects(footprint): return false
	if place == "props" and is_stage and not carrying:
		var at := stage_anchor("target") if crate_placed else stage_anchor("crate")
		if foot.intersects(Rect2(at-Vector2(10,5),Vector2(20,10))): return false
	return true

func _line_blocked(from: Vector2,to: Vector2) -> bool:
	var samples := maxi(1,int(ceil(from.distance_to(to)/4.0)))
	for i in range(samples+1):
		var point := from.lerp(to,float(i)/samples)
		var on_ground := false
		for polygon in walk_regions:
			if Geometry2D.is_point_in_polygon(point,polygon): on_ground = true; break
		if not on_ground: return true
	for footprint in blocks:
		if footprint.has_point(from) or footprint.has_point(to): return true
		var corners := [footprint.position,Vector2(footprint.end.x,footprint.position.y),footprint.end,Vector2(footprint.position.x,footprint.end.y)]
		for i in range(4):
			if Geometry2D.segment_intersects_segment(from,to,corners[i],corners[(i+1)%4]) != null: return true
	return false

func interaction_allowed_at(at: Vector2,radius: float = INTERACTION_RADIUS) -> bool:
	return hero.position.distance_to(at) <= radius and not _line_blocked(hero.position,at)

func can_interact_with(id: String,from: Vector2) -> bool:
	var candidate: Dictionary = {}
	if id in ["crate","target"]:
		if place != "props": return false
		candidate = _spot(id,id,stage_anchor(id))
	else:
		for item in spots:
			if item.id == id: candidate = item; break
	if candidate.is_empty() or not can_stand(from): return false
	var at: Vector2 = candidate.position
	if id == "target" and Rect2(from-FOOT_HALF,FOOT_HALF*2.0).intersects(Rect2(at-Vector2(10,5),Vector2(20,10))): return false
	if from.distance_to(at)>float(candidate.radius): return false
	var normal: Vector2 = candidate.normal
	if normal != Vector2.ZERO and (from-at).dot(normal)<-6.0: return false
	return not _line_blocked(from,at)

func interaction_at(pos: Vector2) -> String:
	var result := ""; var nearest := INF
	for item in spots:
		var distance: float = pos.distance_to(item.position)
		if distance<nearest and can_interact_with(item.id,pos): nearest = distance; result = item.id
	return result

func map_snapshot() -> Dictionary:
	var visible_spots: Array = spots.duplicate(true)
	if place == "props" and is_stage:
		if crate_placed:
			visible_spots.append(_spot("target","场务箱已放好",stage_anchor("target")))
		else:
			if not carrying: visible_spots.append(_spot("crate","场务箱",stage_anchor("crate")))
			visible_spots.append(_spot("target","场务放置区",stage_anchor("target")))
	return {"bounds":scene_bounds,"walk_regions":walk_regions.duplicate(),"blocks":blocks.duplicate(),"objects":objects.duplicate(true),"spots":visible_spots,"player":hero.position,"place":place}

func move_character(direction: Vector2) -> void:
	var start := hero.position; hero.velocity = direction.normalized()*SPEED; hero.move_and_slide()
	if not hero.position.is_equal_approx(start): moved.emit(hero.position)

func _physics_process(delta: float) -> void:
	clock += delta
	var direction := Vector2.ZERO
	if not frozen:
		direction.x = float(Input.is_physical_key_pressed(KEY_D) or Input.is_physical_key_pressed(KEY_RIGHT))-float(Input.is_physical_key_pressed(KEY_A) or Input.is_physical_key_pressed(KEY_LEFT))
		direction.y = float(Input.is_physical_key_pressed(KEY_S) or Input.is_physical_key_pressed(KEY_DOWN))-float(Input.is_physical_key_pressed(KEY_W) or Input.is_physical_key_pressed(KEY_UP))
	move_character(direction); _update_character(direction); _update_stage(); nearby = interaction_at(hero.position) if not frozen else ""
	# Rendering snaps to world pixels; physics and saves keep precise floating point positions.
	hero_image.position = hero.position.round()-hero.position
	for visual in _visuals:
		if not visual.can_fade: continue
		var node: Node2D = visual.node; var sprite: Sprite2D = visual.sprite
		var behind: bool = hero.position.y<node.position.y and hero.position.y>node.position.y-float(visual.height) and absf(hero.position.x-node.position.x)<float(visual.width)*0.38
		sprite.modulate.a = move_toward(sprite.modulate.a,0.38 if behind else 1.0,delta*4.5)
	ground_overlay.queue_redraw(); debug_overlay.queue_redraw()

func _update_character(direction: Vector2) -> void:
	if direction != Vector2.ZERO:
		if absf(direction.x)>absf(direction.y): facing = 2 if direction.x>0 else 1
		else: facing = 0 if direction.y>0 else 3
	var frame_index := facing*4+(int(clock*7.0)%4 if direction != Vector2.ZERO else 0)
	if frame_index != _last_character_frame:
		_set_actor(hero_image,"player",frame_index,4,4); _last_character_frame = frame_index

func _update_stage() -> void:
	if not is_instance_valid(_stage_node): return
	_stage_node.visible = place == "props" and is_stage
	_stage_collision.set_deferred("disabled",not _stage_node.visible or carrying)
	_stage_node.position = hero.position+Vector2(11,-7) if carrying else (stage_anchor("target") if crate_placed else stage_anchor("crate"))
	_stage_node.z_index = 1 if carrying else 0

func _unhandled_key_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo and event.physical_keycode == KEY_F3:
		debug_visible = not debug_visible; debug_overlay.queue_redraw()

func interact() -> void:
	if not frozen:
		nearby = interaction_at(hero.position)
		if not nearby.is_empty(): interaction_requested.emit(nearby)

func near_title() -> String:
	for item in spots:
		if item.id == nearby: return item.title
	return ""
