// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
// Auto Terrain Designations - In-Game Console Commands
using System.Globalization;

#pragma warning disable IDE0051 // Remove unused private members

using System.Text;
using Mafi;
using Mafi.Core.Console;
using UnityEngine;

namespace AutoTerrainDesignations;

/// <summary>
/// Registers ATD console commands. Automatically discovered via [GlobalDependency] scanning.
/// Command names are derived from method names using camelCase tokenization (e.g. atdSetRampWidth -> atd_set_ramp_width).
/// </summary>
[GlobalDependency(RegistrationMode.AsSelf, false, false)]
public sealed class AtdConsoleCommands
{
    [ConsoleCommand(false, false, "Prints all current ATD global settings.", null)]
    private string atdGetSettings()
    {
        var sb = new StringBuilder();
        sb.AppendLine("[ATD] Current settings:");
        sb.AppendLine($"  DesignationMode           = {(int)AutoTerrainDesignationsMod.DesignationMode} ({AutoTerrainDesignationsMod.DesignationMode})");
        sb.AppendLine($"  FlatteningDesignationType = {(int)AutoTerrainDesignationsMod.FlatteningDesignationType} ({AutoTerrainDesignationsMod.FlatteningDesignationType})");
        sb.AppendLine($"  DiagnosticLevel       = {AtdDiagnostics.Describe()}");
        sb.AppendLine($"  MaxHeightDiff         = {AutoTerrainDesignationsMod.MaxHeightDiff}");
        sb.AppendLine($"  RampWidth             = {AutoTerrainDesignationsMod.RampWidth}");
        sb.AppendLine($"  MaxLayersToExcavate   = {AutoTerrainDesignationsMod.MaxLayersToExcavate}");
        sb.AppendLine($"  MaxDepthToDigTo       = {AutoTerrainDesignationsMod.MaxDepthToDigTo?.ToString() ?? "-"}");
        sb.AppendLine($"  OrePurityLevel        = {AutoTerrainDesignationsMod.OrePurityLevel}");
        sb.AppendLine($"  BottomFlattening      = {AutoTerrainDesignationsMod.BottomFlatteningEnabled}");
        sb.AppendLine($"  BottomFlatteningStrength = {AutoTerrainDesignationsMod.BottomFlatteningStrength}");
        sb.AppendLine($"  MinCorridorClearance  = {AutoTerrainDesignationsMod.MinCorridorClearance}");
        sb.AppendLine($"  TerrainPanelCollapsed = {AutoTerrainDesignationsMod.TerrainDesignationsPanelCollapsed}");
        sb.AppendLine($"  OrePanelCollapsed     = {AutoTerrainDesignationsMod.OreCompositionPanelCollapsed}");
        sb.AppendLine($"  FarmingPanelCollapsed = {AutoTerrainDesignationsMod.FarmingPanelCollapsed}");
        sb.AppendLine($"  ExcavatorCompleteNtf  = {AutoTerrainDesignationsMod.ExcavatorCompletionNotificationsEnabled}");
        sb.AppendLine($"  RampNotifications     = {AutoTerrainDesignationsMod.RampNotificationsEnabled}");
        sb.AppendLine($"  AutoReleaseExcavators = {AutoTerrainDesignationsMod.AutoReleaseExcavatorsWhenIdle}");
        sb.AppendLine($"  AutoReleaseTrucks     = {AutoTerrainDesignationsMod.AutoReleaseTrucksWhenIdle}");
        sb.AppendLine($"  TurningRampsExperimental = {AutoTerrainDesignationsMod.TurningRampsExperimental}");
        sb.AppendLine($"  SuppressLegacyRamps   = {AutoTerrainDesignationsMod.SuppressLegacyAccessRamps}");
        sb.AppendLine($"  ExperimentalAStar     = {AutoTerrainDesignationsMod.ExperimentalAccessUseAStar}");
        sb.AppendLine($"  AccessSearchOverlay   = {AutoDepthDesignation.ShowExperimentalAccessSearchOverlay}");
        sb.AppendLine($"  AccessPotentialOverlay = {AutoDepthDesignation.ShowExperimentalAccessPotentialOverlay}");
        sb.AppendLine($"  ExperimentalHeightHull = {AutoTerrainDesignationsMod.ExperimentalAccessUsefulHeightEnvelope}");
        sb.AppendLine($"  HeightHullV1LowerAllowance = {AutoTerrainDesignationsMod.ExperimentalAccessV1HeightEnvelopeLowerAllowance}");
        sb.AppendLine($"  HeightHullV2LowerAllowance = {AutoTerrainDesignationsMod.ExperimentalAccessV2HeightEnvelopeLowerAllowance}");
        sb.AppendLine($"  HeightHullV1UpperAllowance = {AutoTerrainDesignationsMod.ExperimentalAccessV1HeightEnvelopeUpperAllowance}");
        sb.AppendLine($"  HeightHullV2UpperAllowance = {AutoTerrainDesignationsMod.ExperimentalAccessV2HeightEnvelopeUpperAllowance}");
        sb.AppendLine($"  SafetyPolicy          = {AutoTerrainDesignationsMod.GetSafetyPolicy().ToString().ToUpperInvariant()}");
        sb.AppendLine($"  LandslideSlopeFactor  = {AutoTerrainDesignationsMod.AccessRaySlopeConservatism}");
        sb.AppendLine($"  LandslideBuffer       = {AutoTerrainDesignationsMod.AccessRayEndBuffer}");
        sb.AppendLine($"  CornerDesignationMode = {AutoTerrainDesignationsMod.CornerDesignationMode.ToNiceStringLong()}");
        sb.Append(AutoDepthDesignation.FormatPurityArrays());
        return sb.ToString();
    }

