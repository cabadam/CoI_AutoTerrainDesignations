# Building Leveling Assist — Implementation Plan

**Status:** Planning — not started.  
**Requested by:** MrTammy (Hub suggestion: "Level ground for ANY entity/blueprint")  
**Feature description:** Allow players to place any non-farm static entity (buildings, pipes,
storage, etc.) on uneven ground inside a farming-enabled mining tower area. ATD intercepts the
placement, injects leveling designations for every designation cell under the footprint, waits
for vehicles to flatten the site, then auto-places the building when all cells are fulfilled.
Unlike the farming path, no soil (dumping) phase is needed — a level surface is sufficient.

See also: [farm-placement-assist.md](farm-placement-assist.md) — this feature shares several
mechanisms with that one and is implemented as a parallel, non-intrusive path.

---

## Terminology

| Term | Meaning |
|---|---|
| Farming-enabled tower | Any `IAreaManagingTower` for which `IsFarmingAutomationEnabledForTower` returns `true` |
| Designation cell | A 4×4-tile designation grid origin (`DesignationData.OriginTile`); always aligned to a 4-tile boundary |
| Covered cells | The set of designation-grid origins that overlap the building footprint |
| Building leveling session | An ATD-tracked `BuildingLevelingSession` record per tower; holds all pending building intents for that tower |
| Pending building placement | A `PlacementIntentBuilding` record capturing the full placement parameters of an intercepted non-farm entity placement; no ghost is present in the world while the record exists |

---

## Key differences from the farming path

| Concern | Farm | Building (this feature) |
|---|---|---|
| Soil designation (Dumping) | Yes — core requirement | No — leveling is sufficient |
| Phase model | AnalysisLeveling → Preparing → ReadyForFilling → Filling → Done | AnalysisLeveling → Done \| Blocked |
| Ramp proto | Mining or Dumping depending on cluster phase | Always Leveling (handles both cut and fill; avoids "mining in the air" errors when cell is below current terrain) |
| Access ramp logic | `EnsureFarmingAccessForCurrentPhase` (existing) | Reuse same logic, ramp proto = leveling proto |
| Vehicle clearout | Yes (fill trucks during soil phase) | No |
| Dump rule management | Yes (tower soil product) | No |
| Rim alignment | Yes | No |
| Session priority vs. farming | n/a | Held inactive while any farm session on the same tower has active (non-Done, non-Blocked) origins |

---

## Bug fix: farm `PlacementIntent` loses entity configuration

`EntityConfigData` is the engine's full serialization bag for an entity placement. It carries:
`Prototype`, `Transform` (position + rotation + `IsReflected`), `Recipes`, `AssignedProducts`,
`AllowedProducts`, `AssignedVehicles`, and typed key-value stores for any other entity-specific
data. **Everything** a blueprint writes into an entity lives in this one object.

The existing `PlacementIntent` for farms decomposes the batch item into separate fields
(`FarmProto`, `Position`, `Z`, `Rotation90`) and discards the rest. This means:
- Reflection (`IsReflected`) is lost → replayed farm has ports on the wrong side.
- Crop assignments, recipe selections, and any other blueprint-configured state are lost →
  replayed farm is placed blank, ignoring the user's blueprint configuration.

**Fix (applies to both the existing farm path and the new building path):**

Store the original `EntityConfigData` item whole in both `PlacementIntent` and
`PlacementIntentBuilding`. Reconstruct the replay command from it verbatim:

```csharp
// Store (replaces the decomposed Proto/Position/Z/Rotation fields):
public readonly EntityConfigData ConfigData;

// Footprint enumeration at intercept time (reads from the stored item):
var proto = intent.ConfigData.Prototype.ValueOrNull as ILayoutEntityProto;
var transform = intent.ConfigData.Transform.Value;   // TileTransform incl. IsReflected
List<Tile2i> cells = ComputeCoveredDesignationCells(proto, transform);

// Replay:
s_inputScheduler.ScheduleInputCmd(
    new BatchCreateStaticEntitiesCmd(
        ImmutableArray.Create(intent.ConfigData),
        BuildMiniZippersMode.DeferToProto,
        isFree: false,
        allowValidationSuppression: false,
        applyConfiguration: true));  // match the original cmd's ApplyConfiguration flag
```

