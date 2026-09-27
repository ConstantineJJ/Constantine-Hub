@tool
extends EditorPlugin

const EDITOR_BRIDGE = preload("res://addons/constantine_mcp/editor_bridge.gd")
const RUNTIME_AUTOLOAD_NAME := "ConstantineMCPRuntime"
const RUNTIME_AUTOLOAD_PATH := "res://addons/constantine_mcp/runtime_bridge.gd"

var _bridge: Node

func _enter_tree() -> void:
	_bridge = EDITOR_BRIDGE.new()
	_bridge.name = "ConstantineMCPEditorBridge"
	add_child(_bridge)
	_bridge.configure(get_editor_interface())
	if not ProjectSettings.has_setting("autoload/%s" % RUNTIME_AUTOLOAD_NAME):
		add_autoload_singleton(RUNTIME_AUTOLOAD_NAME, RUNTIME_AUTOLOAD_PATH)

func _exit_tree() -> void:
	if is_instance_valid(_bridge):
		_bridge.queue_free()
	# Keep the runtime autoload registered across normal editor/plugin teardown.
	# Constantine Hub owns installation/removal; editor shutdown must not mutate project settings.
