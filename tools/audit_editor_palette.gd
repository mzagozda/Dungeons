extends SceneTree

func _initialize() -> void:
	if not OS.has_feature("template") or not OS.has_feature("editor"):
		push_error("Expected an exported desktop template with the editor feature.")
		quit(1)
		return
	var manifest: Array = JSON.parse_string(FileAccess.get_file_as_string("res://content/index.json"))
	var checked := 0
	for relative: String in manifest:
		if not relative.begins_with("tiles/") and not relative.begins_with("entities/"):
			continue
		var definition: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://content/" + relative))
		var texture: Texture2D = load("res://" + definition["editor"]["icon"])
		if texture == null or texture.get_width() != 32 or texture.get_height() != 32:
			push_error("Missing or invalid packaged editor icon: " + relative)
			quit(1)
			return
		checked += 1
	if checked == 0:
		push_error("No packaged palette definitions were checked.")
		quit(1)
		return
	print("PACKAGED_EDITOR_PALETTE=PASS count=", checked)
	quit()
