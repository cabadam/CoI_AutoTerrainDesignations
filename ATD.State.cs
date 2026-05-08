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
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Terrain.Props;
using Mafi.Core.Terrain.Resources;
using Mafi.Core.Vehicles.Excavators;
using Mafi.Core.World;
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
        private static TerrainPropsManager? s_terrainPropsManager;
        private static string? s_modRootDirectoryPath;

        private const int BATCH_SIZE = 30;
        private const int MAX_BATCH_SIZE = 200;
        private const int PAUSED_BATCH_MULTIPLIER = 4;
        private const int HULL_CONNECTION_WIDTH = 2;
        private static int s_batchSize = BATCH_SIZE;

        private sealed class ATDTowerSettings
        {
            public int MaxHeightDiff { get; private set; }
            public int RampWidth { get; private set; }
            public int MaxLayersToExcavate { get; private set; }
            public int? MaxDepthToDigTo { get; private set; }
            public int OrePurityLevel { get; private set; }
            public int CorridorClearance { get; private set; }

            public ATDTowerSettings(int maxHeightDiff, int rampWidth, int maxLayersToExcavate, int? maxDepthToDigTo, int orePurityLevel, int corridorClearance)
            {
                SetMaxHeightDiff(maxHeightDiff);
                SetRampWidth(rampWidth);
                SetMaxLayersToExcavate(maxLayersToExcavate);
                SetMaxDepthToDigTo(maxDepthToDigTo);
                SetOrePurityLevel(orePurityLevel);
                SetCorridorClearance(corridorClearance);
            }

            public static ATDTowerSettings FromGlobalDefaults() => new ATDTowerSettings(
                AutoTerrainDesignationsMod.MaxHeightDiff,
                AutoTerrainDesignationsMod.RampWidth,
                AutoTerrainDesignationsMod.MaxLayersToExcavate,
                AutoTerrainDesignationsMod.MaxDepthToDigTo,
                AutoTerrainDesignationsMod.OrePurityLevel,
                AutoTerrainDesignationsMod.MinCorridorClearance);

            public void SetMaxHeightDiff(int value) => MaxHeightDiff = Math.Max(1, Math.Min(3, value));

            public void SetRampWidth(int value) => RampWidth = Math.Max(0, Math.Min(5, value));

            public void SetMaxLayersToExcavate(int value) => MaxLayersToExcavate = Math.Max(0, value);

            public void SetMaxDepthToDigTo(int? value) => MaxDepthToDigTo = value;

            public void SetOrePurityLevel(int value) => OrePurityLevel = Math.Max(0, Math.Min(4, value));

            public void SetCorridorClearance(int value) => CorridorClearance = Math.Max(0, Math.Min(2, value));
        }

        private static readonly Tile2i[] s_cardinalDirections =
        {
            new Tile2i(4, 0),
            new Tile2i(-4, 0),
            new Tile2i(0, 4),
            new Tile2i(0, -4),
        };

        // Per-tower ore selection: tower -> selected ore (null = "Auto" = all ores)
        private static readonly Dictionary<IAreaManagingTower, ProductProto?> s_selectedOrePerTower = 
            new Dictionary<IAreaManagingTower, ProductProto?>();
        private static readonly Dictionary<EntityId, ATDTowerSettings> s_towerSettingsByEntityId =
            new Dictionary<EntityId, ATDTowerSettings>();
        private static readonly Dictionary<EntityId, LooseProductProto> s_excavatorPriorityByTowerEntityId =
            new Dictionary<EntityId, LooseProductProto>();
        private static bool s_startupTowerPrioritySyncCompleted;
        private static int s_startupTowerPrioritySyncAttempts;

        [System.Diagnostics.Conditional("DEBUG")]
        private static void LogDebug(string message)
        {
            Log.Info(message);
        }

        internal static ProductProto? GetSelectedOre(IAreaManagingTower tower)
        {
            if (tower == null) return null;
            return s_selectedOrePerTower.TryGetValue(tower, out var ore) ? ore : null;
        }

        internal static void SetSelectedOre(IAreaManagingTower tower, ProductProto? ore)
        {
            if (tower == null) return;
            if (ore == null)
                s_selectedOrePerTower.Remove(tower);
            else
                s_selectedOrePerTower[tower] = ore;
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

        public static void Initialize(
            ITerrainDesignationsManager desigManager,
            ProtosDb protosDb,
            IWorldMapManager worldMapManager,
            MonoBehaviour coroutineHost,
            IEntitiesManager entitiesManager,
            TerrainPropsManager terrainPropsManager)
        {
            // Load defaults after logging is initialized so diagnostics are visible.
            LoadSettingsFromJson();

            s_desigManager = desigManager as TerrainDesignationsManager;
            s_coroutineHost = coroutineHost;
            s_protosDb = protosDb;
            s_worldMapManager = worldMapManager as WorldMapManager;
            s_entitiesManager = entitiesManager;
            s_terrainPropsManager = terrainPropsManager;
            s_startupTowerPrioritySyncCompleted = false;
            s_startupTowerPrioritySyncAttempts = 0;

            if (protosDb.TryGetProto(new Proto.ID("MiningDesignator"), out TerrainDesignationProto proto))
                s_miningProto = proto;
            else
                UnityEngine.Debug.Log("AutoDepth: MiningDesignator proto not found");

            if (protosDb.TryGetProto(new Proto.ID("DumpingDesignator"), out TerrainDesignationProto dumpProto))
                s_dumpingProto = dumpProto;
            else
                Log.Warning("[ATD] DumpingDesignator proto not found");

            if (protosDb.TryGetProto(new Proto.ID("LevelDesignator"), out TerrainDesignationProto levelProto))
                s_levelingProto = levelProto;
            else
                Log.Warning("[ATD] LevelDesignator proto not found");

            if (protosDb.TryGetProto(new Proto.ID("Bedrock_Terrain"), out TerrainMaterialProto bedrockProto))
                s_bedrockTerrainMaterial = bedrockProto;
            else
                Log.Warning("[ATD] Bedrock terrain material not found");

            OreCompositionPanel.Initialize(s_desigManager, s_protosDb, s_bedrockTerrainMaterial);
            DesignationPanel.Initialize(s_protosDb);
        }

        public static void SetModRootDirectoryPath(string? modRootDirectoryPath)
        {
            s_modRootDirectoryPath = modRootDirectoryPath;
        }

        /// <summary>Returns true once Initialize has completed successfully.</summary>
        internal static bool IsInitialized => s_desigManager != null && s_coroutineHost != null;

    }
}
