// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;
using Mafi.Collections;
using Mafi.Core;
using Mafi.Core.Buildings.Mine;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.Entities;
using Mafi.Core.Input;
using Mafi.Core.Notifications;
using Mafi.Core.PathFinding;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Terrain.Props;
using Mafi.Core.Terrain.Resources;
using Mafi.Core.Vehicles.Excavators;
using Mafi.Core.Vehicles.Jobs;
using Mafi.Core.World;
using CoI.AutoHelpers.Logging;
using UnityEngine;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        private static TerrainDesignationsManager? s_desigManager;
        private static TerrainDesignationProto? s_miningProto;
        private static TerrainDesignationProto? s_dumpingProto;
        private static TerrainDesignationProto? s_levelingProto;
        private static TerrainMaterialProto? s_bedrockTerrainMaterial;
        private static MonoBehaviour? s_coroutineHost;
        private static ProtosDb? s_protosDb;
        private static WorldMapManager? s_worldMapManager;
        private static IEntitiesManager? s_entitiesManager;
        private static IInputScheduler? s_inputScheduler;
        private static ConfigSerializationContext? s_configSerializationContext;
        private static TerrainPropsManager? s_terrainPropsManager;
        private static IVehiclePathFindingManager? s_vehiclePathFindingManager;
        private static ParkAndWaitJobFactory? s_parkAndWaitJobFactory;
        private static VehiclePathFindingParams? s_excavatorPathFindingParams;
        private static string? s_modRootDirectoryPath;
        private static int s_worldGeneration;
        internal static readonly ModLogger s_log = new ModLogger("ATD");

        private const int BATCH_SIZE = 30;
        private const int MAX_BATCH_SIZE = 200;
        private const int HULL_CONNECTION_WIDTH = 2;
        private static int s_batchSize = BATCH_SIZE;

        private sealed class ATDTowerSettings
        {
            /// <summary>Automatic designation workflow selected for this tower.</summary>
            public DesignationMode DesignationMode { get; private set; }

            /// <summary>Designation proto type used when this tower is in flattening mode.</summary>
            public FlatteningDesignationType FlatteningDesignationType { get; private set; }
            public int MaxHeightDiff { get; private set; }
            public int RampWidth { get; private set; }
            public int MaxLayersToExcavate { get; private set; }
            public int? MaxDepthToDigTo { get; private set; }
            public int OrePurityLevel { get; private set; }
            public int CorridorClearance { get; private set; }
            public bool AutoReleaseExcavatorsWhenIdle { get; private set; }
            public bool AutoReleaseTrucksWhenIdle { get; private set; }

            /// <summary>Outcome of the most recent ramp generation attempt. Null = no scan run yet.</summary>
            public RampPlacementOutcome? LastRampOutcome { get; set; }

            /// <summary>Creates a per-tower settings snapshot.</summary>
            /// <param name="designationMode">Automatic designation workflow selected for the tower.</param>
            /// <param name="flatteningDesignationType">Designation type to use when the tower is in flattening mode.</param>
            /// <param name="maxHeightDiff">Maximum allowed corner height difference for generated mining designations.</param>
            /// <param name="rampWidth">Ramp width to use when generating access ramps.</param>
            /// <param name="maxLayersToExcavate">Maximum number of terrain layers to excavate from the surface.</param>
            /// <param name="maxDepthToDigTo">Absolute elevation limit or target elevation, depending on designation mode.</param>
            /// <param name="orePurityLevel">Ore purity threshold level used by resource mining.</param>
            /// <param name="corridorClearance">Minimum corridor clearance used when connecting resource mining regions.</param>
            public ATDTowerSettings(DesignationMode designationMode, FlatteningDesignationType flatteningDesignationType, int maxHeightDiff, int rampWidth, int maxLayersToExcavate, int? maxDepthToDigTo, int orePurityLevel, int corridorClearance, bool autoReleaseExcavatorsWhenIdle = false, bool autoReleaseTrucksWhenIdle = false)
            {
                SetDesignationMode(designationMode);
                SetFlatteningDesignationType(flatteningDesignationType);
                SetMaxHeightDiff(maxHeightDiff);
                SetRampWidth(rampWidth);
                SetMaxLayersToExcavate(maxLayersToExcavate);
                SetMaxDepthToDigTo(maxDepthToDigTo);
                SetOrePurityLevel(orePurityLevel);
                SetCorridorClearance(corridorClearance);
                SetAutoReleaseExcavatorsWhenIdle(autoReleaseExcavatorsWhenIdle);
                SetAutoReleaseTrucksWhenIdle(autoReleaseTrucksWhenIdle);
            }

            /// <summary>Creates tower settings from the current global defaults.</summary>
            /// <returns>A new per-tower settings snapshot initialized from the global default values.</returns>
            public static ATDTowerSettings FromGlobalDefaults() => new ATDTowerSettings(
                AutoTerrainDesignationsMod.DesignationMode,
                AutoTerrainDesignationsMod.FlatteningDesignationType,
                AutoTerrainDesignationsMod.MaxHeightDiff,
                AutoTerrainDesignationsMod.RampWidth,
                AutoTerrainDesignationsMod.MaxLayersToExcavate,
                AutoTerrainDesignationsMod.MaxDepthToDigTo,
                AutoTerrainDesignationsMod.OrePurityLevel,
                AutoTerrainDesignationsMod.MinCorridorClearance,
                AutoTerrainDesignationsMod.AutoReleaseExcavatorsWhenIdle,
                AutoTerrainDesignationsMod.AutoReleaseTrucksWhenIdle);

            /// <summary>Sets the tower designation workflow.</summary>
            /// <param name="value">Designation workflow to use for this tower.</param>
            public void SetDesignationMode(DesignationMode value) => DesignationMode = value;

            /// <summary>Sets the tower flattening-mode designation type.</summary>
            /// <param name="value">Designation type to place when this tower is in flattening mode.</param>
            public void SetFlatteningDesignationType(FlatteningDesignationType value) => FlatteningDesignationType = value;

            public void SetMaxHeightDiff(int value) => MaxHeightDiff = Math.Max(1, Math.Min(3, value));

            public void SetRampWidth(int value) => RampWidth = Math.Max(0, Math.Min(5, value));

            public void SetMaxLayersToExcavate(int value) => MaxLayersToExcavate = Math.Max(0, value);

            public void SetMaxDepthToDigTo(int? value) => MaxDepthToDigTo = value;

            public void SetOrePurityLevel(int value) => OrePurityLevel = Math.Max(0, Math.Min(4, value));

            public void SetCorridorClearance(int value) => CorridorClearance = Math.Max(0, Math.Min(2, value));

            public void SetAutoReleaseExcavatorsWhenIdle(bool value) => AutoReleaseExcavatorsWhenIdle = value;

            public void SetAutoReleaseTrucksWhenIdle(bool value) => AutoReleaseTrucksWhenIdle = value;

            public void SetAutoReleaseWhenIdle(bool value)
            {
                SetAutoReleaseExcavatorsWhenIdle(value);
                SetAutoReleaseTrucksWhenIdle(value);
            }

            public bool MatchesGlobalDefaults()
            {
                return MaxHeightDiff == AutoTerrainDesignationsMod.MaxHeightDiff
                    && RampWidth == AutoTerrainDesignationsMod.RampWidth
                    && MaxLayersToExcavate == AutoTerrainDesignationsMod.MaxLayersToExcavate
                    && MaxDepthToDigTo == AutoTerrainDesignationsMod.MaxDepthToDigTo
                    && OrePurityLevel == AutoTerrainDesignationsMod.OrePurityLevel
                    && CorridorClearance == AutoTerrainDesignationsMod.MinCorridorClearance
                    && AutoReleaseExcavatorsWhenIdle == AutoTerrainDesignationsMod.AutoReleaseExcavatorsWhenIdle
                    && AutoReleaseTrucksWhenIdle == AutoTerrainDesignationsMod.AutoReleaseTrucksWhenIdle;
            }
        }

        private static readonly Tile2i[] s_cardinalDirections =
        {
            new Tile2i(4, 0),
            new Tile2i(-4, 0),
            new Tile2i(0, 4),
            new Tile2i(0, -4),
        };

        // Per-tower ore selection: entityId -> selected ore (null = "Auto" = all ores)
        private static readonly Dictionary<EntityId, ProductProto?> s_selectedOrePerTower =
            new Dictionary<EntityId, ProductProto?>();
        private static readonly Dictionary<EntityId, ATDTowerSettings> s_towerSettingsByEntityId =
            new Dictionary<EntityId, ATDTowerSettings>();
        private static readonly Dictionary<EntityId, LooseProductProto> s_excavatorPriorityByTowerEntityId =
            new Dictionary<EntityId, LooseProductProto>();
        // Per-tower panel collapsed states
        private static readonly Dictionary<EntityId, bool> s_terrainPanelCollapsedByEntityId =
            new Dictionary<EntityId, bool>();
        private static readonly Dictionary<EntityId, bool> s_orePanelCollapsedByEntityId =
            new Dictionary<EntityId, bool>();
        private static readonly Dictionary<EntityId, bool> s_farmingPanelCollapsedByEntityId =
            new Dictionary<EntityId, bool>();
        private static bool s_startupTowerPrioritySyncCompleted;
        private static int s_startupTowerPrioritySyncAttempts;

        // Reserved for a future public diagnostics toggle. Keep command-scoped
        // tracing off by default without suppressing warnings or unrelated logs.
        private const bool CreateDesignationsVerboseLoggingEnabled = false;
        private static bool s_createDesignationsDebugContext;

        [System.Diagnostics.Conditional("DEBUG")]
        private static void LogDebug(string message)
        {
            if (s_createDesignationsDebugContext && !CreateDesignationsVerboseLoggingEnabled)
                return;

            s_log.Info(message);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private static void LogLegacyAccessDebug(string message)
        {
            if (Access.AccessDiagnostics.VerboseLoggingEnabled)
                s_log.Info(message);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private static void LogExperimentalAccessDebug(string message)
        {
            s_log.Info(message);
        }

        internal static ProductProto? GetSelectedOre(IAreaManagingTower tower)
        {
            if (tower == null) return null;
            if (!TryGetTowerEntityId(tower, out EntityId entityId)) return null;
            return s_selectedOrePerTower.TryGetValue(entityId, out var ore) ? ore : null;
        }

        internal static void SetSelectedOre(IAreaManagingTower tower, ProductProto? ore)
        {
            if (tower == null) return;
            if (!TryGetTowerEntityId(tower, out EntityId entityId)) return;
            if (ore == null)
                s_selectedOrePerTower.Remove(entityId);
            else
                s_selectedOrePerTower[entityId] = ore;
        }

        private static bool TryGetTowerEntityId(IAreaManagingTower tower, out EntityId entityId)
        {
            entityId = EntityId.Invalid;
            if (tower is IEntity entity && entity.Id.IsValid)
            {
                entityId = entity.Id;
                return true;
            }

            return false;
        }

        private static ATDTowerSettings GetOrCreateTowerSettings(IAreaManagingTower tower)
        {
            if (TryGetTowerEntityId(tower, out EntityId entityId))
            {
                if (!s_towerSettingsByEntityId.TryGetValue(entityId, out ATDTowerSettings settings))
                {
                    settings = ATDTowerSettings.FromGlobalDefaults();
                    s_towerSettingsByEntityId[entityId] = settings;
                }

                return settings;
            }

            return ATDTowerSettings.FromGlobalDefaults();
        }

        // --- Per-tower settings accessors (used by API) ---

        /// <summary>Gets the designation workflow configured for a tower.</summary>
        /// <param name="tower">Tower whose settings should be read.</param>
        /// <returns>The tower-specific designation workflow, or a default-backed value if no settings exist yet.</returns>
        internal static DesignationMode GetTowerDesignationMode(IAreaManagingTower tower) => GetOrCreateTowerSettings(tower).DesignationMode;

        /// <summary>Sets the designation workflow for a tower.</summary>
        /// <param name="tower">Tower whose settings should be updated.</param>
        /// <param name="value">Designation workflow to use for this tower.</param>
        internal static void SetTowerDesignationMode(IAreaManagingTower tower, DesignationMode value) => GetOrCreateTowerSettings(tower).SetDesignationMode(value);

        /// <summary>Gets the flattening-mode designation type configured for a tower.</summary>
        /// <param name="tower">Tower whose settings should be read.</param>
        /// <returns>The tower-specific flattening-mode designation type, or a default-backed value if no settings exist yet.</returns>
        internal static FlatteningDesignationType GetTowerFlatteningDesignationType(IAreaManagingTower tower) => GetOrCreateTowerSettings(tower).FlatteningDesignationType;

        /// <summary>Sets the flattening-mode designation type for a tower.</summary>
        /// <param name="tower">Tower whose settings should be updated.</param>
        /// <param name="value">Designation type to place when this tower is in flattening mode.</param>
        internal static void SetTowerFlatteningDesignationType(IAreaManagingTower tower, FlatteningDesignationType value) => GetOrCreateTowerSettings(tower).SetFlatteningDesignationType(value);

        internal static int GetTowerMaxHeightDiff(IAreaManagingTower tower) => GetOrCreateTowerSettings(tower).MaxHeightDiff;
        internal static void SetTowerMaxHeightDiff(IAreaManagingTower tower, int value) => GetOrCreateTowerSettings(tower).SetMaxHeightDiff(value);

        internal static int GetTowerRampWidth(IAreaManagingTower tower) => GetOrCreateTowerSettings(tower).RampWidth;
        internal static void SetTowerRampWidth(IAreaManagingTower tower, int value) => GetOrCreateTowerSettings(tower).SetRampWidth(value);

        internal static int GetTowerMaxLayersToExcavate(IAreaManagingTower tower) => GetOrCreateTowerSettings(tower).MaxLayersToExcavate;
        internal static void SetTowerMaxLayersToExcavate(IAreaManagingTower tower, int value) => GetOrCreateTowerSettings(tower).SetMaxLayersToExcavate(value);

        internal static int? GetTowerMaxDepthToDigTo(IAreaManagingTower tower) => GetOrCreateTowerSettings(tower).MaxDepthToDigTo;
        internal static void SetTowerMaxDepthToDigTo(IAreaManagingTower tower, int? value) => GetOrCreateTowerSettings(tower).SetMaxDepthToDigTo(value);

        internal static int GetTowerOrePurityLevel(IAreaManagingTower tower) => GetOrCreateTowerSettings(tower).OrePurityLevel;
        internal static void SetTowerOrePurityLevel(IAreaManagingTower tower, int value) => GetOrCreateTowerSettings(tower).SetOrePurityLevel(value);

        internal static int GetTowerCorridorClearance(IAreaManagingTower tower) => GetOrCreateTowerSettings(tower).CorridorClearance;
        internal static void SetTowerCorridorClearance(IAreaManagingTower tower, int value) => GetOrCreateTowerSettings(tower).SetCorridorClearance(value);

        internal static bool GetTowerAutoReleaseExcavatorsWhenIdle(IAreaManagingTower tower)
        {
            if (TryGetTowerEntityId(tower, out EntityId entityId) && s_towerSettingsByEntityId.TryGetValue(entityId, out ATDTowerSettings settings))
                return settings.AutoReleaseExcavatorsWhenIdle;
            return AutoTerrainDesignationsMod.AutoReleaseExcavatorsWhenIdle;
        }

        internal static void SetTowerAutoReleaseExcavatorsWhenIdle(IAreaManagingTower tower, bool value)
        {
            ATDTowerSettings settings = GetOrCreateTowerSettings(tower);
            settings.SetAutoReleaseExcavatorsWhenIdle(value);
            if (!value)
                TryRestoreIdleReleasedVehiclesForTower(tower, settings.AutoReleaseExcavatorsWhenIdle, settings.AutoReleaseTrucksWhenIdle);
        }

        internal static bool GetTowerAutoReleaseTrucksWhenIdle(IAreaManagingTower tower)
        {
            if (TryGetTowerEntityId(tower, out EntityId entityId) && s_towerSettingsByEntityId.TryGetValue(entityId, out ATDTowerSettings settings))
                return settings.AutoReleaseTrucksWhenIdle;
            return AutoTerrainDesignationsMod.AutoReleaseTrucksWhenIdle;
        }

        internal static void SetTowerAutoReleaseTrucksWhenIdle(IAreaManagingTower tower, bool value)
        {
            ATDTowerSettings settings = GetOrCreateTowerSettings(tower);
            settings.SetAutoReleaseTrucksWhenIdle(value);
            if (!value)
                TryRestoreIdleReleasedVehiclesForTower(tower, settings.AutoReleaseExcavatorsWhenIdle, settings.AutoReleaseTrucksWhenIdle);
        }

        internal static bool GetTowerAutoReleaseWhenIdle(IAreaManagingTower tower)
        {
            return GetTowerAutoReleaseExcavatorsWhenIdle(tower) || GetTowerAutoReleaseTrucksWhenIdle(tower);
        }

        internal static void SetTowerAutoReleaseWhenIdle(IAreaManagingTower tower, bool value)
        {
            ATDTowerSettings settings = GetOrCreateTowerSettings(tower);
            settings.SetAutoReleaseWhenIdle(value);
            if (!value)
                TryRestoreIdleReleasedVehiclesForTower(tower, settings.AutoReleaseExcavatorsWhenIdle, settings.AutoReleaseTrucksWhenIdle);
        }

        internal static RampPlacementOutcome? GetTowerLastRampOutcome(IAreaManagingTower tower) => GetOrCreateTowerSettings(tower).LastRampOutcome;

        internal static void SetTowerLastRampOutcome(IAreaManagingTower tower, RampPlacementOutcome outcome)
        {
            GetOrCreateTowerSettings(tower).LastRampOutcome = outcome;
            UpdateTowerRampWarningNotification(tower, outcome);
        }

        internal static void ClearTowerLastRampOutcome(IAreaManagingTower tower)
        {
            GetOrCreateTowerSettings(tower).LastRampOutcome = null;
            ClearTowerRampWarningNotification(tower);
        }

        internal static bool GetTowerTerrainPanelCollapsed(IAreaManagingTower tower)
        {
            if (TryGetTowerEntityId(tower, out EntityId id) && s_terrainPanelCollapsedByEntityId.TryGetValue(id, out bool v)) return v;
            return AutoTerrainDesignationsMod.TerrainDesignationsPanelCollapsed;
        }

        internal static void SetTowerTerrainPanelCollapsed(IAreaManagingTower tower, bool collapsed)
        {
            if (TryGetTowerEntityId(tower, out EntityId id))
                s_terrainPanelCollapsedByEntityId[id] = collapsed;
        }

        internal static bool GetTowerOreCompositionPanelCollapsed(IAreaManagingTower tower)
        {
            if (TryGetTowerEntityId(tower, out EntityId id) && s_orePanelCollapsedByEntityId.TryGetValue(id, out bool v)) return v;
            return AutoTerrainDesignationsMod.OreCompositionPanelCollapsed;
        }

        internal static void SetTowerOreCompositionPanelCollapsed(IAreaManagingTower tower, bool collapsed)
        {
            if (TryGetTowerEntityId(tower, out EntityId id))
                s_orePanelCollapsedByEntityId[id] = collapsed;
        }

        internal static bool GetTowerFarmingPanelCollapsed(IAreaManagingTower tower)
        {
            if (TryGetTowerEntityId(tower, out EntityId id) && s_farmingPanelCollapsedByEntityId.TryGetValue(id, out bool v)) return v;
            return AutoTerrainDesignationsMod.FarmingPanelCollapsed;
        }

        internal static void SetTowerFarmingPanelCollapsed(IAreaManagingTower tower, bool collapsed)
        {
            if (TryGetTowerEntityId(tower, out EntityId id))
                s_farmingPanelCollapsedByEntityId[id] = collapsed;
        }

        internal static string FormatPanelStateDebug()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[ATD] Panel collapsed dict contents:");
            sb.AppendLine($"  Globals: terrain={AutoTerrainDesignationsMod.TerrainDesignationsPanelCollapsed}, ore={AutoTerrainDesignationsMod.OreCompositionPanelCollapsed}, farming={AutoTerrainDesignationsMod.FarmingPanelCollapsed}");
            var allIds = new System.Collections.Generic.HashSet<EntityId>();
            foreach (var p in s_terrainPanelCollapsedByEntityId) allIds.Add(p.Key);
            foreach (var p in s_orePanelCollapsedByEntityId) allIds.Add(p.Key);
            foreach (var p in s_farmingPanelCollapsedByEntityId) allIds.Add(p.Key);
            if (allIds.Count == 0)
            {
                sb.Append("  (no per-tower panel state stored)");
            }
            else
            {
                foreach (EntityId id in allIds)
                {
                    string t = s_terrainPanelCollapsedByEntityId.TryGetValue(id, out bool tv) ? tv.ToString() : "-";
                    string o = s_orePanelCollapsedByEntityId.TryGetValue(id, out bool ov) ? ov.ToString() : "-";
                    string f = s_farmingPanelCollapsedByEntityId.TryGetValue(id, out bool fv) ? fv.ToString() : "-";
                    sb.AppendLine($"  entityId={id.Value}: terrain={t}, ore={o}, farming={f}");
                }
            }
            return sb.ToString();
        }

        internal static int CurrentWorldGeneration => s_worldGeneration;

        internal static bool IsWorldGenerationActive(int worldGeneration)
        {
            return worldGeneration == s_worldGeneration && s_desigManager != null;
        }

        internal static void ResetWorldRuntimeState()
        {
            s_worldGeneration++;

            s_desigManager = null;
            s_miningProto = null;
            s_dumpingProto = null;
            s_levelingProto = null;
            s_bedrockTerrainMaterial = null;
            s_coroutineHost = null;
            s_protosDb = null;
            s_worldMapManager = null;
            s_entitiesManager = null;
            s_terrainPropsManager = null;
            s_vehiclePathFindingManager = null;
            s_parkAndWaitJobFactory = null;
            s_excavatorPathFindingParams = null;
            s_inputScheduler = null;
            s_configSerializationContext = null;
            s_batchSize = BATCH_SIZE;

            s_selectedOrePerTower.Clear();
            s_towerSettingsByEntityId.Clear();
            s_excavatorPriorityByTowerEntityId.Clear();
            s_terrainPanelCollapsedByEntityId.Clear();
            s_orePanelCollapsedByEntityId.Clear();
            s_farmingPanelCollapsedByEntityId.Clear();
            s_startupTowerPrioritySyncCompleted = false;
            s_startupTowerPrioritySyncAttempts = 0;

            ResetTransientNotifications();
            ClearFarmingRuntimeState();
            ClearFarmPlacementAssistRuntimeState();
            ClearIdleVehicleReleaseState();
        }

        public static void Initialize(
            ITerrainDesignationsManager desigManager,
            ProtosDb protosDb,
            IWorldMapManager worldMapManager,
            MonoBehaviour coroutineHost,
            IEntitiesManager entitiesManager,
            TerrainPropsManager terrainPropsManager,
            IVehiclePathFindingManager? vehiclePathFindingManager = null,
            ParkAndWaitJobFactory? parkAndWaitJobFactory = null,
            INotificationsManager? notificationsManager = null,
            IInputScheduler? inputScheduler = null,
            ConfigSerializationContext? configSerializationContext = null)
        {
            ResetWorldRuntimeState();

            // Load defaults after logging is initialized so diagnostics are visible.
            LoadSettingsFromJson();

            s_desigManager = desigManager as TerrainDesignationsManager;
            s_coroutineHost = coroutineHost;
            s_protosDb = protosDb;
            s_worldMapManager = worldMapManager as WorldMapManager;
            s_entitiesManager = entitiesManager;
            s_terrainPropsManager = terrainPropsManager;
            s_vehiclePathFindingManager = vehiclePathFindingManager;
            s_parkAndWaitJobFactory = parkAndWaitJobFactory;
            s_inputScheduler = inputScheduler;
            s_configSerializationContext = configSerializationContext;
            s_excavatorPathFindingParams = FindExcavatorPathFindingParams(protosDb);

            if (protosDb.TryGetProto(new Proto.ID("MiningDesignator"), out TerrainDesignationProto proto))
                s_miningProto = proto;
            else
                s_log.Warning("MiningDesignator proto not found");

            if (protosDb.TryGetProto(new Proto.ID("DumpingDesignator"), out TerrainDesignationProto dumpProto))
                s_dumpingProto = dumpProto;
            else
                s_log.Warning("DumpingDesignator proto not found");

            if (protosDb.TryGetProto(new Proto.ID("LevelDesignator"), out TerrainDesignationProto levelProto))
                s_levelingProto = levelProto;
            else
                s_log.Warning("LevelDesignator proto not found");

            if (protosDb.TryGetProto(new Proto.ID("Bedrock_Terrain"), out TerrainMaterialProto bedrockProto))
                s_bedrockTerrainMaterial = bedrockProto;
            else
                s_log.Warning("Bedrock terrain material not found");

            InitializeTransientNotifications(notificationsManager, protosDb);

            OreCompositionPanel.Initialize(s_desigManager, s_protosDb, s_bedrockTerrainMaterial);
            DesignationPanel.Initialize(s_protosDb);
        }

        public static void SetModRootDirectoryPath(string? modRootDirectoryPath)
        {
            s_modRootDirectoryPath = modRootDirectoryPath;
        }

        /// <summary>Returns true once Initialize has completed successfully.</summary>
        internal static bool IsInitialized => s_desigManager != null && s_coroutineHost != null;

        private static VehiclePathFindingParams FindExcavatorPathFindingParams(ProtosDb protosDb)
        {
            foreach (ExcavatorProto proto in protosDb.All<ExcavatorProto>())
                return proto.PathFindingParams;
            return VehiclePathFindingParams.DEFAULT;
        }

    }
}
