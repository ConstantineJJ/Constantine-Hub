# Update design v0.1.4

Stable updates are discovered from GitHub Releases. The Hub downloads `ConstantineHub-win-x64.zip` and `ConstantineHub-win-x64.sha256`, verifies SHA-256, extracts to a staging directory under `%LOCALAPPDATA%\ConstantineHub\updates`, launches the staged `ConstantineHub.Updater.exe`, then exits.

The updater waits for the old Hub process to exit, backs up files it will replace, installs the staged payload, launches the new Hub with a startup-marker path, and waits up to 20 seconds for the marker. If startup is not confirmed, it restores the backup and restarts the previous Hub.

User configuration remains outside the installation payload under `%APPDATA%` / `%LOCALAPPDATA%`.
