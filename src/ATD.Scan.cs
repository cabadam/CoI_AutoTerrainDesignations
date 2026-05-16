// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
// Auto Terrain Designations - Designation Scanning and Resource Sampling
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mafi;
using Mafi.Collections;
using Mafi.Core.Buildings.Mine;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.Entities;
using Mafi.Core.PathFinding;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Terrain.Props;
using Mafi.Core.Terrain.Resources;
using Mafi.Core.Terrain.Trees;
using UnityEngine;
using AutoTerrainDesignations.Access;
using EntityId = Mafi.Core.EntityId;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        private static int s_latestCreateDesignationsRequestId;
        private static bool s_createDesignationsOperationActive;
        private static readonly Dictionary<EntityId, List<ATDPropRemovalRequestHandle>>
            s_manualDebrisRemovalRequestsByTower =
                new Dictionary<EntityId, List<ATDPropRemovalRequestHandle>>();
        private static readonly Dictionary<EntityId, List<ATDPropRemovalRequestHandle>>
            s_accesswayPropRemovalRequestsByTower =
                new Dictionary<EntityId, List<ATDPropRemovalRequestHandle>>();

        private static IEnumerator RunCreateDesignationsWithDebugGate(
            IEnumerator routine,
            int requestId)
        {
            try
            {
                while (requestId == s_latestCreateDesignationsRequestId)
                {
                    bool movedNext;
                    object? current = null;

                    // A resumed coroutine runs against a potentially refreshed pathability
                    // bitmap. Do not reuse reachability captured in an earlier frame.
                    InvalidateTowerReachabilityFlood();
                    s_createDesignationsDebugContext = true;
                    try
                    {
                        movedNext = routine.MoveNext();
                        if (movedNext)
                            current = routine.Current;
                    }
                    finally
                    {
                        s_createDesignationsDebugContext = false;
                    }

                    if (!movedNext)
                        yield break;

                    // Unity advances nested enumerators separately. Wrap them as well
                    // so cancellation and the debug gate cover the complete operation.
                    yield return current is IEnumerator nested
                        ? RunCreateDesignationsWithDebugGate(nested, requestId)
                        : current;
                }
            }
            finally
            {
                (routine as IDisposable)?.Dispose();
            }
        }

        private static void QueueCreateDesignations(
            IAreaManagingTower tower,
            object? panelKey)
        {
            int requestId = ++s_latestCreateDesignationsRequestId;
            if (s_createDesignationsOperationActive)
            {
                s_cancelExperimentalAccessSearch = true;
                LogDebug($"Create Designations request {requestId} supersedes the active operation.");
            }
            s_coroutineHost?.StartCoroutine(
                RunCreateDesignationsSingleFlight(tower, panelKey, requestId));
        }

        private static IEnumerator RunCreateDesignationsSingleFlight(
            IAreaManagingTower tower,
            object? panelKey,
            int requestId)
        {
            while (s_createDesignationsOperationActive)
            {
                if (requestId != s_latestCreateDesignationsRequestId)
                    yield break;
                yield return null;
            }

            if (requestId != s_latestCreateDesignationsRequestId)
                yield break;

            s_createDesignationsOperationActive = true;
            s_cancelExperimentalAccessSearch = false;
            LogDebug($"Create Designations request {requestId} started.");
            var towerSettings = GetOrCreateTowerSettings(tower);
            IEnumerator guarded = RunCreateDesignationsWithDebugGate(
                CreateDesignationsCoroutine(
                    tower, towerSettings.RampWidth > 0 && ShouldGenerateAccessways(tower), panelKey),
                requestId);
            try
            {
                while (guarded.MoveNext())
                    yield return guarded.Current;
            }
            finally
            {
                (guarded as IDisposable)?.Dispose();
                HideTerrainAnalysisProgressToast();
                s_createDesignationsOperationActive = false;
                if (requestId == s_latestCreateDesignationsRequestId)
                    s_cancelExperimentalAccessSearch = false;
                LogDebug($"Create Designations request {requestId} finished or was superseded.");
            }
        }

        private const float FLATTENING_HEIGHT_EPSILON = 0.05f;
        /// <summary>
        /// Dispatches the create-designations request to the selected tower workflow.
        /// </summary>
        /// <param name="tower">Tower whose managed area should receive designations.</param>
        /// <param name="generateRamps">Whether resource-mining mode should attempt to generate access ramps.</param>
        /// <param name="inspectorInstance">Optional inspector key used to refresh attached panels after designation creation.</param>
        /// <returns>Coroutine enumerator that performs designation creation over multiple frames.</returns>
        private static IEnumerator CreateDesignationsCoroutine(IAreaManagingTower tower, bool generateRamps, object? inspectorInstance = null)
        {
            var towerSettings = GetOrCreateTowerSettings(tower);
            switch (towerSettings.DesignationMode)
            {
                case DesignationMode.ResourceMining:
                    yield return CreateResourceMiningDesignationsCoroutine(tower, generateRamps, inspectorInstance);
                    break;
                case DesignationMode.Flattening:
                    yield return CreatFlatteningDesignationsCoroutine(tower, inspectorInstance);
                    break;
            }
        }

        /// <summary>
        /// Existing resource-aware mining workflow that scans products and follows deposit depth.
        /// </summary>
        /// <param name="tower">Tower whose managed area should be scanned for resources.</param>
        /// <param name="generateRamps">Whether to generate an access ramp after mining designations are placed.</param>
        /// <param name="inspectorInstance">Optional inspector key used to refresh attached panels after designation creation.</param>
        /// <returns>Coroutine enumerator that creates resource-aware mining designations over multiple frames.</returns>
        private static IEnumerator CreateResourceMiningDesignationsCoroutine(IAreaManagingTower tower, bool generateRamps, object? inspectorInstance = null)
        {
            if (s_desigManager == null || s_miningProto == null) yield break;

            var area = tower.Area;
            if (area.IsEmpty) yield break;

            var terrMgr = s_desigManager.TerrainManager;
            var towerSettings = GetOrCreateTowerSettings(tower);
            BuildBuildingOccupiedTiles(tower, forceRefresh: true);
            string miningPlanFingerprint = BuildMiningPlanFingerprint(tower, towerSettings);
            bool autoScan = GetSelectedOre(tower) == null;

            if (IsTowerMiningPlanCurrent(tower, miningPlanFingerprint))
            {
                LogDebug("Mining plan and terrain designations are unchanged; Create Designations is a no-op.");
                yield break;
            }

            if (autoScan && HasTerrainDesignationsInTowerArea(tower))
            {
                LogDebug("AUTO scanning found existing terrain designations; treating them as access-pathfinding goals.");
                var repairResult = new ExistingTerrainWorkAccessResult();
                IEnumerator repairRoutine =
                    RepairExistingTerrainWorkAccessCoroutine(
                        tower, terrMgr, towerSettings, generateRamps,
                        repairResult);
                while (repairRoutine.MoveNext())
                    yield return repairRoutine.Current;

                if (repairResult.InitialReachabilityEvaluated
                    && repairResult.AllClustersInitiallyConnected)
                {
                    LogExperimentalAccessDebug(
                        "[ATD Planned Tower Access] all existing terrain-work " +
                        "clusters were connected when the scan was requested; " +
                        "ghost goals are eligible");
                    var plannedTowerAccess = new PlannedTowerAccessResult();
                    IEnumerator plannedTowerRoutine =
                        TryConnectToPlannedMiningTowerGhostCoroutine(
                            tower, terrMgr, towerSettings, generateRamps,
                            plannedTowerAccess);
                    while (plannedTowerRoutine.MoveNext())
                        yield return plannedTowerRoutine.Current;
                }
                else
                {
                    LogExperimentalAccessDebug(
                        "[ATD Planned Tower Access] ghost goals skipped because " +
                        "existing terrain work was not fully connected to the " +
                        "active tower when the scan was requested");
                }
                MarkTowerMiningPlanCleanFromWorld(tower, towerSettings);
                if (inspectorInstance != null)
                {
                    OreCompositionPanel.ResetContent(inspectorInstance);
                    DesignationPanel.RefreshDisplays(inspectorInstance);
                }
                yield break;
            }

            if (autoScan)
            {
                var plannedTowerAccess = new PlannedTowerAccessResult();
                IEnumerator plannedTowerRoutine =
                    TryConnectToPlannedMiningTowerGhostCoroutine(
                        tower, terrMgr, towerSettings, generateRamps,
                        plannedTowerAccess);
                while (plannedTowerRoutine.MoveNext())
                    yield return plannedTowerRoutine.Current;
                if (plannedTowerAccess.MarkerFound)
                {
                    MarkTowerMiningPlanCleanFromWorld(tower, towerSettings);
                    if (inspectorInstance != null)
                    {
                        OreCompositionPanel.ResetContent(inspectorInstance);
                        DesignationPanel.RefreshDisplays(inspectorInstance);
                    }
                    yield break;
                }
            }

            var bbMin = TerrainDesignation.GetOrigin(area.BoundingBoxMin);
            var bbMax = TerrainDesignation.GetOrigin(area.BoundingBoxMax);
            List<LooseProductProto> scanProducts = GetCandidateScanProducts(tower);
            if (scanProducts.Count == 0)
            {
                yield return RepairExistingTerrainWorkAccessCoroutine(
                    tower, terrMgr, towerSettings, generateRamps);
                MarkTowerMiningPlanCleanFromWorld(tower, towerSettings);
                if (inspectorInstance != null)
                {
                    OreCompositionPanel.ResetContent(inspectorInstance);
                    DesignationPanel.RefreshDisplays(inspectorInstance);
                }
                yield break;
            }

            var productSet = HybridSet<LooseProductProto>.From(scanProducts);
            var tempResults = new Lyst<ProductResource>();

            int scanCount = 0;

            var productCounts = new Dictionary<LooseProductProto, int>();
            var resourceDetailsByTile = new Dictionary<Tile2i, List<ProductResource>>();
            int maxHeightDiff = towerSettings.MaxHeightDiff;
            int maxLayersToExcavate = towerSettings.MaxLayersToExcavate;
            int? maxDepthToDigTo = towerSettings.MaxDepthToDigTo;
            int purityLevel = towerSettings.OrePurityLevel;
            int corridorClearance = towerSettings.CorridorClearance;

            LogDebug(string.Format("Scanning mine area from {0} to {1} for ore depth...", bbMin, bbMax));

            for (int y = bbMin.Y; y < bbMax.Y; y += 4)
            {
                for (int x = bbMin.X; x < bbMax.X; x += 4)
                {
                    var coord = new Tile2i(x, y);
                    
                    // Sample every terrain tile inside the 4x4 designation tile so ore decisions
                    // do not miss interior pockets or contamination.
                    if (!TryGetResourcesFromAllTiles(coord, area, terrMgr, productSet, tempResults, out List<ProductResource> resourcesForTile))
                    {
                        LogDebug(string.Format("Skipping tile with cells outside area: {0}", coord));
                        continue;
                    }

                    if (resourcesForTile.Count == 0)
                    {
                        LogDebug(string.Format("Tile {0}: No resources found in sampled cells", coord));
                        continue;
                    }

                    try
                    {
                        HashSet<LooseProductProto> tileProducts = new HashSet<LooseProductProto>();

                        for (int i = 0; i < resourcesForTile.Count; i++)
                        {
                            ProductResource resource = resourcesForTile[i];
                            tileProducts.Add(resource.Product);
                        }

                        if (resourcesForTile.Count > 0)
                        {
                            resourceDetailsByTile[coord] = resourcesForTile;
                        }

                        foreach (LooseProductProto product in tileProducts)
                        {
                            if (productCounts.TryGetValue(product, out int existingCount))
                            {
                                productCounts[product] = existingCount + 1;
                            }
                            else
                            {
                                productCounts[product] = 1;
                            }
                        }
                    }
                    catch
                    {
                    }

                    scanCount++;
                    int effectiveBatchSize = GetEffectiveBatchSize();
                    if (scanCount % effectiveBatchSize == 0)
                        yield return null;
                }
            }

            List<LooseProductProto> targetProducts = scanProducts
                .Where(product => productCounts.ContainsKey(product))
                .ToList();

            if (targetProducts.Count == 0)
            {
                yield return RepairExistingTerrainWorkAccessCoroutine(
                    tower, terrMgr, towerSettings, generateRamps);
                MarkTowerMiningPlanCleanFromWorld(tower, towerSettings);
                yield break;
            }

            ProductProto? selectedProduct = GetSelectedOre(tower);
            var targetProductIds = BuildTargetProductIdSet(targetProducts);
            var maxOreDepths = new Dict<Tile2i, int>();

            float minBottomOreDensity = s_minBottomOreDensityByLevel[purityLevel];
            float minOrePurity    = s_minOrePurityByLevel[purityLevel];
            float minOreHeight    = s_minOreHeightByLevel[purityLevel];

            foreach (KeyValuePair<Tile2i, List<ProductResource>> kvp in resourceDetailsByTile)
            {
                float terrainH = GetMinSurfaceHeightInDesignatableTile(kvp.Key, terrMgr);

                // Criterion 3: contamination ratio — skip tiles where ore fraction is too low
                if (minOrePurity > 0f)
                {
                    float purityRatio = ComputeTilePurityRatio(kvp.Key, terrMgr, targetProductIds);
                    if (purityRatio < minOrePurity)
                    {
                        LogDebug(string.Format("Tile {0} rejected: purity {1:P0} < threshold {2:P0}", kvp.Key, purityRatio, minOrePurity));
                        continue;
                    }
                }

                // Criterion 2: ore height — skip tiles with too little ore (not just isolated)
                if (minOreHeight > 0f)
                {
                    float tileOreHeight = GetTargetProductAmount(kvp.Value, targetProductIds);
                    if (tileOreHeight < minOreHeight)
                    {
                        LogDebug(string.Format("Tile {0} rejected: ore height {1:F2} < threshold {2:F2}", kvp.Key, tileOreHeight, minOreHeight));
                        continue;
                    }
                }

                // Criterion 1: bottom density trim — stop at the deepest ore zone still meeting the min density threshold
                bool depthFound = minBottomOreDensity > 0f
                    ? TryGetPurityAdjustedDepth(kvp.Value, targetProductIds, terrainH, minBottomOreDensity, out int depthInt)
                    : TryGetDeepestResourceDepth(kvp.Value, targetProductIds, terrainH, out depthInt);

                if (depthFound)
                {
                    // Apply max-layers constraint (0 = unlimited)
                    if (maxLayersToExcavate > 0)
                        depthInt = Math.Max(depthInt, (int)terrainH - maxLayersToExcavate);

                    // Apply absolute min-elevation constraint
                    if (maxDepthToDigTo.HasValue)
                        depthInt = Math.Max(depthInt, maxDepthToDigTo.Value);

                    maxOreDepths[kvp.Key] = depthInt;
                }
            }

            if (maxOreDepths.Count == 0)
            {
                yield return RepairExistingTerrainWorkAccessCoroutine(
                    tower, terrMgr, towerSettings, generateRamps);
                MarkTowerMiningPlanCleanFromWorld(tower, towerSettings);
                yield break;
            }

            LogDebug(string.Format("Before filtering: {0} tiles in designations", maxOreDepths.Count));
            FilterIsolatedDesignations(maxOreDepths, targetProductIds, resourceDetailsByTile, purityLevel);

            if (maxOreDepths.Count == 0)
            {
                yield return RepairExistingTerrainWorkAccessCoroutine(
                    tower, terrMgr, towerSettings, generateRamps);
                MarkTowerMiningPlanCleanFromWorld(tower, towerSettings);
                yield break;
            }

            FillRectilinearHull(maxOreDepths, targetProductIds, resourceDetailsByTile, corridorClearance);
            if (AutoTerrainDesignationsMod.BottomFlatteningEnabled)
            {
                int flattenedBottomTiles = FlattenDesignationBottom(maxOreDepths, purityLevel, AutoTerrainDesignationsMod.BottomFlatteningStrength);
                if (flattenedBottomTiles > 0)
                {
                    LogDebug(string.Format(
                        "Flattened designation bottom with {0} tile adjustment(s) using {1} mode",
                        flattenedBottomTiles,
                        purityLevel <= 0 ? "lower-only" : "leveling"));
                }
            }

            int directProtectedRemoved = RemoveDirectlyProtectedMiningTiles(maxOreDepths, terrMgr);
            if (directProtectedRemoved > 0)
            {
                FilterIsolatedDesignations(maxOreDepths, targetProductIds, resourceDetailsByTile, purityLevel);
                LogDebug($"Mining safety removed {directProtectedRemoved} directly protected designation(s).");
            }

            if (maxOreDepths.Count == 0)
            {
                RemoveObsoleteGeneratedDesignationsForMiningPlan(tower, maxOreDepths);
                ClearGeneratedHarvestTreesForTower(tower);
                yield return RepairExistingTerrainWorkAccessCoroutine(tower, terrMgr, towerSettings, generateRamps);
                MarkTowerMiningPlanCleanFromWorld(tower, towerSettings);
                yield break;
            }

            LogDebug(string.Format("After filtering+connecting: {0} tiles in designations", maxOreDepths.Count));
            LogDebug("Selected product: " + selectedProduct?.Id);

            var maxOreDepthOverall = maxOreDepths.Values.Min();

            LogDebug(string.Format("Creating designations for {0} tiles with overall max depth {1}", maxOreDepths.Count, maxOreDepthOverall));

            var cornerHeights = BuildAndSmoothCornerHeights(maxOreDepths, maxHeightDiff, purityLevel <= 0);
            int rayProtectedRemoved = RemoveRayHazardMiningTiles(maxOreDepths, cornerHeights, terrMgr, tower);
            if (rayProtectedRemoved > 0)
            {
                FilterIsolatedDesignations(maxOreDepths, targetProductIds, resourceDetailsByTile, purityLevel);
                if (maxOreDepths.Count == 0)
                {
                    RemoveObsoleteGeneratedDesignationsForMiningPlan(tower, maxOreDepths);
                    ClearGeneratedHarvestTreesForTower(tower);
                    yield return RepairExistingTerrainWorkAccessCoroutine(tower, terrMgr, towerSettings, generateRamps);
                    MarkTowerMiningPlanCleanFromWorld(tower, towerSettings);
                    yield break;
                }
                cornerHeights = BuildAndSmoothCornerHeights(maxOreDepths, maxHeightDiff, purityLevel <= 0);
                LogDebug($"Mining safety removed {rayProtectedRemoved} designation(s) with protected exterior disturbance.");
            }

            // Safety settings can make the new plan smaller.  Existing ATD-owned
            // cells are not removed by AddOrReplaceDesignation, so discard only
            // origins that no longer belong to the recalculated plan.
            RemoveObsoleteGeneratedDesignationsForMiningPlan(tower, maxOreDepths);
            ClearGeneratedHarvestTreesForTower(tower);
            HashSet<Tile2i> preexistingTerrainWorkOrigins =
                CollectExistingTerrainWorkEndpointOrigins(tower);
            List<Tile2i> recreatedAccesswayOrigins = maxOreDepths.Keys
                .Where(origin => s_lastClearedAccesswayOrigins.Contains(origin))
                .OrderBy(origin => origin.X)
                .ThenBy(origin => origin.Y)
                .ToList();
            LogExperimentalAccessDebug(
                $"[ATD Mining Plan Placement Audit] planned={maxOreDepths.Count} " +
                $"preexistingTerrainWork={preexistingTerrainWorkOrigins.Count} " +
                $"vehicleClearance={towerSettings.VehicleClearance} " +
                $"recreatedClearedAccessways={recreatedAccesswayOrigins.Count} " +
                $"origins=[{string.Join(",", recreatedAccesswayOrigins.Take(24).Select(
                    origin => $"({origin.X},{origin.Y})"))}]");

            int designCount = 0;
            foreach (var kvp in maxOreDepths)
            {
                var tile = kvp.Key;
                var nwCorner = tile;
                var neCorner = tile.AddX(4);
                var seCorner = tile.AddXy(4);
                var swCorner = tile.AddY(4);

                if (!cornerHeights.TryGetValue(nwCorner, out int hNW) ||
                    !cornerHeights.TryGetValue(neCorner, out int hNE) ||
                    !cornerHeights.TryGetValue(seCorner, out int hSE) ||
                    !cornerHeights.TryGetValue(swCorner, out int hSW))
                {
                    s_log.Warning(string.Format("Missing corner heights for tile {0}", tile));
                    continue;
                }

                var data = new DesignationData(tile,
                    new HeightTilesI(hNW), new HeightTilesI(hNE),
                    new HeightTilesI(hSE), new HeightTilesI(hSW));

                if (s_desigManager.AddOrReplaceDesignation(s_miningProto, data))
                {
                    RegisterGeneratedDesignationOrigin(tower, tile);
                }
                else
                {
                    s_log.Warning(string.Format("Failed to create designation for tile {0}", tile));
                }

                designCount++;
                int effectiveBatchSize = GetEffectiveBatchSize();
                if (designCount % effectiveBatchSize == 0)
                    yield return null;
            }

            LogDebug(string.Format("Created {0} designations", designCount));
            if (AccessHarvestDisruptedTrees)
                MarkMiningDisruptedTreesForHarvest(tower, maxOreDepths, terrMgr);

            if (generateRamps)
            {
                LogDebug("Creating access ramp...");
                var placedAccesswayOrigins = new List<Tile2i>();
                var rampResult = new RampGenerationResult();
                yield return CreateAccessRampCoroutine(
                    tower,
                    maxOreDepths,
                    cornerHeights,
                    terrMgr,
                    towerSettings.RampWidth,
                    s_miningProto,
                    placedAccesswayOrigins,
                    null,
                    useLocalSurfaceReference: false,
                    allowExistingPlannedRampShortcut: true,
                    result: rampResult);
                RampPlacementOutcome rampOutcome = rampResult.Outcome;
                SetTowerLastRampOutcome(
                    tower, rampOutcome,
                    rampResult.SuppressWarningNotification);

                var protectedAccesswayOrigins = new HashSet<Tile2i>(preexistingTerrainWorkOrigins);
                protectedAccesswayOrigins.UnionWith(placedAccesswayOrigins);
                RemoveFulfilledDesignationsForTower(tower, protectedAccesswayOrigins);
                CleanupIsolatedLeftoverDesignationsForTower(tower, maxOreDepths, protectedAccesswayOrigins);
            }
            else
            {
                LogDebug("Ramp generation is disabled in settings.");
                ClearTowerLastRampOutcome(tower);
                RemoveFulfilledDesignationsForTower(tower);
                CleanupIsolatedLeftoverDesignationsForTower(tower, maxOreDepths);
            }

            MarkTowerMiningPlanCleanFromWorld(tower, towerSettings);

            // Refresh ore composition panel and designation panel after creating designations
            if (inspectorInstance != null)
            {
                OreCompositionPanel.ResetContent(inspectorInstance);
                DesignationPanel.RefreshDisplays(inspectorInstance);
            }
        }

        private static int RemoveDirectlyProtectedMiningTiles(
            Dict<Tile2i, int> tileDepths, TerrainManager terrMgr)
        {
            if (!AccessAvoidOcean && !AccessAvoidBuildings)
                return 0;

            var rejected = new List<Tile2i>();
            foreach (Tile2i origin in tileDepths.Keys)
            {
                if (DesignationIntersectsProtectedMargin(origin, terrMgr))
                    rejected.Add(origin);
            }
            foreach (Tile2i origin in rejected)
                tileDepths.Remove(origin);
            return rejected.Count;
        }

        private static bool DesignationIntersectsProtectedMargin(
            Tile2i origin, TerrainManager terrMgr)
        {
            int landslideBuffer = AutoTerrainDesignationsMod.AccessRayEndBuffer;
            if (AccessAvoidOcean)
            {
                for (int y = -landslideBuffer; y <= 3 + landslideBuffer; y++)
                for (int x = -landslideBuffer; x <= 3 + landslideBuffer; x++)
                {
                    Tile2i tile = origin + new RelTile2i(x, y);
                    if (terrMgr.IsValidCoord(tile) && terrMgr.IsOcean(tile))
                        return true;
                }
            }

            if (AccessAvoidBuildings)
            {
                int buildingMargin = landslideBuffer + BuildingSafetyBufferTiles;
                for (int y = -buildingMargin; y <= 3 + buildingMargin; y++)
                for (int x = -buildingMargin; x <= 3 + buildingMargin; x++)
                    if (s_buildingOccupiedTiles.Contains(
                        origin + new RelTile2i(x, y)))
                        return true;
            }

            return false;
        }

        private static bool IsWithinBuildingSafetyFootprint(Tile2i tile)
        {
            int safetyRadius = BuildingSafetyBufferTiles;
            for (int y = tile.Y - safetyRadius; y <= tile.Y + safetyRadius; y++)
            for (int x = tile.X - safetyRadius; x <= tile.X + safetyRadius; x++)
                if (s_buildingOccupiedTiles.Contains(new Tile2i(x, y)))
                    return true;
            return false;
        }

        private static int RemoveRayHazardMiningTiles(
            Dict<Tile2i, int> tileDepths, Dict<Tile2i, int> corners,
            TerrainManager terrMgr, IAreaManagingTower tower)
        {
            if (!AccessAvoidOcean && !AccessAvoidBuildings)
                return 0;

            ResolveAccessMaterialSlopes(tower, out float dumpingSlope,
                out float fallbackMiningSlope, out _, out _, out _);
            var rejected = new List<Tile2i>();
            foreach (Tile2i origin in tileDepths.Keys)
            {
                bool exposedWest = !tileDepths.ContainsKey(origin + new RelTile2i(-4, 0));
                bool exposedEast = !tileDepths.ContainsKey(origin + new RelTile2i(4, 0));
                bool exposedNorth = !tileDepths.ContainsKey(origin + new RelTile2i(0, -4));
                bool exposedSouth = !tileDepths.ContainsKey(origin + new RelTile2i(0, 4));
                bool hazardous =
                    HasHazardOnBoundary(exposedWest, 0, 0, 0, 4, new Tile2i(-1, 0)) ||
                    HasHazardOnBoundary(exposedEast, 4, 0, 4, 4, new Tile2i(1, 0)) ||
                    HasHazardOnBoundary(exposedNorth, 0, 0, 4, 0, new Tile2i(0, -1)) ||
                    HasHazardOnBoundary(exposedSouth, 0, 4, 4, 4, new Tile2i(0, 1));
                if (hazardous)
                    rejected.Add(origin);

                bool HasHazardOnBoundary(bool exposed, int firstX, int firstY,
                    int lastX, int lastY, Tile2i direction)
                {
                    if (!exposed)
                        return false;
                    for (int step = 0; step <= 4; step++)
                    {
                        int localX = firstX + (lastX - firstX) * step / 4;
                        int localY = firstY + (lastY - firstY) * step / 4;
                        Tile2i sample = origin + new RelTile2i(localX, localY);
                        if (MiningRayHitsHazard(sample,
                            GetPlannedHeight(localX, localY), direction))
                            return true;
                    }
                    return false;
                }

                float GetPlannedHeight(int localX, int localY)
                {
                    if (!corners.TryGetValue(origin, out int nw)
                        || !corners.TryGetValue(origin.AddX(4), out int ne)
                        || !corners.TryGetValue(origin.AddXy(4), out int se)
                        || !corners.TryGetValue(origin.AddY(4), out int sw))
                        return terrMgr.GetHeight(origin + new RelTile2i(localX, localY)).Value.ToFloat();
                    return (nw * (4 - localX) * (4 - localY)
                        + ne * localX * (4 - localY)
                        + sw * (4 - localX) * localY
                        + se * localX * localY) / 16f;
                }

                bool MiningRayHitsHazard(Tile2i start, float plannedHeight, Tile2i direction)
                {
                    float rayHeight = plannedHeight;
                    float terrainHeight = terrMgr.GetHeight(start).Value.ToFloat();
                    AccessSideRayOperation operation = rayHeight < terrainHeight - 0.0001f
                        ? AccessSideRayOperation.Cut
                        : rayHeight > terrainHeight + 0.0001f
                            ? AccessSideRayOperation.Fill : AccessSideRayOperation.None;
                    if (operation == AccessSideRayOperation.None)
                        return false;
                    int maxDistance = direction.X != 0
                        ? (direction.X < 0 ? start.X : terrMgr.TerrainSize.X - 1 - start.X)
                        : (direction.Y < 0 ? start.Y : terrMgr.TerrainSize.Y - 1 - start.Y);
                    for (int distance = 1; distance <= maxDistance; distance++)
                    {
                        Tile2i tile = start + new RelTile2i(direction.X * distance, direction.Y * distance);
                        // Building rays are deliberately dense and resolve the material at every
                        // step. This avoids treating a mixed-material slope as one long ray.
                        float slope = dumpingSlope;
                        if (operation == AccessSideRayOperation.Cut)
                        {
                            AccessTerrainColumn column = CaptureAccessTerrainColumn(terrMgr, tile);
                            if (!column.TryGetNormalSlopeAt(rayHeight, out slope, out _))
                                slope = fallbackMiningSlope;
                        }
                        rayHeight += operation == AccessSideRayOperation.Cut ? slope : -slope;
                        float sampled = terrMgr.GetHeight(tile).Value.ToFloat();
                        if (AccessAvoidBuildings && IsWithinBuildingSafetyFootprint(tile))
                            return true;
                        if (AccessAvoidOcean && operation == AccessSideRayOperation.Cut
                            && terrMgr.IsOcean(tile) && rayHeight < 1f)
                            return true;
                        bool passedTerrain = operation == AccessSideRayOperation.Cut
                            ? sampled <= rayHeight : sampled >= rayHeight;
                        if (passedTerrain && (operation != AccessSideRayOperation.Cut || rayHeight >= 1f))
                        {
                            for (int tail = 1; tail <= AutoTerrainDesignationsMod.AccessRayEndBuffer; tail++)
                            {
                                Tile2i buffered = tile + new RelTile2i(direction.X * tail, direction.Y * tail);
                                if (!terrMgr.IsValidCoord(buffered))
                                    break;
                                if ((AccessAvoidOcean && terrMgr.IsOcean(buffered))
                                    || (AccessAvoidBuildings && IsWithinBuildingSafetyFootprint(buffered)))
                                    return true;
                            }
                            return false;
                        }
                    }
                    return false;
                }
            }
            foreach (Tile2i origin in rejected)
                tileDepths.Remove(origin);
            return rejected.Count;
        }

        private static void MarkMiningDisruptedTreesForHarvest(
            IAreaManagingTower tower, Dict<Tile2i, int> tileDepths, TerrainManager terrMgr)
        {
            if (s_treesManager == null || s_desigManager == null)
                return;
            var disturbed = new HashSet<Tile2i>();
            foreach (Tile2i origin in tileDepths.Keys)
                for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    disturbed.Add(origin + new RelTile2i(x, y));

            var finalizedDesignations = new Dictionary<Tile2i, TerrainDesignation>();
            foreach (Tile2i origin in tileDepths.Keys)
            {
                Option<TerrainDesignation> designation = s_desigManager.GetDesignationAt(origin);
                if (designation.HasValue)
                    finalizedDesignations[origin] = designation.Value;
            }

            ResolveAccessMaterialSlopes(tower, out float dumpingSlope,
                out float fallbackMiningSlope, out _, out _, out _);
            if (finalizedDesignations.Count > 0)
            {
                int margin = AutoTerrainDesignationsMod.AccessCandidateRayMaxDistance
                    + AutoTerrainDesignationsMod.AccessRayEndBuffer;
                int minX = Math.Max(0, tileDepths.Keys.Min(origin => origin.X) - margin);
                int minY = Math.Max(0, tileDepths.Keys.Min(origin => origin.Y) - margin);
                int maxX = Math.Min(terrMgr.TerrainSize.X - 1,
                    tileDepths.Keys.Max(origin => origin.X + 4) + margin);
                int maxY = Math.Min(terrMgr.TerrainSize.Y - 1,
                    tileDepths.Keys.Max(origin => origin.Y + 4) + margin);
                ProjectedDesignationDisturbance projection =
                    BuildProjectedDesignationDisturbedTiles(
                        finalizedDesignations,
                        terrMgr,
                        new Dictionary<Tile2i, float>(),
                        new Dictionary<Tile2i, AccessTerrainColumn>(),
                        new Tile2i(minX, minY),
                        new Tile2i(maxX, maxY),
                        Tile2i.Zero,
                        new Tile2i(terrMgr.TerrainSize.X - 1, terrMgr.TerrainSize.Y - 1),
                        dumpingSlope,
                        fallbackMiningSlope,
                        vehicleDisturbanceRadius: 0,
                        out string projectionFailure);
                if (!string.IsNullOrEmpty(projectionFailure))
                {
                    Log.Warning("[ATD] Mining tree-harvest projection failed: " + projectionFailure);
                }
                else
                {
                    disturbed.UnionWith(projection.CutTiles);
                    disturbed.UnionWith(projection.FillTiles);
                }

                // The shared projection traces each 4x4 edge from its corners. Trees are
                // point objects, however, and an excavated edge can disturb every intervening
                // world tile. Sweep all five samples on every exposed edge so harvesting is
                // conservative even on a tilted designation or a stepped mine boundary.
                foreach (KeyValuePair<Tile2i, TerrainDesignation> pair in finalizedDesignations)
                {
                    Tile2i origin = pair.Key;
                    AccessHeightProfile profile = ProfileFromDesignation(pair.Value);
                    if (!finalizedDesignations.ContainsKey(origin + new RelTile2i(-4, 0)))
                        for (int y = 0; y <= 4; y++)
                            AddBoundarySweep(origin + new RelTile2i(0, y),
                                profile.GetHeight2NumeratorAt(0, y) / 32f, new Tile2i(-1, 0));
                    if (!finalizedDesignations.ContainsKey(origin + new RelTile2i(4, 0)))
                        for (int y = 0; y <= 4; y++)
                            AddBoundarySweep(origin + new RelTile2i(4, y),
                                profile.GetHeight2NumeratorAt(4, y) / 32f, new Tile2i(1, 0));
                    if (!finalizedDesignations.ContainsKey(origin + new RelTile2i(0, -4)))
                        for (int x = 0; x <= 4; x++)
                            AddBoundarySweep(origin + new RelTile2i(x, 0),
                                profile.GetHeight2NumeratorAt(x, 0) / 32f, new Tile2i(0, -1));
                    if (!finalizedDesignations.ContainsKey(origin + new RelTile2i(0, 4)))
                        for (int x = 0; x <= 4; x++)
                            AddBoundarySweep(origin + new RelTile2i(x, 4),
                                profile.GetHeight2NumeratorAt(x, 4) / 32f, new Tile2i(0, 1));
                }
            }

            var addedTrees = new List<TreeId>();
            foreach (KeyValuePair<TreeId, TreeData> pair in s_treesManager.Trees)
            {
                Tile2i position = pair.Key.Position.AsFull;
                if (!disturbed.Contains(position) || s_treesManager.IsTreeSelected(pair.Key))
                    continue;
                s_treesManager.AddToHarvest(pair.Key);
                addedTrees.Add(pair.Key);
            }
            RegisterGeneratedHarvestTreePositions(tower, addedTrees);
            LogDebug($"Mining disrupted-tree harvest selections={addedTrees.Count} disturbedTiles={disturbed.Count}");

            void AddBoundarySweep(Tile2i start, float plannedHeight, Tile2i direction)
            {
                float terrainHeight = terrMgr.GetHeight(start).Value.ToFloat();
                AccessSideRayOperation operation = plannedHeight < terrainHeight - 0.0001f
                    ? AccessSideRayOperation.Cut
                    : plannedHeight > terrainHeight + 0.0001f
                        ? AccessSideRayOperation.Fill : AccessSideRayOperation.None;
                if (operation == AccessSideRayOperation.None)
                    return;

                float rayHeight = plannedHeight;
                int mapDistance = direction.X != 0
                    ? (direction.X < 0 ? start.X : terrMgr.TerrainSize.X - 1 - start.X)
                    : (direction.Y < 0 ? start.Y : terrMgr.TerrainSize.Y - 1 - start.Y);
                int maxDistance = Math.Min(
                    Math.Max(1, AutoTerrainDesignationsMod.AccessCandidateRayMaxDistance),
                    mapDistance);
                for (int distance = 1; distance <= maxDistance; distance++)
                {
                    Tile2i tile = start + new RelTile2i(direction.X * distance, direction.Y * distance);
                    float slope = dumpingSlope;
                    if (operation == AccessSideRayOperation.Cut)
                    {
                        AccessTerrainColumn column = CaptureAccessTerrainColumn(terrMgr, tile);
                        if (!column.TryGetNormalSlopeAt(rayHeight, out slope, out _))
                            slope = fallbackMiningSlope;
                    }
                    rayHeight += operation == AccessSideRayOperation.Cut ? slope : -slope;
                    disturbed.Add(tile);
                    float sampledHeight = terrMgr.GetHeight(tile).Value.ToFloat();
                    bool passedTerrain = operation == AccessSideRayOperation.Cut
                        ? sampledHeight <= rayHeight : sampledHeight >= rayHeight;
                    if (!passedTerrain || (operation == AccessSideRayOperation.Cut && rayHeight < 1f))
                        continue;
                    for (int tail = 1; tail <= AutoTerrainDesignationsMod.AccessRayEndBuffer; tail++)
                    {
                        Tile2i buffered = tile + new RelTile2i(direction.X * tail, direction.Y * tail);
                        if (terrMgr.IsValidCoord(buffered))
                            disturbed.Add(buffered);
                    }
                    return;
                }
            }
        }

        private static string BuildMiningPlanFingerprint(IAreaManagingTower tower, ATDTowerSettings towerSettings)
        {
            var area = tower.Area;
            string selectedOreId = GetSelectedOre(tower)?.Id.Value ?? "<none>";
            return string.Join("|",
                area.BoundingBoxMin.X,
                area.BoundingBoxMin.Y,
                area.BoundingBoxMax.X,
                area.BoundingBoxMax.Y,
                towerSettings.MaxHeightDiff,
                towerSettings.MaxLayersToExcavate,
                towerSettings.MaxDepthToDigTo?.ToString() ?? "<none>",
                towerSettings.OrePurityLevel,
                towerSettings.VehicleClearance,
                towerSettings.CorridorClearance,
                selectedOreId,
                AutoTerrainDesignationsMod.BottomFlatteningEnabled,
                AutoTerrainDesignationsMod.BottomFlatteningStrength,
                AccessAvoidOcean,
                AccessAvoidBuildings,
                AccessHarvestDisruptedTrees,
                AccessAllowDigToRemoveDebris,
                AccessQuickRemoveDebrisPolicy,
                AutoTerrainDesignationsMod.AccessRaySlopeConservatism,
                AutoTerrainDesignationsMod.AccessRayEndBuffer,
                AutoTerrainDesignationsMod.AccessCandidateRayMaxDistance,
                BuildPlannedTowerGhostFingerprint(tower),
                BuildTerrainDesignationFingerprint(tower),
                BuildMiningBuildingSafetyFingerprint());
        }

        private static string BuildTerrainDesignationFingerprint(
            IAreaManagingTower tower)
        {
            Dictionary<Tile2i, string> state =
                CaptureTerrainDesignationState(tower);
            return string.Join(";", state
                .OrderBy(pair => pair.Key.X)
                .ThenBy(pair => pair.Key.Y)
                .Select(pair =>
                    $"{pair.Key.X}:{pair.Key.Y}={pair.Value}"));
        }

        private static void MarkTowerMiningPlanCleanFromWorld(
            IAreaManagingTower tower,
            ATDTowerSettings towerSettings)
            => MarkTowerMiningPlanClean(
                tower, BuildMiningPlanFingerprint(tower, towerSettings));

        private static string BuildMiningBuildingSafetyFingerprint()
        {
            if (!AccessAvoidBuildings || s_buildingOccupiedTiles.Count == 0)
                return "<none>";

            // The building snapshot is refreshed before the mining fingerprint is
            // built. Sorting makes this stable despite HashSet enumeration order.
            return string.Join(",", s_buildingOccupiedTiles
                .OrderBy(tile => tile.X)
                .ThenBy(tile => tile.Y)
                .Select(tile => tile.X + ":" + tile.Y));
        }

        private static void RemoveObsoleteGeneratedDesignationsForMiningPlan(
            IAreaManagingTower tower,
            Dict<Tile2i, int> plannedOrigins)
        {
            if (s_desigManager == null)
                return;

            // Generated accessways share the designation registry. They are all
            // candidates for regeneration with the new mining plan, so preserve
            // their old locations for the placement audit and reset their separate
            // ownership registry along with any cells removed below.
            CaptureClearedAccesswayOrigins(tower);
            foreach (Tile2i origin in GetRegisteredGeneratedDesignationOrigins(tower))
            {
                if (plannedOrigins.ContainsKey(origin))
                    continue;

                if (s_desigManager.GetDesignationAt(origin).HasValue)
                    s_desigManager.RemoveDesignation(origin);
                UnregisterGeneratedDesignationOrigin(tower, origin);
            }
            ClearRegisteredGeneratedAccessways(tower);
        }

        private sealed class ExistingTerrainWorkAccessResult
        {
            public bool InitialReachabilityEvaluated;
            public bool AllClustersInitiallyConnected;
        }

        private static IEnumerator RepairExistingTerrainWorkAccessCoroutine(
            IAreaManagingTower tower,
            TerrainManager terrMgr,
            ATDTowerSettings towerSettings,
            bool generateRamps,
            ExistingTerrainWorkAccessResult? repairResult = null)
        {
            if (repairResult != null)
            {
                repairResult.InitialReachabilityEvaluated = false;
                repairResult.AllClustersInitiallyConnected = false;
            }
            string? skipReason = !generateRamps
                ? "accessway generation disabled"
                : !AutoTerrainDesignationsMod.TurningRampsExperimental
                    ? "experimental access disabled"
                    : towerSettings.RampWidth != 1 && towerSettings.RampWidth != 2
                        ? $"unsupported width {towerSettings.RampWidth}"
                        : s_miningProto == null
                            ? "mining prototype unavailable"
                            : null;
            if (skipReason != null)
            {
                LogExperimentalAccessDebug(
                    $"[ATD Experimental Access Repair] skipped reason={skipReason} " +
                    $"vehicleClearance={towerSettings.VehicleClearance} width={towerSettings.RampWidth}");
                yield break;
            }

            if (!HasExistingTerrainWorkEndpoint(tower))
            {
                LogExperimentalAccessDebug(
                    "[ATD Experimental Access Repair] skipped reason=no eligible external terrain-work endpoint " +
                    $"vehicleClearance={towerSettings.VehicleClearance} width={towerSettings.RampWidth}");
                yield break;
            }

            var emptyGeneratedPlan = new Dict<Tile2i, int>();
            var endpointCornerHeights = new Dict<Tile2i, int>();
            var placedAccesswayOrigins = new List<Tile2i>();
            var rampResult = new RampGenerationResult();
            yield return CreateAccessRampCoroutine(
                tower,
                emptyGeneratedPlan,
                endpointCornerHeights,
                terrMgr,
                towerSettings.RampWidth,
                s_miningProto,
                placedAccesswayOrigins,
                null,
                useLocalSurfaceReference: false,
                allowExistingPlannedRampShortcut: true,
                result: rampResult);
            if (repairResult != null)
            {
                repairResult.InitialReachabilityEvaluated =
                    rampResult.InitialReachabilityEvaluated;
                repairResult.AllClustersInitiallyConnected =
                    rampResult.AllClustersInitiallyConnected;
            }
            SetTowerLastRampOutcome(
                tower, rampResult.Outcome,
                rampResult.SuppressWarningNotification);
        }

        /// <summary>
        /// Creates full-area flattening designations at the tower's configured target elevation.
        /// </summary>
        /// <param name="tower">Tower whose managed area should be filled with flattening designations.</param>
        /// <param name="inspectorInstance">Optional inspector key used to refresh attached panels after designation creation.</param>
        /// <returns>Coroutine enumerator that creates flattening designations over multiple frames.</returns>
        private static IEnumerator CreatFlatteningDesignationsCoroutine(IAreaManagingTower tower, object? inspectorInstance = null)
        {
            if (s_desigManager == null)
            {
                yield break;
            }

            var area = tower.Area;
            if (area.IsEmpty)
            {
                yield break;
            }

            var terrMgr = s_desigManager.TerrainManager;
            var towerSettings = GetOrCreateTowerSettings(tower);
            int? targetElevation = towerSettings.MaxDepthToDigTo;
            if (!targetElevation.HasValue)
            {
                Log.Warning("[ATD] Flattening mode requires a configured elevation. Set Elevation limit first.");
                yield break;
            }

            TerrainDesignationProto? designationProto = GetFlatteningModeDesignationProto(towerSettings.FlatteningDesignationType);
            if (designationProto == null)
            {
                Log.Warning(string.Format(
                    "[ATD] Flattening mode unavailable: {0} proto was not initialized.",
                    FlatteningDesignationTypeText(towerSettings.FlatteningDesignationType)));
                yield break;
            }

            var bbMin = TerrainDesignation.GetOrigin(area.BoundingBoxMin);
            var bbMax = TerrainDesignation.GetOrigin(area.BoundingBoxMax);
            int target = targetElevation.Value;
            int designCount = 0;
            int skippedNoWorkCount = 0;

            LogDebug(string.Format(
                "Creating {0} flattening-mode designations from {1} to {2} at elevation {3}...",
                FlatteningDesignationTypeText(towerSettings.FlatteningDesignationType),
                bbMin,
                bbMax,
                target));

            for (int y = bbMin.Y; y < bbMax.Y; y += 4)
            {
                for (int x = bbMin.X; x < bbMax.X; x += 4)
                {
                    var tile = new Tile2i(x, y);
                    if (!IsDesignatableTileFullyInsideArea(area, tile))
                    {
                        continue;
                    }

                    if (!FlatteningDesignationWouldPerformWork(tile, terrMgr, target, towerSettings.FlatteningDesignationType))
                    {
                        skippedNoWorkCount++;
                        continue;
                    }

                    var data = new DesignationData(tile,
                        new HeightTilesI(target), new HeightTilesI(target),
                        new HeightTilesI(target), new HeightTilesI(target));

                    if (!s_desigManager.AddOrReplaceDesignation(designationProto, data))
                    {
                        Log.Warning(string.Format("Failed to create {0} flattening-mode designation for tile {1}", FlatteningDesignationTypeText(towerSettings.FlatteningDesignationType), tile));
                    }

                    designCount++;
                    int effectiveBatchSize = GetEffectiveBatchSize();
                    if (designCount % effectiveBatchSize == 0)
                        yield return null;
                }
            }

            LogDebug(string.Format("Created {0} {1} flattening-mode designations at elevation {2}; skipped {3} tile(s) with no work", designCount, FlatteningDesignationTypeText(towerSettings.FlatteningDesignationType), target, skippedNoWorkCount));
            ClearTowerLastRampOutcome(tower);

            if (inspectorInstance != null)
            {
                OreCompositionPanel.ResetContent(inspectorInstance);
                DesignationPanel.RefreshDisplays(inspectorInstance);
            }
        }

        /// <summary>Returns true when a flat target designation would change at least one cell in the 4x4 origin.</summary>
        /// <param name="tileOrigin">Designation origin to inspect.</param>
        /// <param name="terrMgr">Terrain manager used for live surface heights.</param>
        /// <param name="target">Flat target elevation.</param>
        /// <param name="flatteningDesignationType">Selected flattening designation type.</param>
        /// <returns>True when the selected designation type has work to perform; otherwise, false.</returns>
        private static bool FlatteningDesignationWouldPerformWork(
            Tile2i tileOrigin,
            TerrainManager terrMgr,
            int target,
            FlatteningDesignationType flatteningDesignationType)
        {
            GetSurfaceHeightRangeInDesignatableTile(tileOrigin, terrMgr, out float minSurface, out float maxSurface);
            switch (flatteningDesignationType)
            {
                case FlatteningDesignationType.Mining:
                    return maxSurface > target + FLATTENING_HEIGHT_EPSILON;
                case FlatteningDesignationType.Dumping:
                    return minSurface < target - FLATTENING_HEIGHT_EPSILON;
                case FlatteningDesignationType.Leveling:
                    return minSurface < target - FLATTENING_HEIGHT_EPSILON
                        || maxSurface > target + FLATTENING_HEIGHT_EPSILON;
                default:
                    return true;
            }
        }

        /// <summary>Resolves the terrain designation proto used by flattening mode.</summary>
        /// <param name="flatteningDesignationType">Flattening-mode designation type selected by settings.</param>
        /// <returns>The initialized terrain designation proto for the requested type, or null when unavailable.</returns>
        private static TerrainDesignationProto? GetFlatteningModeDesignationProto(FlatteningDesignationType flatteningDesignationType)
        {
            switch (flatteningDesignationType)
            {
                case FlatteningDesignationType.Mining: return s_miningProto;
                case FlatteningDesignationType.Dumping: return s_dumpingProto;
                case FlatteningDesignationType.Leveling: return s_levelingProto;
                default: return s_levelingProto;
            }
        }

        /// <summary>Formats a flattening-mode designation type for logs.</summary>
        /// <param name="flatteningDesignationType">Designation type to format.</param>
        /// <returns>Lowercase text suitable for diagnostic log messages.</returns>
        private static string FlatteningDesignationTypeText(FlatteningDesignationType flatteningDesignationType)
        {
            switch (flatteningDesignationType)
            {
                case FlatteningDesignationType.Mining: return "mining";
                case FlatteningDesignationType.Dumping: return "dumping";
                case FlatteningDesignationType.Leveling: return "leveling";
                default: return flatteningDesignationType.ToString();
            }
        }

        private const int RAMP_ACCESS_SEARCH_MARGIN_TILES = 48;
        private const int MAX_RAMP_ACCESS_SEARCH_TILES = 20000;
        private static readonly RelTile2i[] s_rampAccessSearchDirections =
        {
            new RelTile2i(1, 0),
            new RelTile2i(-1, 0),
            new RelTile2i(0, 1),
            new RelTile2i(0, -1)
        };
        private static readonly HashSet<Tile2i> s_towerReachabilityFloodVisited = new();
        private static readonly Queue<Tile2i> s_towerReachabilityFloodQueue = new();
        private static Tile2i s_towerReachabilityFloodBbMin;
        private static Tile2i s_towerReachabilityFloodBbMax;
        private static Tile2i s_towerReachabilityFloodTowerPosition;
        private static object? s_towerReachabilityFloodPathFindingParams;
        private static bool s_towerReachabilityFloodHasStart;
        private static bool s_towerReachabilityFloodValid;

        /// <summary>
        /// Invalidates the shared tower flood while retaining its allocated collections for reuse.
        /// Call whenever pathability may have changed or a new planning phase begins.
        /// </summary>
        private static void InvalidateTowerReachabilityFlood()
        {
            s_towerReachabilityFloodValid = false;
        }

        /// <summary>Clears shared tower-flood state during world teardown.</summary>
        private static void ClearTowerReachabilityFlood()
        {
            s_towerReachabilityFloodValid = false;
            s_towerReachabilityFloodHasStart = false;
            s_towerReachabilityFloodPathFindingParams = null;
            s_towerReachabilityFloodVisited.Clear();
            s_towerReachabilityFloodQueue.Clear();
        }

        /// <summary>
        /// Flushes pending pathability changes and invalidates reachability derived from the
        /// previous bitmap. Use this instead of calling UpdateChangedTiles directly.
        /// </summary>
        private static void RefreshPathabilityAndInvalidateReachability()
        {
            if (s_vehiclePathFindingManager != null)
            {
                try { s_vehiclePathFindingManager.PathabilityProvider.UpdateChangedTiles(); }
                catch { }
            }

            InvalidateTowerReachabilityFlood();
        }

        /// <summary>
        /// Builds one BFS flood from the tower for the base tower bounds. Subsequent access
        /// checks with targets inside those bounds are answered by set membership instead of
        /// repeating the same flood.
        /// </summary>
        private static void EnsureTowerReachabilityFlood(
            IPathabilityProvider pathabilityProvider,
            VehiclePathFindingParams pfParams,
            Tile2i towerPosition,
            Tile2i bbMin,
            Tile2i bbMax)
        {
            if (s_towerReachabilityFloodValid
                && s_towerReachabilityFloodBbMin == bbMin
                && s_towerReachabilityFloodBbMax == bbMax
                && s_towerReachabilityFloodTowerPosition == towerPosition
                && Equals(s_towerReachabilityFloodPathFindingParams, pfParams))
            {
                return;
            }

            s_towerReachabilityFloodValid = false;
            s_towerReachabilityFloodVisited.Clear();
            s_towerReachabilityFloodQueue.Clear();
            s_towerReachabilityFloodBbMin = bbMin;
            s_towerReachabilityFloodBbMax = bbMax;
            s_towerReachabilityFloodTowerPosition = towerPosition;
            s_towerReachabilityFloodPathFindingParams = pfParams;
            s_towerReachabilityFloodHasStart =
                TryFindNearestPathableTile(pathabilityProvider, pfParams, towerPosition, out Tile2i start);
            if (!s_towerReachabilityFloodHasStart)
            {
                s_towerReachabilityFloodValid = true;
                return;
            }

            int minX = Math.Min(bbMin.X, towerPosition.X) - RAMP_ACCESS_SEARCH_MARGIN_TILES;
            int minY = Math.Min(bbMin.Y, towerPosition.Y) - RAMP_ACCESS_SEARCH_MARGIN_TILES;
            int maxX = Math.Max(bbMax.X, towerPosition.X) + RAMP_ACCESS_SEARCH_MARGIN_TILES;
            int maxY = Math.Max(bbMax.Y, towerPosition.Y) + RAMP_ACCESS_SEARCH_MARGIN_TILES;

            HashSet<Tile2i> visited = s_towerReachabilityFloodVisited;
            Queue<Tile2i> queue = s_towerReachabilityFloodQueue;
            visited.Add(start);
            queue.Enqueue(start);

            while (queue.Count > 0 && visited.Count < MAX_RAMP_ACCESS_SEARCH_TILES)
            {
                Tile2i current = queue.Dequeue();
                foreach (RelTile2i direction in s_rampAccessSearchDirections)
                {
                    Tile2i next = current + direction;
                    if (next.X < minX || next.X > maxX || next.Y < minY || next.Y > maxY)
                        continue;
                    if (visited.Contains(next))
                        continue;
                    if (!pathabilityProvider.IsPathable(next, pfParams.PathabilityQueryMask))
                        continue;

                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }

            s_towerReachabilityFloodValid = true;
            LogLegacyAccessDebug(
                $"[ATD Reachability Debug] Shared flood rebuilt from tower={towerPosition} " +
                $"start={start}: {visited.Count} reachable tile(s)");
        }

        private static bool IsRampMouthReachableFromTower(IAreaManagingTower tower, Tile2i rampMouthOrigin)
        {
            return IsRampMouthReachableFromTower(tower, rampMouthOrigin, tower.Area.BoundingBoxMin, tower.Area.BoundingBoxMax);
        }

        private static bool IsRampMouthReachableFromTower(
            IAreaManagingTower tower,
            Tile2i rampMouthOrigin,
            Tile2i bbMin,
            Tile2i bbMax)
        {
            if (s_vehiclePathFindingManager == null || s_excavatorPathFindingParams == null)
            {
                s_log.Warning("Ramp access check skipped because vehicle pathfinding is unavailable.");
                return true;
            }

            IPathabilityProvider pathabilityProvider = s_vehiclePathFindingManager.PathabilityProvider;
            VehiclePathFindingParams pfParams = s_excavatorPathFindingParams;

            HashSet<Tile2i> targetTiles = BuildRampMouthTargetTiles(rampMouthOrigin, pathabilityProvider, pfParams);
            return IsReachableFromTowerInternal(tower, targetTiles, bbMin, bbMax);
        }

        private static bool IsClusterOriginReadyAndPathable(IAreaManagingTower tower, Tile2i origin)
        {
            if (s_desigManager == null) return false;
            Option<TerrainDesignation> existing = s_desigManager.GetDesignationAt(origin);
            if (!existing.HasValue) return false;

            TerrainDesignation designation = existing.Value;
            bool isReady = IsDesignationReadyForOwnOperation(designation);
            if (!isReady) return false;

            if (s_vehiclePathFindingManager == null || s_excavatorPathFindingParams == null) return true;
            IPathabilityProvider pathabilityProvider = s_vehiclePathFindingManager.PathabilityProvider;
            VehiclePathFindingParams pfParams = s_excavatorPathFindingParams;

            var targetTiles = new HashSet<Tile2i>();
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    Tile2i target = origin + new RelTile2i(x, y);
                    if (pathabilityProvider.IsPathable(target, pfParams.PathabilityQueryMask))
                    {
                        targetTiles.Add(target);
                    }
                }
            }

            return IsReachableFromTowerInternal(tower, targetTiles, tower.Area.BoundingBoxMin, tower.Area.BoundingBoxMax);
        }

        private static bool IsClusterOriginReadyForWork(Tile2i origin)
        {
            if (s_desigManager == null) return false;
            Option<TerrainDesignation> existing = s_desigManager.GetDesignationAt(origin);
            return existing.HasValue && IsDesignationReadyForOwnOperation(existing.Value);
        }

        private static string DescribeClusterOriginReachability(IAreaManagingTower tower, Tile2i origin)
        {
            if (s_desigManager == null)
                return $"{origin}: designationManager=missing";

            Option<TerrainDesignation> existing = s_desigManager.GetDesignationAt(origin);
            if (!existing.HasValue)
                return $"{origin}: designation=missing";

            TerrainDesignation designation = existing.Value;
            bool miningReady = designation.IsReadyToMineNonAmphibious();
            bool dumpingReady = designation.IsReadyToDumpNonAmphibious();
            bool ownReady = IsDesignationReadyForOwnOperation(designation);
            string proto = designation.Prototype.Id.Value;

            if (s_vehiclePathFindingManager == null || s_excavatorPathFindingParams == null)
                return $"{origin}: proto={proto} miningReady={miningReady} dumpingReady={dumpingReady} ownReady={ownReady} pathing=unavailable direct={ownReady}";

            IPathabilityProvider pathabilityProvider = s_vehiclePathFindingManager.PathabilityProvider;
            VehiclePathFindingParams pfParams = s_excavatorPathFindingParams;
            var targetTiles = new HashSet<Tile2i>();
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    Tile2i target = origin + new RelTile2i(x, y);
                    if (pathabilityProvider.IsPathable(target, pfParams.PathabilityQueryMask))
                        targetTiles.Add(target);
                }
            }

            bool direct = ownReady
                && IsReachableFromTowerInternal(tower, targetTiles, tower.Area.BoundingBoxMin, tower.Area.BoundingBoxMax);
            return $"{origin}: proto={proto} miningReady={miningReady} dumpingReady={dumpingReady} ownReady={ownReady} pathableTiles={targetTiles.Count} direct={direct}";
        }

        private static bool IsDesignationReadyForOwnOperation(TerrainDesignation designation)
        {
            TerrainDesignationProto proto = designation.Prototype;
            if (s_miningProto != null && proto == s_miningProto)
                return designation.IsReadyToMineNonAmphibious();
            if (s_dumpingProto != null && proto == s_dumpingProto)
                return designation.IsReadyToDumpNonAmphibious();
            if (s_levelingProto != null && proto == s_levelingProto)
                return designation.IsReadyToMineNonAmphibious()
                    || designation.IsReadyToDumpNonAmphibious();
            return false;
        }

        private static bool IsReachableFromTowerInternal(
            IAreaManagingTower tower,
            HashSet<Tile2i> targetTiles,
            Tile2i bbMin,
            Tile2i bbMax)
        {
            if (s_vehiclePathFindingManager == null || s_excavatorPathFindingParams == null)
            {
                return true;
            }

            if (targetTiles.Count == 0)
            {
                return false;
            }

            IPathabilityProvider pathabilityProvider = s_vehiclePathFindingManager.PathabilityProvider;
            VehiclePathFindingParams pfParams = s_excavatorPathFindingParams;
            Tile2i towerPosition = GetTowerPosition(tower, bbMin, bbMax);

            int baseMinX = Math.Min(bbMin.X, towerPosition.X);
            int baseMinY = Math.Min(bbMin.Y, towerPosition.Y);
            int baseMaxX = Math.Max(bbMax.X, towerPosition.X);
            int baseMaxY = Math.Max(bbMax.Y, towerPosition.Y);
            bool targetsWithinBaseBounds = targetTiles.All(target =>
                target.X >= baseMinX && target.X <= baseMaxX
                && target.Y >= baseMinY && target.Y <= baseMaxY);
            if (targetsWithinBaseBounds)
            {
                EnsureTowerReachabilityFlood(
                    pathabilityProvider, pfParams, towerPosition, bbMin, bbMax);
                if (!s_towerReachabilityFloodHasStart)
                {
                    LogLegacyAccessDebug(
                        $"[ATD Reachability Debug] Cannot find pathable tile near tower {towerPosition}");
                    return false;
                }

                foreach (Tile2i target in targetTiles)
                {
                    if (s_towerReachabilityFloodVisited.Contains(target))
                    {
                        LogLegacyAccessDebug(
                            $"[ATD Reachability Debug] Reachable via shared flood: target {target}");
                        return true;
                    }
                }

                LogLegacyAccessDebug(
                    $"[ATD Reachability Debug] Not reachable via shared flood " +
                    $"({s_towerReachabilityFloodVisited.Count} flooded tiles)");
                return false;
            }

            if (!TryFindNearestPathableTile(pathabilityProvider, pfParams, towerPosition, out Tile2i start))
            {
                LogLegacyAccessDebug($"[ATD Reachability Debug] Cannot find pathable tile near tower {towerPosition}");
                return false;
            }

            var terrMgr = s_desigManager?.TerrainManager;
            int startHeight = terrMgr != null ? GetSurfaceHeight(terrMgr, start) : -1;
            string targetsStr = terrMgr != null ? string.Join(", ", targetTiles.Select(t => $"{t} (h={GetSurfaceHeight(terrMgr, t)})")) : string.Join(", ", targetTiles);

            LogLegacyAccessDebug($"[ATD Reachability Debug] Start search from tower={towerPosition} -> start={start} (h={startHeight}) to targets=[{targetsStr}]");

            if (targetTiles.Contains(start))
            {
                LogLegacyAccessDebug($"[ATD Reachability Debug] Reachable immediately: target contains start");
                return true;
            }

            int minX = Math.Min(Math.Min(bbMin.X, towerPosition.X), targetTiles.Min(t => t.X)) - RAMP_ACCESS_SEARCH_MARGIN_TILES;
            int minY = Math.Min(Math.Min(bbMin.Y, towerPosition.Y), targetTiles.Min(t => t.Y)) - RAMP_ACCESS_SEARCH_MARGIN_TILES;
            int maxX = Math.Max(Math.Max(bbMax.X, towerPosition.X), targetTiles.Max(t => t.X)) + RAMP_ACCESS_SEARCH_MARGIN_TILES;
            int maxY = Math.Max(Math.Max(bbMax.Y, towerPosition.Y), targetTiles.Max(t => t.Y)) + RAMP_ACCESS_SEARCH_MARGIN_TILES;

            var visited = new HashSet<Tile2i>();
            var queue = new Queue<Tile2i>();
            visited.Add(start);
            queue.Enqueue(start);

            while (queue.Count > 0 && visited.Count < MAX_RAMP_ACCESS_SEARCH_TILES)
            {
                Tile2i current = queue.Dequeue();

                foreach (RelTile2i direction in s_rampAccessSearchDirections)
                {
                    Tile2i next = current + direction;
                    if (next.X < minX || next.X > maxX || next.Y < minY || next.Y > maxY)
                        continue;
                    if (visited.Contains(next))
                        continue;
                    if (!pathabilityProvider.IsPathable(next, pfParams.PathabilityQueryMask))
                        continue;
                    if (targetTiles.Contains(next))
                    {
                        LogLegacyAccessDebug($"[ATD Reachability Debug] Reachable via path: reached target {next}");
                        return true;
                    }

                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }

            LogLegacyAccessDebug($"[ATD Reachability Debug] Not reachable: searched {visited.Count} tiles");
            return false;
        }

        private static Tile2i GetTowerPosition(IAreaManagingTower tower, Tile2i bbMin, Tile2i bbMax)
        {
            if (tower is IEntityWithPosition positioned)
                return positioned.Position2f.Tile2i;
            return new Tile2i((bbMin.X + bbMax.X) / 2, (bbMin.Y + bbMax.Y) / 2);
        }

        private static HashSet<Tile2i> BuildRampMouthTargetTiles(
            Tile2i rampMouthOrigin,
            IPathabilityProvider pathabilityProvider,
            VehiclePathFindingParams pfParams)
        {
            var targetTiles = new HashSet<Tile2i>();
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    Tile2i target = rampMouthOrigin + new RelTile2i(x, y);
                    if (pathabilityProvider.IsPathable(target, pfParams.PathabilityQueryMask))
                    {
                        targetTiles.Add(target);
                    }
                }
            }

            return targetTiles;
        }

        internal static bool TryFindNearestPathableTile(
            IPathabilityProvider pathabilityProvider,
            VehiclePathFindingParams pfParams,
            Tile2i origin,
            out Tile2i pathableTile)
        {
            if (pathabilityProvider.IsPathable(origin, pfParams.PathabilityQueryMask))
            {
                pathableTile = origin;
                return true;
            }

            for (int radius = 1; radius <= 24; radius++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    if (TryUsePathableTile(pathabilityProvider, pfParams, origin + new RelTile2i(-radius, y), out pathableTile)
                        || TryUsePathableTile(pathabilityProvider, pfParams, origin + new RelTile2i(radius, y), out pathableTile))
                        return true;
                }

                for (int x = -radius + 1; x < radius; x++)
                {
                    if (TryUsePathableTile(pathabilityProvider, pfParams, origin + new RelTile2i(x, -radius), out pathableTile)
                        || TryUsePathableTile(pathabilityProvider, pfParams, origin + new RelTile2i(x, radius), out pathableTile))
                        return true;
                }
            }

            pathableTile = origin;
            return false;
        }

        private static bool TryUsePathableTile(
            IPathabilityProvider pathabilityProvider,
            VehiclePathFindingParams pfParams,
            Tile2i tile,
            out Tile2i pathableTile)
        {
            if (pathabilityProvider.IsPathable(tile, pfParams.PathabilityQueryMask))
            {
                pathableTile = tile;
                return true;
            }

            pathableTile = tile;
            return false;
        }

        internal static void CreateDesignationsForTower(IAreaManagingTower tower)
        {
            QueueCreateDesignations(tower, null);
        }

        /// <summary>
        /// Same as <see cref="CreateDesignationsForTower(IAreaManagingTower)"/> but passes
        /// <paramref name="panelKey"/> to the coroutine so that the Ore composition panel
        /// registered under that key auto-refreshes when the scan completes.
        /// </summary>
        internal static void CreateDesignationsForTower(IAreaManagingTower tower, object? panelKey)
        {
            QueueCreateDesignations(tower, panelKey);
        }

        internal static void MarkDebrisForRemovalForTower(IAreaManagingTower tower, bool overrideExisting, bool markUnreachable)
        {
            s_coroutineHost?.StartCoroutine(MarkDebrisForRemovalCoroutine(tower, overrideExisting, markUnreachable));
        }

        private static IEnumerator MarkDebrisForRemovalCoroutine(IAreaManagingTower tower, bool overrideExisting, bool markUnreachable)
        {
            if (s_desigManager == null || PropRemovalManager == null)
                yield break;

            var area = tower.Area;
            if (area.IsEmpty)
                yield break;

            TerrainManager terrMgr = s_desigManager.TerrainManager;
            Dictionary<TerrainPropId, HashSet<Tile2i>> debrisOriginsByProp =
                CollectDebrisDesignationOriginsByProp(area, terrMgr);
            var debrisOrigins = new HashSet<Tile2i>(
                debrisOriginsByProp.Values.SelectMany(origins => origins));
            int collectedOriginCount = debrisOrigins.Count;
            if (!markUnreachable)
            {
                FilterReachableDebrisOrigins(tower, debrisOrigins);
            }
            LogRuntimeDebug(
                $"[ATD Debris Button] tower={tower.Id.Value} " +
                $"props={debrisOriginsByProp.Count} origins={collectedOriginCount} " +
                $"eligibleOrigins={debrisOrigins.Count} " +
                $"includeUnreachable={markUnreachable} " +
                $"overrideExisting={overrideExisting}");
            yield return CreateDebrisRemovalRequestsCoroutine(
                tower, area, debrisOriginsByProp, debrisOrigins, overrideExisting);
        }

        private static void FilterReachableDebrisOrigins(IAreaManagingTower tower, HashSet<Tile2i> debrisOrigins)
        {
            if (s_vehiclePathFindingManager == null || debrisOrigins.Count == 0)
            {
                return;
            }

            IPathabilityProvider pathabilityProvider = s_vehiclePathFindingManager.PathabilityProvider;
            VehiclePathFindingParams pfParams =
                GetExcavatorPathFindingParamsForTower(tower,
                    out string pathParamsSource,
                    out int miningApproachRadius);

            Tile2i bbMin = tower.Area.BoundingBoxMin;
            Tile2i bbMax = tower.Area.BoundingBoxMax;
            Tile2i towerPosition = GetTowerPosition(tower, bbMin, bbMax);

            if (!TryFindNearestPathableTile(pathabilityProvider, pfParams, towerPosition, out Tile2i start))
            {
                LogRuntimeDebug(
                    $"[ATD Debris Reachability] tower={tower.Id.Value} " +
                    $"no pathable start near towerPosition={towerPosition}; " +
                    $"rejectedOrigins={debrisOrigins.Count}");
                debrisOrigins.Clear();
                return;
            }

            int minX = Math.Min(Math.Min(bbMin.X, towerPosition.X), debrisOrigins.Min(t => t.X)) - RAMP_ACCESS_SEARCH_MARGIN_TILES;
            int minY = Math.Min(Math.Min(bbMin.Y, towerPosition.Y), debrisOrigins.Min(t => t.Y)) - RAMP_ACCESS_SEARCH_MARGIN_TILES;
            int maxX = Math.Max(Math.Max(bbMax.X, towerPosition.X), debrisOrigins.Max(t => t.X)) + 4 + RAMP_ACCESS_SEARCH_MARGIN_TILES;
            int maxY = Math.Max(Math.Max(bbMax.Y, towerPosition.Y), debrisOrigins.Max(t => t.Y)) + 4 + RAMP_ACCESS_SEARCH_MARGIN_TILES;

            var visited = new HashSet<Tile2i>();
            var queue = new Queue<Tile2i>();
            visited.Add(start);
            queue.Enqueue(start);

            while (queue.Count > 0 && visited.Count < MAX_RAMP_ACCESS_SEARCH_TILES)
            {
                Tile2i current = queue.Dequeue();

                foreach (RelTile2i direction in s_rampAccessSearchDirections)
                {
                    Tile2i next = current + direction;
                    if (next.X < minX || next.X > maxX || next.Y < minY || next.Y > maxY)
                        continue;
                    if (visited.Contains(next))
                        continue;
                    if (!pathabilityProvider.IsPathable(next, pfParams.PathabilityQueryMask))
                        continue;
                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }

            var unreachable = new List<Tile2i>();
            foreach (Tile2i origin in debrisOrigins)
            {
                bool isReachable = false;
                // Match vanilla's prop-containing mining navigation tolerance:
                // (MinMiningDistance + MaxMiningDistance) / 2 for the selected
                // excavator proto. A blocking prop can make the designation
                // interior and its immediate perimeter unpathable even though
                // the excavator can work it from farther away.
                for (int y = -miningApproachRadius;
                    y <= 4 + miningApproachRadius; y++)
                {
                    for (int x = -miningApproachRadius;
                        x <= 4 + miningApproachRadius; x++)
                    {
                        Tile2i cell = origin + new RelTile2i(x, y);
                        // Vanilla considers all 5x5 terrain-designation sample
                        // points (offsets 0..4), then expands them by tolerance.
                        int dx = x < 0 ? -x : x > 4 ? x - 4 : 0;
                        int dy = y < 0 ? -y : y > 4 ? y - 4 : 0;
                        if (dx * dx + dy * dy
                                <= miningApproachRadius * miningApproachRadius
                            && visited.Contains(cell))
                        {
                            isReachable = true;
                            break;
                        }
                    }
                    if (isReachable) break;
                }

                if (!isReachable)
                {
                    unreachable.Add(origin);
                    if (AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Trace))
                        LogExperimentalAccessTrace(
                            $"[ATD Debris Reachability] rejected origin={origin} " +
                            $"reason=no connected interior-or-perimeter approach");
                }
            }

            foreach (Tile2i origin in unreachable)
            {
                debrisOrigins.Remove(origin);
            }
            LogRuntimeDebug(
                $"[ATD Debris Reachability] tower={tower.Id.Value} " +
                $"start={start} pathParams={pathParamsSource} " +
                $"miningApproachRadius={miningApproachRadius} visited={visited.Count} " +
                $"eligibleOrigins={debrisOrigins.Count} " +
                $"rejectedOrigins={unreachable.Count} " +
                $"searchLimitReached={visited.Count >= MAX_RAMP_ACCESS_SEARCH_TILES}");
        }

        private static List<LooseProductProto> GetCandidateScanProducts(IAreaManagingTower tower)
        {
            if (s_protosDb == null)
            {
                return new List<LooseProductProto>();
            }

            var selectedOre = GetSelectedOre(tower);

            // Get all available ores first
            var allOres = s_protosDb.All<LooseProductProto>()
                .Where(product => product != LooseProductProto.Phantom)
                .Where(product => product.CanBeOnTerrain || product.TerrainMaterial != null)
                .Where(product => !IsRockProduct(product))
                .Distinct()
                .ToList();

            if (selectedOre is LooseProductProto selectedLoose)
            {
                // Explicit selections may target dirt; AUTO never falls through to dirt.
                return allOres.Contains(selectedLoose)
                    ? new List<LooseProductProto> { selectedLoose }
                    : new List<LooseProductProto>();
            }

            // AUTO on an area without terrain designations restores only the old
            // useful-product stage. Debris and dirt remain manual selections.
            return allOres.Where(product => !IsDirtProduct(product)).ToList();
        }

        private static bool HasTerrainDesignationsInTowerArea(IAreaManagingTower tower)
        {
            foreach (TerrainDesignation designation in SelectDesignationsInAreaChunked(
                tower.Area.BoundingBoxMin, tower.Area.BoundingBoxMax))
            {
                if (IsOriginInsideTower(tower, designation.OriginTileCoord)
                    && IsTerrainWorkDesignationProto(designation.Prototype))
                    return true;
            }
            return false;
        }

        private static List<LooseProductProto> ResolveTargetScanProducts(
            bool hasSelectedProduct,
            List<LooseProductProto> candidateProducts,
            Dictionary<LooseProductProto, int> productCounts,
            bool hasDebris)
        {
            if (hasSelectedProduct)
            {
                return candidateProducts.Where(product => productCounts.ContainsKey(product)).ToList();
            }

            List<LooseProductProto> usefulProducts = candidateProducts
                .Where(product => !IsDirtProduct(product) && productCounts.ContainsKey(product))
                .ToList();
            if (usefulProducts.Count > 0)
            {
                return usefulProducts;
            }

            if (hasDebris)
            {
                return new List<LooseProductProto>();
            }

            return candidateProducts
                .Where(product => IsDirtProduct(product) && productCounts.ContainsKey(product))
                .ToList();
        }

        internal static int GetProductPickerSortRank(ProductProto product)
        {
            if (ReferenceEquals(product, ProductProto.Phantom))
            {
                return 1;
            }

            if (product is LooseProductProto loose && IsDirtProduct(loose))
            {
                return 2;
            }

            if (product is LooseProductProto looseProduct && IsRockProduct(looseProduct))
            {
                return 3;
            }

            return 0;
        }

        private static HashSet<string> BuildTargetProductIdSet(IEnumerable<LooseProductProto> products)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (LooseProductProto product in products)
            {
                ids.Add(product.Id.ToString());
            }

            return ids;
        }

        private static IEnumerable<Tile2i> EnumerateDesignatableTileCells(Tile2i tileOrigin)
        {
            for (int yOffset = 0; yOffset < 4; yOffset++)
            {
                for (int xOffset = 0; xOffset < 4; xOffset++)
                {
                    yield return new Tile2i(tileOrigin.X + xOffset, tileOrigin.Y + yOffset);
                }
            }
        }

        private static bool IsDesignatableTileFullyInsideArea(PolygonTerrainArea2i area, Tile2i tileOrigin)
        {
            foreach (Tile2i cell in EnumerateDesignatableTileCells(tileOrigin))
            {
                if (!area.ContainsTile(cell))
                {
                    return false;
                }
            }

            return true;
        }

        private static Dictionary<TerrainPropId, HashSet<Tile2i>>
            CollectDebrisDesignationOriginsByProp(
            PolygonTerrainArea2i area,
            TerrainManager terrMgr)
        {
            var originsByProp =
                new Dictionary<TerrainPropId, HashSet<Tile2i>>();
            if (s_terrainPropsManager == null)
            {
                return originsByProp;
            }

            try
            {
                var boundingArea = new RectangleTerrainArea2i(area.BoundingBoxMin, area.BoundingBoxSize);
                var occupiedTiles = new Lyst<Tile2i>();

                foreach (TerrainPropData prop in s_terrainPropsManager.EnumeratePropsInArea(boundingArea))
                {
                    if (prop.Proto.DoesNotBlocksVehicles)
                    {
                        continue;
                    }

                    occupiedTiles.Clear();
                    prop.CalculateOccupiedTiles(terrMgr, occupiedTiles);
                    var origins = new HashSet<Tile2i>();
                    for (int i = 0; i < occupiedTiles.Count; i++)
                    {
                        Tile2i occupiedTile = occupiedTiles[i];
                        if (!area.ContainsTile(occupiedTile))
                        {
                            continue;
                        }

                        Tile2i origin = TerrainDesignation.GetOrigin(occupiedTile);
                        if (IsDesignatableTileFullyInsideArea(area, origin))
                        {
                            origins.Add(origin);
                        }
                    }
                    if (origins.Count > 0)
                    {
                        originsByProp[prop.Id] = origins;
                        if (AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Trace))
                        {
                            LogExperimentalAccessTrace(
                                $"[ATD Debris Discovery] prop={prop.Id} " +
                                $"proto={prop.Proto.Id.Value} position={prop.Position} " +
                                $"placedHeight={prop.PlacedAtHeight.Value.ToFloat():0.###} " +
                                $"origins=[{string.Join(",", origins.OrderBy(item => item.X).ThenBy(item => item.Y))}]");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                s_log.Warning("Failed to collect debris props: " + ex.Message);
            }

            if (originsByProp.Count > 0)
            {
                LogDebug(string.Format("Found {0} debris prop(s) for removal",
                    originsByProp.Count));
            }

            return originsByProp;
        }

        private static HashSet<Tile2i> CollectDebrisDesignationOrigins(
            IAreaManagingTower tower, PolygonTerrainArea2i area,
            TerrainManager terrMgr) => new HashSet<Tile2i>(
                CollectDebrisDesignationOriginsByProp(area, terrMgr)
                    .Values.SelectMany(origins => origins));

        private static IEnumerator CreateDebrisRemovalRequestsCoroutine(
            IAreaManagingTower tower,
            PolygonTerrainArea2i area,
            IReadOnlyDictionary<TerrainPropId, HashSet<Tile2i>> debrisOriginsByProp,
            ISet<Tile2i> eligibleOrigins,
            bool overrideExisting = false)
        {
            if (s_desigManager == null || PropRemovalManager == null)
            {
                yield break;
            }

            int requested = 0;
            int skippedUnreachable = 0;
            int skippedExisting = 0;
            int skippedOutsideArea = 0;
            var initiallyDesignatedOrigins = new HashSet<Tile2i>(
                eligibleOrigins.Where(origin =>
                    s_desigManager.GetDesignationAt(origin).HasValue));
            foreach (KeyValuePair<TerrainPropId, HashSet<Tile2i>> pair
                in debrisOriginsByProp)
            {
                List<Tile2i> reachableOrigins = pair.Value
                    .Where(eligibleOrigins.Contains)
                    .Where(origin => IsDesignatableTileFullyInsideArea(area, origin))
                    .ToList();
                if (reachableOrigins.Count == 0)
                {
                    bool hasEligibleOutsideArea = pair.Value
                        .Any(eligibleOrigins.Contains);
                    if (hasEligibleOutsideArea)
                        skippedOutsideArea++;
                    else
                        skippedUnreachable++;
                    if (AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Trace))
                        LogExperimentalAccessTrace(
                            $"[ATD Debris Request] skipped prop={pair.Key} " +
                            $"reason={(hasEligibleOutsideArea ? "origin-not-fully-inside-area" : "no-reachable-origin")} " +
                            $"discoveredOrigins=[{string.Join(",", pair.Value.OrderBy(item => item.X).ThenBy(item => item.Y))}]");
                    continue;
                }

                List<Tile2i> availableOrigins = reachableOrigins
                    .Where(origin => overrideExisting
                        || !initiallyDesignatedOrigins.Contains(origin))
                    .ToList();
                if (availableOrigins.Count == 0)
                {
                    skippedExisting++;
                    if (AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Trace))
                        LogExperimentalAccessTrace(
                            $"[ATD Debris Request] skipped prop={pair.Key} " +
                            $"reason=existing-designation overrideExisting={overrideExisting} " +
                            $"reachableOrigins=[{string.Join(",", reachableOrigins.OrderBy(item => item.X).ThenBy(item => item.Y))}]");
                    continue;
                }

                Tile2i selectedOrigin = availableOrigins
                    // Shift-click explicitly opts into designation suspension. For
                    // props spanning more than one 4x4 cell, prefer the occupied
                    // cell containing the existing designation so the manager
                    // actually exercises its suspend/restore handoff instead of
                    // silently choosing an adjacent empty cell.
                    .OrderByDescending(origin => origin
                        == TerrainDesignation.GetOrigin(
                            pair.Key.Position.AsFull))
                    .ThenByDescending(origin => overrideExisting
                        && initiallyDesignatedOrigins.Contains(origin))
                    .ThenBy(origin => origin.X)
                    .ThenBy(origin => origin.Y)
                    .FirstOrDefault();

                ATDPropRemovalRequestHandle request = PropRemovalManager.RequestRemoval(
                    pair.Key, selectedOrigin,
                    $"debris-button:{tower.Id.Value}",
                    quickRemove: false);
                TrackManualDebrisRemovalRequest(tower, request);
                requested++;
                if (AtdDiagnostics.IsEnabled(AtdDiagnosticLevel.Trace))
                    LogExperimentalAccessTrace(
                        $"[ATD Debris Request] queued prop={pair.Key} " +
                        $"origin={selectedOrigin} request={request.RequestId} " +
                        $"coalescedOrigin={request.Origin} overrideExisting={overrideExisting}");

                int effectiveBatchSize = GetEffectiveBatchSize();
                if (requested % effectiveBatchSize == 0)
                    yield return null;
            }

            LogRuntimeDebug(
                $"[ATD Debris Request] tower={tower.Id.Value} " +
                $"requested={requested}/{debrisOriginsByProp.Count} " +
                $"skippedUnreachable={skippedUnreachable} " +
                $"skippedExisting={skippedExisting} " +
                $"skippedOutsideArea={skippedOutsideArea}");
            if (requested == 0)
                AddTowerDebrisCleanupEmptyNotification(tower,
                    debrisWasFound: debrisOriginsByProp.Count > 0);
        }

        private static void TrackManualDebrisRemovalRequest(
            IAreaManagingTower tower, ATDPropRemovalRequestHandle request)
        {
            if (request.IsCompleted)
                return;
            EntityId towerId = tower.Id;
            if (!s_manualDebrisRemovalRequestsByTower.TryGetValue(towerId,
                    out List<ATDPropRemovalRequestHandle> requests))
            {
                requests = new List<ATDPropRemovalRequestHandle>();
                s_manualDebrisRemovalRequestsByTower.Add(towerId, requests);
            }
            requests.Add(request);
            request.OnCompleted(_ =>
            {
                if (!s_manualDebrisRemovalRequestsByTower.TryGetValue(towerId,
                        out List<ATDPropRemovalRequestHandle> liveRequests))
                    return;
                liveRequests.Remove(request);
                if (liveRequests.Count == 0)
                {
                    s_manualDebrisRemovalRequestsByTower.Remove(towerId);
                }
            });
        }

        private static void CancelManualDebrisRemovalRequestsForTower(
            IAreaManagingTower tower)
        {
            if (PropRemovalManager == null
                || !s_manualDebrisRemovalRequestsByTower.TryGetValue(tower.Id,
                    out List<ATDPropRemovalRequestHandle> requests))
                return;
            foreach (ATDPropRemovalRequestHandle request in requests.ToArray())
                PropRemovalManager.Cancel(request);
            s_manualDebrisRemovalRequestsByTower.Remove(tower.Id);
        }

        internal static void TrackAccesswayPropRemovalRequest(
            IAreaManagingTower tower,
            ATDPropRemovalRequestHandle request)
        {
            if (request.IsCompleted)
                return;
            EntityId towerId = tower.Id;
            if (!s_accesswayPropRemovalRequestsByTower.TryGetValue(
                    towerId, out List<ATDPropRemovalRequestHandle> requests))
            {
                requests = new List<ATDPropRemovalRequestHandle>();
                s_accesswayPropRemovalRequestsByTower.Add(towerId, requests);
            }
            requests.Add(request);
            request.OnCompleted(_ =>
            {
                if (!s_accesswayPropRemovalRequestsByTower.TryGetValue(
                        towerId, out List<ATDPropRemovalRequestHandle> liveRequests))
                    return;
                liveRequests.Remove(request);
                if (liveRequests.Count == 0)
                    s_accesswayPropRemovalRequestsByTower.Remove(towerId);
            });
        }

        private static void CancelAccesswayPropRemovalRequestsForTower(
            IAreaManagingTower tower)
        {
            if (PropRemovalManager == null
                || !s_accesswayPropRemovalRequestsByTower.TryGetValue(
                    tower.Id, out List<ATDPropRemovalRequestHandle> requests))
                return;
            foreach (ATDPropRemovalRequestHandle request in requests.ToArray())
                PropRemovalManager.Cancel(request);
            s_accesswayPropRemovalRequestsByTower.Remove(tower.Id);
        }

        internal static void CancelPendingPropRemovalRequestsForTower(
            IAreaManagingTower tower)
        {
            CancelManualDebrisRemovalRequestsForTower(tower);
            CancelAccesswayPropRemovalRequestsForTower(tower);
        }

        private static float GetMinSurfaceHeightInDesignatableTile(Tile2i tileOrigin, TerrainManager terrMgr)
        {
            GetSurfaceHeightRangeInDesignatableTile(tileOrigin, terrMgr, out float minHeight, out _);
            return minHeight;
        }

        private static void GetSurfaceHeightRangeInDesignatableTile(Tile2i tileOrigin, TerrainManager terrMgr, out float minHeight, out float maxHeight)
        {
            minHeight = float.MaxValue;
            maxHeight = float.MinValue;
            foreach (Tile2i cell in EnumerateDesignatableTileCells(tileOrigin))
            {
                float h = terrMgr.GetHeight(cell).Value.ToFloat();
                if (h < minHeight)
                {
                    minHeight = h;
                }

                if (h > maxHeight)
                {
                    maxHeight = h;
                }
            }
        }

        private static bool TryGetResourcesFromAllTiles(
            Tile2i tileOrigin,
            PolygonTerrainArea2i area,
            TerrainManager terrMgr,
            HybridSet<LooseProductProto> productSet,
            Lyst<ProductResource> tempResults,
            out List<ProductResource> combinedResources)
        {
            combinedResources = new List<ProductResource>();

            // If any subtile is outside the managed area, reject this designation tile.
            foreach (Tile2i cell in EnumerateDesignatableTileCells(tileOrigin))
            {
                if (!area.ContainsTile(cell))
                {
                    return false;
                }
            }

            // Collect resources from all 16 terrain cells inside the designation tile.
            try
            {
                foreach (Tile2i cell in EnumerateDesignatableTileCells(tileOrigin))
                {
                    tempResults.Clear();
                    GetResourceDetailsNoBedrock(terrMgr, cell, productSet, tempResults);

                    for (int i = 0; i < tempResults.Count; i++)
                    {
                        combinedResources.Add(tempResults[i]);
                    }
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        private static LooseProductProto SelectMostCommonProduct(Dictionary<LooseProductProto, int> productCounts)
        {
            return productCounts
                .OrderByDescending(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key.Id.ToString())
                .First()
                .Key;
        }

        private static int ClampBatchSize(int value)
        {
            return Math.Max(1, Math.Min(MAX_BATCH_SIZE, value));
        }

        private static int GetEffectiveBatchSize()
        {
            int configuredBatchSize = ClampBatchSize(s_batchSize);
            if (Time.timeScale > 0f)
            {
                return configuredBatchSize;
            }

            return int.MaxValue;
        }

        private static bool IsRockProduct(LooseProductProto product)
        {
            string productId = product.Id.ToString();
            return productId.IndexOf("rock", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsDirtProduct(LooseProductProto product)
        {
            string productId = product.Id.ToString();
            return productId.IndexOf("dirt", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void GetResourceDetailsNoBedrock(
            TerrainManager terrMgr,
            Tile2i coord,
            HybridSet<LooseProductProto> products,
            Lyst<ProductResource> result)
        {
            ThicknessTilesF cumulativeDepth = ThicknessTilesF.Zero;
            TerrainLayerEnumerator enumerator = terrMgr.EnumerateLayers(terrMgr.GetTileIndex(coord));
            while (enumerator.MoveNext())
            {
                TerrainMaterialThicknessSlim layer = enumerator.Current;
                if (s_bedrockTerrainMaterial != null && layer.SlimId == s_bedrockTerrainMaterial.SlimId)
                    break;

                TerrainMaterialProto mat = layer.SlimId.ToFull(terrMgr);
                LooseProductProto minedProduct = mat.MinedProduct;
                if (products.Contains(minedProduct))
                {
                    result.Add(new ProductResource(minedProduct, layer.Thickness, cumulativeDepth));
                }
                cumulativeDepth += layer.Thickness;
            }
        }

        private static bool TryGetDeepestResourceDepth(
            List<ProductResource> resources,
            HashSet<string> targetProductIds,
            float terrainHeight,
            out int depthInt)
        {
            depthInt = 0;
            bool found = false;

            foreach (ProductResource resource in resources)
            {
                if (!targetProductIds.Contains(resource.Product.Id.ToString()))
                {
                    continue;
                }

                int candidateDepth = (terrainHeight - resource.Depth.Value.ToFloat() - resource.Height.Value.ToFloat()).FloorToInt();
                if (!found || candidateDepth < depthInt)
                {
                    depthInt = candidateDepth;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// Returns total non-bedrock column thickness and ore thickness for a tile.
        /// Used to compute the overburden contamination ratio.
        /// </summary>
        private static void GetColumnThicknesses(
            TerrainManager terrMgr,
            Tile2i coord,
            HashSet<string> targetProductIds,
            out float totalThickness,
            out float oreThickness)
        {
            totalThickness = 0f;
            oreThickness = 0f;
            TerrainLayerEnumerator enumerator = terrMgr.EnumerateLayers(terrMgr.GetTileIndex(coord));
            while (enumerator.MoveNext())
            {
                TerrainMaterialThicknessSlim layer = enumerator.Current;
                if (s_bedrockTerrainMaterial != null && layer.SlimId == s_bedrockTerrainMaterial.SlimId)
                    break;
                float thickness = layer.Thickness.Value.ToFloat();
                totalThickness += thickness;
                TerrainMaterialProto mat = layer.SlimId.ToFull(terrMgr);
                if (targetProductIds.Contains(mat.MinedProduct.Id.ToString()))
                    oreThickness += thickness;
            }
        }

        /// <summary>
        /// Computes average purity ratio (ore / total column) across every terrain cell in a designatable tile.
        /// Returns 0 if no column data available.
        /// </summary>
        private static float ComputeTilePurityRatio(
            Tile2i tileOrigin,
            TerrainManager terrMgr,
            HashSet<string> targetProductIds)
        {
            float totalOre = 0f, totalAll = 0f;
            foreach (Tile2i cell in EnumerateDesignatableTileCells(tileOrigin))
            {
                try
                {
                    GetColumnThicknesses(terrMgr, cell, targetProductIds, out float colTotal, out float colOre);
                    totalAll += colTotal;
                    totalOre += colOre;
                }
                catch { }
            }
            return totalAll > 0f ? totalOre / totalAll : 0f;
        }

        /// <summary>
        /// Returns the elevation to dig to for a tile using a density-based bottom trim
        /// (Criterion 1: bottom density trim).
        /// Walks ore intervals top-to-bottom. For each interval after the first, computes the
        /// local ore density of the zone from the previous interval's bottom to this one's bottom
        /// (ore_thickness / zone_thickness). If that density falls below minBottomOreDensity the
        /// scan stops — the dig target is set to the bottom of the last qualifying interval.
        /// This avoids digging through large waste gaps to reach thin sparse seams at depth.
        /// </summary>
        private static bool TryGetPurityAdjustedDepth(
            List<ProductResource> resources,
            HashSet<string> targetProductIds,
            float terrainHeight,
            float minBottomOreDensity,
            out int depthInt)
        {
            depthInt = 0;
            var intervals = new List<(float top, float bottom, float thickness)>();
            foreach (var resource in resources)
            {
                if (!targetProductIds.Contains(resource.Product.Id.ToString()))
                    continue;
                float topDepth    = resource.Depth.Value.ToFloat();
                float thickness   = resource.Height.Value.ToFloat();
                float bottomDepth = topDepth + thickness;
                intervals.Add((topDepth, bottomDepth, thickness));
            }
            if (intervals.Count == 0) return false;

            if (minBottomOreDensity <= 0f)
            {
                // No trimming — use deepest bottom
                float deepest = 0f;
                bool anyFound = false;
                foreach (var iv in intervals)
                {
                    if (!anyFound || iv.bottom > deepest) { deepest = iv.bottom; anyFound = true; }
                }
                depthInt = (terrainHeight - deepest).FloorToInt();
                return true;
            }

            // Sort top-to-bottom (shallowest first)
            intervals.Sort((a, b) => a.top.CompareTo(b.top));

            float stopDepth = 0f;
            bool found = false;
            for (int i = 0; i < intervals.Count; i++)
            {
                var iv = intervals[i];
                float localDensity;
                if (i == 0)
                {
                    // Shallowest interval always qualifies — no zone above it to evaluate
                    localDensity = 1f;
                }
                else
                {
                    // Zone = from bottom of previous ore interval to bottom of this one
                    // (includes the waste gap between them plus this ore seam)
                    float zoneThickness = iv.bottom - intervals[i - 1].bottom;
                    localDensity = zoneThickness > 0f ? iv.thickness / zoneThickness : 1f;
                }

                if (localDensity >= minBottomOreDensity)
                {
                    stopDepth = iv.bottom;
                    found = true;
                }
                else
                {
                    // This zone is too sparse — don't dig deeper
                    break;
                }
            }

            if (!found) return false;
            depthInt = (terrainHeight - stopDepth).FloorToInt();
            return true;
        }
    }
}
