// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
using System;
using System.Globalization;
using System.IO;
using System.Text;
using CoI.AutoHelpers.Persistence;
using Mafi.Collections;
using Mafi.Core;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Serialization;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        internal const string TowerSettingsConfigKey = "atdTowerSettingsStateJson";

        private const int TowerSettingsConfigSchemaVersion = 1;

        private static string? s_lastLoadedTowerSettingsJson;

        internal static string? GetLastLoadedTowerSettingsJson() => s_lastLoadedTowerSettingsJson;

        internal static void LoadTowerSettingsFromJsonStore(IModStateJsonStore store)
        {
            string json = store.LoadJson();
            if (string.IsNullOrWhiteSpace(json))
            {
                s_log.Info($"Persistence: no tower settings found in {store.StorageKind}; using defaults.");
                return;
            }

            s_lastLoadedTowerSettingsJson = json;

            try
            {
                if (TryApplyTowerSettingsStateJson(json, out int loadedCount))
                {
                    s_log.Info($"Persistence: loaded {loadedCount} tower setting record(s) from {store.StorageKind}.");
                }
            }
            catch (Exception ex)
            {
                s_log.Warning($"Persistence: failed to load tower settings from {store.StorageKind}: {ex.Message}");
            }
        }

        internal static void SaveTowerSettingsToJsonStore(IModStateJsonStore store)
        {
            string json = BuildTowerSettingsStateJsonForConfig(out int savedCount);
            ModStateJsonSaveResult result = store.SaveJson(json);
            if (!result.Succeeded)
            {
                s_log.Warning($"Persistence: failed to update {result.StorageKind} value '{result.StateKey}': {result.ErrorMessage}");
                return;
            }

            s_log.Info($"Persistence: staged {savedCount} tower setting override record(s) in {store.StorageKind}.");
        }

        internal static string BuildTowerSettingsStateJsonForConfig()
        {
            return BuildTowerSettingsStateJsonForConfig(out int _);
        }

        private static string BuildTowerSettingsStateJsonForConfig(out int savedCount)
        {
            savedCount = 0;
            var sb = new StringBuilder();
            sb.Append("{\"schemaVersion\":");
            sb.Append(TowerSettingsConfigSchemaVersion.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"towerSettings\":[");

            bool first = true;

            // Collect the union of entityIds across all dicts.
            var allEntityIds = new System.Collections.Generic.HashSet<EntityId>();
            foreach (var pair in s_towerSettingsByEntityId)
                allEntityIds.Add(pair.Key);
            foreach (var pair in s_selectedOrePerTower)
                allEntityIds.Add(pair.Key);
            foreach (var pair in s_excavatorPriorityByTowerEntityId)
                allEntityIds.Add(pair.Key);
            foreach (var pair in s_farmingPreparationSessions)
                if (pair.Value.Enabled) allEntityIds.Add(pair.Key);
            foreach (EntityId farmingDisabledId in s_farmingAutomationDisabledTowerIds)
                allEntityIds.Add(farmingDisabledId);
            foreach (var pair in s_terrainPanelCollapsedByEntityId)
                allEntityIds.Add(pair.Key);
            foreach (var pair in s_orePanelCollapsedByEntityId)
                allEntityIds.Add(pair.Key);
            foreach (var pair in s_farmingPanelCollapsedByEntityId)
                allEntityIds.Add(pair.Key);

            foreach (EntityId entityId in allEntityIds)
            {
                if (!entityId.IsValid)
                {
                    continue;
                }

                bool hasSettings = s_towerSettingsByEntityId.TryGetValue(entityId, out ATDTowerSettings settings);
                bool hasOre = s_selectedOrePerTower.TryGetValue(entityId, out ProductProto? ore);
                bool hasPriority = s_excavatorPriorityByTowerEntityId.TryGetValue(entityId, out LooseProductProto priority);
                bool farmingEnabled = s_farmingPreparationSessions.TryGetValue(entityId, out FarmingPreparationSession farmingSession) && farmingSession.Enabled;
                bool farmingDisabled = s_farmingAutomationDisabledTowerIds.Contains(entityId);
                bool hasFarmingState = farmingEnabled || farmingDisabled;
                bool hasTerrainCollapsed = s_terrainPanelCollapsedByEntityId.TryGetValue(entityId, out bool terrainCollapsed);
                bool hasOreCollapsed = s_orePanelCollapsedByEntityId.TryGetValue(entityId, out bool oreCollapsed);
                bool hasFarmingCollapsed = s_farmingPanelCollapsedByEntityId.TryGetValue(entityId, out bool farmingCollapsed);

                // Skip towers with no non-default state to persist.
                if ((!hasSettings || settings.MatchesGlobalDefaults()) && (!hasOre || ore == null) && (!hasPriority || priority == null) && !hasFarmingState && !hasTerrainCollapsed && !hasOreCollapsed && !hasFarmingCollapsed)
                {
                    continue;
                }

                if (!first)
                {
                    sb.Append(',');
                }

                first = false;
                savedCount++;
                sb.Append("{\"entityId\":");
                sb.Append(entityId.Value.ToString(CultureInfo.InvariantCulture));

                if (hasSettings)
                {
                    AppendIntOverride(sb, "maxHeightDiff", settings.MaxHeightDiff, AutoTerrainDesignationsMod.MaxHeightDiff);
                    AppendIntOverride(sb, "rampWidth", settings.RampWidth, AutoTerrainDesignationsMod.RampWidth);
                    AppendIntOverride(sb, "maxLayersToExcavate", settings.MaxLayersToExcavate, AutoTerrainDesignationsMod.MaxLayersToExcavate);
                    AppendNullableIntOverride(sb, "maxDepthToDigTo", settings.MaxDepthToDigTo, AutoTerrainDesignationsMod.MaxDepthToDigTo);
                    AppendIntOverride(sb, "orePurityLevel", settings.OrePurityLevel, AutoTerrainDesignationsMod.OrePurityLevel);
                    AppendIntOverride(sb, "corridorClearance", settings.CorridorClearance, AutoTerrainDesignationsMod.MinCorridorClearance);
                    AppendBoolOverride(sb, "autoReleaseVehiclesWhenIdle", settings.AutoReleaseVehiclesWhenIdle, AutoTerrainDesignationsMod.AutoReleaseVehiclesWhenIdle);
                }

                if (hasOre && ore != null)
                {
                    sb.Append(",\"oreFilter\":\"");
                    sb.Append(ore.Id.Value);
                    sb.Append('"');
                }

                if (hasPriority && priority != null)
                {
                    sb.Append(",\"miningPriority\":\"");
                    sb.Append(priority.Id.Value);
                    sb.Append('"');
                }

                if (hasFarmingState)
                {
                    sb.Append(",\"farmingAutomation\":");
                    AppendJsonBool(sb, farmingEnabled);
                }

                if (hasTerrainCollapsed)
                {
                    sb.Append(",\"terrainPanelCollapsed\":");
                    AppendJsonBool(sb, terrainCollapsed);
                }

                if (hasOreCollapsed)
                {
                    sb.Append(",\"orePanelCollapsed\":");
                    AppendJsonBool(sb, oreCollapsed);
                }

                if (hasFarmingCollapsed)
                {
                    sb.Append(",\"farmingPanelCollapsed\":");
                    AppendJsonBool(sb, farmingCollapsed);
                }

                sb.Append('}');
            }

            sb.Append(']');
            AppendPendingFarmPlacementBatchesJson(sb);
            sb.Append('}');
            return sb.ToString();
        }

        private static bool TryApplyTowerSettingsStateJson(string json, out int loadedCount)
        {
            loadedCount = 0;
            object parsed = new JsonParser().Parse(new StringReader(json));
            if (!(parsed is Dict<string, object> root))
            {
                string snippet = json.Length <= 120 ? json : json.Substring(0, 120) + "...";
                s_log.Warning($"Persistence: tower settings JSON did not parse as an object; skipping. Content: {snippet}");
                return false;
            }

            if (!TryGetInt(root, "schemaVersion", out int schemaVersion)
                || schemaVersion != TowerSettingsConfigSchemaVersion)
            {
                s_log.Warning($"Persistence: unsupported tower settings schema version '{schemaVersion}'.");
                return false;
            }

            if (!root.TryGetValue("towerSettings", out object rawEntries)
                || !(rawEntries is object[] entries))
            {
                string snippet = json.Length <= 120 ? json : json.Substring(0, 120) + "...";
                s_log.Warning($"Persistence: tower settings JSON is missing or has an unexpected 'towerSettings' value; skipping. Content: {snippet}");
                return false;
            }

            if (root.TryGetValue("pendingFarmPlacementBatches", out object rawPendingBatches))
            {
                if (rawPendingBatches is object[] pendingBatchEntries)
                    RestorePendingFarmPlacementBatchesFromJsonEntries(pendingBatchEntries);
                else
                    s_log.Warning("Persistence: pendingFarmPlacementBatches exists but is not an array; skipped.");
            }
            else
            {
                RestorePendingFarmPlacementBatchesFromJsonEntries(Array.Empty<object>());
            }

            s_towerSettingsByEntityId.Clear();
            s_selectedOrePerTower.Clear();
            s_excavatorPriorityByTowerEntityId.Clear();
            s_farmingAutomationDisabledTowerIds.Clear();
            s_terrainPanelCollapsedByEntityId.Clear();
            s_orePanelCollapsedByEntityId.Clear();
            s_farmingPanelCollapsedByEntityId.Clear();

            foreach (object rawEntry in entries)
            {
                if (!(rawEntry is Dict<string, object> entry)
                    || !TryGetInt(entry, "entityId", out int entityIdValue)
                    || entityIdValue <= 0)
                {
                    s_log.Warning($"Persistence: skipping unreadable tower settings entry: {rawEntry}");
                    continue;
                }

                var entityId = new EntityId(entityIdValue);

                var settings = ATDTowerSettings.FromGlobalDefaults();
                if (TryGetInt(entry, "maxHeightDiff", out int maxHeightDiff))
                    settings.SetMaxHeightDiff(maxHeightDiff);
                if (TryGetInt(entry, "rampWidth", out int rampWidth))
                    settings.SetRampWidth(rampWidth);
                if (TryGetInt(entry, "maxLayersToExcavate", out int maxLayersToExcavate))
                    settings.SetMaxLayersToExcavate(maxLayersToExcavate);
                if (TryGetNullableInt(entry, "maxDepthToDigTo", out int? maxDepthToDigTo))
                    settings.SetMaxDepthToDigTo(maxDepthToDigTo);
                if (TryGetInt(entry, "orePurityLevel", out int orePurityLevel))
                    settings.SetOrePurityLevel(orePurityLevel);
                if (TryGetInt(entry, "corridorClearance", out int corridorClearance))
                    settings.SetCorridorClearance(corridorClearance);
                if (TryGetBool(entry, "autoReleaseVehiclesWhenIdle", out bool autoRelease))
                    settings.SetAutoReleaseWhenIdle(autoRelease);

                s_towerSettingsByEntityId[entityId] = settings;

                if (entry.TryGetValue("oreFilter", out object rawOre) && rawOre is string oreIdStr && !string.IsNullOrEmpty(oreIdStr))
                {
                    if (s_protosDb != null && s_protosDb.TryGetProto(new Proto.ID(oreIdStr), out ProductProto oreProto))
                    {
                        s_selectedOrePerTower[entityId] = oreProto;
                    }
                    else
                    {
                        s_log.Warning($"Persistence: ore proto '{oreIdStr}' not found for entity {entityIdValue}; ore filter skipped.");
                    }
                }

                if (entry.TryGetValue("miningPriority", out object rawPriority) && rawPriority is string priorityIdStr && !string.IsNullOrEmpty(priorityIdStr))
                {
                    if (s_protosDb != null && s_protosDb.TryGetProto(new Proto.ID(priorityIdStr), out LooseProductProto priorityProto))
                    {
                        s_excavatorPriorityByTowerEntityId[entityId] = priorityProto;
                    }
                    else
                    {
                        s_log.Warning($"Persistence: mining priority proto '{priorityIdStr}' not found for entity {entityIdValue}; mining priority skipped.");
                    }
                }

                if (TryGetBool(entry, "farmingAutomation", out bool farmingAutomation))
                {
                    if (farmingAutomation)
                    {
                        FarmingPreparationSession session = GetOrCreateFarmingPreparationSession(entityId);
                        session.Enabled = true;
                    }
                    else
                    {
                        s_farmingAutomationDisabledTowerIds.Add(entityId);
                    }
                }

                if (TryGetBool(entry, "terrainPanelCollapsed", out bool terrainPanelCollapsed))
                    s_terrainPanelCollapsedByEntityId[entityId] = terrainPanelCollapsed;
                if (TryGetBool(entry, "orePanelCollapsed", out bool orePanelCollapsed))
                    s_orePanelCollapsedByEntityId[entityId] = orePanelCollapsed;
                if (TryGetBool(entry, "farmingPanelCollapsed", out bool farmingPanelCollapsed))
                    s_farmingPanelCollapsedByEntityId[entityId] = farmingPanelCollapsed;

                loadedCount++;
            }

            return true;
        }

        private static void AppendBoolOverride(StringBuilder sb, string name, bool value, bool defaultValue)
        {
            if (value == defaultValue)
            {
                return;
            }

            sb.Append(",\"");
            sb.Append(name);
            sb.Append("\":");
            AppendJsonBool(sb, value);
        }

        private static void AppendIntOverride(StringBuilder sb, string name, int value, int defaultValue)
        {
            if (value == defaultValue)
            {
                return;
            }

            sb.Append(",\"");
            sb.Append(name);
            sb.Append("\":");
            sb.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendNullableIntOverride(StringBuilder sb, string name, int? value, int? defaultValue)
        {
            if (value == defaultValue)
            {
                return;
            }

            sb.Append(",\"");
            sb.Append(name);
            sb.Append("\":");
            if (value.HasValue)
                sb.Append(value.Value.ToString(CultureInfo.InvariantCulture));
            else
                sb.Append("null");
        }

        private static void AppendJsonBool(StringBuilder sb, bool value)
        {
            sb.Append(value ? "true" : "false");
        }

        private static bool TryGetBool(Dict<string, object> dict, string key, out bool value)
        {
            value = false;
            if (dict.TryGetValue(key, out object rawValue) && rawValue is bool boolValue)
            {
                value = boolValue;
                return true;
            }

            return false;
        }

        private static bool TryGetInt(Dict<string, object> dict, string key, out int value)
        {
            value = 0;
            if (dict.TryGetValue(key, out object rawValue))
            {
                if (rawValue is int intValue)
                {
                    value = intValue;
                    return true;
                }

                if (rawValue is double doubleValue)
                {
                    value = (int)doubleValue;
                    return Math.Abs(value - doubleValue) < 0.0001d;
                }
            }

            return false;
        }

        private static bool TryGetString(Dict<string, object> dict, string key, out string value)
        {
            value = string.Empty;
            if (dict.TryGetValue(key, out object rawValue) && rawValue is string stringValue)
            {
                value = stringValue;
                return true;
            }

            return false;
        }

        private static bool TryGetNullableInt(Dict<string, object> dict, string key, out int? value)
        {
            value = null;
            if (!dict.TryGetValue(key, out object rawValue))
            {
                return false;
            }

            if (rawValue == null)
            {
                // Explicit JSON null — means "no depth limit" (null was deliberately saved).
                value = null;
                return true;
            }

            if (rawValue is int intValue)
            {
                value = intValue;
                return true;
            }

            if (rawValue is double doubleValue)
            {
                int candidate = (int)doubleValue;
                if (Math.Abs(candidate - doubleValue) < 0.0001d)
                {
                    value = candidate;
                    return true;
                }
            }

            return false;
        }
    }
}
