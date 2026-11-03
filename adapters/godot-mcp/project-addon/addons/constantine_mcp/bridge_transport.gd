@tool
extends RefCounted

const MAX_REQUEST_BYTES := 2097152
const MAX_RESPONSE_BYTES := 393216
const MAX_CLIENTS := 32

static func poll(server: TCPServer, clients: Array[Dictionary], handler: Callable) -> void:
	var accepted := 0
	while server.is_connection_available() and accepted < 8:
		var peer := server.take_connection()
		accepted += 1
		if clients.size() >= MAX_CLIENTS:
			peer.disconnect_from_host()
			continue
		clients.append({"peer": peer, "buffer": PackedByteArray(), "output": PackedByteArray(), "sent": 0, "deadline": Time.get_ticks_msec() + 5000})
	for i in range(clients.size() - 1, -1, -1):
		var entry := clients[i]
		var peer: StreamPeerTCP = entry.peer
		peer.poll()
		if Time.get_ticks_msec() > entry.deadline or peer.get_status() in [StreamPeerTCP.STATUS_ERROR, StreamPeerTCP.STATUS_NONE]:
			peer.disconnect_from_host()
			clients.remove_at(i)
			continue
		if entry.output.is_empty():
			var available := mini(peer.get_available_bytes(), 65536)
			if available > 0:
				var packet := peer.get_data(available)
				if packet[0] == OK: entry.buffer.append_array(packet[1])
			if entry.buffer.size() > MAX_REQUEST_BYTES:
				peer.disconnect_from_host()
				clients.remove_at(i)
				continue
			var newline: int = entry.buffer.find(10)
			if newline >= 0:
				var response: Dictionary = handler.call(entry.buffer.slice(0, newline).get_string_from_utf8())
				entry.output = (JSON.stringify(response) + "\n").to_utf8_buffer()
				if entry.output.size() > MAX_RESPONSE_BYTES:
					entry.output = (JSON.stringify({"id": response.get("id"), "ok": false, "error": "Response exceeds the safe transport budget. Use scene-tree pages, node_path or property_filter."}) + "\n").to_utf8_buffer()
				entry.buffer.clear()
		if not entry.output.is_empty():
			var remaining: PackedByteArray = entry.output.slice(entry.sent, mini(entry.sent + 65536, entry.output.size()))
			var sent := peer.put_partial_data(remaining)
			if sent[0] != OK:
				peer.disconnect_from_host()
				clients.remove_at(i)
				continue
			entry.sent += sent[1]
			if entry.sent >= entry.output.size():
				peer.disconnect_from_host()
				clients.remove_at(i)
