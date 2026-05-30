# Farming Designations — Architecture Reference
Current as of release: 0.4.0l [unreleased]

## Feature summary

Farming Designations automates the preparation and topsoil filling of flat level designations so the resulting surface is farmable. It runs as a background per-tower session driven by `AutoTerrainDesignationsMod.onSimUpdate()`, which is registered with `simLoopEvents.Update` and therefore executes on the **simulation thread** once per game-second (`Duration.OneSecond`). Each tower gets its own `FarmingPreparationSession` (keyed by `EntityId`), which holds a `FarmingOriginSession` per tracked designation. The inspector panel is a control and status surface only; closing it does not pause automation.

---

## Source files

| File | Role |
|---|---|
| `ATD.FarmingAnalysis.cs` | Read-only per-origin analysis; produces `FarmingAnalysisRow` |
| `ATD.FarmingPreparationSession.cs` | Session data structures, tick loop, state transitions, save hook |
| `ATD.FarmingFillActivation.cs` | Stage 4 — filling: dump rule manipulation, vehicle clear-out, fill designation activation, rim alignment |
| `ATD.FarmingAccess.cs` | Access ramp placement (preparation and filling phases), result caching |
| `ATD.FarmingDebugTransitions.cs` | Shoulder placement, preparation transitions; debug console commands (`atd_farming_*`) |
| `ATD.FarmingAnalysisPanel.cs` | UI: injects the Farmland Preparation panel into the mine tower inspector |

All farming code lives inside the `AutoDepthDesignation` partial class (`namespace AutoTerrainDesignations`), except the session data classes and the panel helper.

---

## State model

There are two distinct state enums. Do not confuse them.

### `FarmingAnalysisState` — read-only terrain snapshot

Produced fresh on every analysis pass from live terrain and designation data.
Never stored in the session.

| Value | Meaning |
|---|---|
| `Done` | Surface is at target height and the topsoil band is fully farmable |
| `NeedsLeveling` | Surface is at or above target; farmable band is clear; keep the level designation active |
| `ReadyForFilling` | Preparation cleared the band; topsoil fill can begin |
| `NeedsPreparation` | Non-farmable material exists in the future topsoil band, or surface is below `targetHeight - 1` |
| `SkippedNonFlat` | Level designation has unequal corner heights; not captured |

### `FarmingOriginPhase` — per-origin session state

Persisted in `FarmingOriginSession` across ticks.

| Value | Meaning |
|---|---|
| `AnalysisLeveling` | Captured; terrain analysis says `NeedsLeveling`; the original level designation stays active; waiting for vanilla leveling |
| `Preparing` | Surface was below preparation level or had non-farmable material in the topsoil band; temporary preparation designation placed at `targetHeight − 1`; waiting for excavators |
| `ReadyForFilling` | Preparation done; level designation hidden (`IsHiddenUntilFilling = true`); queued for tower-level filling |
| `Filling` | Original fill designation restored at `targetHeight`; waiting for trucks |
| `Done` | Topsoil band validated as farmable by the filling pass; designation hidden; origin is complete. Only reachable through the filling pass — analysis-time farmable tiles enter `ReadyForFilling` first. |
| `Blocked` | Progress failed (missing designation manager, placement failed, etc.) |

### Per-origin phase diagram

```mermaid
stateDiagram-v2
    [*] --> AnalysisLeveling: flat level designation captured

    AnalysisLeveling --> AnalysisLeveling: analysis = NeedsLeveling
    AnalysisLeveling --> Preparing: analysis = NeedsPreparation / place z-1 preparation designation + shoulders
    AnalysisLeveling --> ReadyForFilling: analysis = ReadyForFilling or Done / hide original designation
    AnalysisLeveling --> Blocked: missing manager or placement failure

    Preparing --> Preparing: analysis still below preparation level
    Preparing --> ReadyForFilling: preparation level reached / hide designation
    Preparing --> Blocked: placement or restore failure

    ReadyForFilling --> Filling: tower filling phase begins / restore original designation
    Filling --> Filling: farmable band not complete
    Filling --> Done: filling analysis validates farmable band

    Done --> [*]: session completes or is restored
    Blocked --> [*]: automation disabled or session restored
```

### Tower tick-loop diagram

