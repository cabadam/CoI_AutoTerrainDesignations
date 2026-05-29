v0.4.2 | 2026-05-29
* Added: per-tower settings (max height diff, ramp width, max layers, max depth, ore quality, corridor clearance, auto-release, ore filter, mining priority, and farmland preparation automation state) are now persisted into the vanilla save file via a `config.json` state blob; settings survive save/load cycles without any custom save format
* Changed: ore filter selection is now keyed by entity ID instead of object reference, so it round-trips correctly through save/load
* Changed: `CoI.AutoHelpers` Runtime, VanillaAttachments, and Persistence source modules are now compiled into ATD (previously only Localization and Logging were included)
* Added: collapsed state of each tower's **Mining designations**, **Ore composition**, and **Farmland preparation** panels is now persisted per tower in the save blob and restored on load
* Fixed: all three ATD panels opened in the global default collapsed state after load instead of the saved per-tower state; the entity is null during inspector construction so the per-tower state is now applied on inspector activation when the entity is bound
* Added three debug console commands: **atd_dump_pending_save_json** (prints the JSON that would be written on save), **atd_dump_last_loaded_json** (prints the JSON from the last load), **atd_dump_panel_state** (prints per-tower panel collapsed state from memory)

v0.4.1 | 2026-05-22
* Fixed: corner designation rotation now responds to the player's mapped rotate key instead of always responding to R
* Added **cornerDesignationKey** global setting: the key used to enter and toggle corner designation mode; configurable via **ATDsettings.json** (default: `K`) or the new **atdSetCornerDesignationKey** console command (accepts any Unity KeyCode name, e.g. `Alpha1`, `F1`)
* Fixed: a farming preparation origin backed by a dumping designation was dropped from session tracking on the tick after it reached `ReadyForFilling`; the drop left any shoulder designations in `PreparationShoulderOrigins` permanently blocking `CanStartTowerLevelFilling`, so the session never entered the filling phase; this occurred consistently when shoulder designations were present
* Fixed: a farming preparation origin whose terrain was already at the final target height (`Done` analysis state) while still in the `Preparing` phase was not advanced to `ReadyForFilling` and remained stuck permanently, blocking the session from starting the filling phase
* Fixed: farming preparation corner shoulders are now only placed at true outside corners of the tracked farming cluster, after both adjacent cardinal shoulder ramps exist; this preserves proper corner closure without creating red-edge corner designations inside multi-tile preparation pads
* Fixed: farming filling access ramps now use the dumping fulfillment probe when planning a ramp mouth, so dump ramps continue until they connect to/cross existing terrain instead of stopping early as a floating lip above the ground
* Changed: visible UI wording now uses **Mining designations** for the mining panel, **Ore quality** for the ore filtering preset, and sentence-case panel headings such as **Ore composition** and **Farmland preparation**; technical settings keys and console command names remain unchanged for compatibility
* Changed: ramp warning notifications now use the same designation icon as the **Create Designations** button while retaining warning notification styling
* Changed: farming final filling and final rim alignment now use dumping designations instead of leveling designations, and filling completion checks use dump fulfillment so completed fill orders can transition to `Done`
* Changed: farming completion now removes completed final origin designations immediately after posting the completion notification, preventing old finished designations from disturbing adjacent follow-up farming patches
* Temporarily disabled the preparation-start future rim debris cleanup mining pass while leaving cleanup removal paths intact for already-owned cleanup designations
* Preparation pass now evaluates the access ramp before the future rim debris cleanup step; the ramp gets priority on tile positioning because a ramp designation already excavates any debris on those tiles, so the rim cleanup naturally defers by skipping tiles already occupied by the ramp
* Fixed: rim alignment placement height criteria was one-sided; far-corner height was only checked against a lower bound (`targetHeight − 0.1`); each far corner is now required to be within ±0.1 tiles of `targetHeight`, preventing rim designations from being placed where terrain is significantly above the target and would require unnecessary excavation at the boundary
* Fixed: filling access ramps were re-placed on every tick because `RemoveOwnedFarmingAccessRamps` cleared `LastAccessRampRequestKey` whenever the preparation ramp set was empty (always true during filling), preventing the deduplication guard from suppressing repeat placement
* Fixed: access ramps accumulated across ticks — when the inaccessible-designation set changed (some origins advanced phase), the request key changed and a new ramp was placed without removing the existing one; existing ramps are now reserved in the no-overlap set, and each cluster is skipped when a previous-tick owned ramp that is still present and snapped toward the cluster edge (`IsSnappedTowards`) is found
* Changed: inaccessible-designation detection now propagates reachability through adjacent farming designations that share a matching-height ("non-red") edge; once any designation in a cluster is confirmed reachable via pathfinding BFS, its snapped neighbours are also marked reachable without needing a direct line of sight from the tower, reducing the number of clusters that require a dedicated access ramp

