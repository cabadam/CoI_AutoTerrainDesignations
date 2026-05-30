# Mining Designations — Architecture Reference
Current as of release: 0.4.0k

## Feature summary

ATD's original core feature set centers on automatic mine-tower excavation planning:

- scan a tower area for candidate products
- decide which 4×4 designation cells are worth digging
- connect reachable regions
- generate an access ramp when possible
- place mining designations
- expose ore composition and excavator priority controls in the inspector

This document covers the pre-farming mining systems that existed through the 0.3.0 line.

## Source files

| File | Role |
|---|---|
| `ATD.Scan.cs` | Main scan and designation-placement pipeline |
| `ATD.RampGeneration.cs` | Ramp candidate collection, validation, and placement |
| `ATD.Cleanup.cs` | Clear, fulfilled cleanup, and leftover cleanup helpers |
| `ATD.DesignationPanel.cs` | Main mine-tower ATD panel |
| `ATD.OreCompositionPanel.cs` | Ore-composition analysis and inspector UI |
| `ATD.PrioritySync.cs` | Sticky per-tower excavator priority propagation |
| `ATD.Settings.cs` | Global settings load/save/migration and purity arrays |
| `ATD.IdleVehicleRelease.cs` | Idle vehicle release and restore logic |
| `ATD.Api.cs` | Supported public API surface |

## Scan pipeline

The main entry point is `CreateDesignationsCoroutine(IAreaManagingTower tower, bool generateRamps, object? inspectorInstance = null)` in `ATD.Scan.cs`.

High-level flow:

1. Resolve the tower area and snap the scan bounds to 4×4 designation origins.
2. Collect debris origins already present in the area.
3. Resolve candidate scan products from the tower ore filter or Auto mode.
4. Sample every 1×1 terrain tile inside each 4×4 designation cell.
5. Build `resourceDetailsByTile` and `productCounts` for the sampled cells.
6. Apply the tower settings:
   - `MaxHeightDiff`
   - `MaxLayersToExcavate`
   - `MaxDepthToDigTo`
   - `OrePurityLevel`
   - `CorridorClearance`
7. Reject poor tiles using three purity-related criteria:
   - minimum ore purity ratio
   - minimum ore height
   - minimum bottom density
8. Filter isolated regions.
9. Fill the rectilinear hull and add corridors according to the clearance setting.
10. Optionally flatten the bottom when `BottomFlatteningEnabled` is on. The target depth-percentile is controlled by `BottomFlatteningStrength` (1–10, default 5): strength 10 selects the deepest tile (index 0), strength 5 selects the median (index `Count/2`), strength 1 selects the 90th-percentile depth (index `9*Count/10`). In lower-only mode (purity = Off) tiles are only ever pushed deeper; in leveling mode they are set to the target regardless of direction.
11. Build and smooth corner heights.
12. Place mining designations with `TerrainDesignationsManager.AddOrReplaceDesignation`.
13. Try to generate an access ramp if enabled.
14. Refresh any linked ore-composition panel.

## Product targeting

ATD supports two targeting modes:

- explicit per-tower product selection through the scan filter picker
- Auto mode, which scans useful mineable products first and falls back to cleanup-oriented behavior when needed

The Auto mode is intentionally pragmatic rather than purely ore-only. Through the 0.2.x line it also grew support for debris cleanup and dirt-like cleanup fallback behavior.

## Per-tower settings

ATD stores tower overrides separately from the global defaults. The inspector edits tower-local values; `ATDsettings.json` controls the global defaults used when a tower has not been customized.

Per-tower settings in practice:

| Setting | Effect |
|---|---|
| `MaxHeightDiff` | Limits corner-to-corner roughness between neighboring designation cells |
| `RampWidth` | Controls ramp width; `0` disables ramp generation |
| `MaxLayersToExcavate` | Caps excavation depth relative to the current surface |
| `MaxDepthToDigTo` | Caps excavation at an absolute elevation |
| `OrePurityLevel` | Selects a preset of ore-quality thresholds |
| `CorridorClearance` | Controls corridor widening and connectivity constraints |

## Purity presets

The purity system is backed by four arrays loaded from settings or built-in defaults:

- `minOreHeight`
- `minBottomOreDensity`
- `minOrePurity`
- `minComponentSize`

Each array is indexed by purity level `0..4`.

These arrays are not exposed as first-class API. They are a tunable implementation detail, adjusted through settings and console commands.

## Ramp generation

Ramp generation lives in `ATD.RampGeneration.cs` and is invoked after the main designation region has been decided.

Key points:

- ramps are planned from the current dig layout, not from a separate player-authored path
- the planner builds candidate corridors and scores them
- nearby buildings are treated as occupied tiles and avoided
- the planner can return multiple warning outcomes, not just success/failure

`RampPlacementOutcome` values:

