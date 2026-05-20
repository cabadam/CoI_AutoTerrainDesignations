# Farming Automation Scalability
Current as of release: 0.4.0k

## Status

Implemented and verified through tester log review.

Large farming sessions could stutter or freeze during load-time reactivation and repeated access-ramp checks. Tester saves confirmed sessions with thousands of tracked origins and hundreds of unreachable excavation origins. The worst observed spikes were in access/ramp handling, especially `CreateAccessRamp -> BuildBuildingOccupiedTiles -> GetAllEntitiesOfType<IStaticEntity>()` under contention with CLExporter.

## Completed Work

- Added slow-operation logging for preparation passes, filling passes, access checks, and pending fill-area rebuilds.
- Added `[ATD Farming Perf]` breakdown logging with capture/advance/access/summary/state-scan timings and origin counts.
- Added `tools/get-mod-log.ps1` to extract ATD/AFD rows from Captain of Industry logs.
- Added scalable access recheck intervals:
  - 10 ticks for small work sets.
  - 30 ticks for 250+ active work designations.
  - 90 ticks for 1000+ active work designations.
- Cached pending filling areas per session and rebuilt only when queued filling/rim/shoulder/origin state changes.
- Cached `BuildBuildingOccupiedTiles` per tower for up to 600 farming ticks, removing the primary CLExporter contention path.
- Replaced per-tile designation lookups in ramp tile checks with a per-call `s_designationOriginsInArea` hash set populated by one `SelectDesignationsInArea` scan.
- Kept `s_designationOriginsInArea` current when `PlaceDesignation` creates ramp origins.
- Optimized ramp search:
  - Perimeter ore-tile candidate filtering.
  - `UpdateChangedTiles()` hoisted out of per-candidate loops.
  - Ramp-mouth reachability BFS deduplicated per call.
  - Reachability checks capped at `MAX_RAMP_REACHABILITY_CHECKS = 50`.
  - Search margin reduced from 96 to 48 tiles.
  - Search cap reduced from 250,000 to 20,000 tiles.

## Verification Notes

- Earlier tester logs showed preparation spikes of roughly 3.5-31 seconds, then later 15-22 second spikes caused by access/ramp work.
- After ramp-search and building-occupancy cache optimizations, tester logs showed no spikes above roughly 400 ms.
- Most large-session preparation passes for tower `77674` ran at roughly 25-107 ms.
- Remaining elevated passes were access-dominated and tied to `BuildDesignationOriginsInArea` still rebuilding every `CreateAccessRamp` invocation.

## Not Done Here

- Full origin-analysis batching was deferred as a larger refactor.
- TTL caching for `BuildDesignationOriginsInArea` remains planned.
