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
using UnityEngine;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        private static IEnumerator RunCreateDesignationsWithDebugGate(IEnumerator routine)
        {
            while (true)
            {
                bool movedNext;
                object? current = null;

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
                // so the gate covers the complete Create Designations operation.
                yield return current is IEnumerator nested
                    ? RunCreateDesignationsWithDebugGate(nested)
                    : current;
            }
        }
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

            var bbMin = TerrainDesignation.GetOrigin(area.BoundingBoxMin);
            var bbMax = TerrainDesignation.GetOrigin(area.BoundingBoxMax);
            HashSet<Tile2i> debrisOrigins = CollectDebrisDesignationOrigins(tower, area, terrMgr);
            bool hasSelectedProduct = GetSelectedOre(tower) != null;

            List<LooseProductProto> scanProducts = GetCandidateScanProducts(tower);
            if (scanProducts.Count == 0)
            {
                yield break;
            }

            var productSet = HybridSet<LooseProductProto>.From(scanProducts);
            var tempResults = new Lyst<ProductResource>();

            int scanCount = 0;

            var productCounts = new Dictionary<LooseProductProto, int>();
            var resourceDetailsByTile = new Dictionary<Tile2i, List<ProductResource>>();
            var towerSettings = GetOrCreateTowerSettings(tower);
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

            List<LooseProductProto> targetProducts = ResolveTargetScanProducts(hasSelectedProduct, scanProducts, productCounts, debrisOrigins.Count > 0);

            if (targetProducts.Count == 0)
            {
                if (!hasSelectedProduct && debrisOrigins.Count > 0)
                {
                    yield return CreateDebrisRemovalDesignationsCoroutine(tower, area, terrMgr, debrisOrigins, new HashSet<Tile2i>());
                }
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
                if (!hasSelectedProduct && debrisOrigins.Count > 0)
                {
                    yield return CreateDebrisRemovalDesignationsCoroutine(tower, area, terrMgr, debrisOrigins, new HashSet<Tile2i>());
                }
                yield break;
            }

            LogDebug(string.Format("Before filtering: {0} tiles in designations", maxOreDepths.Count));
            FilterIsolatedDesignations(maxOreDepths, targetProductIds, resourceDetailsByTile, purityLevel);

            if (maxOreDepths.Count == 0)
            {
                if (!hasSelectedProduct && debrisOrigins.Count > 0)
                {
                    yield return CreateDebrisRemovalDesignationsCoroutine(tower, area, terrMgr, debrisOrigins, new HashSet<Tile2i>());
                }
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

            LogDebug(string.Format("After filtering+connecting: {0} tiles in designations", maxOreDepths.Count));
            LogDebug(selectedProduct != null
                ? "Selected product: " + selectedProduct.Id
                : "Selected product mode: None (useful products, then debris, then dirt)");

            var maxOreDepthOverall = maxOreDepths.Values.Min();

            LogDebug(string.Format("Creating designations for {0} tiles with overall max depth {1}", maxOreDepths.Count, maxOreDepthOverall));

            var cornerHeights = BuildAndSmoothCornerHeights(maxOreDepths, maxHeightDiff, purityLevel <= 0);

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

                if (!s_desigManager.AddOrReplaceDesignation(s_miningProto, data))
                {
                    s_log.Warning(string.Format("Failed to create designation for tile {0}", tile));
                }

                designCount++;
                int effectiveBatchSize = GetEffectiveBatchSize();
                if (designCount % effectiveBatchSize == 0)
                    yield return null;
            }

            LogDebug(string.Format("Created {0} designations", designCount));

            if (generateRamps)
            {
                LogDebug("Creating access ramp...");
                var placedAccesswayOrigins = new List<Tile2i>();
                RampPlacementOutcome rampOutcome = CreateAccessRamp(
                    tower,
                    maxOreDepths,
                    cornerHeights,
                    terrMgr,
                    towerSettings.RampWidth,
                    s_miningProto,
                    placedAccesswayOrigins,
                    null,
                    out Tile2i rampTopTile);
                SetTowerLastRampOutcome(tower, rampOutcome);

                var protectedAccesswayOrigins = new HashSet<Tile2i>(placedAccesswayOrigins);
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

            // Refresh ore composition panel and designation panel after creating designations
            if (inspectorInstance != null)
            {
                OreCompositionPanel.ResetContent(inspectorInstance);
                DesignationPanel.RefreshDisplays(inspectorInstance);
            }
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

            LogDebug(string.Format("Created {0} {1} flattening-mode designations at elevation {2}", designCount, FlatteningDesignationTypeText(towerSettings.FlatteningDesignationType), target));
            ClearTowerLastRampOutcome(tower);

            if (inspectorInstance != null)
            {
                OreCompositionPanel.ResetContent(inspectorInstance);
                DesignationPanel.RefreshDisplays(inspectorInstance);
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

            if (!existing.Value.IsReadyToMineNonAmphibious()) return false;

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

        private static bool TryFindNearestPathableTile(
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
            var towerSettings = GetOrCreateTowerSettings(tower);
            s_coroutineHost?.StartCoroutine(RunCreateDesignationsWithDebugGate(
                CreateDesignationsCoroutine(tower, towerSettings.RampWidth > 0, null)));
        }

        /// <summary>
        /// Same as <see cref="CreateDesignationsForTower(IAreaManagingTower)"/> but passes
        /// <paramref name="panelKey"/> to the coroutine so that the Ore composition panel
        /// registered under that key auto-refreshes when the scan completes.
        /// </summary>
        internal static void CreateDesignationsForTower(IAreaManagingTower tower, object? panelKey)
        {
            var towerSettings = GetOrCreateTowerSettings(tower);
            s_coroutineHost?.StartCoroutine(RunCreateDesignationsWithDebugGate(
                CreateDesignationsCoroutine(tower, towerSettings.RampWidth > 0, panelKey)));
        }

        internal static void MarkDebrisForRemovalForTower(IAreaManagingTower tower)
        {
            s_coroutineHost?.StartCoroutine(MarkDebrisForRemovalCoroutine(tower));
        }

        private static IEnumerator MarkDebrisForRemovalCoroutine(IAreaManagingTower tower)
        {
            if (s_desigManager == null || s_miningProto == null)
                yield break;

            var area = tower.Area;
            if (area.IsEmpty)
                yield break;

            TerrainManager terrMgr = s_desigManager.TerrainManager;
            HashSet<Tile2i> debrisOrigins = CollectDebrisDesignationOrigins(tower, area, terrMgr);
            yield return CreateDebrisRemovalDesignationsCoroutine(tower, area, terrMgr, debrisOrigins, new HashSet<Tile2i>());
        }

        private static List<LooseProductProto> GetCandidateScanProducts(IAreaManagingTower tower)
        {
            if (s_protosDb == null)
            {
                return new List<LooseProductProto>();
            }

            // Get all available ores first
            var allOres = s_protosDb.All<LooseProductProto>()
                .Where(product => product != LooseProductProto.Phantom)
                .Where(product => product.CanBeOnTerrain || product.TerrainMaterial != null)
                .Where(product => !IsRockProduct(product))
                .Distinct()
                .ToList();

            // Check if a specific ore is selected for this tower
            var selectedOre = GetSelectedOre(tower);
            if (selectedOre != null && selectedOre is LooseProductProto selectedLoose)
            {
                // Return only the selected product if it is available (dirt is allowed explicitly).
                return allOres.Contains(selectedLoose) ? new List<LooseProductProto> { selectedLoose } : new List<LooseProductProto>();
            }

            return allOres;
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

        private static HashSet<Tile2i> CollectDebrisDesignationOrigins(
            IAreaManagingTower tower,
            PolygonTerrainArea2i area,
            TerrainManager terrMgr)
        {
            var origins = new HashSet<Tile2i>();
            if (s_terrainPropsManager == null)
            {
                return origins;
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
                }
            }
            catch (Exception ex)
            {
                s_log.Warning("Failed to collect debris props: " + ex.Message);
            }

            if (origins.Count > 0)
            {
                LogDebug(string.Format("Found {0} debris designation tile(s)", origins.Count));
            }

            return origins;
        }

        private static IEnumerator CreateDebrisRemovalDesignationsCoroutine(
            IAreaManagingTower tower,
            PolygonTerrainArea2i area,
            TerrainManager terrMgr,
            HashSet<Tile2i> debrisOrigins,
            HashSet<Tile2i> oreOrigins)
        {
            if (s_desigManager == null || s_miningProto == null)
            {
                yield break;
            }

            int created = 0;
            foreach (Tile2i origin in debrisOrigins)
            {
                if (oreOrigins.Contains(origin) ||
                    HasTerrainDesignationAtOrigin(tower, origin) ||
                    !IsDesignatableTileFullyInsideArea(area, origin))
                {
                    continue;
                }

                int hNW = GetSurfaceHeight(terrMgr, origin) + 1;
                int hNE = GetSurfaceHeight(terrMgr, origin.AddX(4)) + 1;
                int hSE = GetSurfaceHeight(terrMgr, origin.AddXy(4)) + 1;
                int hSW = GetSurfaceHeight(terrMgr, origin.AddY(4)) + 1;

                var data = new DesignationData(origin,
                    new HeightTilesI(hNW), new HeightTilesI(hNE),
                    new HeightTilesI(hSE), new HeightTilesI(hSW));

                if (s_desigManager.AddOrReplaceDesignation(s_miningProto, data))
                {
                    created++;
                }

                int effectiveBatchSize = GetEffectiveBatchSize();
                if (created > 0 && created % effectiveBatchSize == 0)
                    yield return null;
            }

            if (created > 0)
            {
                LogDebug(string.Format("Created {0} debris removal designation(s)", created));
            }
        }

        private static float GetMinSurfaceHeightInDesignatableTile(Tile2i tileOrigin, TerrainManager terrMgr)
        {
            float minHeight = float.MaxValue;
            foreach (Tile2i cell in EnumerateDesignatableTileCells(tileOrigin))
            {
                float h = terrMgr.GetHeight(cell).Value.ToFloat();
                if (h < minHeight)
                {
                    minHeight = h;
                }
            }

            return minHeight;
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
