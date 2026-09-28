# CI gate v0.1.4

Required before merge:

- Hub restore/build passes on Windows.
- Updater restore/build passes on Windows.
- Published package contains the updater and Godot adapter payload.
- Release packaging emits ZIP + SHA-256.

Runtime acceptance after merge/release:

- v0.1.4 launches with the compact layout.
- Stable update check can see a later release.
- One forced failed-start update is rolled back successfully.
