extends Node

const PORT := 6263
const HOST := "127.0.0.1"
const Codec = preload("res://addons/constantine_mcp/variant_codec.gd")

var _server := TCPServer.new()
var _clients: Array[Dictionary] = []

func _ready() -> void:
	var err := _server.listen(PORT, HOST)
	if err != OK:
		push_error("Constantine MCP runtime bridge failed to listen on %s:%d (error %d)" % [HOST, PORT, err])
	set_process(true)

func _exit_tree() -> void:
	for client in _clients:
		var peer: StreamPeerTCP = client.peer
		peer.disconnect_from_host()
	_clients.clear()
	_server.stop()

func _process(_delta: float) -> void:
	while _server.is_connection_available():
		var peer := _server.take_connection()
		_clients.append({"peer": peer, "buffer": ""})
	for i in range(_clients.size() - 1, -1, -1):
		var entry := _clients[i]
		var peer: StreamPeerTCP = entry.peer
		peer.poll()
		if peer.get_status() == StreamPeerTCP.STATUS_ERROR or peer.get_status() == StreamPeerTCP.STATUS_NONE:
			_clients.remove_at(i)
			continue
		var available := peer.get_available_bytes()
		if available > 0:
			var packet := peer.get_data(available)
			if packet[0] == OK:
				entry.buffer += packet[1].get_string_from_utf8()
				_clients[i] = entry
		var newline := str(entry.buffer).find("\n")
		if newline >= 0:
			var line := str(entry.buffer).substr(0, newline)
			var response := _handle_line(line)
			peer.put_data((JSON.stringify(response) + "\n").to_utf8_buffer())
			peer.disconnect_from_host()
			_clients.remove_at(i)

func _handle_line(line: String) -> Dictionary:
	var parsed = JSON.parse_string(line)
	if not (parsed is Dictionary): return {"ok": false, "error": "Invalid JSON request"}
	var request_id = parsed.get("id")
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
		"scene_tree": return _scene_tree(int(params.get("max_depth", 8)), bool(params.get("include_internal", false)))
		"inspect_node": return _inspect_node(str(params.get("node_path", "")), params.get("property_filter"))
		"create_node": return _create_node(params)
		"set_property": return _set_property(params)
		"delete_node": return _delete_node(str(params.get("node_path", "")))
		"attach_script": return _attach_script(params)
		"call_method": return _call_method(params)
		"capture_game": return _capture_game()
		"input_action": return _input_action(params)
		_: return _error("Unknown runtime bridge operation: %s" % op)

func _root() -> Node:
	return get_tree().current_scene

func _resolve_node(path: String) -> Node:
	var root := _root()
	if root == null: return null
	if path.is_empty() or path == "." or path == root.name or path == ("/" + root.name): return root
	var normalized := path.strip_edges()
	if normalized.begins_with("/root/"): return get_tree().root.get_node_or_null(NodePath(normalized.trim_prefix("/root/")))
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
		"current_scene": root.scene_file_path if root != null else "",
		"current_scene_root": root.name if root != null else "",
		"bridge": "%s:%d" % [HOST, PORT]
	}

func _scene_tree(max_depth: int, include_internal: bool) -> Dictionary:
	var root := _root()
	if root == null: return _error("No running current_scene")
	return {"root": _node_tree_entry(root, 0, clampi(max_depth, 0, 32), include_internal)}

func _node_tree_entry(node: Node, depth: int, max_depth: int, include_internal: bool) -> Dictionary:
	var item := {"name": node.name, "class": node.get_class(), "path": str(node.get_path()), "children": []}
	if depth >= max_depth: return item
	for child in node.get_children(include_internal): item.children.append(_node_tree_entry(child, depth + 1, max_depth, include_internal))
	return item

func _inspect_node(path: String, property_filter) -> Dictionary:
	var node := _resolve_node(path)
	if node == null: return _error("Node not found: %s" % path)
	var filter := str(property_filter if property_filter != null else "").to_lower()
	var props := {}
	for info in node.get_property_list():
		var usage := int(info.get("usage", 0))
		if (usage & PROPERTY_USAGE_EDITOR) == 0 and (usage & PROPERTY_USAGE_STORAGE) == 0: continue
		var name := str(info.get("name", ""))
		if not filter.is_empty() and name.to_lower().find(filter) < 0: continue
		props[name] = Codec.encode(node.get(name))
	var script = node.get_script()
	return {"name": node.name, "class": node.get_class(), "path": str(node.get_path()), "groups": node.get_groups(), "script": script.resource_path if script is Resource else "", "properties": props}

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
	var props = params.get("properties", {})
	if props is Dictionary:
		for key in props.keys(): node.set(str(key), Codec.decode(props[key]))
	return {"path": str(node.get_path()), "name": node.name, "class": node.get_class()}

func _set_property(params: Dictionary) -> Dictionary:
	var node := _resolve_node(str(params.get("node_path", "")))
	if node == null: return _error("Node not found")
	var property_name := str(params.get("property", ""))
	if property_name.is_empty(): return _error("Property name is required")
	node.set(property_name, Codec.decode(params.get("value")))
	return {"path": str(node.get_path()), "property": property_name, "value": Codec.encode(node.get(property_name))}

func _delete_node(path: String) -> Dictionary:
	var node := _resolve_node(path)
	if node == null: return _error("Node not found: %s" % path)
	if node == _root(): return _error("Deleting current_scene root is blocked")
	var old_path := str(node.get_path())
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

func _capture_game() -> Dictionary:
	var image := get_viewport().get_texture().get_image()
	if image == null: return _error("Viewport image is unavailable")
	var png := image.save_png_to_buffer()
	return {"mime_type": "image/png", "base64": Marshalls.raw_to_base64(png), "width": image.get_width(), "height": image.get_height()}

func _input_action(params: Dictionary) -> Dictionary:
	var action := str(params.get("action", ""))
	if action.is_empty() or not InputMap.has_action(action): return _error("Unknown Input Map action: %s" % action)
	var pressed := bool(params.get("pressed", true))
	var strength := clampf(float(params.get("strength", 1.0)), 0.0, 1.0)
	var duration_ms := clampi(int(params.get("duration_ms", 0)), 0, 10000)
	if pressed:
		Input.action_press(action, strength)
		if duration_ms > 0:
			get_tree().create_timer(float(duration_ms) / 1000.0).timeout.connect(func(): Input.action_release(action), CONNECT_ONE_SHOT)
	else:
		Input.action_release(action)
	return {"action": action, "pressed": pressed, "strength": strength, "duration_ms": duration_ms}

func _error(message: String) -> Dictionary:
	return {"__error": message}