    [ConsoleCommand(false, false, "Prints the JSON that would be written to the save file if saved now.", null)]
    private string atdDumpPendingSaveJson()
    {
        string json = AutoDepthDesignation.BuildTowerSettingsStateJsonForConfig();
        AutoDepthDesignation.s_log.Info($"Pending tower settings JSON:\n{json}");
        return $"[ATD] Pending tower settings JSON logged ({json.Length} chars).";
    }

    [ConsoleCommand(false, false, "Prints the JSON that was loaded from the save file on the last load.", null)]
    private string atdDumpLastLoadedJson()
    {
        string? json = AutoDepthDesignation.GetLastLoadedTowerSettingsJson();
        if (json == null)
        {
            return "[ATD] No tower settings JSON was loaded (no prior load or blob was empty).";
        }
        AutoDepthDesignation.s_log.Info($"Last loaded tower settings JSON:\n{json}");
        return $"[ATD] Last loaded tower settings JSON logged ({json.Length} chars).";
    }

    [ConsoleCommand(false, false, "Dumps the in-memory panel collapsed state for all towers (for debugging).", null)]
    private string atdDumpPanelState()
    {
        string report = AutoDepthDesignation.FormatPanelStateDebug();
        AutoDepthDesignation.s_log.Info(report);
        return report;
    }

    /// <summary>Sets the global default designation workflow.</summary>
    /// <param name="value">Numeric designation workflow value, clamped to the supported enum range.</param>
    /// <returns>Console response describing the selected designation workflow.</returns>
    [ConsoleCommand(false, false, "Sets the global default designation mode (0=resource mining, 1=flattening).", null)]
    private string atdSetDesignationMode(int value)
    {
        AutoTerrainDesignationsMod.SetDesignationMode(value);
        string mode = AutoTerrainDesignationsMod.DesignationMode == DesignationMode.Flattening ? "flattening" : "resource mining";
        return $"[ATD] DesignationMode set to {(int)AutoTerrainDesignationsMod.DesignationMode} ({mode}).";
    }

    /// <summary>Sets the global default designation type used by flattening mode.</summary>
    /// <param name="value">Numeric designation type value, clamped to the supported enum range.</param>
    /// <returns>Console response describing the selected flattening-mode designation type.</returns>
    [ConsoleCommand(false, false, "Sets the global default flattening-mode designation type (0=mining, 1=dumping, 2=leveling).", null)]
    private string atdSetFlatteningDesignationType(int value)
    {
        AutoTerrainDesignationsMod.SetFlatteningDesignationType(value);
        string type = AutoTerrainDesignationsMod.FlatteningDesignationType == FlatteningDesignationType.Mining
            ? "mining"
            : AutoTerrainDesignationsMod.FlatteningDesignationType == FlatteningDesignationType.Dumping
                ? "dumping"
                : "leveling";
        return $"[ATD] FlatteningDesignationType set to {(int)AutoTerrainDesignationsMod.FlatteningDesignationType} ({type}).";
    }