```mermaid
flowchart TD
    Tick[onSimUpdate: once per game-second] --> Enabled{Session enabled and active?}
    Enabled -- No --> Stop[Skip tower]
    Enabled -- Yes --> FillingOwned{Any origin Filling<br/>or tower dump rules owned?}

    FillingOwned -- No --> Prep[Preparation pass]
    Prep --> Capture[Capture new flat level designations]
    Capture --> Advance[Analyze and advance origins]
    Advance --> PrepAccess[Ensure mining access ramps]
    PrepAccess --> PrepComplete{All origins ReadyForFilling or Done?}
    PrepComplete -- No --> Stop
    PrepComplete -- Yes --> BeginFill[BeginFarmingFillingForSession]

    FillingOwned -- Yes --> Fill[Filling pass]
    BeginFill --> Fill
    Fill --> ClearOut[Wait for vehicles to leave fill area]
    ClearOut --> Cleanup[Remove shoulders and preparation ramps]
    Cleanup --> Rim[Place rim alignment designations]
    Rim --> FillAccess[Ensure dumping access ramps]
    FillAccess --> FillAdvance[Analyze filling origins]
    FillAdvance --> FillComplete{All origins Done?}
    FillComplete -- No --> Stop
    FillComplete -- Yes --> Stabilize[Stabilize, restore dump rules, reassign vehicles]
    Stabilize --> Stop
```

### Filling handoff diagram

```mermaid
sequenceDiagram
    participant Prep as Preparation pass
    participant Session as FarmingPreparationSession
    participant Tower as Mine tower
    participant Vehicles as Assigned vehicles
    participant Terrain as TerrainDesignationsManager
    participant Fill as Filling pass

    Prep->>Session: all origins are ReadyForFilling or Done
    Prep->>Session: BeginFarmingFillingForSession()
    Session->>Vehicles: park vehicles outside pending fill area
    Session->>Tower: release empty trucks
    Session->>Tower: snapshot dump rules and allow farmable products only
    Session->>Terrain: restore queued original level designations
    Session->>Session: mark queued origins Filling

    loop each filling tick
        Fill->>Vehicles: wait until clear-out is complete
        Fill->>Terrain: remove preparation shoulders and ramps
        Fill->>Terrain: place or track rim alignment designations
        Fill->>Terrain: place dumping access ramps if needed
        Fill->>Session: analyze filling origins
    end

    Fill->>Session: all origins Done
    Session->>Terrain: remove rim alignment designations
    Session->>Session: wait stabilization timer
    Session->>Tower: restore dump rules and released trucks
    Session->>Terrain: remove filling access ramps
    Session->>Session: clear Active flag
```

---

## "Flat" designation definition

Only level designations where all four `DesignationData` corner heights are equal (within `FARMING_HEIGHT_EPSILON = 0.05f`) are captured. Non-flat designations are analyzed as `SkippedNonFlat` and never enter a session.

---

## Topsoil band

The target band is the 1-tile layer from `targetHeight - 1` to `targetHeight`.

Analysis walks each of the 16 cells in the 4×4 designation tile and checks:

- `farmableThickness` in that band (from `TerrainMaterialProto.IsFarmable`)
- `nonFarmableThickness` — any positive value > `FARMING_HEIGHT_EPSILON` marks the cell as non-farmable

Thresholds:

```csharp
const float FARMING_HEIGHT_EPSILON   = 0.05f;
const float MIN_FARMABLE_THICKNESS   = 0.9003906f;   // from FarmFertileGroundValidator
```

An origin is `Done` when: surface equals `targetHeight` (within epsilon) **and**
`minFarmableThickness >= MIN_FARMABLE_THICKNESS` across all 16 cells.

---

## Farmable dump products

ATD discovers the set of dumpable farmable products dynamically at runtime:

```
LooseProductProto where LooseProductProto.TerrainMaterial.Value.IsFarmable == true
```

This handles dirt variants, compost, and any future or modded farmable materials without hard-coding names. The set is used to restrict the tower's `DumpableProducts` during filling.

---

## "Hide until filling" pattern

When an origin reaches `ReadyForFilling` or is already `Done`, ATD removes its visible level designation from the terrain manager:

```csharp
s_desigManager.RemoveDesignation(originState.Origin);
originState.IsHiddenUntilFilling = true;
```

The original `DesignationData` is still stored in `FarmingOriginSession`. When the tower enters the filling phase, the original level designations are restored for all queued origins in one pass.

This ensures the game's own fill-completion detection can see the level designations and mark them done after trucks finish.

---

## Tick loop

Every tick, each enabled session runs one of two passes:

- **Preparation pass** — if no `Filling`-phase origins are active and tower dump rules are not owned.
- **Filling pass** — if at least one origin is in `Filling`, or the tower dump rules are currently owned by the session.

