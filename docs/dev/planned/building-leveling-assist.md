# Building Leveling Assist — Implementation Plan

**Status:** Planning — not started.  
**Requested by:** MrTammy (Hub suggestion: "Level ground for ANY entity/blueprint")  
**Feature description:** Allow players to place any non-farm static entity (buildings, pipes,
storage, etc.) on uneven ground inside a farming-enabled mining tower area. ATD intercepts the
**entire placement batch**, injects leveling designations for every designation cell under the
footprint of any entity that needs terrain prep, waits for vehicles to flatten the site, then
auto-places the **whole batch at once** when all cells are fulfilled. The entire batch is the
atomic unit — no part of a blueprint is placed until the ground under all of it is ready,
preventing pipes or other entities in the same blueprint from being built first and blocking
vehicle access. Unlike the farming path, no soil (dumping) phase is needed — a level surface
is sufficient.

See also: [farm-placement-assist.md](farm-placement-assist.md) — this feature extends that
one. When this feature is implemented, the farm intercept must also be updated to defer the
entire batch (not just `FarmProto` items) so that mixed farm+pipe blueprints are fully atomic.

---

## Terminology

| Term | Meaning |
|---|---|
| Farming-enabled tower | Any `IAreaManagingTower` for which `IsFarmingAutomationEnabledForTower` returns `true` |
| Designation cell | A 4×4-tile designation grid origin (`DesignationData.OriginTile`); always aligned to a 4-tile boundary |
| Covered cells | The union of all designation-grid origins that overlap any entity in the deferred batch |
| Building leveling session | An ATD-tracked `BuildingLevelingSession` record per tower; holds all pending `PlacementIntentBatch` records for that tower |
| Placement intent batch | A `PlacementIntentBatch` record capturing the full original `BatchCreateStaticEntitiesCmd` items; no ghosts are present in the world while the record exists |

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

Store the original `EntityConfigData` item whole in `PlacementIntentBuilding`. Reconstruct the
replay command from it verbatim:

```csharp
// Store (replaces the decomposed Proto/Position/Z/Rotation fields):
public readonly EntityConfigData ConfigData;

// Footprint enumeration at intercept time (reads from the stored item):
var proto = intent.ConfigData.Prototype.ValueOrNull as ILayoutEntityProto;
var transform = intent.ConfigData.Transform.Value;   // TileTransform incl. IsReflected
List<Tile2i> cells = ComputeCoveredDesignationCells(proto, transform);
```

The `applyConfiguration` flag must also be preserved from the original command (captured at
intercept time), not hardcoded, since single-entity placements and blueprint placements may
differ on this flag.

---

## Full flow

The **batch** (the full `BatchCreateStaticEntitiesCmd`) is the atomic unit of deferral. Every
entity in a batch is deferred together and replayed together — none of them are placed until
the ground under all of them is fully prepared. This prevents pipes, conveyors, or other
non-terrain-needing entities in the same blueprint from being built immediately and blocking
vehicle access before leveling starts.

```
Player hovers a blueprint (may contain farms, buildings, pipes, …) inside a farming-enabled tower area
        │
        ├─ suppress StaticEntitiesTerrainInteractionManager.CanAdd for terrain-height errors
        │  for any LayoutEntityProto within a tower area
        │
Player clicks to place
        │
ATD intercepts BatchCreateStaticEntitiesCmd (before any ghost enters world)
        │
        ├─ Does the batch contain ANY entity within a farming-enabled tower area?
        │     no  → let entire batch through (vanilla)
        │     yes → (1) cancel / consume the entire command — ALL items, not just the ones
        │           │    that need leveling (prevents pipes etc. from building ahead of prep)
        │           (2) record one PlacementIntentBatch for the tower:
        │                - full original ImmutableArray<EntityConfigData>
        │                - originalApplyConfiguration flag
        │                - union of all covered designation-grid cells across every item
        │           (3) session remains inactive while any farm session on the same tower
        │               has active (non-Done, non-Blocked) origins
        │
ATD tick loop  [BuildingLevelingSession is active — no farming work pending on tower]
        │
        ├─ For each covered cell:
        │     - already has a designation?  → skip injection
        │     - otherwise                  → inject flat LevelingDesignation at surface height
        │
        ├─ Ensure access cluster ramps with LevelingProto (reuse FarmingAccessCluster logic)
        │
        ├─ For each pending PlacementIntentBatch:
        │     - player cancels? → remove ATD-injected leveling designations; remove record
        │     - all covered cells Done?
        │           → replay full original BatchCreateStaticEntitiesCmd (all items at once)
        │           → remove record from session
        │
All entities in the blueprint appear at once on leveled ground; vehicles construct normally
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

### 0-D  Intercept scope: batch-level, not entity-level

ATD now intercepts at the **batch level**: if ANY entity in the batch falls within a
farming-enabled tower area, the entire batch is held. Individual entity types are no longer
filtered within the batch — pipes, conveyors, rail segments, and other non-terrain-needing
entities that happen to be in the same blueprint are deferred alongside their neighbours.
This prevents them from being built first and blocking vehicle access.

Entities outside any tower area are never intercepted (the tower-area check is the only
guard). Standalone non-farm placements outside a tower area pass through vanilla unchanged.

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

### Placement intent (batch)

The unit of deferral is the entire original `BatchCreateStaticEntitiesCmd`, not a single
entity. The intent stores the whole batch so the replay is verbatim:

```csharp
private sealed class PlacementIntentBatch
{
    // Full original item list from the intercepted BatchCreateStaticEntitiesCmd.
    // All entities — farms, buildings, pipes, whatever — are replayed together.
    public readonly ImmutableArray<EntityConfigData> Items;
    public readonly IAreaManagingTower               Tower;
    // Union of all designation-grid cells covered by any entity in Items.
    // All cells must be Done before any entity in Items is placed.
    public readonly IReadOnlyList<Tile2i>             CoveredCells;
    public readonly bool                             OriginalApplyConfiguration;

