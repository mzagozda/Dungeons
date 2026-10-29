extends SceneTree

func _initialize() -> void:
	_audit.call_deferred()

func _audit() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 2 or FileAccess.file_exists(args[1]):
		push_error("Expected texture manifest and new desktop audit output.")
		quit(1)
		return
	var manifest = JSON.parse_string(FileAccess.get_file_as_string(args[0]))
	var records: Array[Dictionary] = []
	for entry in manifest.textures:
		var texture := ResourceLoader.load(entry.source, "", ResourceLoader.CACHE_MODE_IGNORE) as Texture2D
		var image := texture.get_image() if texture != null else null
		if image == null or image.is_compressed() or image.get_width() != entry.width or image.get_height() != entry.height or image.has_mipmaps() != entry.mipmaps:
			push_error("Desktop lossless/mipmap import differs: " + entry.source)
			quit(1)
			return
		records.append({"source":entry.source, "gpuBytes":image.get_data().size(), "mipmaps":image.has_mipmaps()})
	var stream := FileAccess.open(args[1], FileAccess.WRITE)
	stream.store_string(JSON.stringify({"passed":true, "textures":records, "physicalAcceptance":false}, "  ") + "\n")
	stream.close()
	print("DESKTOP_TEXTURE_IMPORTS=PASS count=", records.size())
	quit(0)
