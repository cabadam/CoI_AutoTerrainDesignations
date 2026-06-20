// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
using Mafi.Localization;

namespace AutoTerrainDesignations
{
    /// <summary>
    /// Static localization fields for ATD. All LocStr fields are rebound by
    /// <see cref="CoI.AutoHelpers.Localization.ModTranslations.Apply"/> at renderer init state.
    /// </summary>
    internal static class AtdLocalization
    {
        /// <summary>
        /// Returns a <see cref="LocStrFormatted"/> with the ATD mod marker appended,
        /// for use as tooltip text.
        /// </summary>
        public static LocStrFormatted Tip(LocStr s) =>
            new LocStrFormatted(AutoTerrainDesignationsMod.Tt(s.TranslatedString));

        // ------------------------------------------------------------------ //
        // Common levels
        // ------------------------------------------------------------------ //
        public static LocStr LevelOff  = Loc.Str("common.level.off",  "Off",  "Common level setting label: off/disabled.");
        public static LocStr LevelLow  = Loc.Str("common.level.low",  "Low",  "Common level setting label: low.");
        public static LocStr LevelMed  = Loc.Str("common.level.med",  "Med",  "Common level setting label: medium.");
        public static LocStr LevelHigh = Loc.Str("common.level.high", "High", "Common level setting label: high.");
        public static LocStr LevelMax  = Loc.Str("common.level.max",  "Max",  "Common level setting label: maximum.");

        // ------------------------------------------------------------------ //
        // Terrain designation panel
        // ------------------------------------------------------------------ //
        public static LocStr DesigTitle =
            Loc.Str("panel.designations.title", "Mining designations", "Title of the mining designations inspector panel.");
        public static LocStr DesigDescription =
            Loc.Str("panel.designations.description", "Create automatic terrain designations for this tower.", "Tooltip on the terrain designations panel title.");
        public static LocStr DesigCreateBtn =
            Loc.Str("panel.designations.create_button", "Create Designations", "Label on the Create Designations button.");
        public static LocStr DesigCreateTip =
            Loc.Str("panel.designations.create_tooltip", "Scan and place mining designations in this tower's area.", "Tooltip on the Create Designations button.");
        public static LocStr DesigDebrisTip =
            Loc.Str("panel.designations.debris_tooltip", "Designate all debris in the area for mining/removal. Overrides any forestry designations.", "Tooltip on the Debris button.");
        public static LocStr DesigClearTip =
            Loc.Str("panel.designations.clear_tooltip", "Clear all terrain designations in this tower's area.", "Tooltip on the Clear button.");
        public static LocStr DesigOreFilterAuto =
            Loc.Str("panel.designations.ore_filter.auto", "Auto (useful -> debris -> dirt)", "Label for the automatic ore filter option in the ore picker.");
        public static LocStr DesigRampWidthLabel =
            Loc.Str("panel.designations.ramp_width.label", "Ramp width", "Label for the ramp width setting row.");
        public static LocStr DesigRampWidthTip =
            Loc.Str("panel.designations.ramp_width.tooltip", "Width of generated access ramps (0 = disable ramp).", "Tooltip for the ramp width setting.");
        public static LocStr DesigMaxLayersLabel =
            Loc.Str("panel.designations.max_layers.label", "Max layers to excavate", "Label for the max layers setting row.");
        public static LocStr DesigMaxLayersTip =
            Loc.Str("panel.designations.max_layers.tooltip", "Maximum layers to excavate from the surface. (\u221e = no limit.)", "Tooltip for the max layers setting.");
        public static LocStr DesigElevLimitLabel =
            Loc.Str("panel.designations.elevation_limit.label", "Elevation limit", "Label for the elevation limit setting row.");
        public static LocStr DesigElevLimitTip =
            Loc.Str("panel.designations.elevation_limit.tooltip", "Maximum (absolute) excavation depth (-\u221e = no limit.)", "Tooltip for the elevation limit setting.");
        public static LocStr DesigOrePurityLabel =
            Loc.Str("panel.designations.ore_purity.label", "Ore quality", "Label for the ore quality setting row.");
        public static LocStr DesigOrePurityTip =
            Loc.Str("panel.designations.ore_purity.tooltip",
                "How strictly the scan filters for ore quality.\n" +
                "Off: include all tiles, dig to full depth.\n" +
                "Low: exclude very sparse tiles, trim thin trailing ore at the bottom.\n" +
                "Med: moderate quality \u2014 skip tiles with heavy overburden or little ore.\n" +
                "High: only rich tiles with a clean ore column.\n" +
                "Max: near-pure ore only \u2014 strict on overburden, depth and ore density.",
                "Tooltip for the ore purity setting.");
        public static LocStr DesigCorridorClearanceLabel =
            Loc.Str("panel.designations.corridor_clearance.label", "Corridor clearance", "Label for the corridor clearance setting row.");
        public static LocStr DesigCorridorClearanceTip =
            Loc.Str("panel.designations.corridor_clearance.tooltip",
                "Minimum corridor width for connecting ore regions and enforcing passability.\n" +
                "0 = disabled (regions left separate, no corridors or hole-filling).\n" +
                "1 = 1-tile corridors (small and medium vehicles).\n" +
                "2 = 2-tile corridors (mega vehicles).\n",
                "Tooltip for the corridor clearance setting.");
        public static LocStr DesigScanningFilterLabel =
            Loc.Str("panel.designations.scanning_filter.label", "Scanning filter:", "Label for the scanning filter ore picker row.");
        public static LocStr DesigScanningFilterTip =
            Loc.Str("panel.designations.scanning_filter.tooltip", "Force the scan to target a specific product. None = useful products first, then debris, then dirt.", "Tooltip for the scanning filter ore picker.");

