# CHANGE LOG

Kayser's AutoTerrainDesignations

## 0.2.1
* Added the mod marker/version tooltip to both inspector panels.
* Made the Ore Composition panel explicitly collapsible again while keeping it open by default.
* Fixed the Ore Composition panel so it can populate for custom `IAreaManagingTower` implementations; excavator priority controls remain limited to vanilla mine towers.
* Fixed clearing terrain designations so it only removes mining designations and preserves other designation types such as forestry. (*Placing* mining designations will still overwrite other designations.)

## 0.2.0
* Changed project license to MIT and added a repository `LICENSE` file.
* Added short MIT/SPDX license annotations to source files.

## 0.1.15
* Fixed issue with incorrect trimming of poor ore near the bottom of the designation (Replaced parameter `depthTrimFractionByLevel` with `minBottomOreDensityByLevel`)
* Changed the label `Override product` to `Target product` to make it more clear what it does
* Minimum corridor clearance setting on tower level
    - Added **Corridor clearance** setting on tower level — controls the minimum corridor width used when connecting separated ore components and enforcing passability. `0` = disabled (components stay separate, no corridors or hole-filling; useful for vehicle-less excavation setups); `1` = 1-tile corridors (small and medium vehicles); `2` = 2-tile corridors (mega vehicles). Default: 2. Configurable via `settings.json` (`minCorridorClearance`) and the new console command.
    - Global `minCorridorClearance` in `settings.json` and `atd_set_min_corridor_clearance` console command remain as the default applied to newly opened towers.
    - Improved algorithm for clearance=2, should improve passability for mega vehicles
* Added in-game console commands for live tuning of all ATD global defaults (no save reload required):
    - `atd_get_settings` — prints all current ATD settings and purity arrays
    - `atd_set_max_height_diff <1-3>` — sets the global default max slope height diff
    - `atd_set_ramp_width <0-5>` — sets the global default ramp width (0 = off)
    - `atd_set_max_layers_to_excavate <n>` — sets the global default max layers (0 = no limit)
    - `atd_set_max_depth_to_dig_to <n | ->` — sets the global default depth floor (`-` = no limit)
    - `atd_set_ore_purity_level <0-4>` — sets the global default ore purity level
    - `atd_set_min_corridor_clearance <0-2>` — sets the global default min corridor clearance
    - `atd_set_min_ore_height <level> <value>` — overrides `minOreHeightByLevel[level]` at runtime
    - `atd_set_min_bottom_ore_density <level> <value>` — overrides `minBottomOreDensityByLevel[level]` at runtime
    - `atd_set_min_ore_purity <level> <value>` — overrides `minOrePurityRatioByLevel[level]` at runtime
    - `atd_set_min_component_size <level> <value>` — overrides `minComponentSizeByLevel[level]` at runtime
* API updated:
    - API: Added `IsInitialized` property — lets external mods safely check whether ATD has finished loading before calling any other API method
    - API: Added per-tower settings getters and setters — `GetMaxHeightDiff`/`SetMaxHeightDiff`, `GetRampWidth`/`SetRampWidth`, `GetMaxLayersToExcavate`/`SetMaxLayersToExcavate`, `GetMaxDepthToDigTo`/`SetMaxDepthToDigTo`, `GetOrePurityLevel`/`SetOrePurityLevel`, `GetCorridorClearance`/`SetCorridorClearance`
    - API: Added panel builder methods — `BuildDesignationPanel`, `RefreshDesignationPanel`, and `BuildOreCompositionPanel` — so external mods can embed ATD panels in their own entity inspectors
    - API: Removed `generateRamps` parameter from `CreateDesignationsForTower` — ramp generation is now always driven by the per-tower **Ramp Width** setting (0 = disabled)

## 0.1.14
* Improved designation logic to reduce risk of vehichles getting stuck
    - Connectivity now always uses 2-tile-wide designation corridors to guarantee mega-vehicle passability.
    - Enclosed interior holes inside designation regions are now automatically filled during hull processing.
    - Single-tile pinch points inherited from the original ore scan are now widened; the extra tile is placed on the side with the most existing neighbours to keep the shape coherent.

## 0.1.13
* Fixed an issue with `settings.json` not parsing in some locales

## 0.1.12
* Tower settings are now persistent throughout a session
* Excavator priority is now a sticky state per tower (not an action as before)
* Excavator priority can now be reset to None (by unsetting the current priority)
* Ore Purity OFF is now more aggressive on sweeping up all the ore
* Externalized global default parameters to settings file
* Fixed an issue with invalid path to the settings.json file