v0.4.0n | 2026-05-18
* Fixed: trucks were unconditionally released from the tower at the start of every filling tick, so when a rim alignment designation contained debris (terrain above target height) the dispatched excavators had no trucks to haul the material away; trucks are now kept assigned (or restored) for any tick where at least one rim designation has pending excavation work (`IsMiningNotFulfilled`), and released again once all rim tiles are clear
* Changed: rim alignment placement criteria no longer examines a probe tile one step further out from the rim; instead the rim tile itself must pass: for cardinal rims the 2 far corners (the edge furthest from the farming area) must be above `targetHeight − 0.1`; for diagonal corner rims the 3 far corners must be above that threshold; height tolerance changed from 0.2 to 0.1

v0.4.0m | 2026-05-17 [unreleased]
* Fixed: rim alignment designations were removed and re-placed on every filling tick via `AddOrReplaceDesignation`, cancelling haul jobs for any trucks already en route to those tiles; the designation is now re-tracked without replacement when it already exists from a previous tick
* Fixed: `TickIdleVehicleRelease` released all tower vehicles when no mining or leveling work was pending, even during an active farming fill session; vehicles are now kept assigned while any filling origin is in the `ReadyForFilling` or `Filling` phase
* Fixed: trucks released during the filling phase (to prevent them competing with dump vehicles) were permanently unassigned after filling completed; `RestoreTowerTrucksReleasedForFilling` was called without `reassign: true` on all normal exit paths (filling complete, activation abort, and user-stop), so trucks were cleared from tracking without being re-assigned to the tower
* Fixed: `AddProductToDump` was called for every farmable product on each fill activation and for every snapshot product on restore, even when the product was already in the tower's dumpable set; the redundant calls produced a `Trying to make dumpable an already dumpable product` warning each time; both paths now skip products already present in the tower's dumpable set
* Fixed: ramp generation was not skipped when a previously-placed ramp (not yet physically excavated) already provides surface access from the tower; re-scanning the area could place a second redundant ramp in a new direction; ramp generation is now skipped if any existing non-ore designation in the area has a pathable surface tile reachable from the tower
* Fixed: trucks released by ATD (for idle-release or filling) were permanently removed from the tower's vehicle list when another mod's Harmony patch silently blocked `MineTower.AssignVehicle` (e.g. gameplay-plus-plus Parking HQ committing a truck); because `AssignVehicle` is `void`, ATD had no signal the assignment was blocked and cleared its tracking, leaving the truck orphaned; both restore paths now verify the vehicle is in `AllVehicles` after the call and retain blocked vehicles in tracking for retry on the next tick

v0.4.0l | 2026-05-17
* Added **atd_get_assigned_vehicles** console command: lists every mine tower with its assigned vehicles, per-vehicle job state, ATD auto-release setting, and which vehicles ATD has released
* Fixed: filling-area vehicle evacuation issued `CancelAllJobsAndResetState` on all vehicles in the area, including those actively mining or driving; only idle vehicles (no active job) are now ordered to evacuate; vehicles with an active job leave the area naturally when their job finishes
* Fixed: `TickFarmingPreparationSessions` and `TickIdleVehicleRelease` were called from the Unity main thread while the game's simulation thread could concurrently iterate the same terrain designation set, causing an occasional `InvalidOperationException: Set changed while enumerating`; both operations now run on the simulation thread via `simLoopEvents.Update`

