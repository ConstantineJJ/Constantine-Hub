@tool
extends Node

const PORT := 6262
const HOST := "127.0.0.1"
const Codec = preload("res://addons/constantine_mcp/variant_codec.gd")
const Pager = preload("res://addons/constantine_mcp/scene_tree_pager.gd")
const Transport = preload("res://addons/constantine_mcp/bridge_transport.gd")

var _server := TCPServer.new()
var _clients: Array[Dictionary] = []
var _editor: EditorInterface

func configure(editor_interface: EditorInterface) -> void:
	_editor = editor_interface

func _ready() -> void:
	var err := _server.listen(PORT, HOST)
	if err != OK:
		push_error("Constantine MCP editor bridge failed to listen on %s:%d (error %d)" % [HOST, PORT, err])
	set_process(true)

func _exit_tree() -> void:
	for client in _clients:
		var peer: StreamPeerTCP = client.peer
		peer.disconnect_from_host()
	_clients.clear()
	_server.stop()

func _process(_delta: float) -> void:
	Transport.poll(_server, _clients, _handle_line)

func _handle_line(line: String) -> Dictionary:
	var parsed = JSON.parse_string(line)
	if not (parsed is Dictionary):
		return {"ok": false, "error": "Invalid JSON request"}
	var request_id = parsed.get("id")
	var requested_project := str(parsed.get("project_root", ""))
	if not requested_project.is_empty() and requested_project.replace("\\", "/").trim_suffix("/").to_lower() != ProjectSettings.globalize_path("res://").replace("\\", "/").trim_suffix("/").to_lower():
		return {"id": request_id, "ok": false, "error": "Bridge belongs to another Godot project; open the selected project."}
	var op := str(parsed.get("op", ""))
	var params = parsed.get("params", {})
	if not (params is Dictionary): params = {}
	var result = _dispatch(op, params)
	if result is Dictionary and result.has("__error"):
		return {"id": request_id, "ok": false, "error": result.__error}
	return {"id": request_id, "ok": true, "result": result}

func _dispatch(op: String, params: Dictionary):
	match op:
		"status": return _status()
		"scene_tree": return _scene_tree(params)
		"inspect_node": return _inspect_node(str(params.get("node_path", "")), params.get("property_filter"))
		"create_node": return _create_node(params)
		"set_property": return _set_property(params)
		"delete_node": return _delete_node(str(params.get("node_path", "")))
		"attach_script": return _attach_script(params)
		"save_scene": return _save_scene(params.get("path"))
		"open_scene": return _open_scene(str(params.get("path", "")))
		"call_method": return _call_method(params)
		"catalogue": return _catalogue()
		"extended_call": return _extended_call(str(params.get("tool_name", "")), params.get("parameters", {}))
		_: return _error("Unknown editor bridge operation: %s" % op)

func _root() -> Node:
	if _editor == null: return null
	return _editor.get_edited_scene_root()

func _resolve_node(path: String) -> Node:
	var root := _root()
	if root == null: return null
	if path.is_empty() or path == "." or path == root.name or path == ("/" + root.name): return root
	var normalized := path.strip_edges()
	if normalized.begins_with("/root/"):
		var absolute := get_tree().root.get_node_or_null(NodePath(normalized.trim_prefix("/root/")))
		if absolute != null: return absolute
	if normalized.begins_with(root.name + "/"):
		normalized = normalized.substr(root.name.length() + 1)
	elif normalized.begins_with("/" + root.name + "/"):
		normalized = normalized.substr(root.name.length() + 2)
	return root.get_node_or_null(NodePath(normalized))

func _status() -> Dictionary:
	var root := _root()
	return {
		"engine_version": Engine.get_version_info(),
		"project_name": ProjectSettings.get_setting("application/config/name", ""),
		"project_path": ProjectSettings.globalize_path("res://"),
		"edited_scene": root.scene_file_path if root != null else "",
		"edited_scene_root": root.name if root != null else "",
		"editor_playing": _editor.is_playing_scene() if _editor != null else false,
		"bridge": "%s:%d" % [HOST, PORT]
	}

func _scene_tree(params: Dictionary) -> Dictionary:
	var root := _resolve_node(str(params.get("node_path", "")))
	if root == null: return _error("Scene/subtree not found")
	return Pager.build(root, params)

func _inspect_node(path: String, property_filter) -> Dictionary:
	var node := _resolve_node(path)
	if node == null: return _error("Node not found: %s" % path)
	var filter := str(property_filter if property_filter != null else "").to_lower()
	var props := {}
	var value_bytes := 0
	var truncated := false
	for info in node.get_property_list():
		var usage := int(info.get("usage", 0))
		if (usage & PROPERTY_USAGE_EDITOR) == 0 and (usage & PROPERTY_USAGE_STORAGE) == 0: continue
		var name := str(info.get("name", ""))
		if not filter.is_empty() and name.to_lower().find(filter) < 0: continue
		if props.size() >= 128:
			truncated = true
			break
		var encoded = Codec.encode(node.get(name))
		value_bytes += JSON.stringify(encoded).to_utf8_buffer().size() + name.to_utf8_buffer().size() * 6 + 16
		if value_bytes > 196608:
			truncated = true
			break
		props[name] = encoded
	var script = node.get_script()
	return {"name": node.name, "class": node.get_class(), "path": str(node.get_path()), "groups": node.get_groups(), "script": script.resource_path if script is Resource else "", "properties": props, "truncated": truncated, "continuation": "Use property_filter to inspect omitted properties." if truncated else ""}