    [ConsoleCommand(false, false, "Sets the global default max height diff (1-3).", null)]
    private string atdSetMaxHeightDiff(int value)
    {
        AutoTerrainDesignationsMod.SetMaxHeightDiff(value);
        return $"[ATD] MaxHeightDiff set to {AutoTerrainDesignationsMod.MaxHeightDiff}.";
    }

    [ConsoleCommand(false, false, "Sets the global default ramp width (0-5). 0 disables ramp generation.", null)]
    private string atdSetRampWidth(int value)
    {
        AutoTerrainDesignationsMod.SetRampWidth(value);
        return $"[ATD] RampWidth set to {AutoTerrainDesignationsMod.RampWidth}.";
    }

    [ConsoleCommand(false, false, "Sets the global default max layers to excavate from the surface. 0 = no limit.", null)]
    private string atdSetMaxLayersToExcavate(int value)
    {
        AutoTerrainDesignationsMod.SetMaxLayersToExcavate(value);
        return $"[ATD] MaxLayersToExcavate set to {AutoTerrainDesignationsMod.MaxLayersToExcavate}.";
    }

    [ConsoleCommand(false, false, "Sets the global default ore purity level (0=Off, 1=Low, 2=Medium, 3=High, 4=Max).", null)]
    private string atdSetOrePurityLevel(int value)
    {
        AutoTerrainDesignationsMod.SetOrePurityLevel(value);
        return $"[ATD] OrePurityLevel set to {AutoTerrainDesignationsMod.OrePurityLevel}.";
    }

    [ConsoleCommand(false, false, "Enables/disables the extra bottom-flattening pass (true/false, on/off, 1/0).", null)]
    private string atdSetBottomFlattening(string value)
    {
        if (!TryParseConsoleBool(value, out bool parsed))
            return $"[ATD] Invalid value '{value}'. Use true/false, on/off, yes/no, or 1/0.";

        AutoTerrainDesignationsMod.SetBottomFlatteningEnabled(parsed);
        return $"[ATD] BottomFlattening set to {AutoTerrainDesignationsMod.BottomFlatteningEnabled}.";
    }

    [ConsoleCommand(false, false, "Sets the bottom-flattening strength (1-10). Higher = deeper target = more tiles affected.", null)]
    private string atdSetBottomFlatteningStrength(int value)
    {
        AutoTerrainDesignationsMod.SetBottomFlatteningStrength(value);
        return $"[ATD] BottomFlatteningStrength set to {AutoTerrainDesignationsMod.BottomFlatteningStrength}.";
    }

    [ConsoleCommand(false, false, "Sets the global default max depth to dig to (absolute elevation). Use '-' for no limit.", null)]
    private string atdSetMaxDepthToDigTo(string value)
    {
        if (value == "-")
        {
            AutoTerrainDesignationsMod.SetMaxDepthToDigTo(null);
            return "[ATD] MaxDepthToDigTo set to no limit.";
        }
        if (int.TryParse(value, out int parsed))
        {
            AutoTerrainDesignationsMod.SetMaxDepthToDigTo(parsed);
            return $"[ATD] MaxDepthToDigTo set to {AutoTerrainDesignationsMod.MaxDepthToDigTo}.";
        }
        return $"[ATD] Invalid value '{value}'. Use an integer elevation or '-' for no limit.";
    }

    [ConsoleCommand(false, false, "Sets minOreHeight for a purity level (0-4). E.g. atd_set_min_ore_height 2 1.0", null)]
    private string atdSetMinOreHeight(int level, float value)
    {
        if (!AutoDepthDesignation.TrySetMinOreHeightForLevel(level, value))
            return $"[ATD] Level {level} out of range (0-{AutoDepthDesignation.PurityLevelCount - 1}).";
        return $"[ATD] minOreHeight[{level}] set to {value}.";
    }

    [ConsoleCommand(false, false, "Sets the global default corridor clearance (0=none, 1=small+med vehicles, 2=mega vehicles). Per-tower override available in the mine tower inspector.", null)]
    private string atdSetMinCorridorClearance(int value)
    {
        AutoTerrainDesignationsMod.SetMinCorridorClearance(value);
        return $"[ATD] MinCorridorClearance set to {AutoTerrainDesignationsMod.MinCorridorClearance}.";
    }

