# Farm Placement Assist - Current Plan

Current as of release: 0.4.2d [unreleased]

Farm Placement Assist is implemented. The authoritative architecture reference is
`docs/dev/done/farming-designations.md` under **Farm Placement Assist**.

This file tracks remaining planned work only.

## Implemented baseline

- ATD intercepts `BatchCreateStaticEntitiesCmd` before a farm ghost enters the world.
- The farm ghost is not placed while terrain is being prepared, so vehicles are not blocked by the footprint.
- Farm validation errors for fertility and terrain height are suppressed only inside farming-enabled tower areas.
- Farm footprint cells are converted to 4x4 level-designation origins and prepared through the normal farming session.
- The original batch is replayed once every required farm cell reaches `FarmingOriginPhase.Done`.
- Pending batches are persisted in the ATD config-backed save state and restored after load.

## Remaining work

### Full engine-config persistence

Pending farm placement batches currently persist a compact replay record:

- proto ID
- tile transform, including rotation and reflection
- `applyConfiguration`
- farm crop schedule
- farm fertility target

During the same game session, ATD keeps the full original `EntityConfigData` objects and replays them exactly. Across save/load, non-farm items in a mixed batch are restored with proto and transform only. Future work should investigate serializing the full `EntityConfigData` bag through Mafi's `BlobWriter` / `BlobReader` with `ConfigSerializationContext`, so every blueprint-configured setting survives reload for every entity type.

### Player cancellation

There is still no UI or command to cancel a pending farm placement while the ghost is absent. A future implementation should expose pending placements in the tower inspector or add a console command that removes the pending batch and cleans up ATD-injected designations that are still unfulfilled.

### Building Leveling Assist integration

`docs/dev/planned/building-leveling-assist.md` extends the farm placement approach to non-farm static entities. When that work starts, reuse the batch-level intent model introduced for farm placement instead of reintroducing per-entity deferral.