func _create_node(params: Dictionary) -> Dictionary:
	var parent := _resolve_node(str(params.get("parent_path", "")))
	if parent == null: return _error("Parent node not found")
	var node_type := str(params.get("node_type", ""))
	if node_type.is_empty() or not ClassDB.class_exists(node_type): return _error("Unknown Godot class: %s" % node_type)
	var created = ClassDB.instantiate(node_type)
	if not (created is Node): return _error("Class is not a Node: %s" % node_type)
	var node: Node = created
	var requested_name := str(params.get("node_name", ""))
	if not requested_name.is_empty(): node.name = requested_name
	parent.add_child(node)
	var root := _root()
	if root != null: node.owner = root
	var props = params.get("properties", {})
	if props is Dictionary:
		for key in props.keys(): node.set(str(key), Codec.decode(props[key]))
	return {"path": str(node.get_path()), "name": node.name, "class": node.get_class()}

func _set_property(params: Dictionary) -> Dictionary:
	var path := str(params.get("node_path", ""))
	var node := _resolve_node(path)
	if node == null: return _error("Node not found: %s" % path)
	var property_name := str(params.get("property", ""))
	if property_name.is_empty(): return _error("Property name is required")
	node.set(property_name, Codec.decode(params.get("value")))
	return {"path": str(node.get_path()), "property": property_name, "value": Codec.encode(node.get(property_name))}

func _delete_node(path: String) -> Dictionary:
	var node := _resolve_node(path)
	if node == null: return _error("Node not found: %s" % path)
	if node == _root(): return _error("Deleting the edited scene root is blocked")
	var old_path := str(node.get_path())
	var parent := node.get_parent()
	if parent != null: parent.remove_child(node)
	node.queue_free()
	return {"deleted": old_path}

func _attach_script(params: Dictionary) -> Dictionary:
	var node := _resolve_node(str(params.get("node_path", "")))
	if node == null: return _error("Node not found")
	var path := str(params.get("script_path", ""))
	if not path.begins_with("res://"): return _error("script_path must begin with res://")
	var script = load(path)
	if script == null: return _error("Script could not be loaded: %s" % path)
	node.set_script(script)
	return {"path": str(node.get_path()), "script": path}

func _save_scene(path) -> Dictionary:
	if _editor == null: return _error("EditorInterface is not configured")
	if path != null and not str(path).is_empty(): _editor.save_scene_as(str(path))
	else: _editor.save_scene()
	var root := _root()
	return {"saved": true, "path": root.scene_file_path if root != null else str(path)}

func _open_scene(path: String) -> Dictionary:
	if _editor == null: return _error("EditorInterface is not configured")
	if not path.begins_with("res://"): return _error("Scene path must begin with res://")
	if not ResourceLoader.exists(path): return _error("Scene resource does not exist: %s" % path)
	_editor.open_scene_from_path(path)
	return {"opened": path}

func _call_method(params: Dictionary) -> Dictionary:
	var node := _resolve_node(str(params.get("node_path", "")))
	if node == null: return _error("Node not found")
	var method := str(params.get("method", ""))
	if method.is_empty() or method.begins_with("_"): return _error("Method is invalid or private")
	if method in ["free", "queue_free", "notification", "set_script", "remove_child"]: return _error("Dangerous lifecycle method is blocked")
	if not node.has_method(method): return _error("Node does not have method: %s" % method)
	var args = params.get("arguments", [])
	if not (args is Array): args = []
	var decoded := []
	for arg in args: decoded.append(Codec.decode(arg))
	return {"result": Codec.encode(node.callv(method, decoded))}

func _catalogue() -> Dictionary:
	return {"tools": [
		{"name": "editor_selection", "description": "Read selected edited-scene nodes."},
		{"name": "scan_filesystem", "description": "Request an EditorFileSystem rescan."},
		{"name": "play_main_scene", "description": "Run the project's main scene from the editor."},
		{"name": "stop_playing_scene", "description": "Stop the editor-run project."}
	]}

func _extended_call(tool_name: String, _params: Dictionary) -> Dictionary:
	match tool_name:
		"editor_selection":
			var selected := []
			for node in _editor.get_selection().get_selected_nodes(): selected.append({"path": str(node.get_path()), "class": node.get_class(), "name": node.name})
			return {"nodes": selected}
		"scan_filesystem":
			_editor.get_resource_filesystem().scan()
			return {"requested": true}
		"play_main_scene":
			_editor.play_main_scene()
			return {"requested": true}
		"stop_playing_scene":
			_editor.stop_playing_scene()
			return {"requested": true}
		_: return _error("Unknown or blocked extended tool: %s" % tool_name)

func _error(message: String) -> Dictionary:
	return {"__error": message}
