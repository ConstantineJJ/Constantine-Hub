# Constantine Hub — Project Pulse

Updated: 2026-10-02 (Europe/Riga). Feature version: 0.2.7.

Published: [stable v0.2.7](https://github.com/ConstantineJJ/Constantine-Hub/releases/tag/v0.2.7),
code commit `497b353e25d4bf68496c2549106b4fd35c2a4a84` (PR #13).

## Current State

**Skills / Contracts: implemented; scoped QA PASS.** Two compact top-bar buttons
have their own direct handlers. They open Windows Explorer and add the resolved
path to Diagnostics. No editor, file browser, Git UI, knowledge copy or sync was added.

- Skills opens `<canonical Tools_C root>\skills`.
- Contracts opens `<canonical Tools_C root>` because authoritative editable sources
  span `docs\contracts.md`, `docs\foundation.md` and `.tooling\contracts.json`.
- On this workstation these are `E:\MyCreations\Tools_C\skills` and
  `E:\MyCreations\Tools_C`; these are observed paths, not literals in UI/action code.
- Resolution reuses Godot's `tools_c_root` configuration and Blender MCP's existing
  `.tooling/config.json` `tools_root` / `TOOLS_C_ROOT` precedence. Configured roots
  must agree. Relative Blender paths resolve against its configured MCP repository.
- Adapter verification and shortcuts share `Core/CanonicalKnowledge.cs`.
  Knowledge navigation reads the Godot root without requiring its target project.
- Missing roots, manifest, skills folder or contract sources produce a small warning
  and a detailed Diagnostics entry. Conflicting roots fail explicitly.

## Changes in this pass

Added Skills / Contracts shortcuts and shared canonical resolution. Kept the
Dashboard and adapter action/lifecycle structure. Extracted root-only reading from
the existing Godot settings reader; preserved its configuration/default behavior.

Changed modules: `Core/CanonicalKnowledge.cs`, `MainForm.cs`, `UiPass.cs`,
`Adapters/Blender/BlenderSkillVerifier.cs`, `Adapters/Godot/GodotAdapter.cs`,
`Adapters/Godot/GodotSettingsStore.cs`, `ConstantineHub.csproj`,
`ConstantineHub.SmokeTests/Program.cs`, this Pulse and the historical `Hubs_Pulse.md` pointer.

## Current status of earlier fixes

| Area | Status / evidence |
| --- | --- |
| Tunnel MCP plugin install | Fixed in 0.2.6: Windows execute-bit rejection bypassed only for the exact known export error; staged install, executable hint, preserved TOML options/permissions and rollback tested. Installer regressions pass again in this pass. |
| Settings | Fixed in 0.2.5: owner-lived menu and queued action after close. Native portable open/close PASS; 15 repeated cycles and allowlist dispatch PASS. New shortcuts use direct handlers. |
| Start | Fixed in 0.2.5: collapsed-card action dispatch. All three collapsed/expanded Start regressions PASS; user confirms 0.2.6 works. |
| Stop / Restart | Dispatch regression PASS for all three adapters. Ownership/lifecycle code unchanged; full live engine lifecycle is not certified by these dispatch tests. |
| Connection status | Fixed in 0.2.6: offline bridges and stopped tunnels are distinct from setup/service failures. Native and automated status checks PASS. |
| Updater | Stable 0.2.7 published; main CI build/package PASS and downloaded ZIP SHA-256 matches. Protocol, rollback and asset names unchanged. User confirmed 0.2.6 works; installed 0.2.7 update acceptance remains a user-side check. |

## QA evidence and remaining checks

PASS: Release build and portable win-x64 publish; canonical resolution under
relocated paths with spaces, relative Blender config, environment override,
conflicting roots, missing root/skills/contracts and missing target-project cases.
PASS: native portable Skills click opened Explorer at the actual canonical skills
directory; Contracts click opened Explorer at actual Tools_C root; Diagnostics
recorded both paths. Native missing-root click showed a readable warning, logged
the path/exception and returned to a responsive Hub after OK. Native Settings
open/close and dashboard presentation PASS. Existing UI/install regressions PASS;
buttons fit the minimum window size. No drive-specific literal in new UI/action code.

GitHub PR workflow `37022506054` and main release workflow `37022693428`: PASS.
Downloaded release payload contains Hub 0.2.7 at the expected code commit, Updater,
Tomlyn and the bundled Godot adapter. ZIP SHA-256:
`0fef488edea055b330ff2a348a74b0d475709adfbcabcee56ccdd42189432480`.
Native Explorer/error/Settings checks used the local portable publish. Execution of
the downloaded GitHub executable was SKIP: automatic approval review blocked the
combined download-and-launch command without a more specific reason. The archive
was instead verified read only; installed-release acceptance remains below.

Open reproducible bugs in this scoped pass: none observed. Remaining QA gates:
installed 0.2.7 auto-update and retained settings; full Blender/Godot Start/Stop/Restart
with engines/bridges running; mixed-monitor/accessibility font scaling; updater
interruption/rollback. These are pending checks, not newly demonstrated defects.

## Source state and next step

Only the listed feature/docs files belong to this pass and are committed for GitHub
delivery. The original `E:\MyCreations\Constantine-Hub` checkout has unrelated
modified/untracked docs, source and installed release files; they are preserved and
excluded from the feature commits. Tools_C was read only and remains the sole
canonical knowledge source. No deployment/router/cache knowledge was edited.

Next recommended step: update the installed Hub to 0.2.7 using Check Updates,
click Skills / Contracts, then perform the remaining live engine lifecycle QA.

## History — status before this pass

**Skills / Contracts — not started** was the previous state before work began on
2026-10-02. It is historical only; Current State above records the implemented result.
Earlier project history remains in `Hubs_Pulse.md`; its old QA gates do not override
the dated Current State here.
