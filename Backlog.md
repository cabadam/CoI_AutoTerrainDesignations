# Backlog

## Ramp safety margin - low priority
* Review ramp building safety margin logic in more depth. Current margin is based on ramp planning depth rather than actual vertical drop relative to surrounding surface, so it may not fully reflect landslide/building risk in uneven terrain.

## Farming Designations
Goal: When player designate land for farming, it is automatically prepared for farming.

Farming preconditions: Flat, fertile land.

Suggested approach: 
From tower, a button to flip between normal mode and farming mode. When the tower is in farming mode, leveling designations (pre-existing or new) behave in special way.

When entering farming mode or user places a leveling designation: Store the leveling designation(s) internally (will be referenced and restored later), analyze the land where the designation is placed and prepare it for farming. Preparation for farming will need to happen in several steps, du to how dumping rules on the tower works:
* Define the target height as the level of the original leveling designation. Store the leveling designation internally as the original farming designation (to be restored later).
Analysis/Leveling:
* If the designation is already fully farmable and fully at target level: store to be restored at Hold or Done.
* Else If the designation is fully farmable and everywhere at or above target level, leave the designation in place for it to be leveled (will be analyzed again for farmability again when leveled).
* Else If the tile is fully farmable: store designation to be restored at Hold or Filling.
* Assert: Tile is somewhere not farmable => proceed to Preparation.
* When all designations are done/leveled, proceed to Preparation. Else stay in Analysis/Leveling.
Preparation [assert: all remaining designations are not fully fertile]:
* Store remaining active designations internally (to be restored at Hold or Filling). Place leveling designations one level below the designations just stored. This will excavate the infertile designations to make room for the topsoil.
* Keep Preparation until all designations are completely excavated.
Filling [assert: all designations are farmable or at least 1 z below target level]:
* Restrict dumping in the tower to just dirt/compost. (Store original tower dumping restrictions; will be restored later)
* Restore all designations previously stored.
* Keep Filling until all tiles are completely filled.
* Restore original dumping restrictions on tower.


Important events:
* Save game (or pause) must be patched to remove all temporary designations and restore the original leveling designations before the game is saved. Also, restore original dumping restrictions.
* Initialization/Game load (unpause): resume farming mode on all towers with at least one leveling designation.
* Save-removable invariant: saves must remain readable by vanilla without ATD. Do not serialize ATD farming session state into the save. Before saving, restore the original player leveling rectangle/orders and remove temporary ATD scaffolding/rules. After load, ATD should infer the needed working state by re-analyzing the restored original orders and current terrain.

During implementation: use a debug/status bar.


## Alpha feedback: adjacent designations can fight each other - high priority
- Observed issue: two adjacent terrain designations at different target heights can create a perpetual loop. Typical case is mining/leveling on a lower z next to dumping/leveling on a higher z; material slides from the higher tile into the lower tile, then the lower tile is worked again, and the cycle repeats.
- This is especially risky for farming automation because preparation, access ramps, and filling can temporarily create mixed-height work near each other.
- Need mitigation before broad release. Possible directions:
  - Avoid creating temporary access ramps directly adjacent to active farming origins when their target heights oppose the current phase.
  - Treat adjacent higher dump/level designations as blockers for lower excavation work, and adjacent lower mine/level designations as blockers for higher filling work.
  - When switching phases, aggressively remove automation-owned scaffolding that no longer services active work before restoring or creating the next phase's designation.
  - Consider adding a one-tile buffer or coordinated batch transition so neighboring designations do not alternate between excavation and dumping at incompatible heights.

## Alpha feedback: large farming jobs need work sequencing - high priority
- Observed issue: on larger farming designations, excavators and trucks work tiles in an arbitrary order and can create their own access/pathability failures. Example: trucks may dump material in a way that locks other vehicles in or cuts off remaining work.
- This is distinct from ramp generation: the initial access route may exist, but uncontrolled work order can destroy it during filling or preparation.
- Need sequencing/activation strategy, especially for filling and possibly preparation. Possible directions:
  - Activate only a frontier/slice of designations at a time, expanding from accessible terrain inward or from the far side back toward the tower/access route.
  - During filling, keep access corridors and ramps reserved until all work behind them is complete, then remove/fill them last.
  - During preparation, excavate in an order that preserves an exit path for excavators instead of opening the entire area at once.
  - Reuse the pathability/access checker as a planning tool, but avoid running it every tick; compute a batch plan and advance it only when the current slice completes or becomes blocked.

## Alpha feedback: accepted risks / no action
- Terrain modification can undermine a tower and make it fall. Keep this behavior for now: vanilla does not guard against this either, so farming automation should not add special tower-stability rules unless later testing shows it causes surprising automation-only failures.

## Alpha stabilization order
1. Fix farming stutter by throttling or event-gating access/pathability checks. [v0.3.1c: optimized access BFS target lookup and made farming automation opt-in per tower.]
2. Tighten automation-owned scaffolding cleanup and phase boundaries. [v0.3.1c: completed preparation target-1 designations are hidden before controlled filling starts.]
   - Alpha finding: during preparation, completed preparation designations should usually remain active at `target - 1` until the whole preparation phase is complete. This lets them re-level if material slides on/off while neighboring prep work continues. The original player leveling rectangle should be restored only when the tower transitions to controlled filling with farmable dump rules.
   - Alpha finding: an origin reaching `Done` is provisional until the whole tower-level filling session is complete. Neighboring work can disturb it again, so filling must keep tracking and revalidating `Done` origins.
3. Add sequenced filling batches so trucks preserve access while filling larger areas.
4. Add sequenced preparation batches so excavators preserve exit/access paths.
5. Revisit adjacent designation conflict detection only after sequencing is in place; good sequencing may suppress most of these loops without a separate broad blocker.

## Refactor out of the temporary .cs files created during development of farming designations

### Consider improving land reclaim algo; consumes more dirt than necessary as it slides less steep

## Add a bonus: notification when Excavator is complete in the vehicle depot; maybe even the order-from-tower including pre-assign to tower?