    [ConsoleCommand(false, false, "Sets whether the Mining designations panel starts collapsed by default (true/false, on/off, 1/0).", null)]
    private string atdSetTerrainDesignationsPanelCollapsed(string value)
    {
        if (!TryParseConsoleBool(value, out bool parsed))
            return $"[ATD] Invalid value '{value}'. Use true/false, on/off, yes/no, or 1/0.";

        AutoTerrainDesignationsMod.SetTerrainDesignationsPanelCollapsed(parsed);
        return $"[ATD] TerrainDesignationsPanelCollapsed set to {AutoTerrainDesignationsMod.TerrainDesignationsPanelCollapsed}.";
    }

    [ConsoleCommand(false, false, "Sets whether the Ore composition panel starts collapsed by default (true/false, on/off, 1/0).", null)]
    private string atdSetOreCompositionPanelCollapsed(string value)
    {
        if (!TryParseConsoleBool(value, out bool parsed))
            return $"[ATD] Invalid value '{value}'. Use true/false, on/off, yes/no, or 1/0.";

        AutoTerrainDesignationsMod.SetOreCompositionPanelCollapsed(parsed);
        return $"[ATD] OreCompositionPanelCollapsed set to {AutoTerrainDesignationsMod.OreCompositionPanelCollapsed}.";
    }

    [ConsoleCommand(false, false, "Sets whether vehicle depot excavator completion notifications are shown (true/false, on/off, 1/0).", null)]
    private string atdSetExcavatorCompletionNotifications(string value)
    {
        if (!TryParseConsoleBool(value, out bool parsed))
            return $"[ATD] Invalid value '{value}'. Use true/false, on/off, yes/no, or 1/0.";

        AutoTerrainDesignationsMod.SetExcavatorCompletionNotificationsEnabled(parsed);
        return $"[ATD] ExcavatorCompletionNotifications set to {AutoTerrainDesignationsMod.ExcavatorCompletionNotificationsEnabled}.";
    }

    [ConsoleCommand(false, false, "Enables/disables ramp access warning notifications on mine towers (true/false, on/off, 1/0).", null)]
    private string atdSetRampNotifications(string value)
    {
        if (!TryParseConsoleBool(value, out bool parsed))
            return $"[ATD] Invalid value '{value}'. Use true/false, on/off, yes/no, or 1/0.";

        AutoTerrainDesignationsMod.SetRampNotificationsEnabled(parsed);
        return $"[ATD] RampNotifications set to {AutoTerrainDesignationsMod.RampNotificationsEnabled}.";
    }

    [ConsoleCommand(false, false, "Sets whether the Farming panel starts collapsed by default (true/false, on/off, 1/0).", null)]
    private string atdSetFarmingPanelCollapsed(string value)
    {
        if (!TryParseConsoleBool(value, out bool parsed))
            return $"[ATD] Invalid value '{value}'. Use true/false, on/off, yes/no, or 1/0.";

        AutoTerrainDesignationsMod.SetFarmingPanelCollapsed(parsed);
        return $"[ATD] FarmingPanelCollapsed set to {AutoTerrainDesignationsMod.FarmingPanelCollapsed}.";
    }

    [ConsoleCommand(false, false, "Sets the global default for both Auto-release when idle toggles on new towers (true/false, on/off, 1/0).", null)]
    private string atdSetAutoReleaseVehiclesWhenIdle(string value)
    {
        if (!TryParseConsoleBool(value, out bool parsed))
            return $"[ATD] Invalid value '{value}'. Use true/false, on/off, yes/no, or 1/0.";

        AutoTerrainDesignationsMod.SetAutoReleaseVehiclesWhenIdle(parsed);
        return $"[ATD] AutoReleaseExcavatorsWhenIdle and AutoReleaseTrucksWhenIdle set to {parsed}.";
    }

    [ConsoleCommand(false, false, "Sets the World safety policy (MIN, LOW, MED, HIGH, or MAX).", null)]
    private string atdSetSafetyPolicy(string value)
    {
        if (!System.Enum.TryParse(value, true, out SafetyPolicy parsed)
            || parsed < SafetyPolicy.Min || parsed > SafetyPolicy.Max)
            return $"[ATD] Invalid safety policy '{value}'. Use MIN, LOW, MED, HIGH, or MAX.";
        AutoTerrainDesignationsMod.SetSafetyPolicy(parsed);
        return $"[ATD] Safety policy set to {parsed.ToString().ToUpperInvariant()}.";
    }

