# 🌱 Kayser's Automatic Terrain Designations v0.4.0

This one is a pretty big quality-of-life release for anyone who likes making mines, farms, and terrain projects without hand-painting every little bit of dirt.

## 🚜 New: Farmland Preparation Automation

No more dirt-y work!

ATD can now help prepare farmable ground from flat level designations inside a mine tower area.

Draw your flat level designations, select the mine tower, open the new **Farmland Preparation** panel, and enable **Farmland Preparation Automation**. ATD will:

- temporarily dig unsuitable top material one layer lower where needed
- restore the original level designations for final filling
- restrict the tower's dump rules during filling so trucks use farmable material like dirt/compost
- restore the tower's dump rules when the work is done
- add temporary access ramps when excavators or trucks cannot reach the work area
- clear vehicles out of the fill area before committing final fill orders
- add support shoulders near exposed drops/ocean edges to reduce dirt spill-off

It also keeps running in the background after you close the inspector. On load, it can automatically re-enable for towers that were already doing farmland work.

## 🔔 Vanilla-Style Notifications

v0.4.0 adds ATD notifications that behave much more like normal CoI notifications:

- ✅ **Farming complete**: green one-time notification when preparation and filling are done
- 👷 **Excavator complete**: clickable green one-time notification when a vehicle depot finishes building an excavator, so you can send it to work immediately
- ⚠️ **Ramp warnings**: translated warning text for failed or incomplete access ramps

Excavator completion notifications can be toggled with:

```text
atd_set_excavator_completion_notifications 
```

## 🌐 Translations

ATD now has localization support for the main panels, tooltips, status text, and related notifications.

Included languages:

- English
- Swedish
- German
- Russian

This covers **Terrain Designations**, **Farmland Preparation**, **Ore Composition**, and the new notification messages.

ATD will also load any additional language file added under `translations/`, so community translations can be dropped in without needing a new mod build.

## 📋 Quick Farming Start

1. Draw flat level designations inside a mine tower area.
2. Select the mine tower.
3. Open **Farmland Preparation**.
4. Enable **Farmland Preparation Automation**.
5. Let the tower crews do the ugly dirt work.

Only flat level designations participate. Corner/sloped designations are ignored by the farming automation.

Important: in this version, when loading a save, any mine tower that contains only flat leveling designations may be treated as farmland work and start Farmland Preparation Automation automatically. In current ATD versions, farming automation restores from persisted per-tower state instead of this inference.

## 📦 Compatibility

As before, ATD is compatible with vanilla saves and can be added to or removed from existing saves safely.

Any changed settings in ATDsettings.json should migrate.

Thanks to everyone testing, reporting edge cases, and pushing this mod into weirder terrain projects than I would ever have found on my own. Have fun turning questionable rock fields into respectable farmland.
