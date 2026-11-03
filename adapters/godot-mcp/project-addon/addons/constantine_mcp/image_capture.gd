@tool
extends RefCounted

const MAX_PNG_BYTES := 270000

static func encode(image: Image) -> Dictionary:
	if image == null or image.is_empty(): return {"__error": "Viewport image is unavailable"}
	var width := image.get_width()
	var height := image.get_height()
	var working: Image = image.duplicate()
	if working.is_compressed() and working.decompress() != OK:
		return {"__error": "Viewport image could not be decompressed"}
	if working.get_width() > 1280 or working.get_height() > 1280:
		var scale := minf(1280.0 / width, 1280.0 / height)
		working.resize(maxi(1, int(width * scale)), maxi(1, int(height * scale)), Image.INTERPOLATE_LANCZOS)
	# High-detail/entropy images may exceed a byte budget even at 1280 px.
	# Adapt resolution rather than failing a previously valid capture request.
	for attempt in range(6):
		var png := working.save_png_to_buffer()
		if png.size() <= MAX_PNG_BYTES:
			return {"mime_type": "image/png", "base64": Marshalls.raw_to_base64(png), "width": working.get_width(), "height": working.get_height(), "original_width": width, "original_height": height, "resized": working.get_width() != width or working.get_height() != height}
		working.resize(maxi(1, int(working.get_width() * 0.65)), maxi(1, int(working.get_height() * 0.65)), Image.INTERPOLATE_LANCZOS)
	return {"__error": "Viewport PNG exceeds the bounded response budget after adaptive resizing; reduce viewport resolution."}
