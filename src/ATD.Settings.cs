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
                string? fileVersion = ParseSettingsJson(json, isLegacySettingsPath);
                s_loadedSettingsPath = isLegacySettingsPath
                    ? Path.Combine(Path.GetDirectoryName(settingsPath) ?? string.Empty, SETTINGS_FILE_NAME)
                    : settingsPath;

                // If the file predates the current version, or was read from the old
                // settings.json name, rewrite it to the current documented ATDsettings.json
                // format while preserving user values.
                if (isLegacySettingsPath || fileVersion != AutoTerrainDesignationsMod.ModVersion)
                {
                    if (TrySaveSettings(out string migratedPath))
                    {
                        string source = isLegacySettingsPath ? "legacy settings.json" : "ATDsettings.json";
                        s_log.Warning($"{source} migrated to version {AutoTerrainDesignationsMod.ModVersion}: {migratedPath}");
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
            bool migrateGeneratedDefaults = forceMigration || parsedVersion != AutoTerrainDesignationsMod.ModVersion;
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
                int? batchSize = ParseInt(json, "batchSize");
                if (batchSize.HasValue && ShouldPreserveInt(batchSize.Value, migrateGeneratedDefaults, BATCH_SIZE))
                    s_batchSize = ClampBatchSize(batchSize.Value);

                int? slopeDefault = ParseInt(json, "maxSlopeHeightDiff");
                if (slopeDefault.HasValue && ShouldPreserveInt(slopeDefault.Value, migrateGeneratedDefaults, 1))
                    AutoTerrainDesignationsMod.SetMaxHeightDiff(slopeDefault.Value);

                int? rampWidth = ParseInt(json, "rampWidth");
                if (rampWidth.HasValue && ShouldPreserveInt(rampWidth.Value, migrateGeneratedDefaults, 2))
                    AutoTerrainDesignationsMod.SetRampWidth(rampWidth.Value);

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

                int? corridorClearance = ParseInt(json, "minCorridorClearance");
                if (corridorClearance.HasValue && ShouldPreserveInt(corridorClearance.Value, migrateGeneratedDefaults, 2))
                    AutoTerrainDesignationsMod.SetMinCorridorClearance(corridorClearance.Value);

                bool? terrainDesignationsPanelCollapsed = ParseBool(json, "terrainDesignationsPanelCollapsed");
                if (terrainDesignationsPanelCollapsed.HasValue && ShouldPreserveBool(terrainDesignationsPanelCollapsed.Value, migrateGeneratedDefaults, false))
                    AutoTerrainDesignationsMod.SetTerrainDesignationsPanelCollapsed(terrainDesignationsPanelCollapsed.Value);

                bool? oreCompositionPanelCollapsed = ParseBool(json, "oreCompositionPanelCollapsed");
                if (oreCompositionPanelCollapsed.HasValue && ShouldPreserveBool(oreCompositionPanelCollapsed.Value, migrateGeneratedDefaults, false))
                    AutoTerrainDesignationsMod.SetOreCompositionPanelCollapsed(oreCompositionPanelCollapsed.Value);

                bool? reEnableFarmingOnLoad = ParseBool(json, "reEnableFarmingOnLoad");
                if (reEnableFarmingOnLoad.HasValue && ShouldPreserveBool(reEnableFarmingOnLoad.Value, migrateGeneratedDefaults, true))
                    AutoTerrainDesignationsMod.SetReEnableFarmingOnLoad(reEnableFarmingOnLoad.Value);

                bool? excavatorCompletionNotifications = ParseBool(json, "excavatorCompletionNotifications");
                if (excavatorCompletionNotifications.HasValue && ShouldPreserveBool(excavatorCompletionNotifications.Value, migrateGeneratedDefaults, true))
                    AutoTerrainDesignationsMod.SetExcavatorCompletionNotificationsEnabled(excavatorCompletionNotifications.Value);
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
            sb.AppendLine();
            sb.AppendLine("  \"_comment\": \"AutoTerrainDesignations settings. These values set the defaults loaded at game start. Most parameters below can also be changed per mine tower directly in-game via the tower inspector \u2014 this file is for your convenience so you don't have to adjust them every new save.\",");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_batchSize\": \"How many designations are placed per coroutine frame before yielding to the game while unpaused. Lower values keep the game more responsive during large scans; higher values complete scans faster. While paused, ATD does not batch-yield and finishes the placement pass in one coroutine step. Absolute max: 200. Default: 30.\",");
            sb.AppendLine($"  \"batchSize\": {s_batchSize},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_maxSlopeHeightDiff\": \"Default starting value for the Max Slope setting on each mine tower. Controls the maximum allowed height difference between adjacent designation corners during slope smoothing. Lower values produce flatter designations; higher values allow steeper steps. Can be adjusted per tower in-game. Min 1, max 3. Default: 1.\",");
            sb.AppendLine($"  \"maxSlopeHeightDiff\": {AutoTerrainDesignationsMod.MaxHeightDiff},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_rampWidth\": \"Default starting value for the Ramp Width setting on each mine tower. Width of access ramps generated at the edge of designations, in tiles. 0 disables ramp generation entirely. Can be adjusted per tower in-game. Allowed range: 0-5. Default: 2.\",");
            sb.AppendLine($"  \"rampWidth\": {AutoTerrainDesignationsMod.RampWidth},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_maxLayersToExcavate\": \"Default starting value for the Max Layers setting on each mine tower. Maximum number of terrain layers to excavate from the surface downward. 0 = no limit. Can be adjusted per tower in-game. Default: 30.\",");
            sb.AppendLine($"  \"maxLayersToExcavate\": {AutoTerrainDesignationsMod.MaxLayersToExcavate},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_maxDepthToDigTo\": \"Default starting value for the Max Depth setting on each mine tower. Absolute minimum terrain elevation (in tiles) the designation will dig down to. null = no lower-bound limit. Can be adjusted per tower in-game. Default: null.\",");
            sb.AppendLine($"  \"maxDepthToDigTo\": {depthStr},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_orePurityLevel\": \"Default starting value for the Ore Purity Level on each mine tower (0=Off, 1=Low, 2=Med, 3=High, 4=Max). Controls how aggressively poor-quality tiles and sparse ore are excluded. Can be adjusted per tower in-game. Default: 0.\",");
            sb.AppendLine($"  \"orePurityLevel\": {AutoTerrainDesignationsMod.OrePurityLevel},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_bottomFlatteningEnabled\": \"Whether to run the extra designation-bottom flattening pass before placing mining designations. Off purity uses lower-only flattening; other purity modes use conservative leveling. Can also be changed at runtime with atd_set_bottom_flattening. Default: true.\",");
            sb.AppendLine($"  \"bottomFlatteningEnabled\": {BoolToJsonStr(AutoTerrainDesignationsMod.BottomFlatteningEnabled)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_minCorridorClearance\": \"Global default corridor clearance used when connecting separated ore components and enforcing passability. Each mine tower can override this individually via the inspector. 0 = disabled \u2014 components are left separate, no corridors or hole-filling (for vehicle-less excavation mods); 1 = 1-tile corridors (small and medium vehicles); 2 = 2-tile corridors (mega vehicles). Default: 2.\",");
            sb.AppendLine($"  \"minCorridorClearance\": {AutoTerrainDesignationsMod.MinCorridorClearance},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_terrainDesignationsPanelCollapsed\": \"Default collapsed state for the Terrain Designations panel when a mine tower inspector is created. false = expanded by default, true = collapsed by default. Default: false.\",");
            sb.AppendLine($"  \"terrainDesignationsPanelCollapsed\": {BoolToJsonStr(AutoTerrainDesignationsMod.TerrainDesignationsPanelCollapsed)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_oreCompositionPanelCollapsed\": \"Default collapsed state for the Ore Composition panel when a mine tower inspector is created. false = expanded by default, true = collapsed by default. Default: false.\",");
            sb.AppendLine($"  \"oreCompositionPanelCollapsed\": {BoolToJsonStr(AutoTerrainDesignationsMod.OreCompositionPanelCollapsed)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_reEnableFarmingOnLoad\": \"Whether ATD re-enables farming automation after loading a save for mine towers whose managed designations appear to be farmland work: non-empty, all flat level designations. This can also be changed at runtime with atd_set_re_enable_farming_on_load. Default: true.\",");
            sb.AppendLine($"  \"reEnableFarmingOnLoad\": {BoolToJsonStr(AutoTerrainDesignationsMod.ReEnableFarmingOnLoad)},");
            sb.AppendLine();
            sb.AppendLine("  \"_comment_excavatorCompletionNotifications\": \"Whether ATD shows a green one-time notification when any vehicle depot completes an excavator. This can also be changed at runtime with atd_set_excavator_completion_notifications. Default: true.\",");
            sb.AppendLine($"  \"excavatorCompletionNotifications\": {BoolToJsonStr(AutoTerrainDesignationsMod.ExcavatorCompletionNotificationsEnabled)},");
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