    [ConsoleCommand(false, false, "Sets the landslide predictor slope factor (0-1.5). This is the expert value behind Safety policy.", null)]
    private string atdSetLandslidePredictorSlopeFactor(float value)
    {
        AutoTerrainDesignationsMod.SetAccessRaySlopeConservatism(value);
        return $"[ATD] Landslide predictor slope factor set to {AutoTerrainDesignationsMod.AccessRaySlopeConservatism}.";
    }

    [ConsoleCommand(false, false, "Sets the landslide safety buffer (0-16 tiles). This is the expert value behind Safety policy.", null)]
    private string atdSetLandslideBuffer(int value)
    {
        AutoTerrainDesignationsMod.SetAccessRayEndBuffer(value);
        return $"[ATD] Landslide buffer set to {AutoTerrainDesignationsMod.AccessRayEndBuffer}.";
    }

    [ConsoleCommand(false, false, "Sets the global default for Auto-release excavators when idle on new towers (true/false, on/off, 1/0).", null)]
    private string atdSetAutoReleaseExcavatorsWhenIdle(string value)
    {
        if (!TryParseConsoleBool(value, out bool parsed))
            return $"[ATD] Invalid value '{value}'. Use true/false, on/off, yes/no, or 1/0.";

        AutoTerrainDesignationsMod.SetAutoReleaseExcavatorsWhenIdle(parsed);
        return $"[ATD] AutoReleaseExcavatorsWhenIdle set to {AutoTerrainDesignationsMod.AutoReleaseExcavatorsWhenIdle}.";
    }

    [ConsoleCommand(false, false, "Sets the global default for Auto-release trucks when idle on new towers (true/false, on/off, 1/0).", null)]
    private string atdSetAutoReleaseTrucksWhenIdle(string value)
    {
        if (!TryParseConsoleBool(value, out bool parsed))
            return $"[ATD] Invalid value '{value}'. Use true/false, on/off, yes/no, or 1/0.";

        AutoTerrainDesignationsMod.SetAutoReleaseTrucksWhenIdle(parsed);
        return $"[ATD] AutoReleaseTrucksWhenIdle set to {AutoTerrainDesignationsMod.AutoReleaseTrucksWhenIdle}.";
    }

    [ConsoleCommand(false, false, "Sets the key used to enter corner designation mode (Unity KeyCode name, e.g. K, Alpha1, F1).", null)]
    private string atdSetCornerDesignationKey(string value)
    {
        if (!System.Enum.TryParse<KeyCode>(value, true, out KeyCode parsed))
            return $"[ATD] Unknown key '{value}'. Use a valid Unity KeyCode name (e.g. K, Alpha1, F1).";

        AutoTerrainDesignationsMod.SetCornerDesignationMode(AutoTerrainDesignationsMod.FromPrimaryKeys(parsed));
        return $"[ATD] CornerDesignationMode set to {AutoTerrainDesignationsMod.CornerDesignationMode.ToNiceStringLong()}.";
    }

    [ConsoleCommand(false, false, "Enables/disables the legacy straight-ramp generator fallback (true/false, on/off, 1/0).", null)]
    private string atdSetSuppressLegacyRamps(string value)
    {
        if (!TryParseConsoleBool(value, out bool parsed))
            return $"[ATD] Invalid value '{value}'. Use true/false, on/off, yes/no, or 1/0.";

        AutoTerrainDesignationsMod.SetSuppressLegacyAccessRamps(parsed);
        return $"[ATD] SuppressLegacyAccessRamps set to {AutoTerrainDesignationsMod.SuppressLegacyAccessRamps}.";
    }

    [ConsoleCommand(false, false, "Sets minBottomOreDensity for a purity level (0-4), clamped 0-1. Minimum ore/(ore+waste) ratio a zone must have to be included. E.g. atd_set_min_bottom_ore_density 2 0.25", null)]
    private string atdSetMinBottomOreDensity(int level, float value)
    {
        if (!AutoDepthDesignation.TrySetMinBottomOreDensityForLevel(level, value))
            return $"[ATD] Level {level} out of range (0-{AutoDepthDesignation.PurityLevelCount - 1}).";
        return $"[ATD] minBottomOreDensity[{level}] set to {value}.";
    }

    [ConsoleCommand(false, false, "Sets minOrePurity ratio for a purity level (0-4), clamped 0-1. E.g. atd_set_min_ore_purity 2 0.25", null)]
    private string atdSetMinOrePurity(int level, float value)
    {
        if (!AutoDepthDesignation.TrySetMinOrePurityForLevel(level, value))
            return $"[ATD] Level {level} out of range (0-{AutoDepthDesignation.PurityLevelCount - 1}).";
        return $"[ATD] minOrePurity[{level}] set to {value}.";
    }