    public PlacementIntentBatch(ImmutableArray<EntityConfigData> items, IAreaManagingTower tower,
        IReadOnlyList<Tile2i> coveredCells, bool originalApplyConfiguration)
    {
        Items = items; Tower = tower;
        CoveredCells = coveredCells;
        OriginalApplyConfiguration = originalApplyConfiguration;
    }
}
```

At intercept time, `CoveredCells` is the union of `ComputeCoveredDesignationCellsForBuilding`
over every item in the batch whose prototype is a `LayoutEntityProto`. Non-layout items (if
any) contribute no cells but are still deferred as part of the batch.

Replay when all covered cells are Done:
```csharp
s_inputScheduler.ScheduleInputCmd(
    new BatchCreateStaticEntitiesCmd(
        intent.Items,
        BuildMiniZippersMode.DeferToProto,
        isFree: false,
        allowValidationSuppression: false,
        applyConfiguration: intent.OriginalApplyConfiguration));
```

### Session dictionary

```csharp
private static readonly Dictionary<EntityId, BuildingLevelingSession> s_buildingLevelingSessions = new();
```

The `BuildingLevelingSession.Intents` list holds `PlacementIntentBatch` records (not
`PlacementIntentBuilding`). Update the session type accordingly:

```csharp
public List<PlacementIntentBatch> Intents { get; } = new();
```

---

## Phase 2 — Intercept & validator suppression (`ATD.BuildingLevelingAssist.cs`)

### 2-A  Intercept patch

Add a Harmony prefix on the same `BatchCreateStaticEntitiesCmd` processor that the farm
feature patches. The key change from the previous design is that the **batch** is the unit:

1. Scan the batch for any entity within a farming-enabled tower area.
2. If none found → return `true` (vanilla).
3. If any found → consume the entire command (`return false`), then register a single
   `PlacementIntentBatch` for the tower:
   - `Items` = the full original `ImmutableArray<EntityConfigData>` (all items, not just the
     ones that need leveling — pipes etc. are deferred alongside them).
   - `CoveredCells` = union of designation-grid cells across all `LayoutEntityProto` items.
   - `OriginalApplyConfiguration` = `cmd.ApplyConfiguration`.

This guarantees that a blueprint containing both farms and pipes never places the pipes first.

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

When all `CoveredCells` of a `PlacementIntentBatch` are `Done` → trigger placement retry
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
4. For each `PlacementIntentBatch` whose `CoveredCells` are all `Done`:
   ```csharp
   s_inputScheduler.ScheduleInputCmd(
       new BatchCreateStaticEntitiesCmd(
           intent.Items,
           BuildMiniZippersMode.DeferToProto,
           isFree: false,
           allowValidationSuppression: false,
           applyConfiguration: intent.OriginalApplyConfiguration));
   ```
   The replay guard prevents the farm items in the batch from being re-intercepted.
5. Remove fulfilled intents from the session. When a session has no remaining intents
   and all origins are `Done`, mark the session inactive.

---

## Phase 5 — Cleanup & cancellation (`ATD.BuildingLevelingSession.cs`)

```csharp
private static void ClearBuildingLevelingRuntimeState()
{
    s_buildingLevelingSessions.Clear();
}
```

On explicit cancel (or tower removed):
- Remove all `session.AtdInjectedCells` leveling designations from `s_desigManager`
  that have not yet been fulfilled (already-fulfilled cells were valid work; no cleanup needed).
- Remove the session from `s_buildingLevelingSessions`.

---

## Phase 6 — Save game persistence

Pending intents are currently runtime-only (stored inside `BuildingLevelingSession.Intents`
and `s_pendingFarmPlacements`). If the player saves while leveling is in progress the terrain
designations survive the reload (they are engine entities) but the pending intents are gone —
the buildings/farms will never appear and the player must re-place manually.

Each `PlacementIntentBatch` stores the full original `ImmutableArray<EntityConfigData>` which
is the canonical engine serialisation bag; it already holds everything needed to reconstruct
the replay command verbatim.

### What to persist

| Source | Scope | Contents |
|---|---|---|
| `s_pendingFarmPlacements` | global | `List<PlacementIntent>` — each holds one `EntityConfigData` |
| `session.Intents` per tower | per `BuildingLevelingSession` | `List<PlacementIntentBatch>` — each holds `ImmutableArray<EntityConfigData>` |

Both can be persisted as a single JSON array of batch records alongside the existing
tower-settings persistence key in `CoI.AutoHelpers.Persistence`.

### Serializable representation

```csharp
// ATD.Persistence.PendingBatchRecord  (new nested type)
private sealed class PendingBatchRecord
{
    public string Kind { get; set; }                     // "Farm" | "Building"
    // Per-item data for each EntityConfigData in the batch
    public List<PendingItemRecord> Items { get; set; }
    public bool ApplyConfiguration { get; set; }
}

