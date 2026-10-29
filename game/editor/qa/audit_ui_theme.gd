extends Node

func _ready() -> void:
	_check.call_deferred()

func _check() -> void:
	var layer := CanvasLayer.new()
	get_tree().root.add_child(layer)
	var button := Button.new()
	layer.add_child(button)
	await get_tree().process_frame
	var font := button.get_theme_font("font") as FontFile
	var style := button.get_theme_stylebox("normal") as StyleBoxTexture
	var context := HashingContext.new()
	context.start(HashingContext.HASH_SHA256)
	if font == null:
		push_error("Packaged pixel font was not inherited across CanvasLayer.")
		get_tree().quit(1)
		return
	context.update(font.data)
	var identity := context.finish().hex_encode()
	if identity != "c845473330b94c2079ce9af01c51ac8ba2d99c24f4d14c039843bbb8e642ebd8" or font.antialiasing != TextServer.FONT_ANTIALIASING_NONE:
		push_error("Packaged pixel font identity/settings mismatch.")
		get_tree().quit(1)
		return
	for code in [8592, 8593, 8594, 8595, 9671, 9888]:
		if not font.has_char(code):
			push_error("Packaged symbol glyph is missing: " + str(code))
			get_tree().quit(1)
			return
	if style == null or style.texture.get_size() != Vector2(16, 16) or style.texture_margin_left != 4:
		push_error("Packaged nine-slice theme was not inherited.")
		get_tree().quit(1)
		return
	var notice := FileAccess.get_file_as_string("res://licenses/silkscreen_ofl.txt")
	if not notice.contains("Copyright 2001 The Silkscreen Project Authors") or not notice.contains("SIL OPEN FONT LICENSE Version 1.1"):
		push_error("Packaged font copyright/license notice is missing.")
		get_tree().quit(1)
		return
	print("PACKAGED_UI_THEME=PASS font=Silkscreen symbols=6 nineSlice=16 license=OFL-1.1 sha256=", identity)
	get_tree().quit()
