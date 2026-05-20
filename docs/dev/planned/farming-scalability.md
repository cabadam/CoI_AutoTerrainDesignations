# Farming Automation Scalability Follow-ups
Current as of release: 0.4.0k

## Status

Planned follow-up work from the completed farming scalability pass.

## Planned Work

### Add TTL cache for designation origins in tower area

`BuildDesignationOriginsInArea` still rebuilds `s_designationOriginsInArea` once per `CreateAccessRamp` call using `SelectDesignationsInArea`.

Recommended implementation:

- Cache by tower `EntityId`.
- Reuse for about 30 farming ticks.
- Clear when the tower changes or farming tick state resets.
- Keep the existing `PlaceDesignation` update path so newly placed ramp origins are reflected immediately even while the cache is warm.

Reason:

- A roughly 300x300 tower area currently causes thousands of `m_designations` dictionary lookups per rebuild.
- Under navigation/entity lock contention, repeated uncached calls can still produce elevated access times.
- A 30-tick TTL would limit the scan to at most once every ~3 seconds per tower while preserving current ramp-placement behavior.

### Later: batch origin analysis

Process only N farming origins per tick in preparation/filling passes.

Requirements:

- Track per-session cursors.
- Track dirty/full-scan state.
- Preserve phase-transition correctness.
- Keep small sessions responsive.

This should wait until lower-risk cache work is verified.

## Non-goals

- Do not disable `reEnableFarmingOnLoad` by default.
- Do not change user-facing farming behavior beyond reducing repeated expensive work.
- Do not change designation semantics or tower dump-rule timing unless profiling proves it is needed.