        // ------------------------------------------------------------------ //
        // Mod settings window
        // ------------------------------------------------------------------ //
        public static LocStr SettingsModName =
            Loc.Str("settings.mod.name", "Auto Terrain Designations", "Mod name in the shared Mod Settings window.");
        public static LocStr SettingsTabDefaults =
            Loc.Str("settings.tab.defaults", "Defaults", "Settings tab title for ATD defaults.");
        public static LocStr SettingsTabGameSettings =
            Loc.Str("settings.tab.game_settings", "Game settings", "Settings tab title for ATD game settings.");
        public static LocStr SettingsTabOreQuality =
            Loc.Str("settings.tab.ore_quality", "Ore quality", "Settings tab title for ATD ore quality settings.");
        public static LocStr SettingsHeadingMiningDefaults =
            Loc.Str("settings.heading.mining_defaults", "Mine control tower defaults", "Settings section heading for mine control tower defaults.");
        public static LocStr SettingsHeadingPanelDefaults =
            Loc.Str("settings.heading.panel_defaults", "Panel defaults", "Settings section heading for panel defaults.");
        public static LocStr SettingsHeadingDesignations =
            Loc.Str("settings.heading.designations", "Designations", "Settings section heading for designation behavior.");
        public static LocStr SettingsHeadingScanPerformance =
            Loc.Str("settings.heading.scan_performance", "Scan performance", "Settings section heading for scan performance.");
        public static LocStr SettingsHeadingKeyboardShortcuts =
            Loc.Str("settings.heading.keyboard_shortcuts", "Keyboard shortcuts", "Settings section heading for keyboard shortcuts.");
        public static LocStr SettingsHeadingNotifications =
            Loc.Str("settings.heading.notifications", "Notifications", "Settings section heading for notification settings.");
        public static LocStr SettingsHeadingExperimentalAccess =
            Loc.Str("settings.heading.experimental_access", "Experimental accessways", "Settings section heading for experimental accessway settings.");
        public static LocStr SettingsTurningRampsLabel =
            Loc.Str("settings.experimental_access.turning_ramps.label", "Turning ramps (experimental)", "Settings toggle label for experimental turning ramps.");
        public static LocStr SettingsTurningRampsTooltip =
            Loc.Str("settings.experimental_access.turning_ramps.tooltip", "When enabled, ATD may select and place experimental V1 turning or switchback accessways using vanilla flat and slope designations. Requires the tower's ramp width to be set to 1. Corridor clearance is independent. Wider ramps and corner or saddle designations are not included.", "Tooltip for experimental turning ramps.");
        public static LocStr SettingsAccessAStarLabel =
            Loc.Str("settings.experimental_access.astar.label", "Use A* search", "Settings toggle label for experimental A* access search.");
        public static LocStr SettingsAccessAStarTooltip =
            Loc.Str("settings.experimental_access.astar.tooltip", "Use A* instead of reference Dijkstra for experimental accessway dry runs. Dijkstra is the safer validation baseline. A* is faster.", "Tooltip for experimental A* access search.");
        public static LocStr SettingsAccessWorkScaleLabel =
            Loc.Str("settings.experimental_access.work_scale.label", "Work vs. distance cost", "Settings row label for experimental access work cost scale.");
        public static LocStr SettingsAccessWorkScaleTooltip =
            Loc.Str("settings.experimental_access.work_scale.tooltip", "Tile-distance cost assigned to one unit of center-height terrain work. Range: 0-100; default: 1. A higher value will promote routes with less terraforming.", "Tooltip for experimental access work distance scale.");
        public static LocStr SettingsAccessLandslideRunLabel =
            Loc.Str("settings.experimental_access.landslide_run.label", "Landslide protection slope factor", "Settings row label for the experimental landslide envelope scale.");
        public static LocStr SettingsAccessLandslideRunTooltip =
            Loc.Str("settings.experimental_access.landslide_run.tooltip", "Horizontal exclusion distance per vertical terrain level. 1 translates to a 45-degree slope; higher values widen the exclusion zone (use in e.g. pure sand), while lower values narrow it. Range: 0.05-4; default: 1.", "Tooltip for experimental access landslide run setting.");
        public static LocStr SettingsMaxSlopeLabel =
            Loc.Str("settings.max_slope.label", "Max slope", "Settings row label for maximum designation slope.");
        public static LocStr SettingsMaxSlopeTooltip =
            Loc.Str("settings.max_slope.tooltip", "Maximum allowed height difference between adjacent designation corners. Range: 1-3.", "Tooltip for maximum designation slope setting.");
        public static LocStr SettingsBottomFlatteningLabel =
            Loc.Str("settings.bottom_flattening.label", "Bottom flattening", "Settings row label for bottom flattening.");
        public static LocStr SettingsBottomFlatteningTooltip =
            Loc.Str("settings.bottom_flattening.tooltip", "Bottom-flattening strength from 0 to 10. 0 disables the bottom-flattening pass.", "Tooltip for bottom flattening setting.");
        public static LocStr SettingsBatchSizeLabel =
            Loc.Str("settings.batch_size.label", "Batch size", "Settings row label for scan batch size.");
        public static LocStr SettingsBatchSizeTooltip =
            Loc.Str("settings.batch_size.tooltip", "Designations placed per coroutine frame while the game is unpaused. Range: 1-200.", "Tooltip for scan batch size setting.");
        public static LocStr SettingsMiningPanelCollapsedLabel =
            Loc.Str("settings.panel_defaults.mining_collapsed.label", "Mining panel collapsed", "Settings toggle label for default mining panel collapsed state.");
        public static LocStr SettingsMiningPanelCollapsedTooltip =
            Loc.Str("settings.panel_defaults.mining_collapsed.tooltip", "Whether the Mining designations panel starts collapsed by default.", "Tooltip for default mining panel collapsed state.");
        public static LocStr SettingsOrePanelCollapsedLabel =
            Loc.Str("settings.panel_defaults.ore_collapsed.label", "Ore panel collapsed", "Settings toggle label for default ore panel collapsed state.");
        public static LocStr SettingsOrePanelCollapsedTooltip =
            Loc.Str("settings.panel_defaults.ore_collapsed.tooltip", "Whether the Ore composition panel starts collapsed by default.", "Tooltip for default ore panel collapsed state.");
        public static LocStr SettingsFarmingPanelCollapsedLabel =
            Loc.Str("settings.panel_defaults.farming_collapsed.label", "Farming panel collapsed", "Settings toggle label for default farming panel collapsed state.");
        public static LocStr SettingsFarmingPanelCollapsedTooltip =
            Loc.Str("settings.panel_defaults.farming_collapsed.tooltip", "Whether the Farmland preparation panel starts collapsed by default.", "Tooltip for default farming panel collapsed state.");
        public static LocStr SettingsExcavatorNotificationsLabel =
            Loc.Str("settings.notifications.excavator_completion.label", "Excavator completion notifications", "Settings toggle label for excavator completion notifications.");
        public static LocStr SettingsExcavatorNotificationsTooltip =
            Loc.Str("settings.notifications.excavator_completion.tooltip", "Whether ATD shows a green notification when any vehicle depot completes an excavator.", "Tooltip for excavator completion notifications.");
        public static LocStr SettingsRampNotificationsLabel =
            Loc.Str("settings.notifications.ramp_warning.label", "Ramp warning notifications", "Settings toggle label for ramp warning notifications.");
        public static LocStr SettingsRampNotificationsTooltip =
            Loc.Str("settings.notifications.ramp_warning.tooltip", "Whether ATD shows ramp access warning notifications on mine towers.", "Tooltip for ramp warning notifications.");
        public static LocStr SettingsMinOreHeightLabel =
            Loc.Str("settings.ore_quality.min_ore_height.label", "Minimum ore height", "Settings row label for minimum ore height threshold.");
        public static LocStr SettingsMinOreHeightTooltip =
            Loc.Str("settings.ore_quality.min_ore_height.tooltip", "Minimum ore thickness in terrain tiles for this quality level.", "Tooltip for minimum ore height threshold.");
        public static LocStr SettingsMinBottomDensityLabel =
            Loc.Str("settings.ore_quality.min_bottom_density.label", "Minimum bottom density", "Settings row label for minimum bottom density threshold.");
        public static LocStr SettingsMinBottomDensityTooltip =
            Loc.Str("settings.ore_quality.min_bottom_density.tooltip", "Minimum ore density from the previous ore bottom to this ore bottom. Clamped from 0 to 1.", "Tooltip for minimum bottom density threshold.");
        public static LocStr SettingsMinOrePurityLabel =
            Loc.Str("settings.ore_quality.min_ore_purity.label", "Minimum ore purity", "Settings row label for minimum ore purity threshold.");
        public static LocStr SettingsMinOrePurityTooltip =
            Loc.Str("settings.ore_quality.min_ore_purity.tooltip", "Minimum ore-to-column ratio for this quality level. Clamped from 0 to 1.", "Tooltip for minimum ore purity threshold.");
        public static LocStr SettingsMinComponentSizeLabel =
            Loc.Str("settings.ore_quality.min_component_size.label", "Minimum component size", "Settings row label for minimum component size threshold.");
        public static LocStr SettingsMinComponentSizeTooltip =
            Loc.Str("settings.ore_quality.min_component_size.tooltip", "Minimum connected designation tile count for a cluster to survive the isolation filter.", "Tooltip for minimum component size threshold.");
        public static LocStr SettingsCornerModeLabel =
            Loc.Str("settings.corner_mode.label", "Corner designations mode", "Settings row label for corner designations mode shortcut.");
        public static LocStr SettingsCornerModeTooltip =
            Loc.Str("settings.corner_mode.tooltip", "Key used to enter and toggle corner designation mode while a terrain designation tool is active.", "Tooltip for corner designations mode shortcut.");
        public static LocStr SettingsCornerModeInvalidTooltip =
            Loc.Str("settings.corner_mode.invalid_tooltip", "Use a single key such as K, 1, F1, Space, or Escape.", "Validation error tooltip for corner designations mode shortcut.");
        public static LocStr SettingsApplied =
            Loc.Str("settings.status.applied", "Applied", "Status message after applying a setting.");
        public static LocStr SettingsInvalidKey =
            Loc.Str("settings.status.invalid_key", "Invalid key", "Status message for an invalid shortcut key.");
        public static LocStr SettingsSaveAsGlobal =
            Loc.Str("settings.action.save_as_global", "Save as config", "Button label for saving settings as config default.");
        public static LocStr SettingsSaveAsGlobalTooltip =
            Loc.Str("settings.action.save_as_global.tooltip", "Save these settings to ATDsettings.json. They will be used as the defaults for all new games.", "Tooltip for saving settings as config default.");
        public static LocStr SettingsRestoreDefaults =
            Loc.Str("settings.action.restore_defaults", "Restore defaults", "Button label for restoring default settings.");
        public static LocStr SettingsRestoreDefaultsTooltip =
            Loc.Str("settings.action.restore_defaults.tooltip", "Restore the global mod defaults for all settings. (Does not automatically save them as config.)", "Tooltip for restoring default settings.");
        public static LocStr SettingsSavedToFile =
            Loc.Str("settings.status.saved_to_file", "Saved to ATDsettings.json.", "Status message after settings are saved.");
        public static LocStr SettingsSaveFailed =
            Loc.Str("settings.status.save_failed", "Save failed; check the log.", "Status message after settings save fails.");
        public static LocStr SettingsRestoredDefaults =
            Loc.Str("settings.status.restored_defaults", "Restored built-in defaults in memory.", "Status message after settings are restored to defaults.");

