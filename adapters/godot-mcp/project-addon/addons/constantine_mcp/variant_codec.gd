@tool
extends RefCounted
class_name ConstantineMCPVariantCodec

static func decode(value):
	if value is Array:
		var out := []
		for item in value:
			out.append(decode(item))
		return out
	if value is Dictionary:
		var tag := str(value.get("type", ""))
		match tag:
			"Vector2": return Vector2(float(value.get("x", 0.0)), float(value.get("y", 0.0)))
			"Vector3": return Vector3(float(value.get("x", 0.0)), float(value.get("y", 0.0)), float(value.get("z", 0.0)))
			"Vector4": return Vector4(float(value.get("x", 0.0)), float(value.get("y", 0.0)), float(value.get("z", 0.0)), float(value.get("w", 0.0)))
			"Color": return Color(float(value.get("r", 0.0)), float(value.get("g", 0.0)), float(value.get("b", 0.0)), float(value.get("a", 1.0)))
			"Quaternion": return Quaternion(float(value.get("x", 0.0)), float(value.get("y", 0.0)), float(value.get("z", 0.0)), float(value.get("w", 1.0)))
			"ResourcePath":
				var path := str(value.get("path", ""))
				return load(path) if path.begins_with("res://") else null
			"NewResource":
				var class_name_value := str(value.get("class", ""))
				if class_name_value.is_empty() or not ClassDB.class_exists(class_name_value): return null
				var resource = ClassDB.instantiate(class_name_value)
				if resource == null: return null
				var props = value.get("properties", {})
				if props is Dictionary:
					for key in props.keys(): resource.set(str(key), decode(props[key]))
				return resource
		var dict_out := {}
		for key in value.keys(): dict_out[key] = decode(value[key])
		return dict_out
	return value

# Bound nested/cyclic values before materializing a transport response.
static func encode(value):
	return _encode(value, 0, {"remaining": 131072, "items": 2048})

static func _encode(value, depth: int, budget: Dictionary):
	if depth > 8 or budget.remaining <= 0 or budget.items <= 0:
		return {"truncated": true, "reason": "value budget", "hint": "Use a property filter or inspect a smaller value."}
	budget.remaining -= 64
	budget.items -= 1
	if value == null or value is bool or value is int or value is float: return value
	if value is String or value is StringName or value is NodePath:
		var text := str(value)
		var limit := mini(8192, maxi(0, int(budget.remaining) / 6))
		var prefix := text.left(limit)
		budget.remaining -= prefix.to_utf8_buffer().size() * 6
		if prefix.length() < text.length():
			return {"truncated": true, "type": "String", "length": text.length(), "preview": prefix}
		return text
	if value is Vector2: return {"type": "Vector2", "x": value.x, "y": value.y}
	if value is Vector3: return {"type": "Vector3", "x": value.x, "y": value.y, "z": value.z}
	if value is Vector4: return {"type": "Vector4", "x": value.x, "y": value.y, "z": value.z, "w": value.w}
	if value is Color: return {"type": "Color", "r": value.r, "g": value.g, "b": value.b, "a": value.a}
	if value is Quaternion: return {"type": "Quaternion", "x": value.x, "y": value.y, "z": value.z, "w": value.w}
	if value is Transform3D:
		return {"type": "Transform3D", "origin": _encode(value.origin, depth + 1, budget), "basis": [_encode(value.basis.x, depth + 1, budget), _encode(value.basis.y, depth + 1, budget), _encode(value.basis.z, depth + 1, budget)]}
	if value is Array or value is PackedByteArray or value is PackedInt32Array or value is PackedInt64Array or value is PackedFloat32Array or value is PackedFloat64Array or value is PackedStringArray or value is PackedVector2Array or value is PackedVector3Array or value is PackedColorArray:
		var arr := []
		for item in value:
			if arr.size() >= 256 or budget.remaining <= 0 or budget.items <= 0: break
			arr.append(_encode(item, depth + 1, budget))
		if arr.size() < value.size(): return {"truncated": true, "type": "Array", "size": value.size(), "items": arr}
		return arr
	if value is Dictionary:
		var dict_out := {}
		for key in value:
			if dict_out.size() >= 256 or budget.remaining <= 0 or budget.items <= 0:
				dict_out["_conhub_truncated"] = true
				break
			var name := str(key).left(256)
			budget.remaining -= name.to_utf8_buffer().size() * 6
			dict_out[name] = _encode(value[key], depth + 1, budget)
		return dict_out
	if value is Resource: return {"type": "Resource", "class": value.get_class(), "path": str(value.resource_path).left(8192)}
	if value is Object: return {"type": "Object", "class": value.get_class(), "instance_id": value.get_instance_id()}
	return _encode(str(value), depth + 1, budget)
