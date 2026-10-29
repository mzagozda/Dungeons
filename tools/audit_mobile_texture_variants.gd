extends SceneTree

func _initialize() -> void:
	_audit.call_deferred()

func _fail(message: String) -> void:
	push_error(message)
	quit(1)

func _audit() -> void:
	var input = JSON.parse_string(FileAccess.get_file_as_string("res://source-manifest.json"))
	if not input is Dictionary or input.get("formatVersion") != 1 or input.get("codec") != "ETC2":
		_fail("Missing native mobile texture source manifest.")
		return
	var results: Array[Dictionary] = []
	for record in input.textures:
		var source: String = record.source
		var config := ConfigFile.new()
		if config.load(source + ".import") != OK:
			_fail("Missing native texture import: " + source)
			return
		var target: String = config.get_value("remap", "path.etc2", "")
		if not target.ends_with(".etc2.ctex") or config.get_value("params", "compress/mode", -1) != 2:
			_fail("Native ETC2 import was not generated: " + source)
			return
		var texture := ResourceLoader.load(target, "", ResourceLoader.CACHE_MODE_IGNORE) as Texture2D
		if texture == null:
			_fail("Native ETC2 resource did not load: " + source)
			return
		var image := texture.get_image()
		if image == null or not image.is_compressed() or image.get_format() not in [Image.FORMAT_ETC2_RGB8, Image.FORMAT_ETC2_RGBA8, Image.FORMAT_ETC2_RGB8A1]:
			_fail("Native texture does not contain ETC2 GPU data: " + source)
			return
		if image.get_width() != record.width or image.get_height() != record.height or image.has_mipmaps() != record.mipmaps:
			_fail("Native texture dimensions/mipmaps differ: " + source)
			return
		results.append({"source":source, "mobilePath":target, "width":image.get_width(), "height":image.get_height(),
			"mipmaps":image.has_mipmaps(), "mipmapCount":image.get_mipmap_count(), "family":"ETC2",
			"imageFormat":image.get_format(), "gpuBytes":image.get_data().size()})
	var output := ProjectSettings.globalize_path("res://native-audit.json")
	if FileAccess.file_exists(output):
		_fail("Native texture audit output already exists.")
		return
	var stream := FileAccess.open(output, FileAccess.WRITE)
	if stream == null:
		_fail("Could not write native texture audit.")
		return
	stream.store_string(JSON.stringify({"passed":true, "godot":Engine.get_version_info().string,
		"textures":results, "physicalAcceptance":false}, "  ") + "\n")
	stream.close()
	print("MOBILE_TEXTURE_NATIVE_AUDIT=PASS count=", results.size())
	quit(0)
