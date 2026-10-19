extends SceneTree

## Real viewport captures with isolated saves and explicit positioning fixtures.
## These are visual inspection evidence; they do not claim manual traversal.
const MainScene = preload("res://main.tscn")
var app
var captures: Array[String] = []

func _initialize() -> void:
	if DisplayServer.get_name() == "headless":
		printerr("Native renderer required for pixel captures"); quit(1); return
	ProjectSettings.set_setting("application/config/use_custom_user_dir",true)
	ProjectSettings.set_setting("application/config/custom_user_dir_name","MoonZ Pixel Captures/%d" % OS.get_process_id())
	_run.call_deferred()

func _frame() -> void:
	await process_frame
	await process_frame

func _position(place: String, at: Vector2) -> void:
	app.world.change_place(place,at)
	app.model.data.location = place
	app.model.data.position = [app.world.hero.position.x,app.world.hero.position.y]
	app.refresh()
	await _frame()

func _capture(id: String) -> void:
	await create_timer(1.2).timeout
	await RenderingServer.frame_post_draw
	var path := ProjectSettings.globalize_path("res://evidence/pixel-v1/"+id+".png")
	DirAccess.make_dir_recursive_absolute(path.get_base_dir())
	var result := root.get_texture().get_image().save_png(path)
	if result != OK: printerr("Capture failed: "+id); quit(1); return
	captures.append(id); print("PIXEL CAPTURE "+path)

func _run() -> void:
	root.size = Vector2i(1600,900)
	app = MainScene.instantiate(); root.add_child(app)
	await _frame()
	app.new_game(); app.close_modal()
	await _capture("01-harbor")
	app.show_map(); await _capture("02-map"); app.close_modal()
	await _position("island",Vector2(550,400)); await _capture("03-cafe-exterior")
	await _position("island",Vector2(720,305)); await _capture("04-tree-behind")
	await _position("island",Vector2(720,370)); await _capture("05-tree-front")
	await _position("island",Vector2(1470,465)); await _capture("06-workshop-exterior")
	await _position("cafe",Vector2(890,630)); await _capture("07-cafe")
	app.world.debug_visible = true; await _capture("08-cafe-footprints"); app.world.debug_visible = false
	await _position("props",Vector2(805,650)); await _capture("09-workshop")
	app.world.is_stage = true; app.world.frozen = true
	await _capture("10-workshop-crate")
	await _position("cafe",Vector2(792,570)); await _capture("11-counter-interaction")
	await _position("props",Vector2(1058,592)); await _capture("12-lu-interaction")
	await _position("island",Vector2(945,585)); await _capture("13-harbor-interaction")
	var report := {"captures":captures,"kind":"native viewport / positioning fixtures","viewport":[1600,900],"not_validated":["manual continuous traversal","exact reference-game timing","user art approval"]}
	var file := FileAccess.open("res://evidence/pixel-v1/capture-report.json",FileAccess.WRITE)
	file.store_string(JSON.stringify(report,"  ")); file.close()
	app.queue_free(); await _frame()
	for name in ["day1-auto.json","day1-manual.json"]:
		if FileAccess.file_exists("user://"+name): DirAccess.remove_absolute("user://"+name)
	print("PIXEL CAPTURE RESULT ",captures.size()," images")
	quit()
