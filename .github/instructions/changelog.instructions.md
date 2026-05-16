---
description: Instructions for maintaining the AutoTerrainDesignations change log.
applyTo: "changelog.txt"
---

# Change Log Maintenance Rules

The changelog.txt in the root is a user-facing change log that complies with the Mafi requirements for the CoI mod portal, but is maintained in markdown format for ease of editing and pasting into other contexts like Discord announcements. When making changes to the mod that affect user-facing behavior, features, or fixes, the change log must be updated with a clear description of the change, following the formatting and content rules below.

## File location
`changelog.txt` in the workspace root (plain text, required by the Mafi mod portal).

## Format rules
- Each release starts with `v<semver> | <YYYY-MM-DD>` (e.g. `v0.2.6 | 2026-05-08`)
- Top-level bullet entries use `*`.
- Sub-bullets use 4 spaces followed by `-`.
- New changes are **added to the current top entry** (the one matching `manifest.json`). Do not create a new version entry for code changes alone.
- A `[packaged]` suffix is appended to the version header line when that version is actually built as a package, e.g. `v0.4.0d | 2026-05-15 [packaged]`.
- The version (and its entry) is **only bumped when the user explicitly requests a package or release build**.

## Content rules
- One bullet per user-visible change (feature, fix, or behavioral change).
- Internal refactors, code cleanup, and build tooling changes are omitted unless they affect behavior.
- API additions visible to external modders are documented (e.g. new `AutoTerrainDesignationsApi` methods).
- Settings file additions are documented with the key name(s) and what they control.
- Start each bullet with a capital letter; no trailing period.
- Use neutral, factual language. No marketing or selling tone (avoid words like "polished", "first-class", "powerful", etc.).
- Use **bold** for setting names and UI control names (e.g. **Ore Purity Level**, **Ramp Width**).
- Fixes start with `Fixed:` followed by a short description.
- Sub-bullets are used when a single feature has multiple related details worth calling out.

## Versioning
- Version numbers follow `MAJOR.MINOR.PATCH` (currently in `0.x.y` range).
- **Local/alpha builds**: Use a letter suffix — `0.2.5a`, `0.2.5b`, etc. Each local package gets the next letter. Both `changelog.txt` and `manifest.json` are updated to match the new letter version.
- **Public releases**: Collate all lettered alpha entries into a single new version (`0.2.5a + 0.2.5b → 0.2.6`). Increment PATCH for fixes/minor additions, MINOR for noticeable new features. Both `changelog.txt` and `manifest.json` are updated.
- The version in `changelog.txt` must always match `manifest.json`.
- Do not propose version changes unless the user asks to package or release.

## Example entries
```
v0.2.5b | 2026-05-08 [packaged]   ← bumped and marked when package was built
* Corner designations now snap height and variant to adjacent existing designations
* Fixed: ramp generation could place ramps outside the tower area

v0.2.5a | 2026-05-01 [packaged]
* Added ore purity filter

v0.2.4 | 2026-04-20 [packaged]   ← public release collating 0.2.4a + 0.2.4b
* ...
```
