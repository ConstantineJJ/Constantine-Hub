# Constantine Hub — UI Pass Brief

**Target milestone:** next UI pass after stable `v0.1.5`  
**Repository:** `ConstantineJJ/Constantine-Hub`  
**Status:** design brief / implementation context

## Goal

Evolve Constantine Hub from a functional MCP control panel into a polished, compact, premium engineering tool UI while preserving all current behavior, safety rules, adapter ownership semantics and update reliability.

The immediate visual base is **`UI_Small`**.  
The two larger concepts — **`UI_medium`** and **`UI_big_Large_Full`** — are **future vision references**, not exact layouts to reproduce now.

The app must remain primarily a reliable local control plane, not become a decorative dashboard that hides operational state.

---

## Small context for the next agent

Current stable baseline is **Constantine Hub `v0.1.5`**.

Already live and managed by Hub:

- **Local Files MCP** — local filesystem access inside explicit allowed roots;
- **Blender MCP** — Blender bridge, tunnel, skills router verification;
- **Godot MCP** — project plugin, editor bridge, runtime bridge, tunnel and canonical skill/contract checks;
- **self-updater** — Stable GitHub Release update path was live-tested successfully from `v0.1.4 -> v0.1.5`.

`Tools_C` remains the canonical source for skills, contracts, workflows and QA policy. Do not duplicate canonical rules into Hub.

Before editing, read **`Hubs_Pulse.md`** and treat it as the current project handoff/state record.

### Use our tools

Use the connected tools instead of asking the user to manually shuttle files when possible:

- **Local Files MCP** for local repository inspection and edits under allowed roots, especially the working tree at `E:\MyCreations\Constantine-Hub`;
- **GitHub connector** for repository state, branches, commits, pull requests, diffs, CI runs and Releases;
- use Blender/Godot MCP only when their live runtime evidence is relevant to the UI or adapter behavior.

### Repository/update workflow

Preferred change flow:

`local review/edit -> branch -> commit -> PR -> CI -> merge main -> GitHub Release -> Hub self-update`

Do **not** bypass the updater architecture by manually replacing user config/state files.

Release packaging must continue to update **application payload only**. Machine-local settings/logs/state under AppData must remain untouched.

After meaningful work, update **`Hubs_Pulse.md`** with:

- what changed;
- what was tested;
- failures/regressions encountered;
- remaining risks;
- recommended next step.

---

## Visual references

### Immediate layout base

**`UI_Small`**

Use it as the practical basis for the next implementation pass.

Desired characteristics:

- dark premium interface;
- compact information density;
- clear MCP status cards;
- strong hierarchy;
- warm orange/gold accent language;
- green/amber/red reserved primarily for state/health;
- minimal decorative glow;
- engineering-tool feel rather than game launcher or marketing landing page.

### Future vision references

**`UI_medium`** and **`UI_big_Large_Full`**

Treat them as inspiration for later phases:

- project dashboard;
- skills browser;
- environment cards;
- evidence preview;
- workflow pages;
- documentation/search shell;
- richer project navigation.

Do not reproduce their large hero/dashboard composition 1:1 in this pass.

---

## Mascot / icon usage

Two watermelon-cat assets are intentionally used differently.

### Regular watermelon cat

Use for:

- application icon;
- taskbar icon;
- tray icon;
- window icon;
- release/app identity where a compact readable silhouette is needed.

### Waving watermelon cat

Use **inside the application** as a mascot, for example:

- Home/dashboard welcome area;
- empty states;
- ready/success state;
- onboarding/help hints.

Do not use the waving mascot as the primary system/tray icon unless a later design decision explicitly changes this.

---

## Scope

### 1. App shell

Introduce a compact shell inspired by `UI_Small`:

- left navigation;
- top bar;
- main content region;
- optional right-side status rail only where it improves clarity;
- collapsible or tabbed log/diagnostics area.

### 2. Home / dashboard

The default page should provide a concise operational overview rather than a marketing hero.

Show only information that helps the operator answer:

- what project/profile is active;
- which adapters are healthy;
- what needs attention;
- whether an update is available;
- what quick actions are safe to run now.