        // ------------------------------------------------------------------ //
        // Ore composition panel
        // ------------------------------------------------------------------ //
        public static LocStr OreTitle =
            Loc.Str("panel.ore.title", "Ore composition", "Title of the ore composition inspector panel.");
        public static LocStr OreDescription =
            Loc.Str("panel.ore.description", "Ore resources within this tower's current mining designations. (Does not account for potential landslides.)", "Tooltip on the ore composition panel title.");
        public static LocStr OrePromptScan =
            Loc.Str("panel.ore.prompt_scan", "Press \u21ba to scan ore composition.", "Prompt shown before a scan is run.");
        public static LocStr OreScanTip =
            Loc.Str("panel.ore.scan_tooltip", "Scan ore composition", "Tooltip on the scan/refresh button in the ore composition panel.");
        public static LocStr OreNoTower =
            Loc.Str("panel.ore.no_tower", "No tower selected.", "Message shown when no tower is selected in the ore panel.");
        public static LocStr OreNoMinableDesig =
            Loc.Str("panel.ore.no_minable_designations", "No minable designations found.", "Message shown when the scan finds no minable designations.");
        public static LocStr OrePrioritySelectedTipFmt =
            Loc.Str("panel.ore.priority_selected_tooltip", "Tower mining priority set to {0}. Click to unset.", "Tooltip on a priority button when that product is already prioritized. {0} = colored product name.");
        public static LocStr OrePrioritySetTipFmt =
            Loc.Str("panel.ore.priority_set_tooltip", "Set tower mining priority to {0}.", "Tooltip on a priority button. {0} = colored product name.");