## 0.1.11
* The Ore Composition panel now automatically refreshes after creating designations, showing the updated ore breakdown without requiring a manual rescan
* Terrain Designations panel is now collapsible
* Added **Ore Purity Level** setting — 5-level preset (Off/Low/Med/High/Max) that simultaneously controls three quality-filtering criteria:
    - **Depth trimming**: progressively excludes sparse, low-value ore at the bottom of each column (0% to 90% trim at Max purity)
    - **Tile height threshold**: excludes tiles with total ore thickness below the level's minimum (0 to 3.0 tiles); tiles meeting this threshold are included; settings as Off=no filter → Low=0.5 → Med=1.0 → High=2.0 → Max=3.0
    - **Overburden contamination ratio**: excludes tiles where ore/total-column ratio falls below the threshold (0% to 75% minimum purity at Max level); ratio accounts for all terrain layers above bedrock — Off=0% → Low=10% → Med=25% → High=50% → Max=75%
    - Added size-based isolation filtering — at higher purity levels, small scattered components (< 3 to 13 tiles depending on level) are pruned before connection; Off level removes nothing, Max level requires at least 13 tiles per component
    - **Ore purity settings externalized to `settings.json`** — all ore purity threshold parameters are now configurable without recompiling:
        - `minOreHeightByLevel` (float array) — minimum ore thickness per tile for each purity level
        - `depthTrimFractionByLevel` (float array) — fraction of sparse trailing ore to exclude at bottom of column per level
        - `minOrePurityRatioByLevel` (float array) — minimum ore/total-column ratio threshold per level
        - `minComponentSizeByLevel` (int array) — minimum component size before pruning per level
    - Users can tweak purity level behavior directly by editing `settings.json` in the mod folder. Defaults are embedded.
* Ramp now tries to avoid all buildings

## 0.1.10
* Improved ramp generation:
    - Ramp Width can now be selected (replaced ramp on/off button, width 0 => ramp off)
    - Ramp now strives toward the z-level of the tower (rather than always up)
    - Ramps now attach better to sloped and jagged designation edges
    - Ramps now actively avoid hitting the tower
* Added buttons on ore composition cards to set the mining priority for all the tower's excavators.
* UX overhauled:
    - Create Designations button is now more prominent
    - Ore selector renamed 'Override product' and moved to bottom of list (same function)
    - Ore composition cards visually tuned for better blend with with vanilla theme

## 0.1.9
* Fixed: opening other entity inspectors (storage, greenhouse, ore sorting plant, etc.) was broken — labels missing, entities not clickable
* Fixed: Ore Composition panel showed stale data from a previous tower after switching inspectors
* Ore Composition panel is now always visible (no longer collapsible) — removes blank-panel expand issue
* Replaced auto-refresh with a manual scan button (↺) in the panel header — avoids crashes from refresh timing issues after area edits
* Ore Composition panel now clears automatically when switching to a different tower inspector

## 0.1.8
* Ore Composition panel now refreshes correctly when switching between tower inspectors
* Ore Composition panel now only counts material above the designation's target level (excludes material that will remain after levelling)
* Dumping designations are excluded from the Ore Composition scan
* Ore Composition panel redesigned — products now displayed as cards with a proportional color-coded progress bar and percentage share

## 0.1.7
* Added **Ore Composition panel** — collapsible panel in the mine tower inspector showing each ore present in the current designations with product icon, name, expected yield, and percentage share
* Ore Composition panel quantities reflect the game's current **Ore Mining Yield** difficulty setting (accounts for per-material yield multipliers)
* Ore Composition panel refreshes automatically when designations change
* Try fix issue with mod package which was not extractable on Linux

## 0.1.6
* Added **Max layers to excavate** setting — limits how many layers deep the scan will designate (default: 30, ∞ = no limit); supports Shift×5 and Ctrl×10 stepping
* Added **Max depth** setting — sets an absolute terrain height floor below which nothing is designated (-∞ = no limit); supports Shift×5 and Ctrl×10 stepping
* Clearing designations is now instantaneous regardless of area size
* Bedrock is now always excluded from terrain scans — prevents bedrock layers from inflating ore/rock depth calculations
* Added mod integration API (`AutoTerrainDesignationsApi`) — external mods can now call `CreateDesignationsForTower`, `ClearDesignationsForTower`, `SetOreFilter`, and `GetOreFilter` on any `IAreaManagingTower` implementation

## 0.1.5
* Adjusted max slope to 1 to prevent dead spots

## 0.1.4
* Moved UI controls to its own panel for better compatibility and robustness 
* Updated placing algorithm to reduce risk of excavators getting stuck

## 0.1.3
* Fixed UI collission issue by changing from  from text-based buttons to icon-based buttons
* Allow scanning for any minable product through drop-down selector (None=Auto)
* Improved scanning, particularly along edges of deposit
* Added Thumbnail

## 0.1.0
* Initial release