When the preparation pass finishes with all origins `ReadyForFilling` or `Done`, `BeginFarmingFillingForSession` is called automatically.

---

## Preparation pass

1. **Capture** (`CaptureCurrentFlatFarmingDesignations`): scans all level designations currently managed by the tower; any not yet tracked are added as new `AnalysisLeveling` origins. Rim alignment designations (`RimAlignmentOrigins`) are explicitly skipped so they are never captured as new work.

2. **Advance** (`AdvanceCapturedFarmingOrigins`): for each origin, runs terrain analysis and advances the phase:
   - `AnalysisLeveling`: `NeedsLeveling` → stays; `NeedsPreparation` → see below; `Done` or `ReadyForFilling` analysis result → both transition to `ReadyForFilling` phase; designation hidden, origin waits for filling. (Analysis-time `Done` is not trusted as final because preparation work on adjacent tiles can cause dirt to fall, so the filling pass must confirm it.)
   - `NeedsPreparation`: original designation replaced with a temporary level designation at `targetHeight − 1`. **Shoulders** placed where adjacent terrain drops below that level. Transitions to `Preparing`.
   - `Preparing`: re-analyzed each tick. When the surface reaches preparation level (`NeedsLeveling` or better) → transitions to `ReadyForFilling`, designation hidden.

3. **Access check** (`EnsureFarmingAccessForCurrentPhase`, `isFilling: false`): BFS from the tower's nearest pathable tile. Unreachable designations are grouped into spatially disconnected clusters; one **mining-proto access ramp** is placed per cluster so all clusters are unblocked in the same tick. Results are cached per work-key and re-checked every `FARMING_ACCESS_RECHECK_TICKS = 10` ticks.

Pass returns `true` (keep running) while any origin is `AnalysisLeveling` or `Preparing`, or while access is not yet confirmed.

### Shoulders

Sloped dumping designations placed immediately outside the boundary of a `Preparing` origin on any side where the adjacent terrain edge is below the preparation height. Each shoulder slopes from `preparationHeight` on its inner edge down to `preparationHeight − 1` on its outer edge. Tracked in `PreparationShoulderOrigins`; removed at the start of the filling pass.

Shoulder placement skips any tile tracked as a farming origin by any active session — including hidden origins from other sessions that have no active designation in the manager — to prevent corrupting cross-session origin tracking.

### Preparation ramps

Mining-proto ramp designations placed by the access check. Tracked in `PreparationAccessRampOrigins`; removed at the start of the filling pass.

**Vanilla readiness gate** (`IsFarmingDesignationReadyForVehicleWork`, `isFilling: false`): before collecting perimeter target tiles for a designation, `IsReadyToMineNonAmphibious()` is checked. This mirrors the vanilla excavator dispatch condition. If no designation in a cluster passes — for example, because terrain is entirely above the designation target height on a fresh excavation pad and no mining bits are set yet — the cluster has an empty target-tile set and is marked inaccessible immediately, triggering ramp placement without running the BFS.

Ramp tile eligibility (`IsFreeRampTile`):
- Tiles already occupied by any designation (including designations placed by other sessions) are rejected.
- Active work-phase origins (`AnalysisLeveling`/`Preparing`) of the current session are reserved.
- Hidden (completed) origins of the current session are intentionally **not** reserved — ramps must be able to pass through already-finished neighbours to reach an inaccessible cluster.
- All origins from other sessions (all phases) are reserved to prevent cross-session corruption.
- Ramps placed in previous ticks by this session are also reserved before candidate collection begins.

---

## Transition to filling

Filling begins when every tracked origin is either `ReadyForFilling` or `Done`.

Steps in `BeginFarmingFillingForSession`:

1. Vehicles currently inside the pending fill area are ordered to park outside (while still assigned to the tower, so they can receive a valid park-and-wait job). The session waits until they leave (or until stuck vehicles are detected and ignored).
2. Empty trucks are unassigned from the tower so they don't compete with dump vehicles.
3. Tower dump rules are switched to farmable products only (`TrySwitchTowerToFarmableDumpRulesForFilling`). The original rules are snapshotted for later restore.
4. All queued origins (`ReadyForFilling` and un-activated `Done`) have their original level designation restored at `targetHeight`. Each origin moves to `Filling`.
5. `ActivateFarmingFillingOrigins` records `IsFillingActivated = true`; `IsHiddenUntilFilling` is cleared.

ATD **never** reads or modifies `ITerrainDumpingManager.ProductsAllowedToDump` (global dump rules). Only `MineTower.DumpableProducts` is touched.

---

## Filling pass

