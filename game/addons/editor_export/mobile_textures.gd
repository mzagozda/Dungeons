@tool
extends EditorExportPlugin

var _mobile := false
var _valid := false
var _root := ""
var _textures: Dictionary = {}
var _exported: Dictionary = {}

func _get_name() -> String:
	return "DungeonsMobileTextureVariants"

func _eligible(path: String) -> bool:
	return path.ends_with(".png") and (path.begins_with("res://textures/") or path.begins_with("res://models/"))

func _variant_file(relative: String, digest: String, prefix: String) -> bool:
	if not relative.begins_with(prefix) or relative.contains("..") or relative.contains(":") or relative.contains("\\"):
		return false
	var file := _root.path_join(relative)
	return FileAccess.file_exists(file) and digest.length() == 64 and FileAccess.get_sha256(file) == digest

func _export_begin(features: PackedStringArray, _is_debug: bool, _path: String, _flags: int) -> void:
	_mobile = features.has("android") or features.has("ios")
	_valid = false
	_textures.clear()
	_exported.clear()
	if not _mobile:
		return
	_root = OS.get_environment("DUNGEONS_MOBILE_TEXTURE_DIR").replace("\\", "/")
	if not _root.is_absolute_path() or not FileAccess.file_exists(_root.path_join("manifest.json")):
		push_error("Native mobile texture variants are missing. Export with tools/Export-M0.ps1.")
		return
	var manifest = JSON.parse_string(FileAccess.get_file_as_string(_root.path_join("manifest.json")))
	if not manifest is Dictionary or manifest.get("formatVersion") != 1 or manifest.get("codec") != "ETC2" or manifest.get("nativeAuditPassed") != true or not manifest.get("textures") is Array:
		push_error("Mobile texture manifest has not passed its native audit.")
		return
	for entry in manifest.textures:
		if not entry is Dictionary:
			push_error("Invalid mobile texture manifest entry.")
			return
		for key in ["source", "sourceSha256", "desktopImportSha256", "mobileImportFile", "mobileImportSha256", "mobileDataFile", "mobileDataSha256", "mobilePath"]:
			if not entry.get(key) is String:
				push_error("Incomplete mobile texture provenance.")
				return
		var source: String = entry.source
		if not _eligible(source) or source.contains("..") or _textures.has(source) or FileAccess.get_sha256(source) != entry.sourceSha256 or FileAccess.get_sha256(source + ".import") != entry.desktopImportSha256:
			push_error("Desktop texture input changed or is invalid: " + source)
			return
		if not entry.mobileDataFile.ends_with(".etc2.ctex") or entry.mobilePath != "res://" + entry.mobileDataFile or not _variant_file(entry.mobileDataFile, entry.mobileDataSha256, ".godot/imported/") or not _variant_file(entry.mobileImportFile, entry.mobileImportSha256, "export_imports/"):
			push_error("Native mobile texture file changed or escaped its profile: " + source)
			return
		_textures[source] = entry
	_valid = not _textures.is_empty()
	if not _valid:
		push_error("Mobile texture manifest is empty.")

func _export_file(path: String, _type: String, _features: PackedStringArray) -> void:
	if not _mobile or not _eligible(path):
		return
	# Never fall back to silently packaging a desktop lossless texture on failure.
	skip()
	if not _valid:
		return
	if not _textures.has(path):
		_valid = false
		push_error("Mobile texture manifest does not cover: " + path)
		return
	var entry: Dictionary = _textures[path]
	add_file(path + ".import", FileAccess.get_file_as_bytes(_root.path_join(entry.mobileImportFile)), false)
	add_file(entry.mobilePath, FileAccess.get_file_as_bytes(_root.path_join(entry.mobileDataFile)), false)
	_exported[path] = true

func _export_end() -> void:
	if not _mobile:
		return
	if not _valid or _exported.size() != _textures.size():
		push_error("Mobile ETC2 export did not include its complete audited texture set.")
		return
	print("MOBILE_TEXTURE_EXPORT=PASS codec=ETC2 count=", _exported.size())
