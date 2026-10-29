@tool
extends EditorPlugin

var _exporter: EditorExportPlugin
var _mobile_textures: EditorExportPlugin

func _enter_tree() -> void:
	_exporter = preload("res://addons/editor_export/strip_editor.gd").new()
	add_export_plugin(_exporter)
	_mobile_textures = preload("res://addons/editor_export/mobile_textures.gd").new()
	add_export_plugin(_mobile_textures)

func _exit_tree() -> void:
	remove_export_plugin(_exporter)
	remove_export_plugin(_mobile_textures)
	_exporter = null
	_mobile_textures = null