Each tick while filling:

1. **Vehicle clear-out**: if a clear-out is still pending, waits until cached vehicles have left the fill area (or ignores stuck ones).
2. **Cleanup**: removes preparation shoulders and preparation ramps.
3. **Rim alignment** (`PlaceFarmingRimAlignmentDesignations`): places flat level designations at `targetHeight` on boundary tiles immediately outside the fill area, where the terrain one step further out is within 0.2 tiles of `targetHeight`. These repair terrain disturbances left by preparation ramps at the boundary. Uses the leveling proto. Tracked in `RimAlignmentOrigins`. Rim placement skips tiles tracked as farming origins by any other active session to prevent overwriting their active leveling designations.
4. **Access check** (`EnsureFarmingAccessForCurrentPhase`, `isFilling: true`): same BFS approach as preparation. Unreachable filling designations are grouped into disconnected clusters; one **dumping-proto access ramp** is placed per cluster. Tracked in `FillingAccessRampOrigins`. The same tile eligibility rules apply as for preparation ramps. **Vanilla readiness gate**: `IsReadyToDumpNonAmphibious()` is used instead of the mining bitmap, matching the vanilla truck dispatch condition. Fresh fill designations (terrain below target) have the dumping bit unset, so the cluster is correctly marked inaccessible until a ramp reaches it.
5. **Advance origins**: for each `Filling` or `Done` origin, checks the designation still exists. Runs `AnalyzeFarmingFillingDesignation`; transitions to `Done` when the farmable band check passes.

### Completion

When all origins reach `Done`:

1. Rim alignment designations are removed immediately.
2. A stabilization timer (`FARMING_FILLING_STABILIZATION_SECONDS`) runs to let any in-flight material settle.
3. After stabilization: tower dump rules restored, released trucks reassigned, filling access ramps removed.
4. A completion notification is shown on the tower.
5. The session's `Active` flag is cleared.

---

## Save / load behavior

ATD does **not** persist session state. On save:

1. A save-start hook iterates active sessions and restores all temporary preparation designations to their original level designations; ramps, shoulders, and rim designations are removed; dump rules and truck assignments are restored.
2. `session.Active` is set to `session.Enabled`.
3. After the save finishes, the ticker re-bootstraps sessions from the current visible state.

This makes farming tolerant of partial progress, player edits between sessions, and save migrations. The cost is that a running session must redo preparation analysis after every reload.

---

## Performance

Large farming saves can produce sessions with 1000–2500 tracked origins, most of which remain inaccessible until excavators reach them. Three independent optimizations limit the per-tick cost.

### Access check throttling

`EnsureFarmingAccessForCurrentPhase` runs a BFS across pathable terrain to classify designations as reachable or not. The BFS is bounded by `MAX_FARMING_ACCESS_SEARCH_TILES = 250000` visited tiles and a search box padded `FARMING_ACCESS_SEARCH_MARGIN_TILES = 96` tiles beyond the tower and designation extents.

Results are cached per **work-key** (an ordered string of origin coordinates and corner heights). The cache expires after a tick interval that scales with the active work-designation count:

| Work count | Recheck interval |
|---|---|
| < 250 | `FARMING_ACCESS_RECHECK_TICKS = 10` ticks |
| 250–999 | `FARMING_ACCESS_MEDIUM_RECHECK_TICKS = 30` ticks |
| ≥ 1000 | `FARMING_ACCESS_LARGE_RECHECK_TICKS = 90` ticks |

Cache misses also occur when the work key changes (a designation transitioned to a different phase, or a ramp was placed and the work set shrank). On a cache miss the full BFS runs; if the inaccessible set matches the previous `LastAccessRampRequestKey`, ramp placement is skipped — only a new inaccessible set triggers a new `CreateAccessRamp` call.

### Building occupied tiles cache

`CreateAccessRamp` calls `BuildBuildingOccupiedTiles(tower)` to collect tiles occupied by static buildings inside the tower area. This prevents ramps from overwriting existing structures. Internally the method calls `s_entitiesManager.GetAllEntitiesOfType<IStaticEntity>()`, which iterates the full entity collection.

Without caching, this scan ran on every `CreateAccessRamp` call. For a large session that re-evaluates ramp placement roughly every 90 farming ticks (~9 seconds), and if another mod or the autosave holds the entity-collection lock at that moment, this can block for seconds to tens of seconds.

