# ATD Public API — Mining Designations

This document covers the stable public API for ATD's core mining-designation systems. Farming automation is documented separately.

All members are `static` on `AutoTerrainDesignations.AutoTerrainDesignationsApi`.

## Guard

```csharp
bool IsInitialized
```

Returns `true` once ATD has finished initializing. Check this before calling other API methods from early-init code.

## Designation operations

```csharp
void CreateDesignationsForTower(IAreaManagingTower tower)
```
Scans the tower's area and creates mining designations using the tower's current settings and ore filter.

```csharp
void ClearDesignationsForTower(IAreaManagingTower tower)
```
Clears mining designations inside the tower's area.

## Ore filter

```csharp
void SetOreFilter(IAreaManagingTower tower, LooseProductProto? ore)
```
Sets the tower's scan filter. Pass `null` for Auto mode.

```csharp
LooseProductProto? GetOreFilter(IAreaManagingTower tower)
```
Returns the active ore filter, or `null` for Auto mode.

## Per-tower settings

All setters clamp to the same range used by the in-game inspector. Changing a setting does not automatically re-run designation generation.

| Method | Range | Meaning |
|---|---|---|
| `GetMaxHeightDiff` / `SetMaxHeightDiff` | 1–3 | Maximum height difference between adjacent designation cells |
| `GetRampWidth` / `SetRampWidth` | 0–5 | Generated ramp width; `0` disables ramps |
| `GetMaxLayersToExcavate` / `SetMaxLayersToExcavate` | `0+` | Maximum layers from the current surface; `0` means no limit |
| `GetMaxDepthToDigTo` / `SetMaxDepthToDigTo` | `int?` | Absolute minimum elevation; `null` means no limit |
| `GetOrePurityLevel` / `SetOrePurityLevel` | 0–4 | Purity preset from `Off` through `Max` |
| `GetCorridorClearance` / `SetCorridorClearance` | 0–2 | Corridor widening and connectivity rules |

## Panel builders

These methods exist so external mods can embed ATD's UI inside their own inspector layout.

```csharp
PanelWithHeader BuildDesignationPanel(Func<IAreaManagingTower?> getTower, object key)
```
Builds the **Mining designations** panel.

```csharp
void RefreshDesignationPanel(object key)
```
Refreshes the designation panel when an inspector switches to a different tower.

```csharp
PanelWithHeader BuildOreCompositionPanel(Func<IAreaManagingTower?> getTower, object key)
```
Builds the **Ore composition** panel.

Pass the same `key` to all panel-builder and refresh calls. The same key is also used internally so the ore composition panel can refresh after ATD creates designations.

## What is not public API

The following core systems are intentionally internal and should not be treated as stable integration points:

- the scan pipeline and ore-sampling internals
- ramp candidate selection and placement heuristics
- ore composition calculation internals
- excavator priority synchronization internals
- corner designation mode and its Harmony patches
- the contents and parsing details of `ATDsettings.json`

If you need integration hooks in one of those areas, add or request a dedicated API instead of binding to internal classes.
