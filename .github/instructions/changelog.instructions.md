---
description: Instructions for maintaining the AutoTerrainDesignations change log.
applyTo: "changelog.{txt,md}"
---

# Change Log Maintenance Rules

The change log is split across two files:

- **`changelog.md`** — the detailed working change log. All changes go here, including alpha builds. This is the primary file for day-to-day editing and serves as a source for Discord announcements.
- **`changelog.txt`** — the public CoI mod portal change log. Only non-alpha public releases are tracked here. It must remain a `.txt` file as required by the Mafi mod portal.

Both files use the same format and content rules below. When making changes to the mod that affect user-facing behavior, features, or fixes, `changelog.md` must be updated.

## File locations
- `changelog.md` in the workspace root: detailed changelog; all alpha and public entries.
- `changelog.txt` in the workspace root: public releases only (plain text, required by the Mafi mod portal). Never contains alpha-suffixed version entries.

## Entry suffix states
Entry suffix tags apply **only to `changelog.md`** — never to `changelog.txt`.

| Suffix | Meaning |
|---|---|
| `[unreleased]` | Work in progress — changes are being added to this entry |
| `[packaged]` | ZIP has been built and is ready to upload; not yet on the portal |
| *(no suffix)* | Released on the CoI mod portal |

## Format rules
- Each release in `changelog.md` starts with `v<semver> | <YYYY-MM-DD>` followed by the appropriate suffix (e.g. `v0.2.6 | 2026-05-08 [unreleased]`)
- Each release in `changelog.txt` starts with `v<semver> | <YYYY-MM-DD>` with **no suffix tag**
- Top-level bullet entries use `*`.
- Sub-bullets use 4 spaces followed by `-`.
- New changes are **added to the current top entry in `changelog.md`** (the one matching `manifest.json`). Do not create a new version entry for code changes alone.
- Do not bump the version or create a new entry unless explicitly asked to package.

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
- **Local/alpha builds**: Use a letter suffix — `0.2.5a`, `0.2.5b`, etc. Each local package gets the next letter. Only **`changelog.md`** and `manifest.json` are updated. `changelog.txt` is not touched for alpha builds.
- **Public releases**: Collate all lettered alpha entries since the last public release into a single new version (`0.2.5a + 0.2.5b → 0.2.6`). Increment PATCH for fixes/minor additions, MINOR for noticeable new features. **Both `changelog.md` and `changelog.txt`** are updated — `changelog.txt` gets one collated entry for the public version; `manifest.json` is updated.
- **Do not bump the version or advance the entry until the user confirms the package was successfully released.**

## Packaging procedure (exact sequence)

### Alpha package
1. **Commit and push** any uncommitted changes first.
2. **Replace `[unreleased]`** with `[packaged]` on the current top entry in **`changelog.md`** only. Do not touch `changelog.txt`. Do not change the version or date.
3. **Run the package build** (`build.ps1 -Configuration Release -Package`). The ZIP produced will carry the version in `manifest.json` at build time.
4. **Commit and push** the changelog change with a message like `chore: package <version>`.

### Public release package
1. **Commit and push** any uncommitted changes first.
2. **Replace `[unreleased]`** with `[packaged]` on the current top entry in **`changelog.md`** only.
3. **Add a new collated entry** to the top of **`changelog.txt`** with the public version number (no letter suffix) and the current date — no suffix tag. Collate the notable changes from all alpha entries since the last public release into a single clean list.
4. **Run the package build** (`build.ps1 -Configuration Release -Package`). The ZIP produced will carry the version in `manifest.json` at build time.
5. **Commit and push** the changelog changes with a message like `chore: package <version>`.

**Never bump the version before running the build.** The ZIP file name comes from `manifest.json` at build time.

The user will upload the ZIP to the portal manually. Do not advance the version automatically.

## When starting new work after a package
When the user starts making changes and the current top entry in `changelog.md` is marked `[packaged]`, **ask whether it was released to the portal**:

- **Yes — released**:
  - In `changelog.md`: replace `[packaged]` with no suffix, then add a new empty top entry `v<next> | <date> [unreleased]`.
  - In `changelog.txt`: replace `[packaged]` with no suffix on the matching public entry (if one was added for this release).
  - Update `manifest.json` to the next version.
  - Commit and push with a message like `chore: release <version>, advance to <next> [unreleased]`.
- **No — not yet released** (e.g. the package had a problem): Keep the current entry and continue adding changes to it; replace `[packaged]` back with `[unreleased]` in `changelog.md` (and in `changelog.txt` if a public entry was added) since it will need to be rebuilt.

## Example entries

`changelog.md` (detailed, includes all alphas):
```
v0.4.1a | 2026-05-21 [unreleased]   ← in progress
* Fixed: some farming origin bug

v0.4.0 | 2026-05-20                 ← released on portal (public)
* ...

v0.4.0b | 2026-05-19 [packaged]     ← alpha ZIP built, not yet uploaded
* Fixed: ramp generation could place ramps outside the tower area

v0.4.0a | 2026-05-18                ← alpha, superseded by public release
* Added ore purity filter
```

`changelog.txt` (public releases only):
```
v0.4.0 | 2026-05-20                 ← released on portal
* Fixed: ramp generation could place ramps outside the tower area
* Added ore purity filter

v0.3.0 | 2026-04-20                 ← released on portal
* ...
```