The `applyConfiguration` flag must also be preserved from the original command (captured at
intercept time), not hardcoded, since single-entity placements and blueprint placements may
differ on this flag.

---

## Full flow

```
Player hovers non-farm building preview inside farming-enabled tower area
        │
        ├─ suppress StaticEntitiesTerrainInteractionManager.CanAdd for terrain-height errors
        │  (same guard already in place for farms; extend it to non-FarmProto)
        │
Player clicks to place
        │
ATD intercepts entity-add command (before ghost enters world — same intercept point as farms)
        │
        ├─ Is non-FarmProto StaticEntity + within farming-enabled tower area?
        │     no  → let command through (vanilla)
        │     yes → (1) cancel / consume the command
        │           (2) record PlacementIntentBuilding (proto, full TileTransform)
        │           (3) compute covered cells from layout + TileTransform (rotation + reflection)
        │           (4) add record to BuildingLevelingSession for that tower
        │           (5) (session remains inactive if farm work is still pending on this tower)
        │
ATD tick loop  [BuildingLevelingSession is active — no farming work pending on tower]
        │
        ├─ For each covered cell:
        │     - already has a designation?  → skip injection (farm or other designation is handling it)
        │     - otherwise                  → inject flat LevelingDesignation at surface height
        │
        ├─ Ensure access cluster ramps with LevelingProto (reuse FarmingAccessCluster logic)
        │
        ├─ For each pending placement:
        │     - player cancels? → remove ATD-injected leveling designations; remove record
        │     - all covered cells Done?
        │           → replay original placement command
        │           → remove record from session
        │
Building appears on leveled ground; vehicles construct normally
```

---

## Phase 0 — Research & spike

**Goal:** Confirm technical feasibility for non-farm buildings before writing production code.

### 0-A  Verify intercept point covers non-farm protos

The farm feature confirmed `BatchCreateStaticEntitiesCmd` as the intercept point.
Verify that a player-placed non-farm building (e.g. a Smelter T1, a pipe rack, Storage T2)
also passes through this command, not through `CreateStaticEntityCmd` or some other path.
If a different command is used, identify it and add the appropriate prefix.

### 0-B  Confirm footprint API for arbitrary `LayoutEntityProto`

Verify that `proto.Layout.GetOccupiedTilesRelative(TileTransform)` is available and correct
for general `LayoutEntityProto` (not just `FarmProto`). Confirm the call signature accepts
the full `TileTransform` including `IsReflected`, and that the returned tiles correctly reflect
both rotation and reflection.

### 0-C  Confirm validator guard extension

The existing `Patch_StaticEntitiesTerrainInteractionManager_CanAdd_Farm` suppresses the
terrain-height error only when `addRequest.Proto is FarmProto`. Confirm that broadening
this to `addRequest.Proto is LayoutEntityProto && GetFarmingTowerForRequest(addRequest) != null`
does not suppress the validator for blueprints or auto-build operations that should keep the
vanilla check (e.g. blueprints placed outside a tower area).

### 0-D  Identify non-interceptable building types

Some entity types may need to remain vanilla (ships, vehicles, rail segments, etc.).
Define the exact type filter: initially `StaticEntityProto` but not `FarmProto` and not
transport/rail/pipe protos that don't require flat ground.

---

## Phase 1 — Data model (`ATD.BuildingLevelingSession.cs`)

New file. All types are `private` nested inside `AutoDepthDesignation` (same pattern as farming).

### Phase model

```csharp
private enum BuildingLevelingPhase { AnalysisLeveling, Done, Blocked }
```

### Session types

```csharp
private sealed class BuildingOriginSession
{
    public Tile2i             Origin  { get; }
    public BuildingLevelingPhase Phase { get; set; }
    public string             Detail  { get; set; }
}

private sealed class BuildingLevelingSession
{
    public Dictionary<Tile2i, BuildingOriginSession> Origins { get; } = new();
    public List<PlacementIntentBuilding>             Intents { get; } = new();
    public bool                    Enabled { get; set; }
    public bool                    Active  { get; set; }
    public IAreaManagingTower?     Tower   { get; set; }
    public HashSet<Tile2i>         AtdInjectedCells { get; } = new();
}
```

