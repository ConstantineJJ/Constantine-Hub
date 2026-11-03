"""Run bounded traversal/codec assertions in an isolated synthetic Godot project."""
from pathlib import Path
import os
import shutil
import subprocess
import tempfile
import unittest


class EngineLimitsTests(unittest.TestCase):
    @unittest.skipUnless(os.environ.get("GODOT_EXECUTABLE"), "set GODOT_EXECUTABLE for engine regression")
    def test_large_scene_pages_and_bounded_values_in_real_engine(self):
        addon = Path(__file__).resolve().parents[1] / "project-addon" / "addons"
        with tempfile.TemporaryDirectory(prefix="conhub-godot-limits-") as temp:
            root = Path(temp)
            shutil.copytree(addon, root / "addons")
            (root / "project.godot").write_text('[application]\nconfig/name="ConHub. bounded synthetic fixture"\n', encoding="utf-8")
            (root / "fixture.gd").write_text('''extends SceneTree
const Capture = preload("res://addons/constantine_mcp/image_capture.gd")
const Pager = preload("res://addons/constantine_mcp/scene_tree_pager.gd")
const Codec = preload("res://addons/constantine_mcp/variant_codec.gd")
const EditorBridge = preload("res://addons/constantine_mcp/editor_bridge.gd")
const RuntimeBridge = preload("res://addons/constantine_mcp/runtime_bridge.gd")
const Transport = preload("res://addons/constantine_mcp/bridge_transport.gd")
func _initialize():
    call_deferred("_run")
func _run():
    var scene = Node3D.new()
    scene.name = "Synthetic"
    root.add_child(scene)
    for i in range(6500):
        var child = MeshInstance3D.new()
        child.name = "Mesh_%05d" % i
        scene.add_child(child)
    var small = Pager.build(scene, {"max_depth": 0})
    assert(small.root.children.is_empty())
    assert(small.depth_limited)
    var page = Pager.build(scene, {"max_depth": 2, "max_nodes": 128})
    assert(page.root.name == "Synthetic")
    assert(page.returned == 128)
    assert(page.visited <= 2048)
    assert(page.truncated)
    var names = {}
    for item in page.nodes: names[item.path] = true
    var total = page.returned
    while page.next_after != null:
        page = Pager.build(scene, {"max_depth": 2, "max_nodes": 128, "after_path": page.next_after, "scene_id": page.scene_id})
        assert(not page.has("__error"))
        assert(page.returned <= 128)
        assert(page.visited <= 2048)
        for item in page.nodes:
            assert(not names.has(item.path))
            names[item.path] = true
        total += page.returned
    assert(total == 6501)
    var filtered = Pager.build(scene, {"max_depth": 2, "name_filter": "absent"})
    assert(filtered.returned == 0)
    assert(filtered.visited == 2048)
    assert(filtered.next_after != null)
    var selected = Pager.build(scene, {"max_depth": 2, "type_filter": "MeshInstance3D", "max_nodes": 12})
    assert(selected.returned == 12)
    assert(selected.nodes[0].class == "MeshInstance3D")
    assert(Pager.build(scene, {"after_path": "missing"}).has("__error"))
    assert(Pager.build(scene, {"scene_id": -1}).has("__error"))
    var values = []
    for i in range(10000): values.append(i)
    var bounded = Codec.encode(values)
    assert(bounded is Dictionary and bounded.truncated)
    assert(bounded.items.size() <= 256)
    var huge = Codec.encode("x".repeat(1000000))
    assert(huge is Dictionary and huge.truncated)
    var cyclic = []
    cyclic.append(cyclic)
    assert(JSON.stringify(Codec.encode(cyclic)).length() < 10000)
    print("PASS synthetic scene: 6501 nodes; bounded pages/filter/depth/stale cursor/codec")
    var random = RandomNumberGenerator.new()
    random.seed = 42
    var pixels = PackedByteArray()
    pixels.resize(1024 * 768 * 4)
    for i in range(pixels.size()): pixels[i] = random.randi() & 255
    var noisy = Image.create_from_data(1024, 768, false, Image.FORMAT_RGBA8, pixels)
    var captured = Capture.encode(noisy)
    assert(not captured.has("__error"))
    assert(captured.resized)
    assert(Marshalls.base64_to_raw(captured.base64).size() <= Capture.MAX_PNG_BYTES)
    assert(absf(float(captured.width) / captured.height - 1024.0 / 768.0) < 0.02)
    assert(noisy.get_width() == 1024)
    var tiny = Image.create(32, 16, false, Image.FORMAT_RGBA8)
    var small_capture = Capture.encode(tiny)
    assert(small_capture.width == 32 and small_capture.height == 16 and not small_capture.resized)
    print("PASS adaptive high-entropy PNG capture; original aspect and small images preserved")
    _exercise_transport()
    scene.free()
    quit()

func _reply(line: String) -> Dictionary:
    var request = JSON.parse_string(line)
    if request.op == "oversize": return {"id": request.id, "ok": true, "result": "x".repeat(600000)}
    return {"id": request.id, "ok": true, "result": {"connected": true}}

func _request(server: TCPServer, clients: Array[Dictionary], op: String) -> Dictionary:
    var client = StreamPeerTCP.new()
    assert(client.connect_to_host("127.0.0.1", server.get_local_port()) == OK)
    for i in range(1000):
        client.poll()
        Transport.poll(server, clients, _reply)
        if client.get_status() == StreamPeerTCP.STATUS_CONNECTED: break
        OS.delay_msec(1)
    client.put_data((JSON.stringify({"id": "fixture-" + op, "op": op}) + "\\n").to_utf8_buffer())
    var raw = PackedByteArray()
    for i in range(1000):
        Transport.poll(server, clients, _reply)
        client.poll()
        var available = client.get_available_bytes()
        if available > 0: raw.append_array(client.get_data(available)[1])
        if raw.find(10) >= 0: break
        OS.delay_msec(1)
    client.disconnect_from_host()
    return JSON.parse_string(raw.get_string_from_utf8())

func _exercise_transport():
    var server = TCPServer.new()
    assert(server.listen(0, "127.0.0.1") == OK)
    var clients: Array[Dictionary] = []
    var oversized = _request(server, clients, "oversize")
    assert(not oversized.ok and oversized.error.contains("budget"))
    assert(_request(server, clients, "status").result.connected)
    server.stop()
    print("PASS engine transport: oversized response rejected; following status succeeds")
''', encoding="utf-8")
            result = subprocess.run([os.environ["GODOT_EXECUTABLE"], "--headless", "--path", str(root), "--script", "res://fixture.gd", "--quit-after", "10"], capture_output=True, text=True, timeout=60)
            output = result.stdout + result.stderr
            self.assertEqual(result.returncode, 0, output)
            self.assertIn("PASS synthetic scene", output)
            self.assertNotIn("SCRIPT ERROR", output)
            self.assertIn("PASS engine transport", output)


if __name__ == "__main__":
    unittest.main()
