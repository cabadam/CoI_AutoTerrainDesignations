# ATD Regression Test Plan — CoI v0.8.5

Covers the six areas most likely to surface regressions from v0.8.5 changes (new `ForestryDesignationController` subclass, new `MultiAreaEditController` + Ctrl+M shortcut, `MineTowerInspector` constructor changes, vehicle connectivity manager).

---

## 1 — Designation tool activation / deactivation

| # | Steps | Expected |
|---|-------|----------|
| 1.1 | Open mine tower inspector → click the Mine designation button (M-mode). | ATD toolbar K-buttons appear. Debug log shows `Designation tool activated: MiningDesignationController`. |
| 1.2 | Repeat 1.1 for Dump (Z) and Level (N) buttons. | K-buttons appear for each; log shows the matching controller name. |
| 1.3 | Open a **forestry** tower inspector → click its designation editing button (`StartDesignationEditing`). | ATD K-buttons do **not** appear in the forestry toolbar. No errors in log. |
| 1.4 | Activate mine tool → switch to the forestry designation tool → switch back to mine tool. | State transitions cleanly each time. No stuck `s_designationToolActive = true`. |
| 1.5 | Activate mine tool → close the inspector without using a designation button. | `s_designationToolActive` returns to false. Debug log shows deactivation. |

---

## 2 — Corner mode (K-mode)

| # | Steps | Expected |
|---|-------|----------|
| 2.1 | Mine tool active → press K (outer) → drag LMB across a flat area. | Corner designations placed with one corner at base+1. |
| 2.2 | Press K again (toggle inner) → place designations. | Corner designations placed with one corner at base-1. |
| 2.3 | In K-mode, RMB-drag over existing designations. | Designations removed normally (AOE undesignation passes through). |
| 2.4 | In K-mode, press K a third time (or press F). | Corner mode exits. Ramp/Flat game button is visually restored to the previously active state. |
| 2.5 | Enter K-mode → switch designation type (e.g. mine → dump) → verify K-mode exits and re-enters cleanly for the new type. | No ghost button state or null-ref errors. |

---

## 3 — New Ctrl+M shortcut interaction

| # | Steps | Expected |
|---|-------|----------|
| 3.1 | Mine tool active, K-mode **off** → press Ctrl+M. | Area polygon editor opens. Mine tool deactivates (or remains active if input manager allows co-activation — note the actual behaviour for future reference). No ATD errors. |
| 3.2 | Mine tool active, K-mode **on** → press Ctrl+M. | Corner mode exits cleanly (K-buttons deselected, ATD state reset). |
| 3.3 | No designation tool active → press Ctrl+M. | Area polygon editor opens. No ATD state change; no errors. |
| 3.4 | Use the **Edit Areas** button inside the mine tower inspector (calls `MultiAreaEditController.ActivateForTower`). | ATD panels in the inspector remain intact. No null-ref or layout errors. |

---

## 4 — MineTowerInspector UI injection

The inspector constructor now takes `MultiAreaEditController` and `ForestryTowersManager` as new parameters. ATD's `InspectorCtorPostfix` uses dynamic `ctors[0]` reflection and must survive this change.

| # | Steps | Expected |
|---|-------|----------|
| 4.1 | Click any mine tower. | Inspector opens. ATD designation panel, Ore Composition panel, and Farming Analysis panel all render. No errors in log. |
| 4.2 | Click the **Edit Areas** button in the mine tower inspector. | Area polygon editor opens. On close, inspector reopens with all ATD panels intact. |
| 4.3 | Open inspector → `OnActivated` fires (switch tower or reopen). | Ore Composition panel and Farming Analysis panel reset to their prompt state. No errors. |

---

## 5 — Auto-depth scan and ramp generation

| # | Steps | Expected |
|---|-------|----------|
| 5.1 | Place a mine tower on a hill → trigger ATD scan. | Designations cover the tower area; heights respect `MaxHeightDiff`. |
| 5.2 | Set `RampWidth > 0` → scan. | A ramp is generated at a reachable corridor position. Check ATD debug log for `IVehiclePathFindingManager.PathabilityProvider` usage — no null-ref or skip warning. |
| 5.3 | Set `MaxLayersToExcavate` to a small value (e.g. 3) → scan a deep area. | Designations stop at the layer limit. |
| 5.4 | Set `OrePurityLevel > 0` → scan an area with mixed ore quality. | Low-quality tiles are excluded as expected. |
| 5.5 | After scan, add more designations manually then re-scan. | No duplicate or orphaned designations. Cleanup logic works. |

---

## 6 — Surface clearing behavioral regression

v0.8.5 changed surface clearing to respect truck zone construction filters. This is a game-side behavior change but affects scenarios ATD users commonly rely on.

| # | Steps | Expected |
|---|-------|----------|
| 6.1 | Place ATD-generated mining designations → excavators clear terrain normally. | No regression in excavator assignment or clearing speed. |
| 6.2 | Apply a truck zone construction filter → trigger surface clearing in that zone. | Clearing respects the filter (game behavior). ATD-placed designations outside the filter are unaffected. |
| 6.3 | Confirm ATD's protected corner tiles (`s_protectedCornerTiles`) are not cleared unexpectedly by the new filtering logic. | One-shot protection still consumed correctly; no permanent stuck-protection entries. |