### 3. MCP adapter cards

Preserve controls and state for:

- Local Files MCP;
- Blender MCP;
- Godot MCP.

Each adapter should support **compact / expanded** presentation.

Compact view should expose only critical state. Expanded view may expose bridge/profile/tunnel/skills/contracts details and adapter-specific actions.

### 4. Top-level information

Expose cleanly:

- app version;
- update state;
- current project/profile;
- Settings access;
- global service health.

### 5. Logs

Keep logs readable and operational.

Preferred behavior:

- Normal / Verbose remains available;
- log panel can collapse, expand or live in a dedicated tab/pane;
- Save Log remains available;
- do not enlarge log font unnecessarily;
- do not hide errors that are needed for diagnosis.

---

## Must keep

The following are invariants for the UI pass:

- Local Files MCP functionality and allowlist model;
- per-root `Read / Write / Delete` permissions;
- Hub-owned vs external/adopted tunnel distinction;
- Hub must never kill an external/adopted process it did not start;
- Blender MCP bridge/tunnel lifecycle;
- Blender 8/8 deployed skill-router verification;
- Godot project plugin installation/update behavior;
- Godot editor/runtime bridge state;
- Godot canonical Tools_C skills/contracts verification;
- Start / Stop / Restart / Doctor semantics;
- Start All / Stop Managed semantics;
- system tray behavior;
- Stable self-update flow;
- SHA-256 verification;
- updater rollback/startup handshake;
- AppData config/log/state preservation;
- current release/CI workflow unless a reviewed change intentionally improves it.

The UI may change presentation, not silently change these semantics.

---

## Must change

### Near-term UI improvements

- integrate the regular watermelon cat as app/tray/window icon;
- integrate the waving cat as an in-app mascot;
- use `UI_Small` as the visual/layout direction;
- reduce visual noise and improve spacing consistency;
- remove brittle fixed-size text containers where possible;
- ensure high-DPI text never clips;
- make buttons DPI-safe / content-aware rather than relying on brittle fixed widths;
- avoid requiring vertical scrolling just to see the three main adapter summaries on the normal window size;
- make long values such as paths/contracts gracefully ellipsize or expose tooltip/details instead of breaking layout;
- clearly distinguish healthy / idle / degraded / failed states.

### Information architecture

Prepare navigation structure that can later support:

- Home;
- Skills;
- Blender Pipeline;
- Export & Validation;
- Godot Integration;
- MCP & Deployment;
- Projects;
- Tools;
- Documentation;
- Evidence & QA.

Only implement sections that provide real value now. Placeholder-only pages should be avoided unless they serve navigation testing.

---

## Risks

### 1. UI rewrite breaking runtime logic

The largest risk is mixing layout work with adapter lifecycle changes.

Mitigation: keep adapter/core services independent from visual controls and avoid rewriting runtime behavior unless required by a reproducible bug.

### 2. DPI regressions

The project already encountered repeated text clipping from fixed pixel geometry.

Mitigation:

- prefer `AutoSize`, measured font metrics, sensible minimums and DPI-aware layout;
- test normal Windows scaling and the user's actual high-DPI environment;
- avoid solving clipping by repeatedly adding arbitrary pixels without identifying the layout constraint.

### 3. Dashboard bloat

The large concept arts can encourage unnecessary panels and decorative content.

Mitigation: every visible card should answer a concrete operational question.

### 4. Update regression

The updater is now working and is infrastructure-critical.

Mitigation: UI work must not move/delete updater payload files, change release asset names or mutate the Stable update protocol without explicit review and QA.

### 5. Hidden state

Collapsed cards/logs can accidentally hide failures.

Mitigation: critical degraded/error states must remain visible at the shell/dashboard level even when detail panels are collapsed.

---

## Acceptance criteria

The UI pass is accepted only when all of the following are true:

