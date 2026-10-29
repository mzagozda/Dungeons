extends SceneTree

func _initialize() -> void:
	_audit.call_deferred()

func _fail(message: String) -> void:
	push_error(message)
	quit(1)

func _compressed(texture: Texture2D) -> bool:
	if texture == null:
		return false
	var image := texture.get_image()
	return image != null and image.is_compressed() and image.get_format() in [Image.FORMAT_ETC2_RGB8, Image.FORMAT_ETC2_RGBA8, Image.FORMAT_ETC2_RGB8A1]

func _audit() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 3:
		_fail("Expected resource ZIP, manifest and new output path.")
		return
	var manifest = JSON.parse_string(FileAccess.get_file_as_string(args[1]))
	if not manifest is Dictionary or not ProjectSettings.load_resource_pack(args[0], true):
		_fail("Could not mount actual APK resources.")
		return
	var texture_count := 0
	for record in manifest.textures:
		var texture := ResourceLoader.load(record.source, "", ResourceLoader.CACHE_MODE_IGNORE) as Texture2D
		if not _compressed(texture):
			_fail("Packaged source texture did not resolve to ETC2: " + record.source)
			return
		var image := texture.get_image()
		if image.get_width() != record.width or image.get_height() != record.height or image.has_mipmaps() != record.mipmaps:
			_fail("Packaged texture dimensions/mipmaps differ: " + record.source)
			return
		texture_count += 1
	var models: Dictionary = {}
	var index = JSON.parse_string(FileAccess.get_file_as_string("res://content/index.json"))
	for file in index:
		var definition = JSON.parse_string(FileAccess.get_file_as_string("res://content/" + file))
		if not definition is Dictionary:
			continue
		var render: Dictionary = definition.get("render", {})
		if not render.has("model"):
			continue
		var base: String = "res://" + render.model
		for variant in range(int(render.get("variants", 1))):
			var model := base if variant == 0 else base.get_basename() + "_" + str(variant) + "." + base.get_extension()
			models[model] = true
	var material_count := 0
	for path in models:
		var scene := ResourceLoader.load(path, "", ResourceLoader.CACHE_MODE_IGNORE) as PackedScene
		if scene == null:
			_fail("Packaged model did not load: " + path)
			return
		var root_node := scene.instantiate()
		for child in root_node.find_children("*", "MeshInstance3D", true, false):
			var mesh := child as MeshInstance3D
			for surface in range(mesh.mesh.get_surface_count()):
				var material := mesh.get_active_material(surface) as BaseMaterial3D
				if material == null or not _compressed(material.albedo_texture):
					_fail("Packaged model material bypassed ETC2 remapping: " + path)
					root_node.free()
					return
				material_count += 1
		root_node.free()
	if texture_count != manifest.textures.size() or models.is_empty() or material_count == 0 or FileAccess.file_exists(args[2]):
		_fail("Incomplete native package texture audit or existing output.")
		return
	var result := {"passed":true, "sourceTextures":texture_count, "models":models.size(), "materials":material_count, "codec":"ETC2", "physicalAcceptance":false}
	var stream := FileAccess.open(args[2], FileAccess.WRITE)
	stream.store_string(JSON.stringify(result, "  ") + "\n")
	stream.close()
	print("PACKAGED_MOBILE_TEXTURES=PASS textures=", texture_count, " models=", models.size(), " materials=", material_count)
	quit(0)