| Outcome | Meaning |
|---|---|
| `Failed` | No valid ramp corridor was found |
| `Truncated` | A ramp was placed but did not reach the surface |
| `Crested` | Ramp placement succeeded cleanly |
| `NotAccessible` | A ramp exists but a valid path from the tower could not be confirmed |

The designation panel exposes these outcomes as a warning icon with a tooltip message.

## Cleanup behavior

`ClearDesignationsForTower` only removes mining designations. It does not blindly clear every terrain designation in the tower area.

Other cleanup helpers exist for ATD's own lifecycle:

- remove fulfilled designations
- remove isolated leftover mining tiles after ramp cleanup

This conservative cleanup behavior is intentional so ATD does not destroy unrelated player or mod-authored terrain work during a normal clear action.

## Ore Composition panel

`ATD.OreCompositionPanel.cs` is independent from the scan-and-place feature.

Important behavior:

- it reads the tower's current `ManagedDesignations` live each time it refreshes
- it excludes dumping designations from the composition scan
- it estimates quantities using the matching `TerrainMaterialProto` and the live mined-quantity multiplier
- it therefore reflects the current Ore Mining Yield difficulty setting
- it works with any current designations, not just ATD-generated ones

The panel also exposes excavator-priority actions for vanilla `MineTower` instances.

## Excavator priority sync

`ATD.PrioritySync.cs` stores a preferred product per tower entity ID and reapplies it to newly assigned excavators.

Behavior:

- if a tower has a stored preferred product, newly assigned excavators that do not already have a product priority inherit it
- on startup/load, ATD can bootstrap a tower preference from the existing assigned excavators if more than half of them already prioritize the same product

This turns excavator priority into a tower-scoped sticky setting instead of a one-off manual action on individual excavators.

## Settings persistence

Global defaults live in `ATDsettings.json`.

`ATD.Settings.cs` handles:

- first-run generation of the settings file
- migration from legacy `settings.json`
- version-stamped file migration when the mod version changes
- preserving user values while adding or updating documented defaults

The file also includes inline documentation comments for end users.

## Public API boundary

The stable public surface for these mining systems is intentionally small and lives in `ATD.Api.cs`.

Supported API categories:

- create and clear mining designations for a tower
- get/set ore filter
- get/set per-tower designation settings
- build and refresh the main ATD inspector panels

Not public API:

- scan heuristics
- purity-array semantics

## Idle vehicle release

`ATD.IdleVehicleRelease.cs` implements automatic vehicle unassignment and re-assignment per mine tower.

### Runtime state

`s_idleReleasedVehiclesByTower` is a `Dictionary<EntityId, List<Vehicle>>` keyed by tower entity ID.

- A key being present means the tower is currently in the released state.
- The value is the list of vehicles that were unassigned; it may be empty if the tower had no vehicles at release time.
- The dictionary is cleared by `ClearIdleVehicleReleaseState()` on world reset.

### Tick behavior

`TickIdleVehicleRelease()` is called in the farming-sync timer block inside `AutoTerrainDesignationsTicker`, once per `FARMING_SYNC_INTERVAL_GAME_SECONDS` (~1 s). For each live `MineTower`:

1. If the feature is disabled for the tower and the tower is in the released state, vehicles are restored immediately.
2. If the feature is enabled and there are no pending excavation jobs, `ReleaseIdleVehicles` is called.
3. If the feature is enabled and pending excavation work exists, `RestoreIdleReleasedVehicles` is called.

### Pending-work check

`HasPendingExcavationJobs(MineTower)` iterates `ManagedDesignations`. A designation counts as pending when its proto matches the mining or leveling designator proto and `IsMiningNotFulfilled` is true. The method returns `true` (safe, do not release) if the proto references are not yet initialized.

### Release

`ReleaseIdleVehicles` snapshots `AllVehicles`, calls `UnassignVehicle(vehicle, cancelJobs: true)` for each non-destroyed vehicle, and stores the list under the tower's entity ID. An empty list is stored if the tower had no vehicles, which still marks the tower as in the released state.

### Restore

`RestoreIdleReleasedVehicles` iterates the stored list and calls `AssignVehicle` for each vehicle that is not destroyed, not already assigned elsewhere (`vehicle.AssignedTo.HasValue`), and not already in `AllVehicles`. The dictionary entry is removed after restore regardless of how many vehicles were successfully re-assigned.

### Per-tower vs global default

`IsIdleVehicleReleaseEnabledForId(EntityId)` checks `s_towerSettingsByEntityId` first; if no per-tower entry exists it falls back to `AutoTerrainDesignationsMod.AutoReleaseVehiclesWhenIdle`. This means the global default applies to towers that have never been customized, and the inspector toggle writes a per-tower override that shadows the default for that specific tower.

`SetTowerAutoReleaseWhenIdle(IAreaManagingTower, bool)` in `ATD.State.cs` calls `TryRestoreIdleReleasedVehiclesForTower` when the value is set to `false`, so disabling the toggle triggers an immediate restore.
- ramp scoring internals
- ore composition internals
- priority bootstrap logic