    [ConsoleCommand(false, false, "Sets minComponentSize for a purity level (0-4). E.g. atd_set_min_component_size 2 8", null)]
    private string atdSetMinComponentSize(int level, int value)
    {
        if (!AutoDepthDesignation.TrySetMinComponentSizeForLevel(level, value))
            return $"[ATD] Level {level} out of range (0-{AutoDepthDesignation.PurityLevelCount - 1}).";
        return $"[ATD] minComponentSize[{level}] set to {value}.";
    }

    [ConsoleCommand(false, false, "Saves current ATD global settings to ATDsettings.json in the mod folder.", null)]
    private string atdSaveSettings()
    {
        if (AutoDepthDesignation.TrySaveSettings(out string path))
            return $"[ATD] Settings saved to: {path}";
        return "[ATD] Failed to save settings. Check the log for details.";
    }

    [ConsoleCommand(false, false, "Analyzes one flat farming level-designation origin. Coordinates snap to the 4x4 designation origin.", null)]
    private string atdFarmingAnalyzeOrigin(int x, int y)
    {
        return AutoDepthDesignation.AnalyzeFarmingOriginForDebug(x, y);
    }

    [ConsoleCommand(false, false, "Dumps complete farming preparation/session and read-only analysis details for every mine tower.", null)]
    private string atdFarmingDumpAllTowers()
    {
        return AutoDepthDesignation.FormatAllTowersFarmingDesignationDump();
    }

    [ConsoleCommand(false, false, "Stage 2 debug: prepares one NeedsPreparation farming origin by replacing it with target-1 leveling.", null)]
    private string atdFarmingPrepareOrigin(int x, int y)
    {
        return AutoDepthDesignation.PrepareFarmingOriginForDebug(x, y);
    }

    [ConsoleCommand(false, false, "Stage 2 debug: restores the original level designation stored by atd_farming_prepare_origin.", null)]
    private string atdFarmingRestoreOrigin(int x, int y)
    {
        return AutoDepthDesignation.RestoreFarmingOriginForDebug(x, y);
    }

    [ConsoleCommand(false, false, "Resets ATD global settings to built-in defaults in memory only. Use atd_save_settings to write them to ATDsettings.json.", null)]
    private string atdResetToDefaults()
    {
        AutoDepthDesignation.ResetSettingsToDefaults();
        return "[ATD] Settings reset to built-in defaults in memory. Use atd_save_settings to save them.";
    }

    [ConsoleCommand(false, false, "Lists all mine towers with their assigned vehicles and ATD auto-release state.", null)]
    private string atdGetAssignedVehicles()
    {
        return AutoDepthDesignation.FormatAssignedVehiclesDump();
    }

    [ConsoleCommand(false, false, "Gets or sets the session-only ATD diagnostic level. Allowed: default, warning, info, debug, trace.", "atd_diagnostic_level")]
    private string atdDiagnosticLevel(string value = "")
    {
        if (string.IsNullOrWhiteSpace(value))
            return $"[ATD] Diagnostic level: {AtdDiagnostics.Describe()}.";

        if (!AtdDiagnostics.TrySetSessionLevel(value, out string error))
            return $"[ATD] Invalid diagnostic level '{value}'. {error}";

        return $"[ATD] Diagnostic level set for this session: {AtdDiagnostics.Describe()}.";
    }

    [ConsoleCommand(false, false, "Toggles the cursor tile-position overlay (bottom-left corner) for this session. Optionally pass 'on' or 'off'; use atd_save_settings to persist it.", null)]
    private string atdCursorOverlay(string value = "")
    {
        bool current = AutoDepthDesignation.ShowCursorOverlay;
        if (!TryParseConsoleBool(value, out bool parsed))
            parsed = !current;
        AutoDepthDesignation.ShowCursorOverlay = parsed;
        return parsed
            ? "[ATD] Cursor overlay ON."
            : "[ATD] Cursor overlay OFF.";
    }

