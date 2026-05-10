// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
// Auto Terrain Designations - Settings Loading and Parsing
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Mafi;
using UnityEngine;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        private const float MIN_ORE_HEIGHT_THRESHOLD = 1.0f;

        private static float[] s_minOreHeightByLevel = Array.Empty<float>();
        private static float[] s_minBottomOreDensityByLevel = Array.Empty<float>();
        private static float[] s_minOrePurityByLevel = Array.Empty<float>();
        private static int[] s_minComponentSizeByLevel = Array.Empty<int>();
        private const string SETTINGS_FILE_NAME = "ATDsettings.json";
        private const string LEGACY_SETTINGS_FILE_NAME = "settings.json";
        // Increment when a built-in setting default changes in a way that must
        // migrate an older generated settings file. This is separate from the
        // mod version because packages can be rebuilt without changing it.
        private const int SETTINGS_DEFAULTS_REVISION = 3;
        private const int TURNING_RAMPS_DEFAULTS_REVISION = 3;

        private static bool s_settingsLoadAttempted;
        private static string? s_loadedSettingsPath;

        static AutoDepthDesignation()
        {
            ResetPurityLevelDefaults();
        }

        private static float[] DefaultMinOreHeightByLevel() => new float[] { 0f, 0.25f, 0.75f, 2.0f, 3.0f };
        private static float[] DefaultMinBottomOreDensityByLevel() => new float[] { 0f, 0.05f, 0.20f, 0.50f, 0.75f };
        private static float[] DefaultMinOrePurityByLevel() => new float[] { 0f, 0.05f, 0.20f, 0.50f, 0.75f };
        private static int[] DefaultMinComponentSizeByLevel() => new int[] { 0, 2, 6, 20, 40 };

        private static float[] LegacyMinOreHeightByLevel() => new float[] { 0f, 0.5f, 1.0f, 2.0f, 3.0f };
        private static float[] LegacyMinBottomOreDensityByLevel() => new float[] { 0f, 0.10f, 0.25f, 0.50f, 0.75f };
        private static float[] LegacyMinOrePurityByLevel() => new float[] { 0f, 0.10f, 0.25f, 0.50f, 0.75f };
        private static int[] LegacyMinComponentSizeByLevel() => new int[] { 0, 3, 8, 20, 40 };

        private static void ResetPurityLevelDefaults()
        {
            s_minOreHeightByLevel = DefaultMinOreHeightByLevel();
            s_minBottomOreDensityByLevel = DefaultMinBottomOreDensityByLevel();
            s_minOrePurityByLevel = DefaultMinOrePurityByLevel();
            s_minComponentSizeByLevel = DefaultMinComponentSizeByLevel();
        }

        internal static void ResetSettingsToDefaults()
        {
            AutoTerrainDesignationsMod.ResetGlobalDefaults();
            AtdDiagnostics.ResetToBuildDefault();
            ShowCursorOverlay = false;
            ShowExperimentalAccessSearchOverlay = false;
            ShowExperimentalAccessPotentialOverlay = false;
            ShowAccessClusterOverlay = false;
            ResetWorldPathfinderSettingsToDefaults();
            s_batchSize = BATCH_SIZE;
            ResetPurityLevelDefaults();
        }

        internal static bool TrySetMinOreHeightForLevel(int level, float value)
        {
            if (level < 0 || level >= s_minOreHeightByLevel.Length) return false;
            s_minOreHeightByLevel[level] = value;
            return true;
        }

        internal static bool TrySetMinBottomOreDensityForLevel(int level, float value)
        {
            if (level < 0 || level >= s_minBottomOreDensityByLevel.Length) return false;
            s_minBottomOreDensityByLevel[level] = Math.Max(0f, Math.Min(1f, value));
            return true;
        }

        internal static bool TrySetMinOrePurityForLevel(int level, float value)
        {
            if (level < 0 || level >= s_minOrePurityByLevel.Length) return false;
            s_minOrePurityByLevel[level] = Math.Max(0f, Math.Min(1f, value));
            return true;
        }

        internal static bool TrySetMinComponentSizeForLevel(int level, int value)
        {
            if (level < 0 || level >= s_minComponentSizeByLevel.Length) return false;
            s_minComponentSizeByLevel[level] = Math.Max(0, value);
            return true;
        }

        internal static int PurityLevelCount => s_minOreHeightByLevel.Length;

        internal static int BatchSize => s_batchSize;

        internal static void SetBatchSize(int value)
        {
            s_batchSize = ClampBatchSize(value);
        }

        internal static float GetMinOreHeightForLevel(int level)
        {
            return level >= 0 && level < s_minOreHeightByLevel.Length
                ? s_minOreHeightByLevel[level]
                : 0f;
        }

        internal static float GetMinBottomOreDensityForLevel(int level)
        {
            return level >= 0 && level < s_minBottomOreDensityByLevel.Length
                ? s_minBottomOreDensityByLevel[level]
                : 0f;
        }

        internal static float GetMinOrePurityForLevel(int level)
        {
            return level >= 0 && level < s_minOrePurityByLevel.Length
                ? s_minOrePurityByLevel[level]
                : 0f;
        }

        internal static int GetMinComponentSizeForLevel(int level)
        {
            return level >= 0 && level < s_minComponentSizeByLevel.Length
                ? s_minComponentSizeByLevel[level]
                : 0;
        }

        internal static string FormatPurityArrays()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("  Purity arrays (index = level 0-4):");
            sb.Append("    minOreHeight        = [");
            for (int i = 0; i < s_minOreHeightByLevel.Length; i++)
                sb.Append((i > 0 ? ", " : "") + s_minOreHeightByLevel[i].ToString("G", System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine("]");
            sb.Append("    minBottomOreDensity = [");
            for (int i = 0; i < s_minBottomOreDensityByLevel.Length; i++)
                sb.Append((i > 0 ? ", " : "") + s_minBottomOreDensityByLevel[i].ToString("G", System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine("]");
            sb.Append("    minOrePurity        = [");
            for (int i = 0; i < s_minOrePurityByLevel.Length; i++)
                sb.Append((i > 0 ? ", " : "") + s_minOrePurityByLevel[i].ToString("G", System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine("]");
            sb.Append("    minComponentSize    = [");
            for (int i = 0; i < s_minComponentSizeByLevel.Length; i++)
                sb.Append((i > 0 ? ", " : "") + s_minComponentSizeByLevel[i]);
            sb.AppendLine("]");
            return sb.ToString();
        }

        private static void LoadSettingsFromJson()
        {
            s_settingsLoadAttempted = true;

            try
            {
                string? settingsPath = ResolveSettingsPath(out bool isLegacySettingsPath);
                if (string.IsNullOrWhiteSpace(settingsPath) || !File.Exists(settingsPath))
                {
                    // File absent — generate defaults next to the mod folder so users can customise
                    string? genPath = SavedSettingsPath;
                    if (!string.IsNullOrWhiteSpace(genPath))
                    {
                        try
                        {
                            File.WriteAllText(genPath, BuildSettingsJson());
                            s_loadedSettingsPath = genPath;
                            s_log.Warning($"ATDsettings.json not found \u2014 defaults written to: {genPath}");
                        }
                        catch (Exception writeEx)
                        {
                            s_loadedSettingsPath = null;
                            s_log.Warning($"Could not write default ATDsettings.json: {writeEx.Message}");
                        }
                    }
                    else
                    {
                        s_loadedSettingsPath = null;
                        s_log.Warning("ATDsettings.json not found and mod root path is unknown; using built-in defaults.");
                    }
                    return;
                }

                string json = File.ReadAllText(settingsPath);
                int fileDefaultsRevision = ParseInt(
                    json, "settingsDefaultsRevision") ?? 0;
                string? fileVersion = ParseSettingsJson(json, isLegacySettingsPath);
                s_loadedSettingsPath = isLegacySettingsPath
                    ? Path.Combine(Path.GetDirectoryName(settingsPath) ?? string.Empty, SETTINGS_FILE_NAME)
                    : settingsPath;

                // If the file predates the current version, or was read from the old
                // settings.json name, rewrite it to the current documented ATDsettings.json
                // format while preserving user values.
                if (isLegacySettingsPath
                    || fileVersion != AutoTerrainDesignationsMod.ModVersion
                    || fileDefaultsRevision < SETTINGS_DEFAULTS_REVISION)
                {
                    if (TrySaveSettings(out string migratedPath))
                    {
                        string source = isLegacySettingsPath ? "legacy settings.json" : "ATDsettings.json";
                        s_log.Warning(
                            $"{source} migrated to version "
                            + $"{AutoTerrainDesignationsMod.ModVersion} "
                            + $"(defaults revision {SETTINGS_DEFAULTS_REVISION}): "
                            + migratedPath);
                    }
                }
            }
            catch (Exception ex)
            {
                s_loadedSettingsPath = null;
                s_log.Warning($"Failed to load ATDsettings.json: {ex.Message}");
            }
        }

        private static string? ResolveSettingsPath(out bool isLegacySettingsPath)
        {
            isLegacySettingsPath = false;
            var rootDirs = new List<string>();

            try
            {
                TryAddCandidateRoot(rootDirs, s_modRootDirectoryPath);
            }
            catch
            {
            }

            try
            {
                TryAddCandidateRoot(rootDirs, typeof(AutoDepthDesignation).Assembly.Location);
            }
            catch
            {
            }

            try
            {
                string? codeBase = typeof(AutoDepthDesignation).Assembly.CodeBase;
                if (!string.IsNullOrWhiteSpace(codeBase)
                    && Uri.TryCreate(codeBase, UriKind.Absolute, out Uri uri)
                    && uri.IsFile)
                {
                    TryAddCandidateRoot(rootDirs, uri.LocalPath);
                }
            }
            catch
            {
            }

            try
            {
                TryAddCandidateRoot(rootDirs, AppDomain.CurrentDomain.BaseDirectory);
            }
            catch
            {
            }

            try
            {
                TryAddCandidateRoot(rootDirs, Directory.GetCurrentDirectory());
            }
            catch
            {
            }

            foreach (string root in rootDirs)
            {
                // Prefer directories that look like an actual mod root (manifest + ATDsettings),
                // but still allow direct sibling ATDsettings.json next to a loaded DLL path.
                // If the new file is absent, fall back to the old settings.json name so
                // existing user configuration can be migrated into ATDsettings.json.
                DirectoryInfo? dir;
                try
                {
                    dir = new DirectoryInfo(root);
                }
                catch
                {
                    continue;
                }

                for (int i = 0; i < 8 && dir != null; i++)
                {
                    string candidateSettings;
                    string candidateLegacySettings;
                    string candidateManifest;
                    try
                    {
                        candidateSettings = Path.Combine(dir.FullName, SETTINGS_FILE_NAME);
                        candidateLegacySettings = Path.Combine(dir.FullName, LEGACY_SETTINGS_FILE_NAME);
                        candidateManifest = Path.Combine(dir.FullName, "manifest.json");
                    }
                    catch
                    {
                        dir = dir.Parent;
                        continue;
                    }

                    if (File.Exists(candidateSettings) && File.Exists(candidateManifest))
                    {
                        return candidateSettings;
                    }

                    if (i == 0 && File.Exists(candidateSettings))
                    {
                        return candidateSettings;
                    }

                    if (File.Exists(candidateLegacySettings) && File.Exists(candidateManifest))
                    {
                        isLegacySettingsPath = true;
                        return candidateLegacySettings;
                    }

                    if (i == 0 && File.Exists(candidateLegacySettings))
                    {
                        isLegacySettingsPath = true;
                        return candidateLegacySettings;
                    }

                    dir = dir.Parent;
                }
            }

            return null;
        }

        private static void TryAddCandidateRoot(List<string> roots, string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch
            {
                return;
            }

            string? directory;
            try
            {
                if (Directory.Exists(fullPath))
                {
                    directory = fullPath;
                }
                else
                {
                    directory = Path.GetDirectoryName(fullPath);
                }
            }
            catch
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            if (!roots.Contains(directory))
            {
                roots.Add(directory);
            }
        }

        private static string? ParseSettingsJson(string json, bool forceMigration)
        {
            // Simple JSON parser for our specific structure
            string? parsedVersion = ParseString(json, "settingsVersion");
            int parsedDefaultsRevision = ParseInt(
                json, "settingsDefaultsRevision") ?? 0;
            bool migrateGeneratedDefaults = forceMigration
                || parsedVersion != AutoTerrainDesignationsMod.ModVersion
                || parsedDefaultsRevision < SETTINGS_DEFAULTS_REVISION;
            try
            {
                // Extract purityLevels object
                int start = json.IndexOf("\"purityLevels\":");
                if (start >= 0)
                {
                    start = json.IndexOf('{', start);
                    int depth = 0, end = start;
                    for (int i = start; i < json.Length; i++)
                    {
                        if (json[i] == '{') depth++;
                        else if (json[i] == '}') depth--;
                        if (depth == 0) { end = i + 1; break; }
                    }

                    string purityObj = json.Substring(start, end - start);

                    float[]? minOreHeight = ParseFloatArray(purityObj, "minOreHeightByLevel");
                    if (minOreHeight != null && ShouldPreserveFloatArray(minOreHeight, migrateGeneratedDefaults, LegacyMinOreHeightByLevel(), DefaultMinOreHeightByLevel()))
                        s_minOreHeightByLevel = minOreHeight;

                    float[]? minBottomOreDensity = ParseFloatArray(purityObj, "minBottomOreDensityByLevel");
                    if (minBottomOreDensity != null && ShouldPreserveFloatArray(minBottomOreDensity, migrateGeneratedDefaults, LegacyMinBottomOreDensityByLevel(), DefaultMinBottomOreDensityByLevel()))
                        s_minBottomOreDensityByLevel = minBottomOreDensity;

                    float[]? minOrePurity = ParseFloatArray(purityObj, "minOrePurityRatioByLevel");
                    if (minOrePurity != null && ShouldPreserveFloatArray(minOrePurity, migrateGeneratedDefaults, LegacyMinOrePurityByLevel(), DefaultMinOrePurityByLevel()))
                        s_minOrePurityByLevel = minOrePurity;

                    int[]? minComponentSize = ParseIntArray(purityObj, "minComponentSizeByLevel");
                    if (minComponentSize != null && ShouldPreserveIntArray(minComponentSize, migrateGeneratedDefaults, LegacyMinComponentSizeByLevel(), DefaultMinComponentSizeByLevel()))
                        s_minComponentSizeByLevel = minComponentSize;
                }

                // Top-level scalar settings
                string? diagnosticLevel = ParseString(json, "diagnosticLevel");
                if (diagnosticLevel != null
                    && !AtdDiagnostics.TryApplyConfiguredLevel(
                        diagnosticLevel,
                        out string diagnosticLevelError))
                {
                    s_log.Warning(
                        $"Invalid diagnosticLevel '{diagnosticLevel}' in ATDsettings.json. " +
                        diagnosticLevelError + $" Using {AtdDiagnostics.Level}.");
                }

                int? batchSize = ParseInt(json, "batchSize");
                if (batchSize.HasValue && ShouldPreserveInt(batchSize.Value, migrateGeneratedDefaults, BATCH_SIZE))
                    s_batchSize = ClampBatchSize(batchSize.Value);

                DesignationMode? designationMode = ParseEnum<DesignationMode>(json, "designationMode");
                if (designationMode.HasValue)
                    AutoTerrainDesignationsMod.SetDesignationMode(designationMode.Value);

                FlatteningDesignationType? flatteningDesignationType = ParseEnum<FlatteningDesignationType>(json, "flatteningDesignationType");
                if (flatteningDesignationType.HasValue)
                    AutoTerrainDesignationsMod.SetFlatteningDesignationType(flatteningDesignationType.Value);

                int? slopeDefault = ParseInt(json, "maxSlopeHeightDiff");
                if (slopeDefault.HasValue && ShouldPreserveInt(slopeDefault.Value, migrateGeneratedDefaults, 1))
                    AutoTerrainDesignationsMod.SetMaxHeightDiff(slopeDefault.Value);

                int? rampWidth = ParseInt(json, "rampWidth");
                if (rampWidth.HasValue && ShouldPreserveInt(rampWidth.Value, migrateGeneratedDefaults, 1))
                    AutoTerrainDesignationsMod.SetRampWidth(rampWidth.Value);
                int? vehicleClearance = ParseInt(json, "vehicleClearance");
                if (vehicleClearance.HasValue)
                    AutoTerrainDesignationsMod.SetVehicleClearance((AccessVehicleClearanceMode)vehicleClearance.Value);

                int? maxLayers = ParseInt(json, "maxLayersToExcavate");
                if (maxLayers.HasValue && ShouldPreserveInt(maxLayers.Value, migrateGeneratedDefaults, 30))
                    AutoTerrainDesignationsMod.SetMaxLayersToExcavate(maxLayers.Value);

                var (foundDepth, depthVal) = TryParseNullableInt(json, "maxDepthToDigTo");
                if (foundDepth && ShouldPreserveNullableInt(depthVal, migrateGeneratedDefaults, (int?)null))
                    AutoTerrainDesignationsMod.SetMaxDepthToDigTo(depthVal);

                int? purityLevel = ParseInt(json, "orePurityLevel");
                if (purityLevel.HasValue && ShouldPreserveInt(purityLevel.Value, migrateGeneratedDefaults, 0))
                    AutoTerrainDesignationsMod.SetOrePurityLevel(purityLevel.Value);

                bool? bottomFlatteningEnabled = ParseBool(json, "bottomFlatteningEnabled");
                if (bottomFlatteningEnabled.HasValue && ShouldPreserveBool(bottomFlatteningEnabled.Value, migrateGeneratedDefaults, true))
                    AutoTerrainDesignationsMod.SetBottomFlatteningEnabled(bottomFlatteningEnabled.Value);

                int? bottomFlatteningStrength = ParseInt(json, "bottomFlatteningStrength");
                if (bottomFlatteningStrength.HasValue && ShouldPreserveInt(bottomFlatteningStrength.Value, migrateGeneratedDefaults, 5))
                    AutoTerrainDesignationsMod.SetBottomFlatteningStrength(bottomFlatteningStrength.Value);

                int? corridorClearance = ParseInt(json, "minCorridorClearance");
                if (corridorClearance.HasValue && ShouldPreserveInt(corridorClearance.Value, migrateGeneratedDefaults, 2))
                    AutoTerrainDesignationsMod.SetMinCorridorClearance(corridorClearance.Value);

                bool? terrainDesignationsPanelCollapsed = ParseBool(json, "terrainDesignationsPanelCollapsed");
                if (terrainDesignationsPanelCollapsed.HasValue && ShouldPreserveBool(terrainDesignationsPanelCollapsed.Value, migrateGeneratedDefaults, false))
                    AutoTerrainDesignationsMod.SetTerrainDesignationsPanelCollapsed(terrainDesignationsPanelCollapsed.Value);

                bool? oreCompositionPanelCollapsed = ParseBool(json, "oreCompositionPanelCollapsed");
                if (oreCompositionPanelCollapsed.HasValue && ShouldPreserveBool(oreCompositionPanelCollapsed.Value, migrateGeneratedDefaults, false))
                    AutoTerrainDesignationsMod.SetOreCompositionPanelCollapsed(oreCompositionPanelCollapsed.Value);

                bool? excavatorCompletionNotifications = ParseBool(json, "excavatorCompletionNotifications");
                if (excavatorCompletionNotifications.HasValue && ShouldPreserveBool(excavatorCompletionNotifications.Value, migrateGeneratedDefaults, true))
                    AutoTerrainDesignationsMod.SetExcavatorCompletionNotificationsEnabled(excavatorCompletionNotifications.Value);

                bool? rampNotificationsEnabled = ParseBool(json, "rampNotificationsEnabled");
                if (rampNotificationsEnabled.HasValue && ShouldPreserveBool(rampNotificationsEnabled.Value, migrateGeneratedDefaults, true))
                    AutoTerrainDesignationsMod.SetRampNotificationsEnabled(rampNotificationsEnabled.Value);

                bool? farmingPanelCollapsed = ParseBool(json, "farmingPanelCollapsed");
                if (farmingPanelCollapsed.HasValue && ShouldPreserveBool(farmingPanelCollapsed.Value, migrateGeneratedDefaults, true))
                    AutoTerrainDesignationsMod.SetFarmingPanelCollapsed(farmingPanelCollapsed.Value);

                bool? autoReleaseVehiclesWhenIdle = ParseBool(json, "autoReleaseVehiclesWhenIdle");
                if (autoReleaseVehiclesWhenIdle.HasValue && ShouldPreserveBool(autoReleaseVehiclesWhenIdle.Value, migrateGeneratedDefaults, false))
                    AutoTerrainDesignationsMod.SetAutoReleaseVehiclesWhenIdle(autoReleaseVehiclesWhenIdle.Value);

                bool? autoReleaseExcavatorsWhenIdle = ParseBool(json, "autoReleaseExcavatorsWhenIdle");
                if (autoReleaseExcavatorsWhenIdle.HasValue && ShouldPreserveBool(autoReleaseExcavatorsWhenIdle.Value, migrateGeneratedDefaults, false))
                    AutoTerrainDesignationsMod.SetAutoReleaseExcavatorsWhenIdle(autoReleaseExcavatorsWhenIdle.Value);

                bool? autoReleaseTrucksWhenIdle = ParseBool(json, "autoReleaseTrucksWhenIdle");
                if (autoReleaseTrucksWhenIdle.HasValue && ShouldPreserveBool(autoReleaseTrucksWhenIdle.Value, migrateGeneratedDefaults, false))
                    AutoTerrainDesignationsMod.SetAutoReleaseTrucksWhenIdle(autoReleaseTrucksWhenIdle.Value);

                bool? turningRampsExperimental = ParseBool(json, "turningRampsExperimental");
                if (turningRampsExperimental.HasValue)
                {
                    // The old file format cannot distinguish the generated
                    // false from a user's explicit false. Promote both legacy
                    // states to the new true default, but preserve the value
                    // once the setting has passed this one-time migration.
                    AutoTerrainDesignationsMod.SetTurningRampsExperimental(
                        ResolveTurningRampsExperimentalValue(
                            turningRampsExperimental.Value,
                            parsedDefaultsRevision));
                }

                bool? suppressLegacyAccessRamps = ParseBool(json, "suppressLegacyAccessRamps");
                if (suppressLegacyAccessRamps.HasValue && ShouldPreserveBool(suppressLegacyAccessRamps.Value, migrateGeneratedDefaults, false))
                    AutoTerrainDesignationsMod.SetSuppressLegacyAccessRamps(suppressLegacyAccessRamps.Value);

                bool? experimentalAccessUseAStar = ParseBool(json, "experimentalAccessUseAStar");
                if (experimentalAccessUseAStar.HasValue && ShouldPreserveBool(experimentalAccessUseAStar.Value, migrateGeneratedDefaults, false))
                    AutoTerrainDesignationsMod.SetExperimentalAccessUseAStar(experimentalAccessUseAStar.Value);

                bool? cursorOverlayEnabled = ParseBool(json, "cursorOverlayEnabled");
                if (cursorOverlayEnabled.HasValue
                    && ShouldPreserveBool(
                        cursorOverlayEnabled.Value,
                        migrateGeneratedDefaults,
                        false))
                    ShowCursorOverlay = cursorOverlayEnabled.Value;

                bool? experimentalAccessSearchOverlayEnabled = ParseBool(
                    json, "experimentalAccessSearchOverlayEnabled");
                if (experimentalAccessSearchOverlayEnabled.HasValue)
                {
                    if (ShouldPreserveBool(
                            experimentalAccessSearchOverlayEnabled.Value,
                            migrateGeneratedDefaults,
                            false))
                        ShowExperimentalAccessSearchOverlay =
                            experimentalAccessSearchOverlayEnabled.Value;
                }
                else
                {
                    // Compatibility with the short-lived integer-duration
                    // experiment: any positive duration keeps the overlay on.
                    int? legacyOverlaySeconds = ParseInt(
                        json, "experimentalAccessSearchOverlaySeconds");
                    if (legacyOverlaySeconds > 0)
                        ShowExperimentalAccessSearchOverlay = true;
                }

                bool? experimentalAccessPotentialOverlayEnabled = ParseBool(
                    json, "experimentalAccessPotentialOverlayEnabled");
                if (experimentalAccessPotentialOverlayEnabled.HasValue)
                {
                    if (ShouldPreserveBool(
                            experimentalAccessPotentialOverlayEnabled.Value,
                            migrateGeneratedDefaults,
                            false))
                        ShowExperimentalAccessPotentialOverlay =
                            experimentalAccessPotentialOverlayEnabled.Value;
                }
                else
                {
                    // The immediately preceding combined overlay setting
                    // displayed both traces. Preserve that behavior once,
                    // then emit the independent key on save.
                    int? legacyOverlaySeconds = ParseInt(
                        json, "experimentalAccessSearchOverlaySeconds");
                    ShowExperimentalAccessPotentialOverlay =
                        experimentalAccessSearchOverlayEnabled == true
                            || legacyOverlaySeconds > 0;
                }

                bool? accessClusterOverlayEnabled = ParseBool(
                    json, "accessClusterOverlayEnabled");
                if (accessClusterOverlayEnabled.HasValue
                    && ShouldPreserveBool(
                        accessClusterOverlayEnabled.Value,
                        migrateGeneratedDefaults,
                        false))
                    ShowAccessClusterOverlay =
                        accessClusterOverlayEnabled.Value;

                bool? accessAvoidOcean = ParseBool(json, "accessAvoidOcean");
                if (accessAvoidOcean.HasValue && ShouldPreserveBool(accessAvoidOcean.Value, migrateGeneratedDefaults, true))
                    AutoTerrainDesignationsMod.SetAccessAvoidOcean(accessAvoidOcean.Value);

                bool? accessAvoidBuildings = ParseBool(json, "accessAvoidBuildings");
                if (accessAvoidBuildings.HasValue && ShouldPreserveBool(accessAvoidBuildings.Value, migrateGeneratedDefaults, true))
                    AutoTerrainDesignationsMod.SetAccessAvoidBuildings(accessAvoidBuildings.Value);

                bool? allowRampsOutsideTowerAreas = ParseBool(json, "allowRampsOutsideTowerAreas");
                if (allowRampsOutsideTowerAreas.HasValue && ShouldPreserveBool(allowRampsOutsideTowerAreas.Value, migrateGeneratedDefaults, true))
                    AutoTerrainDesignationsMod.SetAllowRampsOutsideTowerAreas(allowRampsOutsideTowerAreas.Value);

                bool? accessHarvestDisruptedTrees = ParseBool(json, "accessHarvestDisruptedTrees");
                if (accessHarvestDisruptedTrees.HasValue && ShouldPreserveBool(accessHarvestDisruptedTrees.Value, migrateGeneratedDefaults, true))
                    AutoTerrainDesignationsMod.SetAccessHarvestDisruptedTrees(accessHarvestDisruptedTrees.Value);

                bool? accessAllowDigToRemoveDebris = ParseBool(json, "accessAllowDigToRemoveDebris");
                if (accessAllowDigToRemoveDebris.HasValue && ShouldPreserveBool(accessAllowDigToRemoveDebris.Value, migrateGeneratedDefaults, true))
                    AutoTerrainDesignationsMod.SetAccessAllowDigToRemoveDebris(accessAllowDigToRemoveDebris.Value);

                int? accessQuickRemoveDebrisPolicy = ParseInt(json,
                    "accessQuickRemoveDebrisPolicy");
                if (accessQuickRemoveDebrisPolicy.HasValue)
                    AutoTerrainDesignationsMod.SetAccessQuickRemoveDebrisPolicy(
                        (QuickRemoveDebrisPolicy)accessQuickRemoveDebrisPolicy.Value);

                float? accessLandscapingCostDistanceScale = ParseFloat(json, "accessLandscapingCostDistanceScale")
                    ?? ParseFloat(json, "accessWorkDistanceScale");
                if (accessLandscapingCostDistanceScale.HasValue && ShouldPreserveFloat(accessLandscapingCostDistanceScale.Value, migrateGeneratedDefaults, 1f))
                    AutoTerrainDesignationsMod.SetAccessLandscapingCostDistanceScale(accessLandscapingCostDistanceScale.Value);

                float? accessPropCleanupLandscapingCost = ParseFloat(json, "accessPropCleanupLandscapingCost");
                if (accessPropCleanupLandscapingCost.HasValue && ShouldPreserveFloat(accessPropCleanupLandscapingCost.Value, migrateGeneratedDefaults, 6f, 8f))
                    AutoTerrainDesignationsMod.SetAccessPropCleanupLandscapingCost(accessPropCleanupLandscapingCost.Value);

                float? accessLandslideRunPerHeight = ParseFloat(json, "accessLandslideRunPerHeight");
                if (accessLandslideRunPerHeight.HasValue && ShouldPreserveFloat(accessLandslideRunPerHeight.Value, migrateGeneratedDefaults, 1f))
                    AutoTerrainDesignationsMod.SetAccessLandslideRunPerHeight(accessLandslideRunPerHeight.Value);

                ApplyFloat("accessGeneratedVFixedCost", 1f, AutoTerrainDesignationsMod.SetAccessGeneratedVFixedCost);
                ApplyFloat("accessDirectWorkWeight", 1f, AutoTerrainDesignationsMod.SetAccessDirectWorkWeight);
                ApplyFloat("accessSideRayWeight", 1f, AutoTerrainDesignationsMod.SetAccessSideRayWeight);
                float? accessRaySlopeConservatism =
                    ParseFloat(json, "accessRaySlopeConservatism")
                    ?? ParseFloat(json, "accessCandidateRaySlopeFactor")
                    ?? ParseFloat(json, "accessProjectedRaySlopeFactor");
                if (accessRaySlopeConservatism.HasValue
                    && ShouldPreserveFloat(
                        accessRaySlopeConservatism.Value,
                        migrateGeneratedDefaults,
                        0.9f,
                        1f,
                        0.8f,
                        0.85f))
                    AutoTerrainDesignationsMod.SetAccessRaySlopeConservatism(
                        accessRaySlopeConservatism.Value);

                int? accessRayEndBuffer =
                    ParseInt(json, "accessRayEndBuffer")
                    ?? ParseInt(json, "accessCandidateRayEndBuffer")
                    ?? ParseInt(json, "accessProjectedRayEndBuffer");
                if (accessRayEndBuffer.HasValue
                    && ShouldPreserveInt(
                        accessRayEndBuffer.Value,
                        migrateGeneratedDefaults,
                        3,
                        2,
                        1))
                    AutoTerrainDesignationsMod.SetAccessRayEndBuffer(
                        accessRayEndBuffer.Value);
                ApplyInt("accessCandidateRayMaxDistance", 16, AutoTerrainDesignationsMod.SetAccessCandidateRayMaxDistance);
                float? accessRayMaxCost = ParseFloat(json, "accessRayMaxCost");
                if (accessRayMaxCost.HasValue
                    && ShouldPreserveFloat(accessRayMaxCost.Value, migrateGeneratedDefaults, 500f, 512f))
                    AutoTerrainDesignationsMod.SetAccessRayMaxCost(accessRayMaxCost.Value);

                float? accessRayUnresolvedPenalty = ParseFloat(json, "accessRayUnresolvedPenalty");
                if (accessRayUnresolvedPenalty.HasValue
                    && ShouldPreserveFloat(accessRayUnresolvedPenalty.Value, migrateGeneratedDefaults, 200f, 128f))
                    AutoTerrainDesignationsMod.SetAccessRayUnresolvedPenalty(accessRayUnresolvedPenalty.Value);
                ApplyInt("accessMaxVisitedNodes", 250000, AutoTerrainDesignationsMod.SetAccessMaxVisitedNodes);
                ApplyInt("accessSearchTimeoutSeconds", 60, AutoTerrainDesignationsMod.SetAccessSearchTimeoutSeconds);
                ApplyInt("accessSearchFrameBudgetMs", 30, AutoTerrainDesignationsMod.SetAccessSearchFrameBudgetMs);
                ApplyInt("accessManagerAutomatedFrameBudgetMs", 10, AutoTerrainDesignationsMod.SetAccessManagerAutomatedFrameBudgetMs);
                ApplyInt("accessManagerInteractiveFrameBudgetMs", 15, AutoTerrainDesignationsMod.SetAccessManagerInteractiveFrameBudgetMs);
                ApplyInt("accessManagerPausedMaxFrameBudgetMs", 30, AutoTerrainDesignationsMod.SetAccessManagerPausedMaxFrameBudgetMs);

                void ApplyFloat(string key, float defaultValue, Action<float> setter)
                {
                    float? parsed = ParseFloat(json, key);
                    if (parsed.HasValue && ShouldPreserveFloat(parsed.Value, migrateGeneratedDefaults, defaultValue))
                        setter(parsed.Value);
                }

                void ApplyInt(string key, int defaultValue, Action<int> setter)
                {
                    int? parsed = ParseInt(json, key);
                    if (parsed.HasValue && ShouldPreserveInt(parsed.Value, migrateGeneratedDefaults, defaultValue))
                        setter(parsed.Value);
                }

            }
            catch (Exception ex)
            {
                s_log.Warning($"Error parsing ATDsettings.json: {ex.Message}");
            }
            return parsedVersion;
        }

        private static bool ShouldPreserveInt(int value, bool migrateGeneratedDefaults, params int[] knownDefaults)
            => !migrateGeneratedDefaults || Array.IndexOf(knownDefaults, value) < 0;

        private static bool ShouldPreserveNullableInt(int? value, bool migrateGeneratedDefaults, params int?[] knownDefaults)
            => !migrateGeneratedDefaults || Array.IndexOf(knownDefaults, value) < 0;

        private static bool ShouldPreserveBool(bool value, bool migrateGeneratedDefaults, params bool[] knownDefaults)
            => !migrateGeneratedDefaults || Array.IndexOf(knownDefaults, value) < 0;

        private static bool ResolveTurningRampsExperimentalValue(
            bool value,
            int parsedDefaultsRevision)
            => parsedDefaultsRevision < TURNING_RAMPS_DEFAULTS_REVISION
                ? true
                : value;

        internal static bool ValidateTurningRampsExperimentalMigrationFixtures(
            out string failure)
        {
            if (!ResolveTurningRampsExperimentalValue(true, 0))
            {
                failure = "A legacy true turning-ramp value was not preserved.";
                return false;
            }

            if (!ResolveTurningRampsExperimentalValue(false, 0))
            {
                failure = "A legacy generated false turning-ramp value was not promoted.";
                return false;
            }

            if (!ResolveTurningRampsExperimentalValue(false, 2))
            {
                failure = "A revision-2 downgraded false turning-ramp value was not repaired.";
                return false;
            }

            if (ResolveTurningRampsExperimentalValue(false, 3)
                || !ResolveTurningRampsExperimentalValue(true, 3))
            {
                failure = "Current-revision turning-ramp values were not preserved.";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static bool ShouldPreserveFloat(float value, bool migrateGeneratedDefaults, params float[] knownDefaults)
            => !migrateGeneratedDefaults || Array.IndexOf(knownDefaults, value) < 0;

        private static bool ShouldPreserveString(string? value, bool migrateGeneratedDefaults, params string?[] knownDefaults)
            => !migrateGeneratedDefaults || Array.IndexOf(knownDefaults, value) < 0;

        private static bool ShouldPreserveFloatArray(float[] value, bool migrateGeneratedDefaults, params float[][] knownDefaults)
        {
            if (!migrateGeneratedDefaults)
                return true;

            foreach (float[] knownDefault in knownDefaults)
            {
                if (FloatArraysEqual(value, knownDefault))
                    return false;
            }

            return true;
        }

        private static bool ShouldPreserveIntArray(int[] value, bool migrateGeneratedDefaults, params int[][] knownDefaults)
        {
            if (!migrateGeneratedDefaults)
                return true;

            foreach (int[] knownDefault in knownDefaults)
            {
                if (IntArraysEqual(value, knownDefault))
                    return false;
            }

            return true;
        }

        private static bool FloatArraysEqual(float[] a, float[] b)
        {
            if (a.Length != b.Length)
                return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (Math.Abs(a[i] - b[i]) > 0.0001f)
                    return false;
            }

            return true;
        }

        private static bool IntArraysEqual(int[] a, int[] b)
        {
            if (a.Length != b.Length)
                return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }

        private static float[]? ParseFloatArray(string json, string key)
        {
            try
            {
                int idx = json.IndexOf($"\"{key}\":");
                if (idx < 0) return null;
                idx = json.IndexOf('[', idx);
                int end = json.IndexOf(']', idx);
                if (idx < 0 || end < 0) return null;

                string arrayStr = json.Substring(idx + 1, end - idx - 1);
                var parts = arrayStr.Split(',');
                var result = new float[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                        return null;
                    result[i] = val;
                }
                return result;
            }
            catch { return null; }
        }

        private static int[]? ParseIntArray(string json, string key)
        {
            try
            {
                int idx = json.IndexOf($"\"{key}\":");
                if (idx < 0) return null;
                idx = json.IndexOf('[', idx);
                int end = json.IndexOf(']', idx);
                if (idx < 0 || end < 0) return null;

                string arrayStr = json.Substring(idx + 1, end - idx - 1);
                var parts = arrayStr.Split(',');
                var result = new int[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int val))
                        return null;
                    result[i] = val;
                }
                return result;
            }
            catch { return null; }
        }

        private static int? ParseInt(string json, string key)
        {
            try
            {
                int idx = json.IndexOf($"\"{key}\":");
                if (idx < 0) return null;
                // Skip past the colon and whitespace
                int valStart = idx + key.Length + 3;
                while (valStart < json.Length && (json[valStart] == ' ' || json[valStart] == '\t')) valStart++;
                int valEnd = valStart;
                while (valEnd < json.Length && (char.IsDigit(json[valEnd]) || json[valEnd] == '-')) valEnd++;
                if (valEnd == valStart) return null;
                if (int.TryParse(json.Substring(valStart, valEnd - valStart), out int result))
                    return result;
                return null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Parses a nullable int value from JSON. Returns (true, value) if the key exists
        /// (value is null when the JSON value is literally null), or (false, null) if the key
        /// is absent.
        /// </summary>
        private static (bool found, int? value) TryParseNullableInt(string json, string key)
        {
            try
            {
                int idx = json.IndexOf($"\"{key}\":");
                if (idx < 0) return (false, null);
                int valStart = json.IndexOf(':', idx) + 1;
                while (valStart < json.Length && (json[valStart] == ' ' || json[valStart] == '\t' || json[valStart] == '\r' || json[valStart] == '\n')) valStart++;
                if (valStart + 4 <= json.Length && json.Substring(valStart, 4) == "null")
                    return (true, null);
                int valEnd = valStart;
                while (valEnd < json.Length && (char.IsDigit(json[valEnd]) || json[valEnd] == '-')) valEnd++;
                if (valEnd > valStart && int.TryParse(json.Substring(valStart, valEnd - valStart), out int val))
                    return (true, val);
                return (false, null);
            }
            catch { return (false, null); }
        }

        private static bool? ParseBool(string json, string key)
        {
            try
            {
                int idx = json.IndexOf($"\"{key}\":");
                if (idx < 0) return null;
                int valStart = json.IndexOf(':', idx) + 1;
                while (valStart < json.Length && (json[valStart] == ' ' || json[valStart] == '\t' || json[valStart] == '\r' || json[valStart] == '\n')) valStart++;
                if (valStart + 4 <= json.Length && string.Compare(json, valStart, "true", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
                    return true;
                if (valStart + 5 <= json.Length && string.Compare(json, valStart, "false", 0, 5, StringComparison.OrdinalIgnoreCase) == 0)
                    return false;
                return null;
            }
            catch { return null; }
        }

        private static float? ParseFloat(string json, string key)
        {
            try
            {
                int idx = json.IndexOf($"\"{key}\":", StringComparison.Ordinal);
                if (idx < 0) return null;
                int valStart = json.IndexOf(':', idx) + 1;
                while (valStart < json.Length && char.IsWhiteSpace(json[valStart])) valStart++;
                int valEnd = valStart;
                while (valEnd < json.Length && "-+0123456789.eE".IndexOf(json[valEnd]) >= 0) valEnd++;
                if (valEnd == valStart) return null;
                return float.TryParse(json.Substring(valStart, valEnd - valStart), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                    ? value
                    : (float?)null;
            }
            catch { return null; }
        }

        private static string? ParseString(string json, string key)
        {
            try
            {
                int idx = json.IndexOf($"\"{key}\":");
                if (idx < 0) return null;
                int valStart = json.IndexOf('"', idx + key.Length + 3);
                if (valStart < 0) return null;
                valStart++;
                int valEnd = json.IndexOf('"', valStart);
                if (valEnd < 0) return null;
                return json.Substring(valStart, valEnd - valStart);
            }
            catch { return null; }
        }

        internal static bool TryLoadCornerDesignationKeyFromSettings(out KeyCode key)
        {
            key = KeyCode.K;

            try
            {
                string? settingsPath = ResolveSettingsPath(out bool isLegacySettingsPath);
                if (string.IsNullOrWhiteSpace(settingsPath) || !File.Exists(settingsPath))
                    return false;

                string json = File.ReadAllText(settingsPath);
                string? cornerKeyStr = ParseString(json, "cornerDesignationKey");
                if (string.IsNullOrWhiteSpace(cornerKeyStr))
                    return false;

                if (!System.Enum.TryParse(cornerKeyStr, true, out key))
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                s_log.Warning($"Failed to read legacy corner designation key: {ex.Message}");
                return false;
            }
        }

        /// <summary>Parses a string-backed enum setting from the JSON settings text.</summary>
        /// <typeparam name="TEnum">Enum type to parse.</typeparam>
        /// <param name="json">JSON settings text containing the optional enum setting.</param>
        /// <param name="key">JSON property name to read.</param>
        /// <returns>The parsed enum value when present and valid; otherwise, null.</returns>
        private static TEnum? ParseEnum<TEnum>(string json, string key) where TEnum : struct
        {
            string? value = ParseString(json, key);
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (Enum.TryParse(value, ignoreCase: true, out TEnum parsed))
                return parsed;

            return null;
        }

        // -----------------------------------------------------------------------
        // Settings serialisation helpers
        // -----------------------------------------------------------------------

        private static string FloatToJsonStr(float v)
            => v.ToString("G", CultureInfo.InvariantCulture);

        private static string BoolToJsonStr(bool v) => v ? "true" : "false";

        private static string FloatArrayToJson(float[] a)
        {
            var sb = new System.Text.StringBuilder("[");
            for (int i = 0; i < a.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(FloatToJsonStr(a[i]));
            }
            sb.Append("]");
            return sb.ToString();
        }

        private static string IntArrayToJson(int[] a)
        {
            var sb = new System.Text.StringBuilder("[");
            for (int i = 0; i < a.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(a[i]);
            }
            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// Serialises the current in-memory settings to a JSON string in the same
        /// format as ATDsettings.json, including all _comment_ documentation keys
        /// and a <c>settingsVersion</c> stamp.
        /// </summary>
        internal static string BuildSettingsJson()
        {
            string depthStr = AutoTerrainDesignationsMod.MaxDepthToDigTo.HasValue
                ? AutoTerrainDesignationsMod.MaxDepthToDigTo.Value.ToString(CultureInfo.InvariantCulture)
                : "null";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"settingsVersion\": \"{AutoTerrainDesignationsMod.ModVersion}\",");
            sb.AppendLine($"  \"settingsDefaultsRevision\": {SETTINGS_DEFAULTS_REVISION},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_diagnosticLevel\": \"Controls ATD diagnostic output. Default selects Debug in Debug builds and Info in Release builds. Warning keeps only warnings/errors; Info adds concise operational messages; Debug adds search summaries and timings; Trace adds full paths, plan tiles, successors, and handoffs. The atd_diagnostic_level command overrides this for the current session. Allowed: Default, Warning, Info, Debug, Trace.\",");
            sb.AppendLine($"  \"diagnosticLevel\": \"{AtdDiagnostics.ConfiguredLevel}\",");
            sb.AppendLine();
            sb.AppendLine("  \"_comment\": \"AutoTerrainDesignations settings. These values set the defaults loaded at game start. Most parameters below can also be changed per mine tower directly in-game via the tower inspector \u2014 this file is for your convenience so you don't have to adjust them every new save.\",");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_batchSize\": \"How many designations are placed per coroutine frame before yielding to the game while unpaused. Lower values keep the game more responsive during large scans; higher values complete scans faster. While paused, ATD does not batch-yield and finishes the placement pass in one coroutine step. Absolute max: 200. Default: 30.\",");
            sb.AppendLine($"  \"batchSize\": {s_batchSize},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_designationMode\": \"Default terrain designation mode for each mine tower. ResourceMining = resource-aware mining scan; Flattening = fill the tower area at maxDepthToDigTo. Default: ResourceMining.\",");
            sb.AppendLine($"  \"designationMode\": \"{AutoTerrainDesignationsMod.DesignationMode}\",");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_flatteningDesignationType\": \"Default designation type used when designationMode is Flattening. Valid values: Mining, Dumping, Leveling. Default: Mining.\",");
            sb.AppendLine($"  \"flatteningDesignationType\": \"{AutoTerrainDesignationsMod.FlatteningDesignationType}\",");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_maxSlopeHeightDiff\": \"Default starting value for the Max Slope setting on each mine tower. Controls the maximum allowed height difference between adjacent designation corners during slope smoothing. Lower values produce flatter designations; higher values allow steeper steps. Can be adjusted per tower in-game. Min 1, max 3. Default: 1.\",");
            sb.AppendLine($"  \"maxSlopeHeightDiff\": {AutoTerrainDesignationsMod.MaxHeightDiff},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_rampWidth\": \"Legacy/API-compatible numeric access ramp width. Values map to accessway modes when loaded: 0=OFF, 1=AUTO, 2=T3, and 3-5=legacy straight-only widths.\",");
            sb.AppendLine($"  \"rampWidth\": {AutoTerrainDesignationsMod.RampWidth},");
            sb.AppendLine("  \"_comment_vehicleClearance\": \"Default accessway mode: 0=OFF, 1=AUTO, 2=T1, 3=T2, 4=T3, 5=Legacy 3, 6=Legacy 4, 7=Legacy 5. Legacy modes generate straight ramps only.\",");
            sb.AppendLine($"  \"vehicleClearance\": {(int)AutoTerrainDesignationsMod.VehicleClearance},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_maxLayersToExcavate\": \"Default starting value for the Max Layers setting on each mine tower. Maximum number of terrain layers to excavate from the surface downward. 0 = no limit. Can be adjusted per tower in-game. Default: 30.\",");
            sb.AppendLine($"  \"maxLayersToExcavate\": {AutoTerrainDesignationsMod.MaxLayersToExcavate},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_maxDepthToDigTo\": \"Default starting value for the Elevation setting on each mine tower. In mining mode, this is the absolute minimum terrain elevation (in tiles) the designation will dig down to; null = no lower-bound limit. In flattening mode, this is the target elevation and must be set. Can be adjusted per tower in-game. Default: null.\",");
            sb.AppendLine($"  \"maxDepthToDigTo\": {depthStr},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_orePurityLevel\": \"Default starting value for the Ore Purity Level on each mine tower (0=Off, 1=Low, 2=Med, 3=High, 4=Max). Controls how aggressively poor-quality tiles and sparse ore are excluded. Can be adjusted per tower in-game. Default: 0.\",");
            sb.AppendLine($"  \"orePurityLevel\": {AutoTerrainDesignationsMod.OrePurityLevel},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_bottomFlatteningEnabled\": \"Whether to run the extra designation-bottom flattening pass before placing mining designations. Off purity uses lower-only flattening; other purity modes use leveling. Can also be changed at runtime with atd_set_bottom_flattening. Default: true.\",");
            sb.AppendLine($"  \"bottomFlatteningEnabled\": {BoolToJsonStr(AutoTerrainDesignationsMod.BottomFlatteningEnabled)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_bottomFlatteningStrength\": \"How aggressively the bottom-flattening pass levels the designation floor (1-10). Controls which depth-percentile of each connected ore component is chosen as the flattening target. 1 = mildest (90th-percentile depth, few tiles affected); 5 = moderate, median target (default); 10 = strongest (deepest tile, everything pulled down). Can also be changed at runtime with atd_set_bottom_flattening_strength. Default: 5.\",");
            sb.AppendLine($"  \"bottomFlatteningStrength\": {AutoTerrainDesignationsMod.BottomFlatteningStrength},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_minCorridorClearance\": \"Global default corridor clearance used when connecting separated ore components and enforcing passability. Each mine tower can override this individually via the inspector. 0 = disabled \u2014 components are left separate, no corridors or hole-filling (for vehicle-less excavation mods); 1 = 1-tile corridors (small and medium vehicles); 2 = 2-tile corridors (mega vehicles). Default: 2.\",");
            sb.AppendLine($"  \"minCorridorClearance\": {AutoTerrainDesignationsMod.MinCorridorClearance},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_terrainDesignationsPanelCollapsed\": \"Default collapsed state for the Mining designations panel when a mine tower inspector is created. false = expanded by default, true = collapsed by default. Default: false.\",");
            sb.AppendLine($"  \"terrainDesignationsPanelCollapsed\": {BoolToJsonStr(AutoTerrainDesignationsMod.TerrainDesignationsPanelCollapsed)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_oreCompositionPanelCollapsed\": \"Default collapsed state for the Ore composition panel when a mine tower inspector is created. false = expanded by default, true = collapsed by default. Default: false.\",");
            sb.AppendLine($"  \"oreCompositionPanelCollapsed\": {BoolToJsonStr(AutoTerrainDesignationsMod.OreCompositionPanelCollapsed)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_excavatorCompletionNotifications\": \"Whether ATD shows a green one-time notification when any vehicle depot completes an excavator. This can also be changed at runtime with atd_set_excavator_completion_notifications. Default: true.\",");
            sb.AppendLine($"  \"excavatorCompletionNotifications\": {BoolToJsonStr(AutoTerrainDesignationsMod.ExcavatorCompletionNotificationsEnabled)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_rampNotificationsEnabled\": \"Whether ATD shows ramp access warning notifications on mine towers (Failed, Truncated, NotAccessible). Disable to suppress ramp warning icons on all towers. This can also be changed at runtime with atd_set_ramp_notifications. Default: true.\",");
            sb.AppendLine($"  \"rampNotificationsEnabled\": {BoolToJsonStr(AutoTerrainDesignationsMod.RampNotificationsEnabled)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_farmingPanelCollapsed\": \"Default collapsed state for the Farming panel when a mine tower inspector is created. false = expanded by default, true = collapsed by default. Default: true.\",");
            sb.AppendLine($"  \"farmingPanelCollapsed\": {BoolToJsonStr(AutoTerrainDesignationsMod.FarmingPanelCollapsed)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_autoReleaseExcavatorsWhenIdle\": \"Default starting value for the Auto-release excavators when idle toggle on each mine tower. When enabled, ATD automatically unassigns excavators from the tower once no managed designation has pending excavation work, or while the tower is paused. Vehicles are tracked and re-assigned when excavation work returns. Can be toggled per tower in-game. Default: false.\",");
            sb.AppendLine($"  \"autoReleaseExcavatorsWhenIdle\": {BoolToJsonStr(AutoTerrainDesignationsMod.AutoReleaseExcavatorsWhenIdle)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_autoReleaseTrucksWhenIdle\": \"Default starting value for the Auto-release trucks when idle toggle on each mine tower. When enabled, ATD automatically unassigns trucks from the tower once no managed designation has pending excavation work, or while the tower is paused. Vehicles are tracked and re-assigned when excavation work returns. Can be toggled per tower in-game. Default: false.\",");
            sb.AppendLine($"  \"autoReleaseTrucksWhenIdle\": {BoolToJsonStr(AutoTerrainDesignationsMod.AutoReleaseTrucksWhenIdle)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_turningRampsExperimental\": \"When enabled, AUTO and T1-T3 may use routed turning or switchback accessways. Legacy 3-5 remain straight-only. Default: true.\",");
            sb.AppendLine($"  \"turningRampsExperimental\": {BoolToJsonStr(AutoTerrainDesignationsMod.TurningRampsExperimental)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_suppressLegacyAccessRamps\": \"Disable the legacy straight-ramp generator so experimental accessway results and failures can be tested directly. Leave false for normal fallback behavior. Default: false.\",");
            sb.AppendLine($"  \"suppressLegacyAccessRamps\": {BoolToJsonStr(AutoTerrainDesignationsMod.SuppressLegacyAccessRamps)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_experimentalAccessUseAStar\": \"Use paired-goal height-aware A* instead of reference Dijkstra for experimental access search. Set false for route and cost comparison. Default: true.\",");
            sb.AppendLine($"  \"experimentalAccessUseAStar\": {BoolToJsonStr(AutoTerrainDesignationsMod.ExperimentalAccessUseAStar)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_cursorOverlayEnabled\": \"Whether to show the bottom-left terrain cursor coordinates at game start. Coordinates are displayed as (x, y, z). The atd_cursor_overlay console command can still override this for the current session. Default: false.\",");
            sb.AppendLine($"  \"cursorOverlayEnabled\": {BoolToJsonStr(ShowCursorOverlay)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_experimentalAccessSearchOverlayEnabled\": \"Whether to show the fading explored-node frontier for the experimental access search. Debug-only and default: false. The atd_access_search_overlay console command can still override this for the current session.\",");
            sb.AppendLine($"  \"experimentalAccessSearchOverlayEnabled\": {BoolToJsonStr(ShowExperimentalAccessSearchOverlay)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_experimentalAccessPotentialOverlayEnabled\": \"Whether to show the persistent sparse P-field trace for V2 A*. The trace remains until replaced by a later search or cleared with the designation Clear button or atd_clear_diagnostic_overlays. Debug-only and default: false. The atd_access_potential_overlay console command can still override this for the current session.\",");
            sb.AppendLine($"  \"experimentalAccessPotentialOverlayEnabled\": {BoolToJsonStr(ShowExperimentalAccessPotentialOverlay)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_accessClusterOverlayEnabled\": \"Whether to show access-cluster identity, state, origin count, arithmetic center, and tied center roots at game start. Default: false. The atd_access_cluster_overlay console command can still override this for the current session.\",");
            sb.AppendLine($"  \"accessClusterOverlayEnabled\": {BoolToJsonStr(ShowAccessClusterOverlay)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_accessAvoidOcean\": \"New-game default for the per-world option that avoids ocean in accessways and Mining Designations. Mining cells directly overlapping ocean are excluded and projected underwater cutting is avoided. Default: true.\",");
            sb.AppendLine($"  \"accessAvoidOcean\": {BoolToJsonStr(AutoTerrainDesignationsMod.AccessAvoidOcean)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_accessAvoidBuildings\": \"New-game default for the per-world option that avoids building footprints and safety perimeters in accessways and Mining Designations. Default: true.\",");
            sb.AppendLine($"  \"accessAvoidBuildings\": {BoolToJsonStr(AutoTerrainDesignationsMod.AccessAvoidBuildings)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_allowRampsOutsideTowerAreas\": \"New-game default for Allow ramps outside tower areas. When enabled, experimental narrow and T3/Mega accessways retry within 16 tiles beyond the tower boundary only after the in-area search exhausts its available routes. Timeouts and other interrupted searches do not retry. The game may show its normal outside-area alarm. Default: true.\",");
            sb.AppendLine($"  \"allowRampsOutsideTowerAreas\": {BoolToJsonStr(AutoTerrainDesignationsMod.AllowRampsOutsideTowerAreas)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_accessHarvestDisruptedTrees\": \"New-game default for the per-world Harvest disrupted trees option. When enabled, finalized accessways and Mining Designations mark trees in their disturbance zones for harvest; when disabled, ATD creates no tree harvest orders. Default: true.\",");
            sb.AppendLine($"  \"accessHarvestDisruptedTrees\": {BoolToJsonStr(AutoTerrainDesignationsMod.AccessHarvestDisruptedTrees)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_accessAllowDigToRemoveDebris\": \"New-game default for the per-world Landscape to remove debris option. When disabled, prop-removal requests fail instead of changing terrain when a no-landscaping cleanup profile is unavailable. Default: true.\",");
            sb.AppendLine($"  \"accessAllowDigToRemoveDebris\": {BoolToJsonStr(AutoTerrainDesignationsMod.AccessAllowDigToRemoveDebris)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_accessQuickRemoveDebrisPolicy\": \"New-game default for Quick Remove accessway debris. 0 = Always, 1 = Restrictive, 2 = Never. Quick Remove spends Unity. Default: 1.\",");
            sb.AppendLine($"  \"accessQuickRemoveDebrisPolicy\": {(int)AutoTerrainDesignationsMod.AccessQuickRemoveDebrisPolicy},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_accessLandscapingCostDistanceScale\": \"Tile-distance cost assigned to one unit of landscaping cost in experimental access search. One landscaping-cost unit is equivalent to dumping or digging one unit of rock. Range: 0-100. Default: 1.\",");
            sb.AppendLine($"  \"accessLandscapingCostDistanceScale\": {FloatToJsonStr(AutoTerrainDesignationsMod.AccessLandscapingCostDistanceScale)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_accessPropCleanupLandscapingCost\": \"Landscaping cost charged once per prop cleanup origin used by experimental access search. One landscaping-cost unit is equivalent to dumping or digging one unit of rock. Default: 8, calibrated from observed excavator cleanup effort. Range: 0-100.\",");
            sb.AppendLine($"  \"accessPropCleanupLandscapingCost\": {FloatToJsonStr(AutoTerrainDesignationsMod.AccessPropCleanupLandscapingCost)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_accessLandslideRunPerHeight\": \"Horizontal exclusion distance per vertical terrain level for the experimental landslide hourglass. 1 = 45 degrees; higher values are wider and more conservative, lower values are narrower. Range: 0.05-2. Default: 1.\",");
            sb.AppendLine($"  \"accessLandslideRunPerHeight\": {FloatToJsonStr(AutoTerrainDesignationsMod.AccessLandslideRunPerHeight)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_accessGeneratedVFixedCost\": \"Fixed cost penalty added for generated V-turn or switchback vertices in experimental access search. Default: 1.\",");
            sb.AppendLine($"  \"accessGeneratedVFixedCost\": {FloatToJsonStr(AutoTerrainDesignationsMod.AccessGeneratedVFixedCost)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_accessDirectWorkWeight\": \"Cost weight factor applied to direct terrain work (dig/fill volume) in experimental access search. Default: 1.\",");
            sb.AppendLine($"  \"accessDirectWorkWeight\": {FloatToJsonStr(AutoTerrainDesignationsMod.AccessDirectWorkWeight)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_accessSideRayWeight\": \"Cost weight factor applied to side-ray clearance checking in experimental access search. Default: 1.\",");
            sb.AppendLine($"  \"accessSideRayWeight\": {FloatToJsonStr(AutoTerrainDesignationsMod.AccessSideRayWeight)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_safetyPolicyExpertValues\": \"Expert values behind the World settings Safety policy. Defaults by policy: MAX=[1.1,4], HIGH=[1.0,3], MED=[0.9,2], LOW=[0.85,1], MIN=[0.8,0]. Custom values are allowed and the UI displays the nearest policy.\",");
            sb.AppendLine($"  \"accessRaySlopeConservatism\": {FloatToJsonStr(AutoTerrainDesignationsMod.AccessRaySlopeConservatism)},");
            sb.AppendLine($"  \"accessRayEndBuffer\": {AutoTerrainDesignationsMod.AccessRayEndBuffer},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_accessCandidateRayMaxDistance\": \"Maximum search distance in tiles for clearance rays during accessway pathfinding. Default: 16.\",");
            sb.AppendLine($"  \"accessCandidateRayMaxDistance\": {AutoTerrainDesignationsMod.AccessCandidateRayMaxDistance},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_accessRayMaxCost\": \"Maximum path cost budget allowed for an accessway route candidate before aborting search. Default: 500.\",");
            sb.AppendLine($"  \"accessRayMaxCost\": {FloatToJsonStr(AutoTerrainDesignationsMod.AccessRayMaxCost)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_accessRayUnresolvedPenalty\": \"Cost penalty added when an accessway clearance ray cannot fully resolve terrain contact. Default: 200.\",");
            sb.AppendLine($"  \"accessRayUnresolvedPenalty\": {FloatToJsonStr(AutoTerrainDesignationsMod.AccessRayUnresolvedPenalty)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_accessMaxVisitedNodes\": \"Maximum node exploration limit for experimental access pathfinding search. Default: 250000.\",");
            sb.AppendLine($"  \"accessMaxVisitedNodes\": {AutoTerrainDesignationsMod.AccessMaxVisitedNodes},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_accessSearchTimeoutSeconds\": \"Maximum elapsed search timeout in seconds for background accessway pathfinding. Default: 60.\",");
            sb.AppendLine($"  \"accessSearchTimeoutSeconds\": {AutoTerrainDesignationsMod.AccessSearchTimeoutSeconds},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_accessSearchFrameBudgetMs\": \"Time budget in milliseconds per frame allocated to sliced background access search. Default: 30.\",");
            sb.AppendLine($"  \"accessSearchFrameBudgetMs\": {AutoTerrainDesignationsMod.AccessSearchFrameBudgetMs},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_accessManagerAutomatedFrameBudgetMs\": \"Managed farming and Construction Assist search budget per rendered frame during normal play. Default: 10.\",");
            sb.AppendLine($"  \"accessManagerAutomatedFrameBudgetMs\": {AutoTerrainDesignationsMod.AccessManagerAutomatedFrameBudgetMs},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_accessManagerInteractiveFrameBudgetMs\": \"Managed direct-interaction search budget per rendered frame during normal play. Reserved for interactive migration. Default: 15.\",");
            sb.AppendLine($"  \"accessManagerInteractiveFrameBudgetMs\": {AutoTerrainDesignationsMod.AccessManagerInteractiveFrameBudgetMs},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_accessManagerPausedMaxFrameBudgetMs\": \"Maximum managed access-search budget per rendered frame while paused. Default: 30.\",");
            sb.AppendLine($"  \"accessManagerPausedMaxFrameBudgetMs\": {AutoTerrainDesignationsMod.AccessManagerPausedMaxFrameBudgetMs},");
            sb.AppendLine();
            sb.AppendLine("  \"purityLevels\": {");
            sb.AppendLine("    \"_comment\": \"Thresholds applied at each Ore Purity Level. Arrays have 5 entries: [Off, Low, Med, High, Max]. Off (index 0) should always be 0 / no filtering. These define what each level means \u2014 edit if you want to retune the purity steps.\",");
            sb.AppendLine();
            sb.AppendLine("    \"_comment_minOreHeightByLevel\": \"Minimum ore thickness (in terrain tiles) a tile must contain to be included in the designation. Tiles below this threshold are excluded entirely. Default: [0.0, 0.25, 0.75, 2.0, 3.0].\",");
            sb.AppendLine($"    \"minOreHeightByLevel\": {FloatArrayToJson(s_minOreHeightByLevel)},");
            sb.AppendLine();
            sb.AppendLine("    \"_comment_minBottomOreDensityByLevel\": \"Minimum ore density (0.0-1.0) a depth zone must have to be excavated. For each ore interval below the first, the zone from the previous ore's bottom to this ore's bottom is evaluated: density = ore_thickness / zone_thickness. If density falls below this threshold, digging stops there. Default: [0.0, 0.05, 0.20, 0.50, 0.75].\",");
            sb.AppendLine($"    \"minBottomOreDensityByLevel\": {FloatArrayToJson(s_minBottomOreDensityByLevel)},");
            sb.AppendLine();
            sb.AppendLine("    \"_comment_minOrePurityRatioByLevel\": \"Minimum ratio of ore thickness to total column thickness (0.0-1.0). Tiles where ore makes up less than this fraction of the full terrain column (down to bedrock) are excluded as too contaminated with overburden. Default: [0.0, 0.05, 0.20, 0.50, 0.75].\",");
            sb.AppendLine($"    \"minOrePurityRatioByLevel\": {FloatArrayToJson(s_minOrePurityByLevel)},");
            sb.AppendLine();
            sb.AppendLine("    \"_comment_minComponentSizeByLevel\": \"Minimum number of connected designation tiles a cluster must have to survive the isolation filter. Smaller clusters are pruned as insignificant. Default: [0, 2, 6, 20, 40].\",");
            sb.AppendLine($"    \"minComponentSizeByLevel\": {IntArrayToJson(s_minComponentSizeByLevel)}");
            sb.AppendLine("  }");
            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// The file path where settings will be saved.  Returns the path the settings were
        /// loaded from (or previously generated to), or falls back to
        /// <c>ATDsettings.json</c> in the mod root directory.
        /// </summary>
        internal static string? SavedSettingsPath
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(s_loadedSettingsPath))
                    return s_loadedSettingsPath;
                if (!string.IsNullOrWhiteSpace(s_modRootDirectoryPath))
                    return Path.Combine(s_modRootDirectoryPath, SETTINGS_FILE_NAME);
                return null;
            }
        }

        /// <summary>
        /// Serialises current in-memory settings to <see cref="SavedSettingsPath"/> and
        /// updates <c>s_loadedSettingsPath</c> on success.
        /// </summary>
        /// <param name="savedPath">Receives the path written on success, or <see cref="string.Empty"/> on failure.</param>
        /// <returns><c>true</c> if the file was written successfully.</returns>
        internal static bool TrySaveSettings(out string savedPath)
        {
            string? target = SavedSettingsPath;
            if (target == null || target.Trim().Length == 0)
            {
                savedPath = string.Empty;
                s_log.Warning("Cannot save ATDsettings.json: mod root path is unknown.");
                return false;
            }
            string targetPath = target;

            try
            {
                File.WriteAllText(targetPath, BuildSettingsJson());
                s_loadedSettingsPath = targetPath;
                savedPath = targetPath;
                return true;
            }
            catch (Exception ex)
            {
                savedPath = string.Empty;
                s_log.Warning($"Failed to save ATDsettings.json: {ex.Message}");
                return false;
            }
        }
    }
}
