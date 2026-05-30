# Farm Placement Assist — Implementation Plan
Current as of release: 0.4.0k

**Status:** Phase 0 spike complete — all S1–S5 confirmed in-game. Ready for Phase 1 production implementation.  
**Feature description:** Allow players to place farm buildings on uneven or infertile ground
inside a farming-enabled mining tower area. ATD intercepts the placement before the ghost
enters the world, injects farming designations for the footprint, lets vehicles prepare the
site unobstructed, then auto-places the farm when the site is ready.

---

## Terminology

| Term | Meaning |
|---|---|
| Farming-enabled tower | Any `IAreaManagingTower` for which `IsFarmingAutomationEnabledForTower` returns `true` |
| Farm footprint | The set of 1×1 tiles occupied by a `FarmProto` entity |
| Designation cell | A 4×4-tile designation grid origin (`DesignationData.OriginTile`); always aligned to a 4-tile boundary |
| Covered cells | The set of designation-grid origins that overlap the farm footprint |
| Pending placement | An ATD-tracked `PlacementIntent` record that holds the parameters (proto, position, rotation) of an intercepted farm placement and the designation cells the site requires; no ghost entity is present in the world while the record exists |

---

## Vehicle blocking constraint

**Critical constraint discovered during research (2026-05-29):**

A farm ghost in `InConstruction` state registers its footprint tiles in `TerrainOccupancyManager` on entity addition and sets vehicle-blocking tile flags via the `TileFlagReporter` system. Pausing construction (`TrySetConstructionPause`) does not remove those flags — it only stops vehicle *job* queuing. The tiles remain physically impassable.

This means the original plan (place ghost → pause → prepare ground) is unworkable: vehicles cannot reach the footprint tiles to do the leveling work while the ghost occupies them.

**The ghost must not be present in the world during site preparation.**

### Approach comparison

| Approach | Description | Pros | Cons |
|---|---|---|---|
| **A — Intercept & defer (recommended)** | ATD cancels the entity-add command before the ghost enters the world, records the placement intent (proto, position, rotation), prepares the site, then re-issues the placement when all cells are `Done` | Clean UX; no blocking issue; no fighting engine internals | Must intercept the input command or entity-add early enough; player sees no immediate ghost |
| **B — TileFlagReporter override** | After ghost is placed, ATD creates a `TileFlagReporter` that clears vehicle-blocking bits on the farm tiles | Ghost stays visible | Requires fighting the occupancy manager; fragile; not a supported mod API |
| **C — Remove & re-place** | ATD lets the ghost be placed, then immediately removes the entity, records the intent, prepares the site, re-places when done | Entity lifetime is normal | Player sees the ghost flicker in and out; entity IDs change; needs the same intercept path anyway |

Approach A is recommended. The exact intercept point (Harmony prefix on the entity-add command or on the layout entity placement controller) needs to be confirmed in Phase 0-B.

## Overview of the full flow (revised)

