---
description: Instructions for maintaining AutoTerrainDesignations documentation under docs/.
applyTo: 'docs/**/*.md'
---

# ATD documentation instructions

Read the shared workspace instructions first:

- `../../../.github/instructions/coi-maintained-mods.instructions.md`

# ATD documentation structure
- `docs/player/` is for players. Focus on what the feature does, how to use it, relevant settings, and any console commands a normal player may reasonably use.
- `docs/api/` is for modders integrating with ATD. Document only public or intentionally supported integration points.
- `docs/dev/done/` is for implemented behavior and architecture. Write these docs from the code as it exists now, not from historical plans.
- `docs/dev/in-progress/` is for systems that are real but expected to change soon. Call out the expected replacement or migration path explicitly.
- `docs/dev/planned/` is for future work that is not yet implemented.

# What to update when code changes
- If a gameplay feature changes, update the matching player doc.
- If a public API changes, update the matching API doc.
- If an internal architecture or runtime contract changes, update the matching dev doc.
- If a feature moves from planning to implemented, move or replace the documentation so the stable behavior lives in `docs/dev/done/`.

# Writing style
- Keep player docs concrete and task-oriented. Prefer the player's mental model over internal enum names unless the names are shown in-game or in console output.
- Keep API docs contract-focused. Avoid promising internal behavior that is not meant to be stable.
- Keep dev docs precise and implementation-based. Name the real classes, phases, save/load behavior, and ownership boundaries.
- Avoid stale planning language like "will", "should", or "phase 1" in `docs/dev/done/` unless describing history or a deliberate compatibility constraint.

# Maintenance rules
- Prefer small edits to the relevant doc over creating new one-off markdown files.
- Remove or replace obsolete plan docs once they are superseded by implementation docs.
- Keep terminology consistent across docs: use the same feature name, setting name, and command names that the code and UI use.
- When a system is expected to be replaced soon, document both the current implementation and the expected migration target so future agents do not treat the current design as permanent.
