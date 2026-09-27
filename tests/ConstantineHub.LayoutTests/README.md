# Hub layout regression checks

Run on Windows with .NET 10:

```powershell
dotnet run --project tests/ConstantineHub.LayoutTests -c Release -- artifacts/layout
dotnet run --project tests/ConstantineHub.LayoutTests -c Release -- artifacts/layout --native
```

The harness creates the actual MainForm controls in layout-preview mode. It does
not construct adapters, read/write machine settings, start timers, create a tray
icon, launch engines, contact bridges, or start/stop tunnel processes.

Checks cover all 13 status values, labels, buttons and their containing panels;
long status text; reachability of the last Godot row through scrolling; constant
log font and viewport height as card content grows. Native mode additionally
changes the font and window width after handle creation.

The deterministic matrix uses native WinForms text measurement/rendering with
physical sizes and fonts scaled to simulate 96/120/144/192 DPI, UI fonts of
15/18/22.5 pt, and 900/1280 logical-pixel widths. It is not a claim of moving a
visible window across four physical monitors. Native mode uses PerMonitorV2 at
the current monitor DPI. PNGs capture the top and bottom of the scroll viewport;
the status values are fixtures, not live connection evidence.

Manual acceptance still includes moving the Hub between monitors with different
DPI and changing Windows accessibility text size. Engine/runtime acceptance is
separate from these layout checks.
