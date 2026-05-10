// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
// Auto Terrain Designations - In-Game Console Commands

#pragma warning disable IDE0051 // Remove unused private members

using System.Text;
using Mafi;
using Mafi.Core.Console;

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
        sb.AppendLine($"  DesignationMode               = {(int)AutoTerrainDesignationsMod.DesignationMode} ({AutoTerrainDesignationsMod.DesignationMode})");
        sb.AppendLine($"  FlatteningDesignationType     = {(int)AutoTerrainDesignationsMod.FlatteningDesignationType} ({AutoTerrainDesignationsMod.FlatteningDesignationType})");
        sb.AppendLine($"  MaxHeightDiff                 = {AutoTerrainDesignationsMod.MaxHeightDiff}");
        sb.AppendLine($"  RampWidth                     = {AutoTerrainDesignationsMod.RampWidth}");
        sb.AppendLine($"  MaxLayersToExcavate           = {AutoTerrainDesignationsMod.MaxLayersToExcavate}");
        sb.AppendLine($"  MaxDepthToDigTo               = {AutoTerrainDesignationsMod.MaxDepthToDigTo?.ToString() ?? "-"}");
        sb.AppendLine($"  OrePurityLevel                = {AutoTerrainDesignationsMod.OrePurityLevel}");
        sb.AppendLine($"  BottomFlattening              = {AutoTerrainDesignationsMod.BottomFlatteningEnabled}");
        sb.AppendLine($"  MinCorridorClearance          = {AutoTerrainDesignationsMod.MinCorridorClearance}");
        sb.AppendLine($"  TerrainPanelCollapsed         = {AutoTerrainDesignationsMod.TerrainDesignationsPanelCollapsed}");
        sb.AppendLine($"  OrePanelCollapsed             = {AutoTerrainDesignationsMod.OreCompositionPanelCollapsed}");
        sb.AppendLine($"  ReEnableFarmingOnLoad         = {AutoTerrainDesignationsMod.ReEnableFarmingOnLoad}");
        sb.AppendLine($"  ExcavatorCompleteNtf          = {AutoTerrainDesignationsMod.ExcavatorCompletionNotificationsEnabled}");
        sb.Append(AutoDepthDesignation.FormatPurityArrays());
        return sb.ToString();
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

    [ConsoleCommand(false, false, "Sets whether the Terrain Designations panel starts collapsed by default (true/false, on/off, 1/0).", null)]
    private string atdSetTerrainDesignationsPanelCollapsed(string value)
    {
        if (!TryParseConsoleBool(value, out bool parsed))
            return $"[ATD] Invalid value '{value}'. Use true/false, on/off, yes/no, or 1/0.";

        AutoTerrainDesignationsMod.SetTerrainDesignationsPanelCollapsed(parsed);
        return $"[ATD] TerrainDesignationsPanelCollapsed set to {AutoTerrainDesignationsMod.TerrainDesignationsPanelCollapsed}.";
    }

    [ConsoleCommand(false, false, "Sets whether the Ore Composition panel starts collapsed by default (true/false, on/off, 1/0).", null)]
    private string atdSetOreCompositionPanelCollapsed(string value)
    {
        if (!TryParseConsoleBool(value, out bool parsed))
            return $"[ATD] Invalid value '{value}'. Use true/false, on/off, yes/no, or 1/0.";

        AutoTerrainDesignationsMod.SetOreCompositionPanelCollapsed(parsed);
        return $"[ATD] OreCompositionPanelCollapsed set to {AutoTerrainDesignationsMod.OreCompositionPanelCollapsed}.";
    }

    [ConsoleCommand(false, false, "Sets whether ATD re-enables farming automation for apparent farmland towers on load (true/false, on/off, 1/0).", null)]
    private string atdSetReEnableFarmingOnLoad(string value)
    {
        if (!TryParseConsoleBool(value, out bool parsed))
            return $"[ATD] Invalid value '{value}'. Use true/false, on/off, yes/no, or 1/0.";

        AutoTerrainDesignationsMod.SetReEnableFarmingOnLoad(parsed);
        return $"[ATD] ReEnableFarmingOnLoad set to {AutoTerrainDesignationsMod.ReEnableFarmingOnLoad}.";
    }

    [ConsoleCommand(false, false, "Alias for atd_set_re_enable_farming_on_load.", null)]
    private string atdReEnableFarmingOnLoad(string value)
    {
        return atdSetReEnableFarmingOnLoad(value);
    }

    [ConsoleCommand(false, false, "Sets whether vehicle depot excavator completion notifications are shown (true/false, on/off, 1/0).", null)]
    private string atdSetExcavatorCompletionNotifications(string value)
    {
        if (!TryParseConsoleBool(value, out bool parsed))
            return $"[ATD] Invalid value '{value}'. Use true/false, on/off, yes/no, or 1/0.";

        AutoTerrainDesignationsMod.SetExcavatorCompletionNotificationsEnabled(parsed);
        return $"[ATD] ExcavatorCompletionNotifications set to {AutoTerrainDesignationsMod.ExcavatorCompletionNotificationsEnabled}.";
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
}