```
Player hovers farm preview
        │
        ├─ footprint within farming-enabled tower area?
        │     yes → suppress FarmFertileGroundValidator + LayoutEntityTerrainValidator errors
        │     no  → vanilla behaviour (no changes)
        │
Player clicks to place
        │
ATD intercepts entity-add command (before ghost enters the world)
        │
        ├─ Is FarmProto + within farming-enabled tower area?
        │     no  → let command through (vanilla)
        │     yes → (1) cancel / consume the command
        │           (2) record PlacementIntent (proto, position, rotation)
        │           (3) compute covered cells
        │           (4) for each covered cell, ensure a farming-level designation exists
        │               or create one at the current surface height
        │           (5) show ATD placement-pending indicator (optional)
        │           (6) register a PendingFarmPlacement record
        │
Vehicles prepare the site (no ghost blocking them)
        │
ATD tick loop
        │
        ├─ For each PendingFarmPlacement:
        │     (a) if player cancels intent → clean up ATD designations, remove record
        │     (b) are all covered cells in Done state?
        │           → issue the original placement command (auto-place ghost)
        │           → remove record
        │
Farm ghost appears on prepared ground, vehicles construct the farm normally
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

### 0-B  Intercept point for entity-add cancellation

The ghost must be prevented from entering the world entirely (Approach A). Find the earliest
call-site where the farm placement command can be intercepted and cancelled before the entity
is registered in the world:

- Locate the input command or controller method that processes the player's placement click
  for layout entities (e.g. `BuildingPlacementController.TryPlace`, `LayoutEntityPlacementController.CommitPlacement`,
  or the equivalent simulation-thread command).
- Confirm the intercept point is *before* occupancy registration, not *after*.
- Confirm the full placement parameters (proto, position, rotation) can be read and stored
  from that call-site so ATD can re-issue them later.
- If the only intercept point is an `EntityAdded` event (post-add), Approach C (remove & re-place)
  must be used instead; document the tradeoffs.

### 0-C  Verify ghost vehicle-blocking in practice

Confirm in-game that a farm ghost in `InConstruction` state blocks vehicles from entering its
tiles (expected based on source analysis: occupancy manager registers blocking flags on add).
Also confirm that terrain designations *can* be placed on the farm's tiles once the ghost is
absent — i.e. there is no other blocker that would prevent designation placement on a freshly
cleared site.

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
// FarmFertileGroundValidator.CanAdd takes the concrete LayoutEntityAddRequest (non-generic, public method).
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
// LayoutEntityTerrainValidator.CanAdd is an explicit interface impl of
// IEntityAdditionValidator<ILayoutEntityAddRequest>. The IL method name is compiler-mangled;
// use TargetMethod() to locate it via the interface map rather than a string name.
static class Patch_LayoutEntityTerrainValidator_CanAdd_Farm
{
    static MethodBase TargetMethod()
    {
        var iface = typeof(IEntityAdditionValidator<ILayoutEntityAddRequest>);
        var map = typeof(LayoutEntityTerrainValidator).GetInterfaceMap(iface);
        return map.TargetMethods[0]; // only one method on this interface
    }

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

## Phase 3 — Placement intercept and designation injection

**Goal:** Intercept the farm placement before the ghost enters the world, inject farming
designations for all covered cells, and record the placement intent for later replay.

### 3-A  Intercept — prefix on `EntitiesCommandsProcessor.Invoke(BatchCreateStaticEntitiesCmd)`

**Confirmed in Phase 0 spike (2026-05-30):** farms use `BatchCreateStaticEntitiesCmd`, not
`CreateStaticEntityCmd`. No entity is instantiated before this prefix fires — the intercept
is completely clean (no ghost, no occupancy registration).

```csharp
[HarmonyPatch]
static class Patch_EntitiesCommandsProcessor_Invoke_Batch_Farm
{
    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(EntitiesCommandsProcessor), "Invoke",
            new[] { typeof(BatchCreateStaticEntitiesCmd) });

    static bool Prefix(BatchCreateStaticEntitiesCmd cmd)
    {
        if (!IsInitialized || s_protosDb == null) return true;

        bool anyIntercepted = false;
        foreach (EntityConfigData item in cmd.ConfigData)
        {
            if (item.Prototype.ValueOrNull is not FarmProto farmProto) continue;
            TileTransform? transform = item.Transform;
            if (!transform.HasValue) continue;

            Tile2i position = transform.Value.Position.Xy;
            IAreaManagingTower? tower = GetFarmingTowerForTile(position);
            if (tower == null) continue;

            // Site already ready? Let through (no deferral needed).
            IEnumerable<Tile2i> covered = ComputeCoveredDesignationCells(
                farmProto, position, transform.Value.Rotation);
            if (AreCellsAlreadyFarmable(covered)) continue;

            OnFarmPlacementIntercepted(farmProto, position, transform.Value.Rotation,
                transform.Value.Position.Z, tower, covered);
            anyIntercepted = true;
        }

        if (!anyIntercepted) return true;

        // Report success so the engine doesn't play the error sound / show a toast.
        cmd.SetResultSuccess();
        return false;
    }
}
```

**Replay:** use `IInputScheduler.ScheduleInputCmd(new CreateStaticEntityCmd(protoId, transform))`.
The replay fires on the `~Mai` thread via the Unity ticker (≈1 s after intercept in spike;
production should replay immediately when `AreCellsDone` becomes true, not on a fixed timer).

### 3-B  `OnFarmPlacementIntercepted(FarmProto proto, Tile2i position, Rotation rotation, IAreaManagingTower tower)`

```csharp
static void OnFarmPlacementIntercepted(
    FarmProto proto, Tile2i position, Rotation rotation, IAreaManagingTower tower)
{
    IEnumerable<Tile2i> coveredCells = ComputeCoveredDesignationCells(proto, position, rotation);

    // If site already fully prepared, replay the placement immediately.
    if (AreCellsAlreadyFarmable(coveredCells))
    {
        ReplayFarmPlacement(proto, position, rotation);
        return;
    }

    // Inject leveling designations for cells not already managed.
    foreach (Tile2i origin in coveredCells)
        EnsureFarmingDesignationForCell(tower, origin, s_terrainManager.GetHeight(position).Value);

    // Register intent for later replay.
    var intent = new PlacementIntent(proto, position, rotation, tower.Id, coveredCells.ToHashSet());
    s_pendingFarmPlacements.Add(intent);
    // Optional: show ATD placeholder indicator at position.
}
```

### 3-C  `ComputeCoveredDesignationCells(FarmProto proto, Tile2i position, Rotation rotation) → IEnumerable<Tile2i>`

Derive the farm's occupied tiles from its proto layout (no live entity needed — the footprint
is fully determined by proto + position + rotation). Apply `SnapToDesignationGrid` to each
occupied tile and return distinct origins.

### 3-D  `EnsureFarmingDesignationForCell(tower, origin, placementHeight)`

Same as original plan: if no farming-level designation is already present at `origin` in the
current tower session, create a flat `LevelingDesignator` designation at `placementHeight`.
Let the session's normal capture logic pick it up on the next tick.

### 3-E  `PlacementIntent` record

```csharp
private sealed class PlacementIntent
{
    public readonly FarmProto Proto;
    public readonly Tile2i Position;
    public readonly Rotation Rotation;
    public readonly EntityId TowerId;
    public readonly HashSet<Tile2i> RequiredCells;
    public readonly HashSet<Tile2i> AtdInjectedCells;