v0.4.0k | 2026-05-17
* Fixed: mine tower trucks were unassigned from the tower before vehicles in the fill area were ordered to evacuate; trucks are now released only after the evacuation order has been issued, so vehicles still assigned to the tower can receive a valid park-and-wait job
* Fixed: one truck was always kept assigned to the tower during filling due to a leftover guard; all empty assigned trucks are now released when filling begins
* Fixed: tiles already farmable at analysis time were set directly to the `Done` phase instead of `ReadyForFilling`; because filling only starts when at least one `ReadyForFilling` origin exists, those tiles were stuck hidden permanently with no way to trigger filling; analysis-time farmable tiles now enter `ReadyForFilling` so filling starts normally and the filling pass confirms them as `Done`

v0.4.0j | 2026-05-17 [packaged]
* Added diagonal corner rim alignment designations: when both adjacent cardinal rim designations are placed and both outward probe tiles pass the height criteria, a corner rim is placed at the diagonal position to close the corner of the fill area
* Fixed: completed farming origins (Done phase, designation restored after a previous fill cycle) were not re-hidden when a new adjacent preparation designation was placed; dirt sliding from the z tile into the z-1 preparation area caused the Done origin's terrain to drop, triggering trucks to continuously refill it while excavators dug it out again; Done origins adjacent to any active preparation designation are now re-hidden and will be restored together with the new tiles during the filling phase
* Fixed: ramp failure/truncated notifications were not cleared when **Ramp Width** was set to 0; a stale notification from a previous scan could remain on the tower after re-scanning with ramp generation disabled
* Fixed: farming preparation origins that were hidden pending filling (ReadyForFilling / Done phase) were permanently dropped from the session when an access ramp was routed through their tile; hidden origins have no active designation so the ramp placement was allowed, but the preparation pass then saw a non-leveling designation at the tile and removed the origin from tracking, causing it to never be restored at the end of the process
* Farming access ramp generation is substantially faster for large sessions:
    - Ramp candidate collection now only probes perimeter ore tiles (tiles with at least one non-ore cardinal neighbour); interior tiles that cannot produce a valid ramp exit are skipped, reducing candidate generation work by roughly 7× for large farming areas
    - Pathability update is now called once per ramp placement attempt instead of once per candidate
    - Ramp-mouth reachability BFS results are cached within a placement attempt to avoid re-running identical checks
    - Reachability checks are capped at 50 per placement attempt; candidates beyond the cap fall back to the best already-checked position
    - BFS search margin reduced from 96 to 48 tiles and tile visit cap reduced from 250 000 to 20 000

v0.4.0i | 2026-05-16
* Added **atd_set_auto_release_vehicles_when_idle** console command to set the global default for the **Auto-release when idle** toggle; also added **AutoReleaseWhenIdle** to the **atd_get_settings** output
* Fixed: vehicles released by **Auto-release when idle** were not re-assigned to the tower before saving, so after a save/load the tower had no record of them and could not reclaim them when excavation work resumed

v0.4.0h | 2026-05-16
* Removed ramp warning icon from the **Terrain Designations** panel; ramp outcome warnings are now shown through the vanilla notification system

v0.4.0g | 2026-05-16 [packaged]
* Farming automation performance pass for large auto-reactivated farming sessions:
    - Added throttling for farming access/pathability rechecks so thousand-origin sessions do not repeatedly run the expensive access scan every few ticks when designation state has not changed
    - Cached pending farming fill-area tile sets and rebuild them only when queued filling origins, shoulders, rims, or origin state changes
    - Added targeted `[ATD Farming Perf]` log markers for slow preparation, filling, access, and pending fill-area operations
    - Added preparation breakdown logging with capture/advance/access/summary/state-scan timings to diagnose remaining large-session hitches from tester saves
    - Added `tools/extract-atd-farming-perf.ps1` to extract relevant ATD farming performance rows from the newest Captain of Industry log
* Findings from tester save: initial multi-second farming preparation freezes were reduced substantially; remaining slow rows are mostly access/ramp handling on very large unreachable farming areas, not origin analysis

