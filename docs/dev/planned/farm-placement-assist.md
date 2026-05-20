# Farm Placement Assist — Implementation Plan
Current as of release: 0.4.0k

**Status:** Planning / not yet implemented.  
**Feature description:** Allow players to place farm buildings on uneven or infertile ground
inside a farming-enabled mining tower area. ATD suppresses the vanilla placement blockers,
injects farming designations for the footprint, pauses construction until the site is prepared,
then unpauses so the farm can be built normally.

---

## Terminology

| Term | Meaning |
|---|---|
| Farming-enabled tower | Any `IAreaManagingTower` for which `IsFarmingAutomationEnabledForTower` returns `true` |
| Farm footprint | The set of 1×1 tiles occupied by a `FarmProto` entity |
| Designation cell | A 4×4-tile designation grid origin (`DesignationData.OriginTile`); always aligned to a 4-tile boundary |
| Covered cells | The set of designation-grid origins that overlap the farm footprint |
| Pending placement | An ATD-tracked record pairing a placed but not-yet-constructible farm with the designation cells it requires |

---

## Overview of the full flow

```
Player hovers farm preview
        │
        ├─ footprint within farming-enabled tower area?
        │     yes → suppress FarmFertileGroundValidator + LayoutEntityTerrainValidator errors
        │     no  → vanilla behaviour (no changes)
        │
Player clicks to place
        │
        ├─ Farm ghost appears (ConstructionState = InConstruction)
        │
ATD EntityAdded hook fires
        │
        ├─ Is FarmProto + within farming-enabled tower area?
        │     no  → ignore
        │     yes → (1) pause construction via TrySetConstructionPause
        │           (2) compute covered cells
        │           (3) for each covered cell, ensure a farming-level designation is present
        │               or create one at the current surface height
        │           (4) register a PendingFarmPlacement record
        │
ATD tick loop
        │
        ├─ For each PendingFarmPlacement:
        │     (a) if entity removed → clean up designations ATD placed, remove record
        │     (b) are all covered cells in Done state? → TrySetConstructionPause(false)
        │                                                 remove record
        │
Vehicles prepare the site (existing farming session logic)
        │
Farm ghost resumes, vehicles construct the farm normally
```

---

## Phase 0 — Research & spike

**Goal:** Resolve open unknowns before writing any production code.

### 0-A  Validator hook point

Confirm that a Harmony prefix on
`FarmFertileGroundValidator.CanAdd(LayoutEntityAddRequest)` and
`LayoutEntityTerrainValidator.CanAdd(ILayoutEntityAddRequest)` is called synchronously
during the game's entity-add path (not just the UI preview path), and that returning
`true` from the prefix with a pass-through result actually allows placement.

- Check whether `EntityAddReason` (Ghost, Blueprint, Normal) is accessible on `addRequest`
  and whether we need to restrict suppression to specific reasons.
- Confirm that `addRequest.Transform.Position.Xy` is already tile-snapped at validator call time.
- Verify that suppressing only for `FarmProto` doesn't break non-farm buildings on the same tiles.

### 0-B  Entity-added event

Locate the event on `IEntitiesManager` (likely `EntityAdded` or similar) that fires after a
farm ghost is successfully added to the world.  Confirm:
- It fires on the simulation thread (important: `TrySetConstructionPause` must be called
  from the right thread context, or queued as an input command).
- The `IStaticEntity` argument is queryable for its `Proto` type and `Transform`.

### 0-C  Construction pause behaviour while `InConstruction`

