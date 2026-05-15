# Farmland Preparation

## What it does

Flat level designations normally tell mine towers to move terrain to a target height, but the resulting top layer may contain rock, gravel, or other non-farmable material. Farmland Preparation automates the full workflow so the final surface is ready for farming crops.

The mod works in two steps:

1. **Preparation** — Temporarily lowers the dig target one layer below the farm height where needed, so excavators remove non-farmable material from the future topsoil band. The original designation is restored once the band is clear.

2. **Filling** — Once preparation is complete, restricts the tower's dump rules to farmable products (dirt, compost, and similar) so trucks fill the topsoil band with farmable material only. When all origins are done the dump rules are restored automatically.

Vehicle access ramps are added automatically when excavators or trucks cannot reach a work area.

## How to use it

1. Draw one or more **flat level designations** inside a mine tower's area. All four corners of each 4×4 designation tile must be at the same target height.

2. Select the mine tower and open its inspector panel. Scroll down to **Farmland Preparation**.

3. Enable the **Farmland Preparation Automation** toggle. The mod takes it from there.

4. You can close the inspector — automation continues running in the background.

5. To pause, disable the toggle. All temporary modifications are restored immediately: original level designations reappear and dump rules return to their previous state.

## Status phases

| Phase | Meaning |
|---|---|
| `NeedsPreparation` | Non-farmable material is in the future topsoil band. Temporary dig being placed. |
| `NeedsLeveling` | Surface is at or above target but farmable. Normal leveling will finish it. |
| `Preparing` | Temporary dig designation is active; waiting for excavators to clear the band. |
| `ReadyForFilling` | Preparation is complete; waiting for tower-level filling to begin. |
| `Filling` | Farmable fill designation is active; waiting for trucks to complete the fill. |
| `Done` | Top layer is already farmable at the target height. No work needed. |
| `Blocked` | Something prevented normal progress (e.g. a designation was externally removed). |

## Things to know

- Only **flat** level designations participate. Designations with different corner heights are ignored.
- The tower's dump rules are modified only during filling, and only for that tower.
- The mod never changes global (cross-tower) dump rules.
- Automation state is not saved. After reloading a save, re-enable the toggle to resume.
- If you manually remove or replace a tracked designation, the mod drops that tile from the session. Place a new flat level designation and the next scan will pick it up.
- When extending a farming area, be careful to place new designations at the correct target level. Otherwise, new designations may attach to an adjacent area that is still in preparation one level below the intended elevation.

## Settings

### `reEnableFarmingOnLoad` (ATDsettings.json)

Controls whether ATD automatically re-enables Farmland Preparation Automation after loading a save for towers whose managed designations look like farmland work: non-empty and made up entirely of flat level designations.

Default: `true`

Set this to `false` if you prefer to always enable the toggle manually after a reload, or to avoid unintended re-activation on towers with flat level designations that are not intended for farming.

Can also be changed at runtime — see console commands below.

### `farmingPanelCollapsed` (ATDsettings.json)

Controls the default collapsed state of the **Farmland Preparation** panel in the mine tower inspector.

Default: `true` (collapsed)

Set this to `false` if you prefer the panel to start expanded whenever you open a mine tower inspector.

Can also be changed at runtime — see console commands below.

## Console commands

Open the in-game developer console (default: **F8** or **~**) to run these.

| Command | What it does |
|---|---|
| `atd_set_re_enable_farming_on_load true\|false` | Toggles the re-enable-on-load setting at runtime. Change is saved to `ATDsettings.json`. |
| `atd_set_farming_panel_collapsed true\|false` | Sets whether the **Farmland Preparation** panel starts collapsed by default. Change is saved to `ATDsettings.json`. |
| `atd_farming_analyze_origin x y` | Prints the read-only farming analysis for the designation at tile (x, y). Coordinates snap to the 4×4 designation origin. Useful for checking why a tile is `Blocked` or `NeedsPreparation`. |
| `atd_farming_dump_all` | Prints full session state and terrain analysis for every mine tower. Useful for a broad overview of what all towers are doing. |

The following commands are debug tools for single-origin testing, not intended for normal play:

| Command | What it does |
|---|---|
| `atd_farming_prepare_origin x y` | Manually places the temporary preparation designation for one `NeedsPreparation` origin. |
| `atd_farming_restore_origin x y` | Restores the original level designation stored by the prepare command above. |