v0.4.0f | 2026-05-16 [packaged]
* Added **Auto-release when idle** toggle in the Terrain Designations panel: when enabled for a tower, all excavators and trucks are unassigned while none of the tower's managed mining or leveling designations have pending excavation work; released vehicles are tracked and automatically re-assigned when excavation work resumes
    - Global default is controlled by **autoReleaseVehiclesWhenIdle** in ATDsettings.json (default: false); the per-tower toggle in the inspector overrides this default
* Added diagonal corner preparation shoulders: when both adjacent cardinal shoulders are needed, a corner shoulder is placed at the diagonal position with 3 outer corners at z-2 and the inner corner (facing the farming origin) at z-1
* Fixed: filling transition could begin before shoulder designations were fully filled; the transition is now deferred until all active shoulder designations are fulfilled
* Fixed: preparation shoulders were not placed on sides where the terrain drop only crosses the diagonal corner of the designation; the check now samples the center 2×2 tiles of each adjacent 4×4 area instead of the boundary strip
* Fixed: filling access ramps were placed before rim alignment designations had a chance to be built; the BFS pathability check uses actual terrain, so a pending-but-not-yet-raised rim was invisible to it, causing ramps to be routed in wrong directions (e.g. into the sea)
    - Filling ramp placement is now deferred until the current tick places no new rim alignment designations, giving the rim terrain a chance to reach target height first
    - Stale filling ramps are now removed as soon as the fill area becomes pathable, rather than waiting until filling fully completes
* Vehicle clear-out before filling transition now also evicts vehicles from shoulder designation tiles, not only from the fill area itself

v0.4.0e | 2026-05-15
* Added **rampNotificationsEnabled** global setting: suppresses ramp access warning notifications (Failed, Truncated, NotAccessible) on all towers; toggle in **ATDsettings.json** or with **atd_set_ramp_notifications on|off**
* Added **farmingPanelCollapsed** global setting: the **Farmland Preparation** inspector panel now starts collapsed by default; toggle in **ATDsettings.json** or with **atd_set_farming_panel_collapsed on|off**

v0.4.0d | 2026-05-15 [packaged]
* When multiple farming designation clusters are inaccessible at the same time, access ramps are now placed for all clusters in the same tick instead of one per tick; applies to both preparation and filling phases
* Fixed: access ramps could overlap previously placed ramps from the same or another session, or land on any other existing designation
* Fixed: shoulder designations could be placed on hidden (completed) farming origins belonging to another tower's session, potentially corrupting its designation tracking
* Fixed: rim alignment designations could be placed on farming origins belonging to another tower's session
* Ramp generation: removed the half-space direction filter so all four cardinal directions are evaluated as ramp candidates; scoring already ranks by alignment toward the tower, so preferred directions are still tried first while directions away from the tower serve as fallbacks when the preferred corridor is blocked by a building, debris, or void
* Ramp placement now checks vehicle accessibility before committing a candidate; accessible candidates are preferred; if no accessible candidate exists the best available is placed and reported as not accessible
* Farming access ramp failures now show a warning notification on the tower, matching mining ramp notification behavior; the notification is cleared when all farming designations become accessible
* Fixed: German translation of ore mining priority tooltip
* Farming session status messages (diagnostic output from commands such as **atd_farming_dump_all_towers** and the status line returned by farming automation commands) are no longer localized and always display in English; panel labels and tooltips in the **Farmland Preparation** inspector are unaffected

v0.4.0c | 2026-05-15
* Improved ramp generation resilience: when the primary (dominant-axis) direction is blocked, the ramp now falls back to any other direction pointing toward the tower, tried in order of alignment angle

v0.4.0b | 2026-05-14
* Fixed: preparation-phase access ramp was not created for the last remaining preparation tile when all neighbouring tiles had already finished, because done origins were still blocking ramp entry positions

v0.4.0a | 2026-05-14
* Fixed: after farmland filling completes, flat level designations are now placed at rim tiles adjacent to the filled area where the surrounding terrain matches the fill target height, preventing V-shaped pits left by preparation-phase access ramps
    - Rim designations are placed before the filling access ramp is evaluated, so the ramp avoids tiles already covered by a rim designation
    - Rim designations are removed as soon as all fill origins reach Done, before the stabilization period, to prevent them from being picked up as new farming work


