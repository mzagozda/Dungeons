extends SceneTree

func _initialize() -> void:
	_render.call_deferred()

func _render() -> void:
	var pack := ""
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--mobile-resource-pack="):
			pack = argument.trim_prefix("--mobile-resource-pack=")
	if pack.is_empty() or not ProjectSettings.load_resource_pack(pack, true):
		push_error("Actual mobile resource ZIP is required for the compressed-texture preview.")
		quit(1)
		return
	if change_scene_to_file("res://scenes/m3_preview.tscn") != OK:
		push_error("Could not open actual terrain preview with mobile resources.")
		quit(1)
