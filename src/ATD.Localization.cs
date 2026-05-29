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
            Loc.Str("panel.designations.clear_tooltip", "Clear all mining designations in this tower's area.", "Tooltip on the Clear button.");
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
        public static LocStr FarmingIdleReleaseLabel =
            Loc.Str("panel.farming.idle_release.label", "Auto-release when idle", "Label on the auto-release vehicles when idle toggle.");
        public static LocStr FarmingIdleReleaseTip =
            Loc.Str("panel.farming.idle_release.tooltip",
                "Automatically unassign all excavators and trucks from this tower when no designation has pending excavation work.\n" +
                "Vehicles are tracked and re-assigned when excavation work returns.",
                "Tooltip on the auto-release vehicles when idle toggle.");

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