### Placement intent (building)

```csharp
private sealed class PlacementIntentBuilding
{
    public readonly EntityConfigData          ConfigData;     // full original item — proto, transform, recipes, etc.
    public readonly IAreaManagingTower        Tower;
    public readonly IReadOnlyList<Tile2i>     CoveredCells;
    public readonly bool                      OriginalApplyConfiguration;

    public PlacementIntentBuilding(EntityConfigData configData, IAreaManagingTower tower,
        IReadOnlyList<Tile2i> coveredCells, bool originalApplyConfiguration)
    {
        ConfigData = configData; Tower = tower;
        CoveredCells = coveredCells;
        OriginalApplyConfiguration = originalApplyConfiguration;
    }
}
```

The `ILayoutEntityProto` and `TileTransform` are read from `ConfigData.Prototype` and
`ConfigData.Transform` as needed; they are not stored as separate fields.

### Session dictionary

```csharp
private static readonly Dictionary<EntityId, BuildingLevelingSession> s_buildingLevelingSessions = new();
```

---

## Phase 2 — Intercept & validator suppression (`ATD.BuildingLevelingAssist.cs`)

### 2-A  Intercept patch

Add a Harmony prefix on the same `BatchCreateStaticEntitiesCmd` / `CreateStaticEntityCmd`
processor that the farm feature patches. When `item.Prototype` is a non-`FarmProto`
`StaticEntityProto` within a farming-enabled tower area → capture the full `TileTransform`
(position + rotation + `IsReflected`), compute covered cells, register the intent, suppress
the original command.

### 2-B  Validator suppression

Extend the existing `Patch_StaticEntitiesTerrainInteractionManager_CanAdd_Farm` to also
suppress for non-farm `LayoutEntityProto` within a tower area. The type guard changes from:

```csharp
if (addRequest.Proto is not FarmProto) return true;
```

to:

```csharp
if (addRequest.Proto is FarmProto) return true;  // farm path handles its own suppression
if (addRequest.Proto is not LayoutEntityProto) return true;
if (GetFarmingTowerForRequest(addRequest) == null) return true;
```

### 2-C  Footprint → designation cell mapping

```csharp
internal static List<Tile2i> ComputeCoveredDesignationCellsForBuilding(
    ILayoutEntityProto proto, TileTransform transform)
{
    ImmutableArray<OccupiedTileRelative> relTiles =
        proto.Layout.GetOccupiedTilesRelative(transform);
    var origins = new HashSet<Tile2i>();
    foreach (OccupiedTileRelative rel in relTiles)
        origins.Add(SnapToDesignationGrid(transform.Position.Xy + rel.RelCoord));
    return new List<Tile2i>(origins);
}
```

Called at intercept time as:
```csharp
var proto      = item.Prototype.ValueOrNull as ILayoutEntityProto;
var transform  = item.Transform.Value;   // TileTransform — includes IsReflected
List<Tile2i> cells = ComputeCoveredDesignationCellsForBuilding(proto, transform);
```

Passing `TileTransform` directly to `GetOccupiedTilesRelative` handles all 8 symmetry
permutations (4 rotations × 2 reflections) without any additional logic. The same approach
applies to the corrected farm path.

---

## Phase 3 — Analysis & designation injection

### Sequencing guard (farm-first)

Before doing any work in a `BuildingLevelingSession`, check:

```csharp
private static bool HasActiveFarmingWork(EntityId towerId)
{
    return s_farmingPreparationSessions.TryGetValue(towerId, out var fs)
        && fs.Active
        && fs.Origins.Values.Any(o => o.Phase != FarmingOriginPhase.Done
                                   && o.Phase != FarmingOriginPhase.Blocked);
}
```

In `AdvanceBuildingLevelingSession`: if `HasActiveFarmingWork(session.Tower.Id)` → return
immediately. This guarantees farms finish first (including soil fill), so the rim/shoulder
leveling designations from the farming session may have already flattened cells that the
building session would otherwise need to inject for.

### Designation injection

For each `BuildingOriginSession` in phase `AnalysisLeveling`:

