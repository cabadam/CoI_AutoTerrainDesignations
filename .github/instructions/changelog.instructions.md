---
description: Instructions for maintaining the AutoTerrainDesignations change log.
applyTo: "changelog.txt"
---

# Change Log Maintenance Rules

## File location
`changelog.txt` in the workspace root (plain text, required by the Mafi mod portal).

## Format
- Each release starts with `v<semver> | <YYYY-MM-DD>` (e.g. `v0.2.6 | 2026-05-08`)
- Top-level bullet entries use `*`.
- Sub-bullets use 4 spaces followed by `-`.
- No markdown headings (`#`).
- Unreleased / in-progress work goes in the **first entry** at the top of the file with the next version number and today's date. Do not use an "Unreleased" or "WIP" label.

## Content rules
- One bullet per user-visible change (feature, fix, or behavioral change).
- Internal refactors, code cleanup, and build tooling changes are omitted unless they affect behavior.
- API additions visible to external modders are documented (e.g. new `AutoTerrainDesignationsApi` methods).
- Settings file additions are documented with the key name(s) and what they control.
- Start each bullet with a capital letter; no trailing period.
- Use **bold** for setting names and UI control names (e.g. **Ore Purity Level**, **Ramp Width**).
- Fixes start with `Fixed:` followed by a short description.
- Sub-bullets are used when a single feature has multiple related details worth calling out.

## Versioning
- Version numbers follow `MAJOR.MINOR.PATCH` (currently in `0.x.y` range).
- Check the manifest version and existing change log entries to determine the current version number.
- Increment PATCH for bug fixes and minor additions.
- Increment MINOR for new features or behavioral changes that are noticeable to the user.
- After a release is packaged (zip exists in `artifacts/` matching the manifest version), start a new entry for the next version.

## Example entry
```
v0.1.13 | 2026-05-01
* **Terrain Designations** panel and **Ore Composition** panel can now be embedded in external mod inspectors via `AutoTerrainDesignationsApi.BuildDesignationPanel` and `BuildOreCompositionPanel`
* Removed `generateRamps` parameter from `CreateDesignationsForTower` API — ramp generation is now always controlled by the per-tower **Ramp Width** setting (0 = disabled)
```
