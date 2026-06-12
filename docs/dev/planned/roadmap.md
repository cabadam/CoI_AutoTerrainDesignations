# Roadmap

Planned and candidate improvements for Kayser's Automatic Terrain Designations.

# Planned
* Climb cliff?
* Cut/Copy/Paste/Blueprint designations?

Auto-release vehicles also when tower paused.

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

## Issue?: Vehichles auto-released can be assigned to the tower again (or to something else)
Maybe reverse meaning and make auto-assign instead of auto-unassign?
Improve tower with an assigned list of vehichles.
Can same vehicles be soft-assigned to several towers?
Split soft-unassign into excavators and trucks.

## Improve ramps? make them turn?

## Saddle designation?

## Level ground for ANY entity/blueprint /MrTammy

See [building-leveling-assist.md](building-leveling-assist.md) for full implementation plan.

## CornerDesignationKey Tooltip Global Scope clarification
Ensure the settings UI tooltip for the CornerDesignationKey explicitly documents that it is a global setting persisted in ATDsettings.json rather than per-save.

Check Wanderer's issues