1. If `GetDesignationAt(origin).HasValue` → skip (farming or other designation is already
   handling this cell; treat it as in-progress and re-check next tick).
2. Otherwise → inject a flat `LevelingDesignation` at the current surface height of the
   cell's origin corner:
   ```csharp
   int h = GetSurfaceHeight(terrMgr, origin);
   var data = new DesignationData(origin, new HeightTilesI(h));
   if (s_desigManager.AddOrReplaceDesignation(s_levelingProto, data))
       session.AtdInjectedCells.Add(origin);
   ```
   The leveling designator handles both cut and fill internally — no need to inspect which
   direction the terrain needs to move.

### Transition to Done

Each tick, re-check cells that have a designation:
- `designation.IsFulfilled` → transition `BuildingOriginSession.Phase` to `Done`.

When all `CoveredCells` of a `PlacementIntentBuilding` are `Done` → trigger placement retry
(Phase 4).

### Access ramps

Reuse `EnsureFarmingAccessForCurrentPhase` / `FarmingAccessCluster` machinery unchanged,
with one difference: the ramp proto is always `s_levelingProto` (never `s_miningProto` or
`s_dumpingProto`). A leveling designation handles both above-surface and below-surface cells
without producing "mining in the air" or "dumping below surface" errors.

```csharp
TerrainDesignationProto rampProto = s_levelingProto ?? defaultFallback;
```

---

## Phase 4 — Tick advancement & placement retry (`ATD.Ticker.cs`)

Hook into the existing ticker or add `AdvanceBuildingLevelingSession` called from it.

Each tick per tower:
1. Sequencing guard (Phase 3) — skip if farm work is active.
2. Inject designations for uncovered cells.
3. Ensure access ramps.
4. For each `PlacementIntentBuilding` whose `CoveredCells` are all `Done`:
   ```csharp
   s_inputScheduler.ScheduleOnce(() =>
       s_entitiesCommandsProcessor.Invoke(originalCmd));
   ```
5. Remove fulfilled intents from the session. When a session has no remaining intents
   and all origins are `Done`, mark the session inactive.

---

## Phase 5 — Cleanup & cancellation (`ATD.BuildingLevelingSession.cs`)

```csharp
private static void ClearBuildingLevelingRuntimeState()
{
    s_buildingLevelingSessions.Clear();
    s_pendingBuildingPlacements.Clear();
}
```

On explicit cancel (or tower removed):
- Remove all `session.AtdInjectedCells` leveling designations from `s_desigManager`
  that have not yet been fulfilled (already-fulfilled cells were valid work; no cleanup needed).
- Remove the session from `s_buildingLevelingSessions`.

---

## Phase 6 — Configuration

Add to `ATD.Mod.cs` / `ATD.Settings.cs`:

```csharp
bool BuildingLevelingAssistEnabled = true;   // master on/off; persisted in ATDsettings.json
```

When `false`, the intercept patches return `true` immediately (vanilla behaviour) and no
sessions are created.

---

## Phase 7 — UI / Inspector integration

Reuse the existing inspector panel injection point (`ATD.Inspector.cs`). When the inspected
tower has an active `BuildingLevelingSession`, inject a status row:

```
[ATD] Building prep: 3 cells leveling, 1 done — waiting for 2 cells
```

No dedicated analysis panel is planned for v1.

---

## Open questions

1. **Which non-farm protos should be included?** Initial scope: all `StaticEntityProto` that
   are not `FarmProto`. Transports and other non-flat-requiring entities are unlikely to be
   placed inside a tower area but should be tested in spike 0-D.

2. **Blueprint batch placements.** If a player drops a blueprint containing both a farm and a
   pipe rack, both intercept paths fire on the same `BatchCreateStaticEntitiesCmd`. The farm
   items go to the farm path; the non-farm items go to the building leveling path. Each is
   handled independently, sequenced by the farm-first rule at the session level.

3. **Multiple buildings on same cells.** If two different buildings share a covered cell,
   the cell appears in both `PlacementIntentBuilding.CoveredCells`. The designation is injected
   once (the second injection is a no-op due to `GetDesignationAt().HasValue` guard). Both
   intents wait for the cell to become `Done` independently, which is correct.
