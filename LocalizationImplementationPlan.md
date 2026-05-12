# Localization Implementation Plan

## Goals
- Support language-specific UI and notification text selected at mod initialization.
- Keep all localized strings outside the C# source files in `mod_directory/Localization/`.
- Provide safe fallback behavior when localized resources are missing.

## Proposed structure

### 1. Localization assets
Create one file per language in `Localization/`, for example:
- `Localization/en-US.json`
- `Localization/de-DE.json`
- `Localization/zh-CN.json`

Each file should map stable string keys to translated text:

```json
{
  "mod.name": "Auto Terrain Designations",
  "panel.designate": "Designate",
  "ramp.warning.failed": "{entity} could not start an access ramp"
}
```

### 2. Runtime service (`ATD.Localization.cs`)
Add a dedicated localization service class that:
- Accepts a `language` parameter and mod root path during initialization.
- Loads `Localization/<language>.json`.
- Falls back to `Localization/en-US.json` when:
  - selected language file is missing,
  - selected language file is malformed,
  - a key is missing in the selected language.
- Exposes an API such as:
  - `string Tr(string key)`
  - `string Tr(string key, params (string name, string value)[] tokens)`

Token replacement should support placeholders already used by the code, such as `{entity}`.

### 3. String key catalog (`ATD.Localization.Keys.cs`)
Add constants for key names to avoid typos:
- `ModName`
- `RampAccessFailed`
- `RampAccessTruncated`
- `RampAccessNotAccessible`
- panel labels/tooltips keys

This keeps call sites discoverable and refactor-safe.

## Migration strategy for existing code

### Phase 1: high-impact user-facing strings
Migrate the most visible strings first:
- Inspector/panel labels and button text.
- Notification message templates in `ATD.Notifications.cs`.
- User-facing status/report strings in farming panels/sessions.

### Phase 2: remaining user-facing text
Migrate:
- Tooltips and descriptive copy (`Tt(...)` call paths).
- Console command help text (if shown to players).

### Phase 3: leave debug/internal logs as English by default
Do **not** localize noisy developer-only logs unless needed; this reduces translation burden.

## Integration points in current codebase
- `ATD.Mod.cs`: replace hardcoded mod name and any user-visible literals with `Tr(...)` lookups.
- `ATD.Notifications.cs`: replace warning templates with localization keys and resolve text through localization service before proto registration.
- UI panel files (`ATD.DesignationPanel.cs`, `ATD.OreCompositionPanel.cs`, `ATD.FarmingAnalysisPanel.cs`): replace static labels/tooltips with keys.
- `ATD.FarmingPreparationSession.cs`: replace user-visible status text with localized entries.

## Error handling and observability
- On missing language file: log one warning and continue with fallback language.
- On missing key: return fallback language key value; if absent there too, return `"!<key>!"` and log once per key.
- Keep a cached dictionary per language for performance.

## Testing approach

### Unit-level
- Load valid language file and verify `Tr(key)`.
- Load unknown language and verify fallback to `en-US`.
- Verify token interpolation for placeholders (`{entity}`, etc.).
- Verify missing-key behavior (`!key!` and one-time warning).

### Integration-level
- Initialize mod with different language inputs and verify panel labels and notification text change.
- Validate no crashes when `Localization/` is missing entirely.

## Rollout suggestion
1. Add localization service + English baseline file.
2. Migrate notification strings and one panel end-to-end.
3. Add at least one additional language file to validate workflow.
4. Complete migration across remaining user-facing strings.

## Notes
- Prefer JSON for readability and easy contribution by translators.
- If the game engine has an official localization API, wrap it behind the same `Tr(...)` interface so this implementation can switch backend later with minimal churn.