v0.4.1 | 2026-05-14
* Revised Swedish, German, and Russian translations — terminology aligned with base game wording and reviewed for accuracy across all panels, tooltips, and status messages


v0.4.0 | 2026-05-14
* Added farmland preparation automation for mine towers
    - Enabled via the **Farmland Preparation Automation** checkbox in the **Farmland Preparation** inspector panel
    - Automatically re-enables on load for towers that were running preparation work; toggle with **reEnableFarmingOnLoad** in **ATDsettings.json** or **atd_set_re_enable_farming_on_load** / **atd_re_enable_farming_on_load**
    - Clears out vehicles from the fill area before committing final fill designations to reduce the chance of vehicles getting trapped
    - Adds temporary sloped support shoulders on exposed sides near steep drops or ocean edges to reduce dirt spill-off during final filling
    - Added a green one-time completion notification when preparation and filling are done
    - Detailed per-tower diagnostics available via **atd_farming_dump_all_towers**
* Added localization support for the **Terrain Designations**, **Farmland Preparation**, and **Ore Composition** panels and related tooltips and status text
    - Includes English, Swedish, German, and Russian translations under **translations/**
* Added a green one-time notification when any vehicle depot completes an excavator; toggle with **excavatorCompletionNotifications** in **ATDsettings.json** or **atd_set_excavator_completion_notifications**
* Changed the excavator completion notification to use the mining toolbar icon
* Improved **ATDsettings.json** migration so defaults from older releases are upgraded to current defaults while user-changed values are preserved


v0.3.1 | 2026-05-09
* Ramp generation warnings now shown as a yellow icon next to the **Create Designations** button (tooltip contains the message):
    - "Ramp generation failed — no valid path found."
    - "Ramp placed but did not reach the surface — excavators may not be able to excavate."
    - "Couldn't find a valid path from the tower to the generated ramp. Check for access problems."
* Increased generated ramp safety margin around nearby buildings by 1 tile. Landslide can still reach buildings if built on deep dirt/sand.
* Added a bottom-flattening pass to reduce rough/bumpy excavation floors; designations floor should now be noticeably flatter. Can be toggled on/off with **atd_set_bottom_flattening on/off**.
* Tuned default **Low** and **Med** ore purity thresholds to reduce the jump from **Off**; existing **ATDsettings.json** values are preserved. Added **atd_reset_to_defaults** to try built-in defaults in memory.
* Renamed the **Target product** label to **Scanning filter** to avoid confusion with mining priority.


v0.3.0 | 2026-05-08
* Corner designations can now be placed manually from within any terrain designation tool (**M**, **Z**, **N**) — use the new **▲**/**▽** toolbar buttons or press **K** (**K** again flips between outer/inner); supports height offset (**Q**/**E**), rotation (**R**), and auto-snap to neighbouring designations; vanilla designations also snap to corner designations
    - Corner mode exits automatically when chosing one of the vanilla modes (flat/ramp) or the designation tool is deactivated
* Better quality thumbnail


v0.2.4 | 2026-05-04
* Settings file renamed from **settings.json** to **ATDsettings.json** to avoid conflicts with other mods
* Existing **settings.json** files are still read as a legacy fallback and migrated into the generated **ATDsettings.json** format
* **ATDsettings.json** is no longer distributed inside the mod ZIP — it is generated automatically in the mod folder on first run, populated with the current defaults and inline documentation
* Settings file now contains a **settingsVersion** stamp; when the mod version advances the file is automatically migrated (user values are preserved and any new keys are added)
* Added **atd_save_settings** console command to write the current in-memory global defaults back to **ATDsettings.json** at any time
* Added global defaults for whether the Terrain Designations and Ore Composition panels start expanded or collapsed, configurable via **ATDsettings.json** or console commands.
* Fixed Corridor clearance modifier-click handling so Shift/Ctrl jumps directly between Off and 2 instead of requiring multiple clicks.

v0.2.3 | 2026-05-03
* Added experimental Remove Debris scan support, available as a dedicated action and as an auto fallback when no useful product is found.

v0.2.2 | 2026-05-03
* Added concise license and attribution notices to source files and README.

v0.2.1 | 2026-05-03
* Added the mod marker/version tooltip to both inspector panels.
* Made the Ore Composition panel explicitly collapsible again while keeping it open by default.
* Added horizontal scrolling to Ore Composition cards so towers with many ore products no longer overflow the inspector column.
* Fixed the Ore Composition panel so it can populate for custom IAreaManagingTower implementations; excavator priority controls remain limited to vanilla mine towers.
* Fixed clearing terrain designations so it only removes mining designations and preserves other designation types such as forestry. Placing mining designations will still overwrite other designations.

v0.2.0 | 2026-05-01
* Changed project license to MIT and added a repository LICENSE file.
* Added short MIT/SPDX license annotations to source files.

v0.1.15 | 2026-05-01
* Fixed incorrect trimming of poor ore near the bottom of the designation.
* Changed the label Override product to Target product to make it clearer.
* Added a per-tower Corridor clearance setting for separated ore components and passability.
* Added in-game console commands for live tuning of all ATD global defaults.
* Updated the API with initialization checks, per-tower settings accessors, panel builder methods, and ramp-width driven designation creation.

v0.1.14 | 2026-05-01
* Improved designation logic to reduce the risk of vehicles getting stuck.
* Connectivity now uses 2-tile-wide designation corridors for mega-vehicle passability.
* Enclosed interior holes inside designation regions are now automatically filled.
* Single-tile pinch points inherited from the ore scan are now widened.

v0.1.13 | 2026-05-01
* Fixed an issue with settings.json not parsing in some locales.

v0.1.12 | 2026-05-01
* Tower settings are now persistent throughout a session.
* Excavator priority is now a sticky state per tower and can be reset to None.
* Ore Purity OFF is now more aggressive when sweeping up ore.
* Externalized global default parameters to settings.json.
* Fixed an invalid settings.json path issue.

v0.1.11 | 2026-05-01
* The Ore Composition panel now refreshes automatically after creating designations.
* Terrain Designations panel is now collapsible.
* Added the Ore Purity Level preset setting and externalized its thresholds to settings.json.
* Ramp generation now tries to avoid all buildings.

v0.1.10 | 2026-05-01
* Improved ramp generation with selectable ramp width, better z-level targeting, better attachment to designation edges, and tower avoidance.
* Added buttons on ore composition cards to set mining priority for all tower excavators.
* Overhauled the UI with a more prominent Create Designations button and visually tuned ore composition cards.

v0.1.9 | 2026-05-01
* Fixed broken non-tower inspectors.
* Fixed stale Ore Composition data after switching inspectors.
* Made the Ore Composition panel always visible and replaced auto-refresh with a manual scan button.
* Ore Composition panel now clears when switching to a different tower inspector.

v0.1.8 | 2026-05-01
* Ore Composition panel now refreshes correctly when switching tower inspectors.
* Ore Composition now counts only material above the designation target level.
* Dumping designations are excluded from the Ore Composition scan.
* Redesigned Ore Composition cards with proportional color-coded progress bars and percentage shares.

v0.1.7 | 2026-05-01
* Added the Ore Composition panel for the mine tower inspector.
* Ore Composition quantities reflect the current Ore Mining Yield difficulty setting.
* Ore Composition panel refreshes automatically when designations change.
* Fixed a mod package extraction issue on Linux.

v0.1.6 | 2026-05-01
* Added Max layers to excavate and Max depth settings.
* Clearing designations is now instantaneous regardless of area size.
* Bedrock is now always excluded from terrain scans.
* Added the AutoTerrainDesignationsApi integration API.

v0.1.5 | 2026-05-01
* Adjusted max slope to 1 to prevent dead spots.

v0.1.4 | 2026-05-01
* Moved UI controls to their own panel for better compatibility and robustness.
* Updated the placement algorithm to reduce the risk of excavators getting stuck.

v0.1.3 | 2026-05-01
* Fixed UI collision by changing from text-based buttons to icon-based buttons.
* Added scanning for any mineable product through the product selector.
* Improved scanning, particularly along deposit edges.
* Added thumbnail.

v0.1.0 | 2026-05-01
* Initial release.