1. The app launches with the new UI and all existing adapters are still recognized.
2. Local Files / Blender / Godot can still be started and stopped through Hub.
3. External/adopted process ownership safety is unchanged.
4. The three primary MCP summaries fit in the intended normal layout without clipped text.
5. No status text is vertically clipped at the user's Windows DPI.
6. Long labels/paths do not collide with values or buttons.
7. Regular watermelon-cat icon appears correctly in window/taskbar/tray.
8. Waving watermelon cat is used inside the app without becoming distracting.
9. Normal/Verbose logs and Save Log still work.
10. Stable updater still detects and installs a later release.
11. `%APPDATA%` / `%LOCALAPPDATA%` machine settings survive update unchanged.
12. CI passes for Hub + Updater + bundled Godot payload.
13. A PR diff is reviewed before merge.
14. `Hubs_Pulse.md` is updated with implementation results and live QA evidence.

---

## Suggested implementation order

1. Read `Hubs_Pulse.md` and current `main`.
2. Inspect current UI code through Local Files MCP.
3. Create a dedicated UI branch.
4. Separate/refactor visual shell code only where necessary; do not rewrite adapters casually.
5. Integrate icon assets.
6. Implement shell + navigation + adapter-card layout.
7. Implement compact/expanded card behavior.
8. Implement collapsible/tabbed log presentation.
9. Run local structural review using Local Files MCP.
10. Push branch and open PR using GitHub connector.
11. Review PR diff and CI.
12. Merge only after CI passes.
13. Let GitHub Actions publish the next Release.
14. Update the installed Hub through its own updater for live QA.
15. Record evidence and remaining issues in `Hubs_Pulse.md`.

---

## Pulse note template

Append/update `Hubs_Pulse.md` with something like:

> **UI Pass** — Implemented `UI_Small`-inspired shell while preserving adapter lifecycle and updater semantics. Added watermelon-cat app/tray icon and waving in-app mascot. Verified high-DPI layout, compact/expanded MCP cards, log behavior, Local Files/Blender/Godot status, CI and in-app update. Record any unresolved DPI, navigation, updater or adapter regressions explicitly.

---

# Execution prompt for the next agent

Use this as the task prompt:

> Continue development of `ConstantineJJ/Constantine-Hub` from the current stable `v0.1.5` baseline.
>
> First read `Hubs_Pulse.md` and this `Constantine_Hub_UI_pass.md` brief. Do not rely on memory when repository state can be checked directly.
>
> Use **Local Files MCP** for inspection and edits of the local working tree under `E:\MyCreations\Constantine-Hub` when available. Use the **GitHub connector** for repository state, branch/commit/PR/diff/CI/Release work. Prefer those tools over asking the user to copy file contents manually.
>
> Goal: integrate the `UI_Small` design direction as the practical new Constantine Hub visual base while preserving all current MCP functionality, process-ownership safety and updater behavior. Treat `UI_medium` and `UI_big_Large_Full` only as future vision references, not exact layouts.
>
> Use the regular watermelon-cat asset as the app/window/taskbar/tray icon. Use the waving watermelon cat inside the application as a mascot/welcome/success element.
>
> Build a compact product shell with left navigation, top status area, main dashboard, compact/expanded Local Files / Blender / Godot MCP cards, and a collapsible or tabbed diagnostics/log area. Keep the UI operational and readable rather than decorative.
>
> Preserve: Local Files allowlist permissions, external/adopted ownership protection, Blender bridge/tunnel/skills behavior, Godot plugin/editor/runtime/tunnel/contracts behavior, Start/Stop/Restart/Doctor semantics, tray support, Stable updater, SHA-256 verification, rollback and AppData config preservation.
>
> Pay special attention to Windows high-DPI behavior. Avoid brittle fixed row heights/widths for text and buttons. No status text should clip vertically or horizontally on the user's current display scaling.
>
> Work in a branch. Review the diff before merge. The expected delivery flow is:
>
> `Local review/edit -> branch -> commit -> PR -> CI -> merge main -> GitHub Release -> in-app Hub update`
>
> Do not manually overwrite machine-local user configuration. Release/update code may replace application payload only.
>
> After implementation, update `Hubs_Pulse.md` with changes, live QA evidence, regressions, remaining risks and the next recommended step.
