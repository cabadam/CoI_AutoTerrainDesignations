# ATD Public API — Farming

The farming automation feature does not yet have a public API surface. The `AutoTerrainDesignationsApi` class covers designation creation, clearing, per-tower settings, and panel building. See below for what is available and what is not.

---

## `AutoTerrainDesignationsApi` — full surface

All members are `static` on `AutoTerrainDesignations.AutoTerrainDesignationsApi`.

### Guard

```csharp
bool IsInitialized
```

Returns `true` once ATD has finished initializing. Call this before any other API method from early-init code.

---

### Designation operations

```csharp
void CreateDesignationsForTower(IAreaManagingTower tower)
```
Scans the tower's area for ore and creates mining designations. Respects per-tower ore selection, ramp settings, and all other tower settings.

```csharp
void ClearDesignationsForTower(IAreaManagingTower tower)
```
Removes all ATD-placed mining designations inside the tower's area.

---

### Ore filter

```csharp
void SetOreFilter(IAreaManagingTower tower, LooseProductProto? ore)
```
Sets the ore filter. Pass `null` for Auto mode (all non-rock ores).

```csharp
LooseProductProto? GetOreFilter(IAreaManagingTower tower)
```
Returns the active ore filter, or `null` for Auto mode.

---

### Per-tower settings

All setters clamp to the same valid range used by the inspector. Changing a setting does not automatically re-run designations; call `CreateDesignationsForTower` to apply new values.

| Method | Range | Description |
|---|---|---|
| `GetMaxHeightDiff` / `SetMaxHeightDiff` | 1–3 | Max height difference across a designation |
| `GetRampWidth` / `SetRampWidth` | 0–5 | Access ramp width; 0 disables ramps |
| `GetMaxLayersToExcavate` / `SetMaxLayersToExcavate` | ≥0 | Layers from surface; 0 = no limit |
| `GetMaxDepthToDigTo` / `SetMaxDepthToDigTo` | `int?` | Absolute minimum elevation; `null` = no limit |
| `GetOrePurityLevel` / `SetOrePurityLevel` | 0–4 | Ore quality threshold (0 = Off, 4 = Max) |
| `GetCorridorClearance` / `SetCorridorClearance` | 0–2 | Vehicle corridor clearance |

---

### Panel builders

Use these to embed ATD panels inside a custom inspector's `Column` layout.

```csharp
PanelWithHeader BuildDesignationPanel(Func<IAreaManagingTower?> getTower, object key)
```
Builds the Mining designations panel. Pass an opaque `key` (typically your inspector instance) that links this panel to `RefreshDesignationPanel`.

```csharp
void RefreshDesignationPanel(object key)
```
Refreshes display values when the inspector activates or switches tower.

```csharp
PanelWithHeader BuildOreCompositionPanel(Func<IAreaManagingTower?> getTower, object key)
```
Builds the Ore composition panel. Pass the same `key` as `BuildDesignationPanel` so the composition display auto-refreshes after a scan.

---

## Farming automation — no public API yet

Farming preparation sessions are fully internal. There is currently no programmatic way for an external mod to:

- start or stop a farming session on a tower
- query session state or origin phases
- subscribe to session events

This is intentional: the session lifecycle is tightly coupled to the save/load
hook and to per-tower dump rule ownership, and exposing it prematurely would
create brittle contracts before the design is stable.

If you need to integrate with farming sessions, file a request in the backlog.
