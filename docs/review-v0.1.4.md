# Review notes v0.1.4

Observed before this pass:

- live Hub identified as v0.1.3 while repository csproj still declared v0.1.2;
- the 15 pt UI plus large fixed rows forced the Godot card below the comfortable viewport;
- Godot runtime bridge absence was colored as a hard failure even when no runtime session was expected;
- raw tunnel-client FX JSON dominated the operational log;
- builds were CI artifacts only, so users still had to fetch each new build manually.

v0.1.4 addresses those points with explicit version reconciliation, a compact layout, idle runtime semantics, Normal/Verbose logging and a release-backed updater with checksum verification and rollback.
