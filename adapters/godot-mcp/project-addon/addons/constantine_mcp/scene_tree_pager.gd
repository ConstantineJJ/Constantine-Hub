@tool
extends RefCounted

# Traversal is bounded in breadth, time spent skipping/filtering and output size.
# Cursor is a relative node path from this scene, not a retained Node reference.
const MAX_VISITED := 2048
const MAX_PAGE_BYTES := 196608

static func build(root: Node, params: Dictionary) -> Dictionary:
	var max_depth := clampi(int(params.get("max_depth", 8)), 0, 32)
	var max_nodes := clampi(int(params.get("max_nodes", 256)), 1, 1024)
	var internal := bool(params.get("include_internal", false))
	var after := str(params.get("after_path", ""))
	var name_filter := str(params.get("name_filter", "")).to_lower()
	var type_filter := str(params.get("type_filter", ""))
	var scene_id := root.get_instance_id()
	if int(params.get("scene_id", 0)) not in [0, scene_id]:
		return {"__error": "Scene changed; start a new scene-tree query without the old cursor."}
	var current: Node = root
	if not after.is_empty():
		var previous := root.get_node_or_null(NodePath(after))
		if previous == null or (previous != root and not root.is_ancestor_of(previous)):
			return {"__error": "Scene-tree cursor is stale; start a new query."}
		current = _next(previous, root, max_depth, internal)
	var nodes := []
	var tree_entries := {}
	var tree_root = null
	var visited := 0
	var byte_count := 0
	var last := after
	var depth_limited := false
	while current != null and visited < MAX_VISITED and nodes.size() < max_nodes:
		var depth := _depth(current, root)
		if depth == max_depth and current.get_child_count(internal) > 0:
			depth_limited = true
		var matches := (name_filter.is_empty() or str(current.name).to_lower().contains(name_filter)) and (type_filter.is_empty() or current.is_class(type_filter))
		if matches:
			var item := {"name": str(current.name), "class": current.get_class(), "path": str(current.get_path())}
			# Reserve for both legacy nested root and the flat paginated nodes.
			var item_bytes := JSON.stringify(item).to_utf8_buffer().size() * 2 + 64
			if byte_count + item_bytes > MAX_PAGE_BYTES:
				if nodes.is_empty():
					return {"__error": "One node description exceeds the scene-tree page budget; query a smaller subtree."}
				break
			byte_count += item_bytes
			nodes.append(item)
			if after.is_empty() and name_filter.is_empty() and type_filter.is_empty():
				var nested := item.duplicate()
				nested.children = []
				tree_entries[current.get_instance_id()] = nested
				if current == root:
					tree_root = nested
				elif tree_entries.has(current.get_parent().get_instance_id()):
					tree_entries[current.get_parent().get_instance_id()].children.append(nested)
		visited += 1
		last = str(root.get_path_to(current))
		current = _next(current, root, max_depth, internal)
	return {
		"root": tree_root, "nodes": nodes, "returned": nodes.size(), "visited": visited,
		"truncated": current != null or depth_limited, "depth_limited": depth_limited,
		"next_after": last if current != null else null, "scene_id": scene_id,
		"limits": {"max_depth": max_depth, "max_nodes": max_nodes, "max_visited": MAX_VISITED, "max_page_bytes": MAX_PAGE_BYTES},
		"continuation": "Pass next_after as after_path with scene_id and the same depth/filter options. For depth-limited branches query node_path as a subtree."
	}

static func _depth(node: Node, root: Node) -> int:
	var depth := 0
	while node != root:
		depth += 1
		node = node.get_parent()
	return depth

static func _next(node: Node, root: Node, max_depth: int, internal: bool) -> Node:
	if _depth(node, root) < max_depth and node.get_child_count(internal) > 0:
		return node.get_child(0, internal)
	while node != root:
		var parent := node.get_parent()
		var index := node.get_index(internal) + 1
		if index < parent.get_child_count(internal):
			return parent.get_child(index, internal)
		node = parent
	return null