Place a farm ghost in-game and call `TrySetConstructionPause(entity, true)` immediately.
Confirm:
- Vehicles do not queue construction tasks for the paused farm.
- The pause survives a save/load cycle (or determine that we need to re-apply it from
  ATD's own persistent state on load).
- The entity is still physically present and blocks terrain designation placement on its tiles.

**Decision point after 0-C:** If the entity blocks designation placement, we may need to place
designations *before* placement (see Phase 2 alternatives), or investigate whether designations
can coexist with an entity ghost.

### 0-D  Covering cells vs. entity footprint alignment

Verify how `OccupiedTiles` on the add request maps to 4×4 designation cells.  A farm T1 is
4×4 tiles; T2 is 8×8; T3 is 12×12.  All should align cleanly to the designation grid.
Confirm the snapping formula `(int)Math.Floor(tile / 4.0) * 4` handles all farm sizes without
off-by-one errors at the edges.

### 0-E  Suppress preview warnings during hover

The vanilla placement controller shows red tile overlays and a tooltip when validators fail.
Identify which call path drives the hover-time validation:
- `TerrainDesignationController.previewInitialDesignationAt` is already patched for ATD
  designations, but farm placement is a different controller.
- Find the `BuildingPlacementController` (or equivalent) that calls validators in
  preview mode, and confirm whether a prefix on the validator `CanAdd` already suppresses
  the red tiles, or whether a separate preview method must also be patched.

---

## Phase 1 — Tower-area membership helper

**Goal:** Add a fast utility that answers "does tile T fall within any farming-enabled tower area?"
This is used by both the validator patches (Phase 2) and the post-placement hook (Phase 3).

### 1-A  `GetFarmingTowerForTile(Tile2i tile) → IAreaManagingTower?`

Add this helper to `ATD.FarmingPreparationSession.cs` (it can be `internal static`).

```csharp
// Returns the first farming-enabled tower whose area contains tile, or null.
internal static IAreaManagingTower? GetFarmingTowerForTile(Tile2i tile)
{
    foreach (var kvp in s_farmingPreparationSessions)
    {
        if (!s_towers.TryGetValue(kvp.Key, out IAreaManagingTower tower)) continue;
        if (tower.Area.ContainsTile(tile)) return tower;
    }
    return null;
}
```

- `s_towers` is the existing per-tower entity lookup (verify name in `ATD.State.cs`).
- If iteration over all sessions is too slow at validator call time, cache a
  `HashSet<Tile2i>`-based lookup keyed by designation origin.
- Footprint membership check: check whether *any* tile in the farm's occupied-tile list
  falls in the tower area, or whether we require *all* tiles to be inside.  Recommend
  requiring all tiles for simplicity (partial-overlap placement should not be assisted).

### 1-B  Multi-farm footprint check

Add a helper that checks all tiles in an add request against the same tower area:

```csharp
internal static IAreaManagingTower? GetFarmingTowerForRequest(ILayoutEntityAddRequest request)
{
    if (request.Proto is not FarmProto) return null;
    Tile2i origin = request.Transform.Position.Xy;
    IAreaManagingTower? tower = null;
    foreach (OccupiedTileRelative rel in request.OccupiedTiles)
    {
        tower = GetFarmingTowerForTile(origin + rel.RelCoord);
        if (tower == null) return null; // all tiles must be inside
    }
    return tower;
}
```

---

## Phase 2 — Validator suppression patches

**Goal:** Allow a farm to be placed as a ghost on uneven or infertile ground when all its
tiles are inside a farming-enabled tower area.

### 2-A  Patch `FarmFertileGroundValidator.CanAdd`

```csharp
[HarmonyPatch(typeof(FarmFertileGroundValidator), nameof(FarmFertileGroundValidator.CanAdd))]
static class Patch_FarmFertileGroundValidator_CanAdd
{
    static bool Prefix(LayoutEntityAddRequest addRequest, ref EntityValidationResult __result)
    {
        if (AutoDepthDesignation.GetFarmingTowerForRequest(addRequest) == null) return true;
        __result = EntityValidationResult.Success;
        return false;
    }
}
```

### 2-B  Patch `LayoutEntityTerrainValidator.CanAdd` for farms

The terrain validator checks height flatness and whether tiles are at the correct elevation.
Add a targeted prefix that only suppresses errors for `FarmProto` within a tower area.

```csharp
[HarmonyPatch(typeof(LayoutEntityTerrainValidator), "CanAdd")]   // interface explicit impl — verify method name
static class Patch_LayoutEntityTerrainValidator_CanAdd_Farm
{
    static bool Prefix(ILayoutEntityAddRequest addRequest, ref EntityValidationResult __result)
    {
        if (addRequest.Proto is not FarmProto) return true;
        if (AutoDepthDesignation.GetFarmingTowerForRequest(addRequest) == null) return true;
        __result = EntityValidationResult.Success;
        return false;
    }
}
```

**Risk:** The terrain validator is a high-priority validator (`EntityValidatorPriority.High`)
and is applied broadly.  Test carefully that suppressing it for farms in tower areas does not
allow placement in illegal positions (e.g., ocean tiles, outside map bounds).  Consider adding
an explicit ocean/bounds check before returning Success.

### 2-C  Suppress hover-time red tile overlay (if needed)

If Phase 0-E shows that red tiles still appear during preview despite validator suppression,
add a prefix on the building placement preview method that mirrors the same gate.  Hold off
on implementing this until Phase 0-E is resolved.

---

## Phase 3 — Post-placement hook and designation injection

**Goal:** When a farm ghost is placed in a tower area, pause its construction and inject
farming designations for all covered designation cells that are not already managed.

### 3-A  Subscribe to entity-added event

In `ATD.State.cs` (or a new `ATD.FarmPlacementAssist.cs`), subscribe to `IEntitiesManager.EntityAdded`
(or the equivalent event found in Phase 0-B) during ATD initialization.

### 3-B  `OnEntityAdded(IEntity entity)` handler

```csharp
static void OnEntityAdded(IEntity entity)
{
    if (entity is not IStaticEntity staticEntity) return;
    if (staticEntity.Proto is not FarmProto) return;
    IAreaManagingTower? tower = GetFarmingTowerForTile(staticEntity.Transform.Position.Xy);
    if (tower == null) return;

    // Do not assist if the site is already fully farmable.
    IEnumerable<Tile2i> coveredCells = ComputeCoveredDesignationCells(staticEntity);
    if (AreCellsAlreadyFarmable(coveredCells)) return;

    // Pause construction immediately.
    s_constructionManager.TrySetConstructionPause(staticEntity, true);

    // Inject flat leveling designations for cells that do not already have one.
    foreach (Tile2i origin in coveredCells)
    {
        EnsureFarmingDesignationForCell(tower, origin, staticEntity.Transform.Position.Z);
    }

    // Register a pending placement record.
    s_pendingFarmPlacements[staticEntity.Id] = new PendingFarmPlacement(tower.Id, coveredCells.ToHashSet());
}
```

### 3-C  `ComputeCoveredDesignationCells(IStaticEntity entity) → IEnumerable<Tile2i>`

Enumerate `entity.OccupiedTiles`, compute `SnapToDesignationGrid(origin + rel.RelCoord)` for
each tile, and return distinct origins.

### 3-D  `EnsureFarmingDesignationForCell(tower, origin, placementHeight)`

If no farming-level designation is already present at `origin` in the current tower session,
create a flat `LevelingDesignator` designation at `placementHeight` (the Z-height of the
entity's transform).  Use the same proto and `DesignationData` pattern as the existing
farming preparation logic to make it compatible with `FarmingPreparationSession`.

**Open question:** Should this adopt the cell into the *existing* farming session for the tower,
or should the session automatically capture it on the next tick?  Prefer the latter (let the
session's normal capture logic pick it up) to avoid duplicating session management code.

### 3-E  `PendingFarmPlacement` record

```csharp
private sealed class PendingFarmPlacement
{
    public readonly EntityId TowerId;
    public readonly HashSet<Tile2i> RequiredCells;
    public readonly HashSet<Tile2i> AtdInjectedCells; // cells ATD created (to clean up if abandoned)

    public PendingFarmPlacement(EntityId towerId, HashSet<Tile2i> requiredCells)
    {
        TowerId = towerId;
        RequiredCells = requiredCells;
        AtdInjectedCells = new HashSet<Tile2i>();
    }
}

private static readonly Dictionary<EntityId, PendingFarmPlacement> s_pendingFarmPlacements = new();
```

### 3-F  Save/load — why ATD cannot use the game save

ATD must be safe to remove from an existing save.  Writing `PendingFarmPlacement` data into
the game's save file would leave orphaned records if the mod is removed, potentially causing
`CorruptedSaveException`.  This rules out any game-side serialization hook.

`s_pendingFarmPlacements` is therefore pure runtime state and must be **reconstructed from
world-derived state** every time the game loads, the same way the existing farming session
is rebuilt.

### 3-G  Load-time reconstruction

**Preferred approach — world-state inference:**

On load (in the `ReEnableFarmingOnLoad` restoration path or equivalent), scan all entities:

```csharp
// Pseudocode — called after farming sessions are restored
static void RestorePendingFarmPlacements()
{
    foreach (IStaticEntity entity in s_entitiesManager.Entities)
    {
        if (entity.Proto is not FarmProto) continue;
        if (entity.ConstructionState != ConstructionState.InConstruction) continue;
        // Only re-adopt if the entity is actually in a tower area with active farming prep
        IAreaManagingTower? tower = GetFarmingTowerForTile(entity.Transform.Position.Xy);
        if (tower == null || !IsFarmingAutomationEnabledForTower(tower)) continue;
        IEnumerable<Tile2i> cells = ComputeCoveredDesignationCells(entity);
        if (AreCellsDone(cells, tower.Id)) continue; // already farmable — unblock immediately
        s_constructionManager.TrySetConstructionPause(entity, true);  // re-apply if not persistent
        s_pendingFarmPlacements[entity.Id] = new PendingFarmPlacement(tower.Id, cells.ToHashSet());
    }
}
```

This works because the farm entity (its position and construction state) *is* stored in the
game save — only ATD's runtime bookkeeping is not.  As long as:
- the farm entity is still `InConstruction` in the save (it will be, because it was paused), and
- the tower still has farming automation enabled, and
- at least one covered designation cell is not yet `Done`

...the inference will correctly re-register the pending placement.

**Edge case — player manually unpaused before save:**  
If the player unpaused the farm themselves and saved, the construction state is still
`InConstruction` but the pause bit is cleared.  On load, `RestorePendingFarmPlacements` will
re-pause it (because the site is still not ready).  This is the correct behaviour — the player
placing the farm opted into ATD management of that site.  Document this in the player-facing
docs once the feature ships.

**Edge case — OQ-4 (pause survives load natively):**  
If `TrySetConstructionPause` state is serialized by the vanilla construction manager, the re-apply
in `RestorePendingFarmPlacements` is a no-op (pausing an already-paused entity).  Safe in either case.

**Alternative — `ATDsettings.json` persistence:**  
If world-state inference proves unreliable (e.g., ambiguity with player-paused farms in non-ATD
contexts), persist a `List<int>` of raw `EntityId` values to `ATDsettings.json` under a
`"PendingFarmPlacements"` key.  On load, read those IDs, attempt to look up each entity, and
re-register.  `EntityId` values are stable within a save, so this is safe.  Prefer the inference
approach first; fall back to settings-file persistence only if needed.

---

## Phase 4 — Completion monitoring and construction ungate

**Goal:** Unpause the farm's construction when all covered designation cells have been fully
prepared (i.e., all are in `FarmingOriginPhase.Done`).

### 4-A  Tick loop check in `TickFarmingPreparationSessions` (or a new helper)

```csharp
static void TickPendingFarmPlacements()
{
    foreach (EntityId farmId in s_pendingFarmPlacements.Keys.ToList())
    {
        PendingFarmPlacement pending = s_pendingFarmPlacements[farmId];

        // Entity removed?
        if (!s_entitiesManager.TryGetEntity(farmId, out IStaticEntity farmEntity))
        {
            CleanUpAbandonedFarmPlacement(pending);
            s_pendingFarmPlacements.Remove(farmId);
            continue;
        }

        // All covered cells done?
        if (AreCellsDone(pending))
        {
            s_constructionManager.TrySetConstructionPause(farmEntity, false);
            s_pendingFarmPlacements.Remove(farmId);
        }
    }
}
```

### 4-B  `AreCellsDone(PendingFarmPlacement pending) → bool`

Look up the tower's `FarmingPreparationSession` and check that every `Tile2i` in
`pending.RequiredCells` maps to a `FarmingOriginSession` in `FarmingOriginPhase.Done`.

If a cell is not tracked in the session (e.g. it completed before the farm was placed and
the designation was removed), treat it as done.

### 4-C  `CleanUpAbandonedFarmPlacement(PendingFarmPlacement)`

Remove ATD-injected designations from `pending.AtdInjectedCells` that the player did not
already clear.  Do not remove designations placed by the player or by ATD's main auto-depth
scan (use `pending.AtdInjectedCells` to distinguish).

---

## Phase 5 — Edge cases and UX polish

### 5-A  Farm placed on already-prepared ground

If all covered cells are already in `Done` state when the farm is placed, skip the pause
entirely.  `OnEntityAdded` should return without registering a pending placement.

### 5-B  Tower disabled or farming automation turned off mid-construction

If `IsFarmingAutomationEnabledForTower` returns `false` for the tower while a pending
placement exists, optionally unpause the farm and log a warning.  The player can handle it
manually.  Do not silently leave a permanently paused farm.

### 5-C  Farm placed partially outside tower area

Phase 1 requires all tiles to be inside the tower area.  If only partial overlap is detected,
the suppression does not apply and the vanilla validators run normally.  This may produce a
confusing experience (farm partially in area, still blocked).  Consider a softer warning
message in the future.

### 5-D  Player manually unpauses the farm before site is ready

ATD must detect this (either by checking the pause state each tick or by subscribing to
`IConstructionManager.EntityPauseStateChanged`) and either re-pause or drop the pending
record (letting the player take control).  Recommend dropping the record with a log message.

### 5-E  Multiple tower areas that overlap

If a farm footprint spans two adjacent farming-enabled towers, `GetFarmingTowerForRequest`
returns `null` (no single tower covers all tiles).  This prevents ATD from assisting.  Acceptable
limitation for the initial version; document it.

### 5-F  Notifications

When construction is unpaused by ATD after site preparation, optionally fire a transient ATD
notification (using the existing `ATD.Notifications.cs` pattern) to tell the player the farm
is ready to build.

---

## Open questions / blockers

| # | Question | Needed by |
|---|---|---|
| OQ-1 | Can `TrySetConstructionPause` be called safely from an entity-added event callback? | Phase 3 |
| OQ-2 | Does the farm ghost physically block terrain designation placement on its tiles? | Phase 3 |
| OQ-3 | Do leveling designations need to be injected before or after the ghost appears? | Phase 3 |
| OQ-4 | Does the construction pause state persist through a save/load cycle natively? (ATD re-applies the pause on load either way via `RestorePendingFarmPlacements`, so this only affects whether double-pausing is a no-op or causes issues) | Phase 3-G |
| OQ-5 | What method name does `LayoutEntityTerrainValidator` use for the explicit interface implementation of `CanAdd`? | Phase 2 |

---

## File plan

| File | Changes |
|---|---|
| `ATD.FarmingPreparationSession.cs` | Add `GetFarmingTowerForTile`, `GetFarmingTowerForRequest`, `AreCellsDone`, `CleanUpAbandonedFarmPlacement`, `TickPendingFarmPlacements`, `s_pendingFarmPlacements` |
| New `ATD.FarmPlacementAssist.cs` | Validator patches (Phase 2), `OnEntityAdded`, `ComputeCoveredDesignationCells`, `EnsureFarmingDesignationForCell`, `PendingFarmPlacement` class |
| `ATD.Ticker.cs` | Call `TickPendingFarmPlacements()` in the tick loop |
| `ATD.State.cs` | Subscribe to entity-added event during init; resolve `IConstructionManager`; add load-restoration logic via `RestorePendingFarmPlacements` called from the `ReEnableFarmingOnLoad` path |
