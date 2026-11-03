# ConHub. Godot MCP

The editor and runtime plugins listen on loopback (`127.0.0.1:6262` and
`127.0.0.1:6263`). Existing `godot-*` configuration paths, tunnel profiles,
package identifiers and MCP tool names remain compatible.
The launcher explicitly loads its adjacent `src` directory, so a reused Python
environment cannot silently select an older editable-install source checkout.

## Bounded scene-tree queries

`godot_get_scene_tree` retains `target`, `max_depth` and `include_internal`.
The first unfiltered page retains the legacy nested `root`. Every page also
includes a flat `nodes` list. A large tree is intentionally partial.

New optional arguments:

| Argument | Default | Meaning |
| --- | --- | --- |
| `max_nodes` | 256 | Returned nodes per page, clamped to 1–1024 |
| `node_path` | empty | Select a subtree using an existing node path |
| `after_path` | empty | Resume after the previous page's `next_after` |
| `scene_id` | 0 | Pass the returned scene ID to reject a changed scene |
| `name_filter` | empty | Case-insensitive node-name substring |
| `type_filter` | empty | Godot class, including subclasses |

The bridge visits at most 2048 nodes per call, even when no node matches a
filter. Traversal stops as soon as a page/node/byte limit is reached, and does
not first construct the entire scene tree. `max_depth` is clamped to 0–32.
`truncated`, `depth_limited`, `returned`, `visited`, `limits`, `next_after`
and `scene_id` describe the result.

For another page pass `next_after` as `after_path`, plus `scene_id`, with the
same subtree/depth/filter options. Continue until `next_after` is null.
When `depth_limited` is true, inspect a deeper branch with `node_path` or
increase `max_depth`; pagination alone does not increase traversal depth.
The first unfiltered page contains `root`; continuation/filtered pages use
`nodes` and return `root: null`. Start again if the scene changes; pagination
is a live view rather than a frozen snapshot.

## Response and lifecycle limits

- Scene-tree page budget: 192 KiB (reserves both nested and flat forms).
- Engine response: 384 KiB; Python refuses bridge frames over 512 KiB.
- Final serialized MCP result: 1 MiB, counting both text and structured
  content and JSON escaping. Oversized results become small tool errors,
  leaving substantial headroom below the tunnel's 10 MiB limit.
- Node property collections, nested arrays/dictionaries, strings and cyclic
  values are bounded before serialization. Truncated values expose metadata;
  narrow `property_filter` to inspect omitted properties.
- Runtime captures preserve aspect ratio, fit within 1280×1280 and adaptively
  reduce high-detail images in at most six steps to fit 270,000 PNG bytes.
  Small images retain their original resolution; returned metadata records
  original dimensions and whether resizing occurred.
- Every bridge request owns a fresh socket and exact response ID. Missing or
  mismatched IDs, invalid frames, EOF and size violations fail only that call.
  A total four-second deadline also stops drip-fed responses. MCP engine
  calls use async sockets; cancellation closes the request socket immediately.
- Engine clients have a five-second deadline, bounded input buffers and
  incremental output writes. Disconnecting clients cannot accumulate forever.

Cancellation stops waiting and releases sockets. It cannot undo a mutation
already dispatched to Godot or interrupt an arbitrary user script method
executing on the engine's main thread. Bounded built-in tree traversal avoids
that problem for scene inspection.

## Verification

Install dependencies with `python -m pip install -e .`, then run:

```powershell
python -m unittest discover -s tests -p 'test_*.py' -v
python tests/smoke_mcp.py
```

Set `GODOT_EXECUTABLE` to a Godot 4 executable to enable the isolated real-engine
fixture. It creates 6501 synthetic nodes, checks complete bounded pagination,
filters, depth limits, stale cursors, cyclic/large property values, adaptive
high-entropy PNG capture, and actual TCP response-budget rejection followed by
a successful status exchange.
No user scene or game asset is copied or modified. Without Godot, this one
engine fixture is explicitly skipped; socket, cancellation, wire-size and
stdio protocol regressions still run.

The supplied November 1 logs confirm tunnel 413 errors at 02:02 and 04:46,
then closed stdio channels and missing-ID failures. They contain opaque RPC
IDs but no tool arguments or response bodies, so they do not establish which
specific tool produced each oversized response. Unlimited breadth and value
encoding were independently reproduced as source defects, then bounded here.
