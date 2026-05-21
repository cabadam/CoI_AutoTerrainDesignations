# 🌱 Kayser's Automatic Terrain Designations v0.4.1

This is a follow-up release for v0.4.0's farmland preparation automation, focused on making it smoother, faster, and a lot less fussy around awkward terrain.

The short version: farmland preparation is more robust, access ramps are smarter, large jobs should be noticeably lighter, and there are a few new quality-of-life controls for keybindings, notifications, and vehicle assignment.

## 🚜 Farmland Preparation Improvements

Farmland preparation now handles more of the strange edge cases that show up when you ask a mine tower to turn rough terrain into nice respectable farm plots.

The biggest changes are around access and edges:

- access ramps can now be created for multiple inaccessible farming clusters in the same tick
- reachability can propagate through adjacent farming designations that share matching-height edges, reducing unnecessary extra ramps
- final filling and rim alignment now use dumping designations instead of leveling designations, ignoring debris
- completed final farming designations are removed immediately after the completion notification, so they do not interfere with nearby follow-up farming patches
- exposed corners now get diagonal preparation shoulders when both adjacent cardinal shoulders exist, avoiding issues with dirt falling off in corners
- diagonal corner rim alignment designations now close fill-area corners more cleanly

This should make elevated and coastal farming projects behave better, especially around shoulders, rims, and follow-up patches.

## 🧩 Mod Interoperability

v0.4.1 has been extensively tested with **Moriarty's GamePlay++** to make sure farmland preparation interoperates cleanly during real farming workflows.

## ⚡ Faster Large Farming Sessions

Large farmland jobs should be noticeably less expensive to run.

Ramp generation now probes perimeter tiles instead of scanning deep into every possible interior tile, which can reduce candidate work by roughly 7x for large areas. Ramp reachability checks are also cached during placement attempts and capped so pathological cases do not spiral into long stalls.

Farming automation itself also does less repeated work:

- access/pathability rechecks are throttled for large active sessions
- pending fill-area tile sets are cached and rebuilt only when the farming state changes
- large auto-reactivated sessions should produce less background overhead after loading a save

## 🛣️ Better Access Ramp Behavior

Ramp placement now checks whether vehicles can actually reach the ramp before committing to a candidate. Accessible candidates are preferred; if no accessible candidate exists, ATD still places the best available option and reports that clearly.

Ramp generation also evaluates all four cardinal directions now. The old half-space direction restriction has been removed, while scoring still prefers ramps aligned toward the tower. This gives ATD useful fallback routes when the obvious direction is blocked by terrain, buildings, or water.

Ramp warnings are now shown only as vanilla-style notifications on the mine tower:

- failed ramp generation
- truncated ramp placement
- ramp placed but not currently accessible

If you prefer quieter automation, these warnings can be disabled globally with `rampNotificationsEnabled` in `ATDsettings.json` or:

```text
atd_set_ramp_notifications on|off
```

## 🚚 Auto-Release Idle Vehicles

The **Terrain Designations** inspector now has an **Auto-release when idle** toggle.

When enabled for a tower, ATD will temporarily unassign excavators and trucks while no mining or leveling work is pending, then automatically reassign them when work resumes. This is useful for farmland automation that switches between mining and dumping, or for towers that only occasionally need attention and otherwise tie up vehicles.

The global default is:

```text
autoReleaseVehiclesWhenIdle
```

It can also be changed with:

```text
atd_set_auto_release_vehicles_when_idle
```

For debugging and vehicle bookkeeping, v0.4.1 also adds:

```text
atd_get_assigned_vehicles
```

This lists mine towers, assigned vehicles, per-vehicle job state, ATD's auto-release setting, and which vehicles ATD has released.

## ⌨️ Corner Mode Settings

Corner designation mode is now more configurable:

- corner rotation now uses the player's mapped rotate key instead of always using **R**
- the corner mode key can be changed with the new `cornerDesignationKey` setting
- the default is still **K**

You can edit `ATDsettings.json` directly or use:

```text
atd_set_corner_designation_key
```

The command accepts Unity KeyCode names, such as `Alpha1` or `F1`.

## 🌐 Translations and UI Polish

This release includes revised Swedish, German, and Russian translations, with terminology aligned more closely to the base game.

Portuguese translation has also been added.

Some panel wording has been cleaned up:

- **Mining designations**
- **Ore quality**
- **Ore composition**
- **Farmland preparation**

The **Farmland Preparation** panel can now start collapsed by default using:

```text
farmingPanelCollapsed
```

or:

```text
atd_set_farming_panel_collapsed on|off
```

## 📦 Compatibility

As before, ATD is compatible with vanilla saves and can be added to or removed from existing saves safely.

Settings should migrate automatically. Existing changed settings are preserved, and new settings are added with their defaults.

Thanks again to everyone testing farmland automation in the wild. v0.4.1 is very much a "make the big new thing behave better under pressure" release, and your edge cases are what make that possible.
