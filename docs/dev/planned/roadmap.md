# Roadmap

Planned and candidate improvements for Kayser's Automatic Terrain Designations.

# Planned
* Climb cliff? (i.e. path A->B)
* Cut/Copy/Paste/Blueprint designations?
* rail incline (12.5%) designations
* Avoid ocean toggle, including landslide predictor

## Ramp safety margin — low priority

Review ramp building safety margin logic in more depth. Current margin is based on ramp planning depth rather than actual vertical drop relative to surrounding surface, so it may not fully reflect landslide/building risk in uneven terrain.

## Place leveling designations with a farm
Handle overlapping towers?
Auto-prepare ground anywhere
- If farm placed outside mining tower area -> warn that global filling rules must be adjusted
- Handle case where no storage exports soil -> warn, force-export, or alter truck behavior
- Not supported yet: Gas injection pump (requires 6 levels of limestone)

## Order truck or excavator from tower, pre-assigned to tower (+supress completed notification)

## Make Create Designations consider possible farming work

## Make corridors go up then down if they are long

## Tighten access generation framework

Define shared access-generation language, helpers, diagnostics, and regression scenarios for mining designations and farmland preparation.

See [../in-progress/access-framework.md](../in-progress/access-framework.md) for the working glossary and architecture notes.

## Issue?: Vehichles auto-released can be assigned to the tower again (or to something else)
Maybe reverse meaning and make auto-assign instead of auto-unassign?
Handled in v0.4.3a:
- Split soft-unassign into excavators and trucks.
- Auto-release vehicles also when tower paused.
- Improve tower with an assigned list of vehicles.
- Same vehicles cannot be truly soft-assigned to several towers because vanilla vehicle assignment is single-owner; ATD restore skips vehicles already assigned elsewhere.

## Improve ramps? make them turn?

Proposed approach: treat accessway generation as a least-work corridor search over the heightfield (turning/switchbacks fall out for free). See [accessway-pathfinding.md](accessway-pathfinding.md).

## Saddle designation?

## Prepare ground for ANY entity/blueprint /MrTammy

See [../in-progress/construction-assist.md](../in-progress/construction-assist.md) for the design; the non-farm leveling facet is the remaining work.

### Long term: Support underground pipe construction both for modded and unmodded games
* Highly complicated if unmodded: must dig trench, place pipes, build pipes, prepare remaining ground, place rest of BP.

## CornerDesignationKey Tooltip Global Scope clarification
Ensure the settings UI tooltip for the CornerDesignationKey explicitly documents that it is a global setting persisted in ATDsettings.json rather than per-save.

## Topsoil Optimization
Investigate placing only the minimum required soil to satisfy farmability (e.g., 95% thickness) instead of a full topsoil band. Deferred during the access framework rewrite.

Check Wanderer's issues
