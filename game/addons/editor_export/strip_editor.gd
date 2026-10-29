@tool
extends EditorExportPlugin

func _get_name() -> String:
	return "DungeonsEditorBoundary"

func _export_begin(features: PackedStringArray, _is_debug: bool, _path: String, _flags: int) -> void:
	print("DUNGEONS_EDITOR_EXPORT included=", features.has("editor"))

func _export_file(path: String, _type: String, features: PackedStringArray) -> void:
	if path.begins_with("res://editor/") and not features.has("editor"):
		skip()