private sealed class PendingItemRecord
{
    public string ProtoId { get; set; }                  // Prototype.Id.Value
    public int PosX { get; set; }                        // Transform.Position.X
    public int PosY { get; set; }                        // Transform.Position.Y
    public int PosZ { get; set; }                        // Transform.Position.Z (height)
    public int Rotation { get; set; }                    // Transform.Rotation.Value (0-3)
    public bool IsReflected { get; set; }                // Transform.IsReflected

    // Optional extra data — only fields that deviate from prototype defaults need storing.
    // For a minimum-viable implementation, omit and accept that recipe/crop assignments
    // made in a blueprint are lost on reload (same behaviour as today).
}
```

A full implementation would serialise the entire `EntityConfigData` key-value bag; a
minimum-viable implementation stores only position/rotation and re-fetches the prototype by
ID on load, accepting that blueprint-configured overrides (recipes, crop assignments) are lost
on reload.

### Save hook

In `ATD.TowerSettingsConfigPersistence.cs`, alongside `SaveTowerSettings`, add:

```csharp
private static void SavePendingPlacements(IPersistenceStore store)
{
    var records = new List<PendingBatchRecord>();
    // Farm path — each PlacementIntent is a single-item batch
    foreach (var intent in s_pendingFarmPlacements)
        records.Add(PendingBatchRecord.FromFarmIntent(intent));
    // Building path — each PlacementIntentBatch may have multiple items
    foreach (var session in s_buildingLevelingSessions.Values)
        foreach (var batch in session.Intents)
            records.Add(PendingBatchRecord.FromBuildingBatch(batch));
    store.Set("PendingPlacements_v1", JsonConvert.SerializeObject(records));
}
```

### Load hook

On load, deserialise each record, look up each proto, reconstruct `EntityConfigData` items,
and push the batch back through the intercept path (schedule a synthetic
`BatchCreateStaticEntitiesCmd` — the intercept re-registers the intent exactly as if the
player had re-placed it).

### Open sub-questions

- Does `EntityConfigData`'s JSON form require a custom converter, or does the engine already
  expose one?
- The replay guard (`s_farmPlacementReplayPositions`) must be pre-populated before scheduling
  the synthetic load command; confirm the same guard works for the building path.
- Minimum-viable vs. full serialisation: decide whether recipe/crop loss on reload is
  acceptable or must be fixed before shipping persistence.

---

## Phase 7 — Configuration

Add to `ATD.Mod.cs` / `ATD.Settings.cs`:

```csharp
bool BuildingLevelingAssistEnabled = true;   // master on/off; persisted in ATDsettings.json
```

When `false`, the intercept patches return `true` immediately (vanilla behaviour) and no
sessions are created.

---

## Phase 8 — UI / Inspector integration

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
   pipe rack, the entire batch is intercepted as one `PlacementIntentBatch`. Farm cells get
   farming-path designations injected; non-farm building cells get leveling designations
   injected. The full batch is replayed only after all covered cells (farm and building
   footprints combined) are Done. This prevents pipes from being placed ahead of terrain prep.
   The farm-first sequencing rule still applies at the `BuildingLevelingSession` level.

3. **Multiple buildings on same cells.** If two different buildings share a covered cell,
   the cell appears in both `PlacementIntentBatch.CoveredCells`. The designation is injected
   once (the second injection is a no-op due to `GetDesignationAt().HasValue` guard). Both
   intents wait for the cell to become `Done` independently, which is correct.

---

## Future direction: hybrid ramp+bridge accessways

### Current position

Today an "accessway" is either a pure ramp (climbs face-z to surface-z over its body) or a
pure bridge (flat tiles at face-z spanning across other designations at the same target z).
The ramp-vs-bridge selection happens implicitly: a candidate is a bridge when its mouth
approach z already matches the face z, otherwise it's a ramp.

### Proposed extension

An accessway is generally a **ramp prefix + bridge suffix**: N ramp tiles climbing from
face-z up to some intermediate z, then M flat bridge tiles continuing at that intermediate z
until they meet the surrounding reachable surface. Pure ramp = `M=0`; pure bridge = `N=0`.
Hybrids let the system handle "horizontal distance 4, vertical distance 2" cases naturally
(e.g. 2 ramp steps + 2 bridge tiles) without forcing either a too-steep climb or a wasted
flat span.

### Scoring: minimum work (material displacement)

Geometric length is not the right cost — the right cost is **material displaced** by carving
or filling the accessway body. For a candidate at intermediate z `m`:

- **Mining context** (designation target z is below surface): prefer higher `m`. A higher
  intermediate z means less rock removed under the bridge tiles. Bridges at face-z are
  cheapest only when face-z equals the surrounding target z; otherwise the bridge has to
  cut down through rock and a partial climb is preferable.
- **Dumping context** (designation target z is above surface): prefer lower `m`. A lower
  intermediate z means less fill placed under the bridge tiles.

In both cases the cost is the integral of `|m - localGroundZ|` over the bridge-suffix
footprint (where `localGroundZ` is the current surface z under each bridge tile). The ramp
prefix has its own intrinsic cost (the body wedge) which is already roughly captured by the
existing geometric score / `attachmentDepthSum`.

### Why deferred

Generating hybrid candidates means enumerating bridge-suffix lengths per ramp candidate and
scoring each against the live reachable set. That's a meaningful expansion of candidate
generation and the placement loop (the mouth carving has to lay out flat bridge tiles at z
`m` rather than just the natural mouth z). The simpler "ordered iterative pure-ramp /
pure-bridge selection" delivers most of the value first; hybrids are a refinement once that
foundation is in place and we have telemetry on how often the pure variants leave money on
the table.

---

## Future direction: placement anywhere (no tower-area requirement)

### Current position

The design above intercepts only when any entity in the batch falls within a farming-enabled
tower area. This was the natural starting point because ATD's designation injection machinery
is tower-scoped.

### Why the tower-area check should eventually be dropped

Only entities that need terrain prep require a tower. If the player places a batch partly
inside and partly outside a tower area, the "outside" pieces should be deferred alongside the
rest — the player already consented to deferral by placing the whole blueprint at once.

If designations are injected outside the tower area the engine will surface a vanilla alert:
*"Mining/leveling designations are outside the mining area"* — this is acceptable. The player
can see the problem, expand the tower, or move the blueprint. ATD does not need to validate
the geometry itself.

To give the player visibility before they commit, a **hovering mesh preview** of the
designation footprint (the set of 4×4 cells that ATD would inject) is planned as a follow-on
feature. The preview would colour-code cells green (inside tower area) vs. amber (outside),
letting the player make an informed decision at hover time rather than discovering the alert
after placement.

### Farm-outside-tower complication

Farms placed outside a tower area require soil (dumping phase). Dumping designations outside
a tower area have no associated soil product configuration — the global dump rule would need
to be altered, or a new dump rule synthesised, for the fill-with-soil operation to proceed.
This is non-trivial and has not been designed yet.

**Decision: defer the farm-outside-tower case entirely.** The tower-area check is dropped
only for non-farm entities. For `FarmProto` items in a batch, the existing tower-area guard
remains in place until the dump-rule problem is solved. A mixed batch (farms inside tower +
non-farms outside tower) is handled by the union of both rules: farm items require a tower
area, non-farm items do not.