    [ConsoleCommand(false, false, "Toggles the fading explored-node frontier. Optionally pass 'on' or 'off'; use atd_save_settings to persist it.", null)]
    private string atdAccessSearchOverlay(string value = "")
    {
        bool current = AutoDepthDesignation.ShowExperimentalAccessSearchOverlay;
        if (!TryParseConsoleBool(value, out bool parsed))
            parsed = !current;
        AutoDepthDesignation.ShowExperimentalAccessSearchOverlay = parsed;
        if (!parsed)
            AutoDepthDesignation.ClearExperimentalAccessSearchOverlay();
        return parsed
            ? "[ATD] Experimental access search overlay ON. Re-run accessway generation to populate it."
            : "[ATD] Experimental access search overlay OFF.";
    }

    [ConsoleCommand(false, false, "Toggles the persistent sparse P-field trace. Optionally pass 'on' or 'off'; use atd_save_settings to persist it.", null)]
    private string atdAccessPotentialOverlay(string value = "")
    {
        bool current = AutoDepthDesignation
            .ShowExperimentalAccessPotentialOverlay;
        if (!TryParseConsoleBool(value, out bool parsed))
            parsed = !current;
        AutoDepthDesignation.ShowExperimentalAccessPotentialOverlay = parsed;
        if (!parsed)
            AutoDepthDesignation
                .ClearExperimentalAccessPotentialOverlay();
        return parsed
            ? "[ATD] Experimental access P-field overlay ON. Re-run accessway generation to populate it."
            : "[ATD] Experimental access P-field overlay OFF.";
    }

    [ConsoleCommand(false, false, "Clears all stored ATD diagnostic overlays without changing which overlays are enabled.", null)]
    private string atdClearDiagnosticOverlays()
    {
        AutoDepthDesignation.ClearDiagnosticOverlays();
        return "[ATD] Diagnostic overlays cleared.";
    }

    [ConsoleCommand(false, false, "Toggles a persistent V2 Mega-handoff overlay for the latest access search: red = locally pathable but disconnected from the tower, green = tower-reachable, cyan = selected route. Session-only.", null)]
    private string atdV2PathabilityOverlay(string value = "")
    {
        bool current = AutoDepthDesignation.ShowV2PathabilityOverlay;
        if (!TryParseConsoleBool(value, out bool parsed))
            parsed = !current;
        AutoDepthDesignation.ShowV2PathabilityOverlay = parsed;
        if (!parsed)
            AutoDepthDesignation.ClearV2PathabilityOverlay();
        return parsed
            ? "[ATD] V2 Mega pathability overlay ON. Re-run accessway generation to populate it."
            : "[ATD] V2 Mega pathability overlay OFF.";
    }

    [ConsoleCommand(false, false, "Toggles the access-cluster overlay: identity, state, origin count, arithmetic center, and tied center roots. Optionally pass 'on' or 'off'; use atd_save_settings to persist it.", null)]
    private string atdAccessClusterOverlay(string value = "")
    {
        bool current = AutoDepthDesignation.ShowAccessClusterOverlay;
        if (!TryParseConsoleBool(value, out bool parsed))
            parsed = !current;
        AutoDepthDesignation.ShowAccessClusterOverlay = parsed;
        if (!parsed)
            AutoDepthDesignation.ClearAccessClusterOverlay();
        return parsed
            ? "[ATD] Access cluster overlay ON. Re-run designation generation to populate it."
            : "[ATD] Access cluster overlay OFF.";
    }

    [ConsoleCommand(false, false, "Builds the experimental access useful-height hull and prunes generated-profile centers for newly created snapshots. Session-only. Optionally pass 'on' or 'off'.", null)]
    private string atdAccessHeightEnvelope(string value = "")
    {
        bool current = AutoTerrainDesignationsMod.ExperimentalAccessUsefulHeightEnvelope;
        if (!TryParseConsoleBool(value, out bool parsed))
            parsed = !current;
        AutoTerrainDesignationsMod.SetExperimentalAccessUsefulHeightEnvelope(parsed);
        return parsed
            ? "[ATD] Experimental access useful-height hull and V1/V2 center pruning ON."
            : "[ATD] Experimental access useful-height hull and V1/V2 center pruning OFF.";
    }

