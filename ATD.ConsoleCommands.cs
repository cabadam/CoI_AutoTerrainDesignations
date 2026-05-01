// Copyright (c) 2026 Kayser
// SPDX-License-Identifier: MIT
// Auto Terrain Designations - In-Game Console Commands
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
        sb.AppendLine($"  MaxHeightDiff         = {AutoTerrainDesignationsMod.MaxHeightDiff}");
        sb.AppendLine($"  RampWidth             = {AutoTerrainDesignationsMod.RampWidth}");
        sb.AppendLine($"  MaxLayersToExcavate   = {AutoTerrainDesignationsMod.MaxLayersToExcavate}");
        sb.AppendLine($"  MaxDepthToDigTo       = {AutoTerrainDesignationsMod.MaxDepthToDigTo?.ToString() ?? "-"}");
        sb.AppendLine($"  OrePurityLevel        = {AutoTerrainDesignationsMod.OrePurityLevel}");
        sb.AppendLine($"  MinCorridorClearance  = {AutoTerrainDesignationsMod.MinCorridorClearance}");
        sb.Append(AutoDepthDesignation.FormatPurityArrays());
        return sb.ToString();
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
}
