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

static func encode(value):
	if value == null or value is bool or value is int or value is float or value is String: return value
	if value is Vector2: return {"type": "Vector2", "x": value.x, "y": value.y}
	if value is Vector3: return {"type": "Vector3", "x": value.x, "y": value.y, "z": value.z}
	if value is Vector4: return {"type": "Vector4", "x": value.x, "y": value.y, "z": value.z, "w": value.w}
	if value is Color: return {"type": "Color", "r": value.r, "g": value.g, "b": value.b, "a": value.a}
	if value is Quaternion: return {"type": "Quaternion", "x": value.x, "y": value.y, "z": value.z, "w": value.w}
	if value is Transform3D:
		return {"type": "Transform3D", "origin": encode(value.origin), "basis": [encode(value.basis.x), encode(value.basis.y), encode(value.basis.z)]}
	if value is Array:
		var arr := []
		for item in value: arr.append(encode(item))
		return arr
	if value is Dictionary:
		var dict_out := {}
		for key in value.keys(): dict_out[str(key)] = encode(value[key])
		return dict_out
	if value is Resource: return {"type": "Resource", "class": value.get_class(), "path": value.resource_path}
	if value is Object: return {"type": "Object", "class": value.get_class(), "instance_id": value.get_instance_id()}
	return str(value)
