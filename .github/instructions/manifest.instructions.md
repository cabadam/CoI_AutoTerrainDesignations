---
description: Instructions for maintaining the AutoTerrainDesignations manifest.json file.
applyTo: "manifest.json"
---

# ATD manifest instructions

Read the shared workspace instructions first:

- `../../../.github/instructions/coi-maintained-mods.instructions.md`

# manifest.json maintenance rules

This file contains only ATD-specific manifest reminders. Use the shared
workspace instructions and the MaFi modding documentation for general manifest
field rules.

- ATD's `id` must stay `AutoTerrainDesignations`.
- ATD's `primary_dlls` must include Harmony before the ATD DLL:
  `["0Harmony.dll", "AutoTerrainDesignations.dll"]`.
- ATD declares `can_add_to_saved_game` and `can_remove_from_saved_game` as
  `true`; keep those declarations aligned with the shared workspace
  save-removability rules.
