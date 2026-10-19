extends SceneTree
func _initialize() -> void:
	var f=FileAccess.open("res://LICENSES/GODOT.txt",FileAccess.WRITE)
	f.store_string(Engine.get_license_text())
	f.close()
	var notices=FileAccess.open("res://LICENSES/ENGINE-THIRD-PARTY.json",FileAccess.WRITE)
	notices.store_string(JSON.stringify({"copyright":Engine.get_copyright_info(),"licenses":Engine.get_license_info()},"\t"))
	notices.close()
	quit()