The scan result is now cached per tower `EntityId` for `BUILDING_OCCUPIED_TILES_CACHE_TICKS = 600` farming ticks (~60 seconds). `BuildBuildingOccupiedTiles` returns immediately if the cached tower ID matches and fewer than 600 ticks have elapsed since the last rebuild; otherwise it rebuilds and updates `s_buildingOccupiedTilesCachedTowerId` / `s_buildingOccupiedTilesCachedTick`.

The cache is invalidated automatically when:
- A different tower calls `CreateAccessRamp` (tower ID mismatch → rebuild).
- `s_farmingAutomationTickIndex` is reset to 0 at game reload (the cached tick becomes "in the future"; the `ticksSinceCache >= 0` guard treats this as a miss → rebuild).

Building placement inside an active farming area is rare enough that a ~60-second staleness window is safe.

### Pending filling area cache

The pending fill area — the set of tiles that belong to queued `ReadyForFilling` or unactivated `Done` origins — is used for vehicle clear-out and pruning. Computing it requires iterating all origins and their designation tile footprints.

`FarmingPreparationSession.CachedPendingFillingArea` stores the last-computed set. `PendingFillingAreaDirty` is a dirty bit that is set when any of the following change:
- The origin map (origin added or removed)
- Shoulder origins (`PreparationShoulderOrigins`)
- Rim alignment origins (`RimAlignmentOrigins`)
- Any origin's phase (`ReadyForFilling` activations, filling completions)

`GetPendingFillingArea` returns the cached set immediately when `PendingFillingAreaDirty` is false; otherwise it rebuilds the set, stores it in `CachedPendingFillingArea`, and clears the dirty bit.

### Performance logging

Slow operations emit log lines prefixed `[ATD Farming Perf]` at Unity log level `Info`.

| Log label | What it covers |
|---|---|
| `preparation pass` | Full `RunFarmingPreparationPass` duration |
| `filling pass` | Full `RunFarmingFillingPass` duration |
| `access check` | BFS-only portion of `EnsureFarmingAccessForCurrentPhase` |
| `pending fill-area rebuild` | `GetPendingFillingArea` rebuild duration |
| `preparation breakdown` | Detailed sub-timings for preparation: `capture`, `advance`, `access`, `summary`, `stateScan`, plus origin/analysis counts |

A line is emitted only when the measured duration exceeds `FARMING_PERF_LOG_THRESHOLD_MS = 25` ms. To avoid flooding the log during sustained load, each session enforces a cooldown of `FARMING_PERF_LOG_COOLDOWN_TICKS = 5` ticks between consecutive messages of the same category (`LastFarmingPerfLogTick` / `LastFarmingPerfBreakdownLogTick`).

The extraction script `tools/extract-atd-farming-perf.ps1` filters `[ATD Farming Perf]` lines from the newest (or a specified) log file.

**On load**: if `ReEnableFarmingOnLoad` is enabled in settings, any tower whose managed designations are all flat level designations has farming automation re-enabled automatically.

---

## Restoration (disable)

Calling `RestoreFarmingPreparationForTower` (or toggling automation off):

- Restores all original level designations.
- Removes all owned shoulders, ramps, and rim designations.
- Restores tower dump rules and truck assignments.
- Removes the session when all origins are successfully restored.

---

## Debug console commands

Defined in `ATD.FarmingDebugTransitions.cs`:

| Command | Effect |
|---|---|
| `atd_farming_analyze_origin x y` | Print Stage 1 analysis for the designation at (x, y) |
| `atd_farming_prepare_origin x y` | If `NeedsPreparation`, place temporary preparation designation |
| `atd_farming_restore_origin x y` | Restore original designation stored by the prepare command |
| `atd_farming_analyze_tower` | Print analysis for all designations of the currently selected tower |
| `atd_farming_dump_all` | Print full session state for every mine tower |

---

## Key Mafi API facts confirmed during development

- `TerrainDesignationsManager.AddOrReplaceDesignation(proto, data)` and `RemoveDesignation(origin)` are safe to call mid-game and produce the expected visual and pathfinding results.
- `MineTower.CanAcceptDumpOf(product)` checks only the tower's own `DumpableProducts` set — not the global `ITerrainDumpingManager`.
- `LooseProductProto.TerrainMaterial` is `Option<TerrainMaterialProto>`; `.Value.IsFarmable` is the correct farmability check.
- `IAreaManagingTower.ManagedDesignations` and `MineTower.ManagedDumpingDesignations` are safe to enumerate during a tick.
- `FarmFertileGroundValidator` uses `0.9` as the minimum farmable thickness threshold; ATD mirrors this as `MIN_FARMABLE_THICKNESS = 0.9003906f`.