    [ConsoleCommand(false, false, "Sets the session-only V1 fixed-endpoint lower hull extension. Nonnegative; rounded to 1/32 height. Default 1.0. Applies to newly built snapshots.", "atd_access_height_envelope_v1_lower_allowance")]
    private string atdAccessHeightEnvelopeV1LowerAllowance(string value)
    {
        if (!TryParseConsoleFloat(value, out float parsed))
            return $"[ATD] Invalid V1 lower allowance '{value}'. Use a finite nonnegative number, for example 1.0.";
        if (!AutoTerrainDesignationsMod
                .TrySetExperimentalAccessV1HeightEnvelopeLowerAllowance(parsed))
            return $"[ATD] Invalid V1 lower allowance '{value}'. Use a finite nonnegative number, for example 1.0.";
        return $"[ATD] V1 fixed-endpoint lower hull extension set to {AutoTerrainDesignationsMod.ExperimentalAccessV1HeightEnvelopeLowerAllowance}. New snapshots will use this value.";
    }

    [ConsoleCommand(false, false, "Sets the session-only V2 fixed-endpoint lower hull extension. Nonnegative; rounded to 1/32 height. Default 1.5. Applies to newly built snapshots.", "atd_access_height_envelope_v2_lower_allowance")]
    private string atdAccessHeightEnvelopeV2LowerAllowance(string value)
    {
        if (!TryParseConsoleFloat(value, out float parsed))
            return $"[ATD] Invalid V2 lower allowance '{value}'. Use a finite nonnegative number, for example 1.5.";
        if (!AutoTerrainDesignationsMod
                .TrySetExperimentalAccessV2HeightEnvelopeLowerAllowance(parsed))
            return $"[ATD] Invalid V2 lower allowance '{value}'. Use a finite nonnegative number, for example 1.5.";
        return $"[ATD] V2 fixed-endpoint lower hull extension set to {AutoTerrainDesignationsMod.ExperimentalAccessV2HeightEnvelopeLowerAllowance}. New snapshots will use this value.";
    }

    [ConsoleCommand(false, false, "Sets the session-only V1 fixed-endpoint upper hull extension. Nonnegative; rounded to 1/32 height. Default 1.0. Applies to newly built snapshots.", "atd_access_height_envelope_v1_upper_allowance")]
    private string atdAccessHeightEnvelopeV1UpperAllowance(string value)
    {
        if (!TryParseConsoleFloat(value, out float parsed))
            return $"[ATD] Invalid V1 upper allowance '{value}'. Use a finite nonnegative number, for example 1.0.";
        if (!AutoTerrainDesignationsMod
                .TrySetExperimentalAccessV1HeightEnvelopeUpperAllowance(parsed))
            return $"[ATD] Invalid V1 upper allowance '{value}'. Use a finite nonnegative number, for example 1.0.";
        return $"[ATD] V1 fixed-endpoint upper hull extension set to {AutoTerrainDesignationsMod.ExperimentalAccessV1HeightEnvelopeUpperAllowance}. New snapshots will use this value.";
    }

    [ConsoleCommand(false, false, "Sets the session-only V2 fixed-endpoint upper hull extension. Nonnegative; rounded to 1/32 height. Default 1.5. Applies to newly built snapshots.", "atd_access_height_envelope_v2_upper_allowance")]
    private string atdAccessHeightEnvelopeV2UpperAllowance(string value)
    {
        if (!TryParseConsoleFloat(value, out float parsed))
            return $"[ATD] Invalid V2 upper allowance '{value}'. Use a finite nonnegative number, for example 1.5.";
        if (!AutoTerrainDesignationsMod
                .TrySetExperimentalAccessV2HeightEnvelopeUpperAllowance(parsed))
            return $"[ATD] Invalid V2 upper allowance '{value}'. Use a finite nonnegative number, for example 1.5.";
        return $"[ATD] V2 fixed-endpoint upper hull extension set to {AutoTerrainDesignationsMod.ExperimentalAccessV2HeightEnvelopeUpperAllowance}. New snapshots will use this value.";
    }

    private static bool TryParseConsoleBool(string value, out bool parsed)
    {
        switch ((value ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "true":
            case "on":
            case "yes":
            case "1":
                parsed = true;
                return true;
            case "false":
            case "off":
            case "no":
            case "0":
                parsed = false;
                return true;
            default:
                parsed = false;
                return false;
        }
    }

    private static bool TryParseConsoleFloat(string value, out float parsed)
    {
        string text = (value ?? string.Empty).Trim();
        return float.TryParse(
                text, NumberStyles.Float, CultureInfo.InvariantCulture,
                out parsed)
            || float.TryParse(
                text, NumberStyles.Float, CultureInfo.CurrentCulture,
                out parsed);
    }
}