        // ------------------------------------------------------------------ //
        // Farming analysis panel
        // ------------------------------------------------------------------ //
        public static LocStr FarmingTitle =
            Loc.Str("panel.farming.title", "Farmland preparation", "Title of the farmland preparation inspector panel.");
        public static LocStr FarmingDescription =
            Loc.Str("panel.farming.description", "Automates the preparation and final filling of flat level designations so their top layer becomes farmable.", "Tooltip on the farmland preparation panel title.");
        public static LocStr FarmingToggleLabel =
            Loc.Str("panel.farming.automation_toggle.label", "Farmland preparation automation", "Label on the farming automation toggle.");
        public static LocStr FarmingToggleTip =
            Loc.Str("panel.farming.automation_toggle.tooltip", "Prepare flat level designations for farmland by clearing unsuitable top material, then restoring the final fill orders.", "Tooltip on the farming automation toggle.");
        public static LocStr FarmingIdleReleaseExcavatorsLabel =
            Loc.Str("panel.farming.idle_release_excavators.label", "Auto-release excavators when idle", "Label on the auto-release excavators when idle toggle.");
        public static LocStr FarmingIdleReleaseExcavatorsTip =
            Loc.Str("panel.farming.idle_release_excavators.tooltip",
                "Automatically unassign excavators from this tower when no designation has pending excavation work, or while the tower is paused.\n" +
                "Excavators are tracked and re-assigned when excavation work returns.",
                "Tooltip on the auto-release excavators when idle toggle.");
        public static LocStr FarmingIdleReleaseTrucksLabel =
            Loc.Str("panel.farming.idle_release_trucks.label", "Auto-release trucks when idle", "Label on the auto-release trucks when idle toggle.");
        public static LocStr FarmingIdleReleaseTrucksTip =
            Loc.Str("panel.farming.idle_release_trucks.tooltip",
                "Automatically unassign trucks from this tower when no designation has pending excavation work, or while the tower is paused.\n" +
                "Trucks are tracked and re-assigned when excavation work returns.",
                "Tooltip on the auto-release trucks when idle toggle.");
        // ------------------------------------------------------------------ //
        // Toolbox items
        // ------------------------------------------------------------------ //
        public static LocStr CornerOuterTip =
            Loc.Str("toolbox.corner_outer.tooltip", "Corner (outer): place convex corner ramps.", "Tooltip on the outer corner toolbox item.");
        public static LocStr CornerInnerTip =
            Loc.Str("toolbox.corner_inner.tooltip", "Corner (inner): place concave corner ramps.", "Tooltip on the inner corner toolbox item.");

        // ------------------------------------------------------------------ //
        // Notifications
        // ------------------------------------------------------------------ //
        public static LocStr NotifRampFailed =
            Loc.Str("notification.ramp_access_failed", "[ATD] {entity} could not start an access ramp", "Notification: ramp generation failed. {entity} is substituted by the game.");
        public static LocStr NotifRampTruncated =
            Loc.Str("notification.ramp_access_truncated", "[ATD] {entity} could not fit a full access ramp", "Notification: ramp was truncated. {entity} is substituted by the game.");
        public static LocStr NotifRampNotAccessible =
            Loc.Str("notification.ramp_access_not_accessible", "[ATD] {entity} could not path to the ramp", "Notification: ramp not accessible. {entity} is substituted by the game.");
        public static LocStr NotifFarmingComplete =
            Loc.Str("notification.farming_complete", "[ATD] {entity} farming preparation and filling complete", "Notification: farming complete. {entity} is substituted by the game.");
        public static LocStr NotifExcavatorCompleted =
            Loc.Str("notification.excavator_completed", "[ATD] {entity} completed an excavator", "Notification: excavator built. {entity} is substituted by the game.");

    }
}