    public PlacementIntent(FarmProto proto, Tile2i position, Rotation rotation,
        EntityId towerId, HashSet<Tile2i> requiredCells)
    {
        Proto = proto; Position = position; Rotation = rotation;
        TowerId = towerId; RequiredCells = requiredCells;
        AtdInjectedCells = new HashSet<Tile2i>();
    }
}

private static readonly List<PlacementIntent> s_pendingFarmPlacements = new();
```

### 3-F  Save/load persistence

Because no ghost entity exists in the world, there is no world-state to infer from on load.
The placement intent must be persisted explicitly. Use the existing `config.json` state blob
(same mechanism as per-tower settings): serialize `s_pendingFarmPlacements` as a JSON array
under a new key (e.g. `"pendingFarmPlacements"`). Each entry stores: proto ID string,
position (x, y), rotation int, tower entity ID.

On load, deserialize the list and re-register each intent. If the site is already done at
load time (cells all `Done`), replay the placement immediately instead of re-registering.

---

## Phase 4 — Completion monitoring and construction ungate

**Goal:** Unpause the farm's construction when all covered designation cells have been fully
prepared (i.e., all are in `FarmingOriginPhase.Done`).

### 4-A  Tick loop check in `TickFarmingPreparationSessions` (or a new helper)

```csharp
static void TickPendingFarmPlacements()
{
    for (int i = s_pendingFarmPlacements.Count - 1; i >= 0; i--)
    {
        PlacementIntent intent = s_pendingFarmPlacements[i];

        // All covered cells done? → auto-place the farm.
        if (AreCellsDone(intent))
        {
            ReplayFarmPlacement(intent.Proto, intent.Position, intent.Rotation);
            s_pendingFarmPlacements.RemoveAt(i);
            // Optional: notify player that the farm is ready and has been placed.
        }
    }
}
```

### 4-B  `AreCellsDone(PlacementIntent intent) → bool`

Look up the tower's `FarmingPreparationSession` and check that every `Tile2i` in
`intent.RequiredCells` maps to a `FarmingOriginSession` in `FarmingOriginPhase.Done`.
Cells not tracked in the session (preparation already removed) are treated as done.

### 4-C  `ReplayFarmPlacement(FarmProto proto, Tile2i position, Rotation rotation)`

```csharp
static void ReplayFarmPlacement(FarmProto proto, Tile2i position, Rotation90 rotation)
{
    // Use IInputScheduler to issue a CreateStaticEntityCmd — the same path as a player click.
    // allowValidationSuppression is false; the Phase 2 validator patches handle suppression.
    // IInputScheduler must be stored during Initialize() alongside the other managers.
    var transform = new TileTransform(
        new Tile3i(position.X, position.Y, s_terrainManager.GetHeight(position).Value),
        rotation,
        isReflected: false);
    var cmd = new CreateStaticEntityCmd(proto.Id, transform, isFree: false,
        allowValidationSuppression: false);
    s_inputScheduler.ScheduleInputCmd(cmd);
    // Note: cmd.Result (the new EntityId) is available after processing — ignore for now.
}
```

`IInputScheduler` must be added as a parameter to `AutoDepthDesignation.Initialize()` and stored
as `s_inputScheduler`. Inject it from `ATD.AutoTerrainDesignationsMod.cs` the same way other
managers are injected.

### 4-D  `CleanUpAbandonedFarmPlacement(PlacementIntent intent)`

Called when the player cancels the pending placement (Phase 5 UX). Removes ATD-injected
designations from `intent.AtdInjectedCells` that have not already been cleared.

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

### 5-D  Player cancels the pending placement

ATD needs a way for the player to cancel a pending intent (no ghost is visible, so the normal
demolish/cancel tool cannot be used). Options: a console command (`atd_cancel_farm_placement`),
an entry in the tower inspector showing pending farm placements, or a dedicated placeholder
entity the player can demolish. Define the UX before implementing.

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
| OQ-1 | ~~Can `TrySetConstructionPause` be called safely from an entity-added event callback?~~ N/A — ghost is not placed | — |
| OQ-2 | ~~Does the farm ghost physically block terrain designation placement on its tiles?~~ Resolved: YES (occupancy manager blocks vehicles) — ghost must not be placed during prep | Phase 3 |
| OQ-3 | ~~Do leveling designations need to be injected before or after the ghost appears?~~ Before — no ghost during prep | — |
| OQ-4 | ~~Does the construction pause state persist through a save/load cycle?~~ N/A | — |
| OQ-5 | ~~What method name does `LayoutEntityTerrainValidator` use for the explicit interface implementation of `CanAdd`?~~ Resolved: explicit impl of `IEntityAdditionValidator<ILayoutEntityAddRequest>`. Harmony patch must use `TargetMethod()` returning the mapped interface target (see Phase 2-B). | — |
| OQ-6 | ~~What is the earliest intercept point for a farm placement command where the ghost has not yet entered the world?~~ Resolved: **`EntitiesCommandsProcessor.Invoke(BatchCreateStaticEntitiesCmd)`** — farms use `BatchCreateStaticEntitiesCmd`, NOT `CreateStaticEntityCmd`. Harmony prefix on this method fires before any entity is instantiated. Read `cmd.ConfigData`, pattern-match each entry's `Prototype.ValueOrNull` as `FarmProto`, extract `item.Transform`. Return false + `cmd.SetResultSuccess()` to silently consume. Replay via `IInputScheduler.ScheduleInputCmd(new CreateStaticEntityCmd(protoId, transform))` scheduled from the Unity ticker (~1 s delay, acceptable for spike). Confirmed in-game 2026-05-30: intercept on `~Sim` thread, replay on `~Mai` thread, farm appears. | — |

---

## File plan

| File | Changes |
|---|---|
| `ATD.FarmingPreparationSession.cs` | Add `GetFarmingTowerForTile`, `GetFarmingTowerForRequest`, `AreCellsDone`, `CleanUpAbandonedFarmPlacement`, `TickPendingFarmPlacements`, `s_pendingFarmPlacements` |
| New `ATD.FarmPlacementAssist.cs` | Placement-intercept patch (Phase 3-A), `OnFarmPlacementIntercepted`, `ComputeCoveredDesignationCells`, `EnsureFarmingDesignationForCell`, `ReplayFarmPlacement`, `PlacementIntent` class; validator patches (Phase 2) |
| `ATD.Ticker.cs` | Call `TickPendingFarmPlacements()` in the tick loop |
| `ATD.State.cs` | Wire intercept patch during init; resolve any needed managers; call `RestorePendingFarmPlacements` (from persisted JSON) in the load path |
| `ATD.TowerSettingsConfigPersistence.cs` | Serialize/deserialize `s_pendingFarmPlacements` under `"pendingFarmPlacements"` key in the config blob |
