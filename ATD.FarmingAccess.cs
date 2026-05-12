// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
// Auto Terrain Designations - Farming Access Ramps
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mafi;
using Mafi.Collections;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.PathFinding;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        private const int FARMING_ACCESS_SEARCH_MARGIN_TILES = 96;
        private const int MAX_FARMING_ACCESS_SEARCH_TILES = 250000;
        private const int FARMING_ACCESS_RECHECK_TICKS = 10;

        private static bool EnsureFarmingAccessForCurrentPhase(
            IAreaManagingTower tower,
            FarmingPreparationSession session,
            bool isFilling)
        {
            session.LastAccessRampDetail = string.Empty;

            if (s_desigManager == null)
                return true;

            TerrainDesignationProto? rampProto = isFilling ? s_dumpingProto : s_miningProto;
            if (rampProto == null)
            {
                session.LastAccessRampDetail = "Access ramp skipped: ramp designation proto unavailable.";
                return false;
            }

            var currentWork = new List<TerrainDesignation>();
            foreach (FarmingOriginSession originState in session.Origins.Values)
            {
                if (!IsFarmingAccessWorkPhase(originState.Phase, isFilling))
                    continue;

                var currentDesignation = s_desigManager.GetDesignationAt(originState.Origin);
                if (currentDesignation.HasValue && IsFarmingAccessDesignationForCurrentPhase(currentDesignation.Value, originState, isFilling))
                    currentWork.Add(currentDesignation.Value);
            }

            if (currentWork.Count == 0)
            {
                if (isFilling && HasQueuedFarmingFillingOrigins(session))
                    return true;

                int removed = RemoveOwnedFarmingAccessRamps(session, isFilling);
                if (removed > 0)
                {
                    string cleanupMode = isFilling ? "dumping" : "excavation";
                    session.LastAccessRampDetail = $"Removed {removed} stale {cleanupMode} access ramp designation(s).";
                }

                return true;
            }

            string workKey = BuildFarmingAccessWorkKey(currentWork, isFilling);
            if (TryUseCachedFarmingAccessResult(session, workKey, out bool cachedReady))
                return cachedReady;

            if (!TryFindInaccessibleFarmingDesignations(tower, currentWork, isFilling, out List<TerrainDesignation> inaccessible))
            {
                SetFarmingAccessCache(session, workKey, ready: true, string.Empty);
                return true;
            }

            if (inaccessible.Count == 0)
            {
                SetFarmingAccessCache(session, workKey, ready: true, string.Empty);
                return true;
            }

            var towerSettings = GetOrCreateTowerSettings(tower);
            if (towerSettings.RampWidth <= 0)
            {
                session.LastAccessRampDetail = $"Access ramp needed for {inaccessible.Count} origin(s), but ramp generation is disabled.";
                SetFarmingAccessCache(session, workKey, ready: false, session.LastAccessRampDetail);
                return false;
            }

            string requestKey = BuildFarmingAccessRampRequestKey(inaccessible, isFilling);
            if (session.LastAccessRampRequestKey == requestKey)
            {
                string waitMode = isFilling ? "dumping" : "excavation";
                session.LastAccessRampDetail =
                    $"Access ramp already requested for {inaccessible.Count} unreachable {waitMode} origin(s); waiting for terrain/designation state to change.";
                SetFarmingAccessCache(session, workKey, ready: false, session.LastAccessRampDetail);
                return false;
            }

            var tileDepths = new Dict<Tile2i, int>();
            var cornerHeights = new Dict<Tile2i, int>();
            foreach (TerrainDesignation designation in inaccessible)
            {
                DesignationData data = designation.Data;
                tileDepths[data.OriginTile] = data.OriginTargetHeight.Value
                    .Min(data.PlusXTargetHeight.Value)
                    .Min(data.PlusXyTargetHeight.Value)
                    .Min(data.PlusYTargetHeight.Value);
                cornerHeights[data.OriginTile] = data.OriginTargetHeight.Value;
                cornerHeights[data.PlusXTileCoord] = data.PlusXTargetHeight.Value;
                cornerHeights[data.PlusXyTileCoord] = data.PlusXyTargetHeight.Value;
                cornerHeights[data.PlusYTileCoord] = data.PlusYTargetHeight.Value;
            }

            int configuredRampWidth = inaccessible.Count < towerSettings.RampWidth
                ? 1
                : towerSettings.RampWidth;

            var placedRampOrigins = new List<Tile2i>();
            var reservedRampTiles = new HashSet<Tile2i>(session.Origins.Keys);
            RampPlacementOutcome outcome = CreateAccessRamp(
                tower,
                tileDepths,
                cornerHeights,
                s_desigManager.TerrainManager,
                configuredRampWidth,
                rampProto,
                placedRampOrigins,
                reservedRampTiles,
                useLocalSurfaceReference: isFilling,
                out Tile2i rampTopTile);

            string mode = isFilling ? "dumping" : "excavation";
            session.LastAccessRampRequestKey = requestKey;
            HashSet<Tile2i> ownedRamps = GetOwnedFarmingAccessRamps(session, isFilling);
            foreach (Tile2i origin in placedRampOrigins)
                ownedRamps.Add(origin);

            session.LastAccessRampDetail = outcome == RampPlacementOutcome.Failed
                ? $"Access ramp failed for {inaccessible.Count} unreachable {mode} origin(s)."
                : $"Access ramp placed for {inaccessible.Count} unreachable {mode} origin(s): {outcome} at ({rampTopTile.X},{rampTopTile.Y}).";
            SetFarmingAccessCache(session, workKey, ready: false, session.LastAccessRampDetail);
            return false;
        }

        private static string BuildFarmingAccessWorkKey(
            List<TerrainDesignation> currentWork,
            bool isFilling)
        {
            var sb = new StringBuilder();
            sb.Append(isFilling ? "fill" : "prep");
            foreach (TerrainDesignation designation in currentWork
                .OrderBy(designation => designation.OriginTileCoord.Y)
                .ThenBy(designation => designation.OriginTileCoord.X))
            {
                DesignationData data = designation.Data;
                sb.Append('|')
                    .Append(data.OriginTile.X).Append(',').Append(data.OriginTile.Y)
                    .Append(':')
                    .Append(data.OriginTargetHeight.Value).Append(',')
                    .Append(data.PlusXTargetHeight.Value).Append(',')
                    .Append(data.PlusXyTargetHeight.Value).Append(',')
                    .Append(data.PlusYTargetHeight.Value);
            }

            return sb.ToString();
        }

        private static bool TryUseCachedFarmingAccessResult(
            FarmingPreparationSession session,
            string workKey,
            out bool ready)
        {
            ready = true;
            if (session.LastAccessCheckWorkKey != workKey)
                return false;

            int ticksSinceCheck = s_farmingAutomationTickIndex - session.LastAccessCheckTick;
            if (ticksSinceCheck < 0 || ticksSinceCheck >= FARMING_ACCESS_RECHECK_TICKS)
                return false;

            ready = session.LastAccessCheckReady;
            session.LastAccessRampDetail = session.LastAccessCheckDetail;
            return true;
        }

        private static void SetFarmingAccessCache(
            FarmingPreparationSession session,
            string workKey,
            bool ready,
            string detail)
        {
            session.LastAccessCheckWorkKey = workKey;
            session.LastAccessCheckReady = ready;
            session.LastAccessCheckDetail = detail;
            session.LastAccessCheckTick = s_farmingAutomationTickIndex;
        }

        private static void ClearFarmingAccessCache(FarmingPreparationSession session)
        {
            session.LastAccessCheckWorkKey = string.Empty;
            session.LastAccessCheckReady = true;
            session.LastAccessCheckDetail = string.Empty;
            session.LastAccessCheckTick = int.MinValue;
        }

        private static string BuildFarmingAccessRampRequestKey(
            List<TerrainDesignation> inaccessible,
            bool isFilling)
        {
            var sb = new StringBuilder();
            sb.Append(isFilling ? "fill" : "prep");
            foreach (TerrainDesignation designation in inaccessible
                .OrderBy(designation => designation.OriginTileCoord.Y)
                .ThenBy(designation => designation.OriginTileCoord.X))
            {
                DesignationData data = designation.Data;
                sb.Append('|')
                    .Append(data.OriginTile.X).Append(',').Append(data.OriginTile.Y)
                    .Append(':')
                    .Append(data.OriginTargetHeight.Value).Append(',')
                    .Append(data.PlusXTargetHeight.Value).Append(',')
                    .Append(data.PlusXyTargetHeight.Value).Append(',')
                    .Append(data.PlusYTargetHeight.Value);
            }

            return sb.ToString();
        }

        private static bool IsFarmingAccessWorkPhase(FarmingOriginPhase phase, bool isFilling)
        {
            if (isFilling)
                return phase == FarmingOriginPhase.Filling;

            return phase == FarmingOriginPhase.AnalysisLeveling
                || phase == FarmingOriginPhase.Preparing;
        }

        private static bool IsFarmingAccessDesignationForCurrentPhase(
            TerrainDesignation designation,
            FarmingOriginSession originState,
            bool isFilling)
        {
            if (!isFilling)
                return IsLevelingDesignation(designation);

            return IsLevelingDesignation(designation);
        }

        private static HashSet<Tile2i> GetOwnedFarmingAccessRamps(FarmingPreparationSession session, bool isFilling)
        {
            return isFilling
                ? session.FillingAccessRampOrigins
                : session.PreparationAccessRampOrigins;
        }

        private static int RemoveOwnedFarmingAccessRamps(FarmingPreparationSession session, bool isFilling)
        {
            if (s_desigManager == null)
                return 0;

            TerrainDesignationProto? rampProto = isFilling ? s_dumpingProto : s_miningProto;
            if (rampProto == null)
                return 0;

            HashSet<Tile2i> ownedRamps = GetOwnedFarmingAccessRamps(session, isFilling);
            int removed = 0;
            foreach (Tile2i origin in ownedRamps.ToList())
            {
                var currentDesignation = s_desigManager.GetDesignationAt(origin);
                if (currentDesignation.HasValue && currentDesignation.Value.Prototype == rampProto)
                {
                    s_desigManager.RemoveDesignation(origin);
                    removed++;
                }

                ownedRamps.Remove(origin);
            }

            if (removed > 0 || ownedRamps.Count == 0)
                session.LastAccessRampRequestKey = string.Empty;

            ClearFarmingAccessCache(session);
            return removed;
        }

        private static bool TryFindInaccessibleFarmingDesignations(
            IAreaManagingTower tower,
            List<TerrainDesignation> designations,
            bool isFilling,
            out List<TerrainDesignation> inaccessible)
        {
            inaccessible = new List<TerrainDesignation>();

            if (designations.Count == 0)
                return true;

            if (s_vehiclePathFindingManager == null || s_excavatorPathFindingParams == null)
            {
                Log.Warning("[ATD] Farming access check skipped because vehicle pathfinding is unavailable.");
                return false;
            }

            IPathabilityProvider pathabilityProvider = s_vehiclePathFindingManager.PathabilityProvider;
            VehiclePathFindingParams pfParams = s_excavatorPathFindingParams;

            try
            {
                pathabilityProvider.UpdateChangedTiles();
            }
            catch
            {
            }

            Tile2i bbMin = tower.Area.BoundingBoxMin;
            Tile2i bbMax = tower.Area.BoundingBoxMax;
            Tile2i towerPosition = GetTowerPosition(tower, bbMin, bbMax);
            if (!TryFindNearestPathableTile(pathabilityProvider, pfParams, towerPosition, out Tile2i start))
            {
                inaccessible.AddRange(designations);
                return true;
            }

            var targetTilesByOrigin = new Dictionary<Tile2i, HashSet<Tile2i>>();
            var originsByTargetTile = new Dictionary<Tile2i, List<Tile2i>>();
            foreach (TerrainDesignation designation in designations)
            {
                if (!IsFarmingDesignationReadyForVehicleWork(designation, isFilling))
                {
                    inaccessible.Add(designation);
                    continue;
                }

                HashSet<Tile2i> targets = BuildFarmingAccessTargetTiles(designation.OriginTileCoord, pathabilityProvider, pfParams);
                if (targets.Count == 0)
                    inaccessible.Add(designation);
                else
                {
                    targetTilesByOrigin[designation.OriginTileCoord] = targets;
                    foreach (Tile2i target in targets)
                    {
                        if (!originsByTargetTile.TryGetValue(target, out List<Tile2i> origins))
                        {
                            origins = new List<Tile2i>();
                            originsByTargetTile[target] = origins;
                        }

                        origins.Add(designation.OriginTileCoord);
                    }
                }
            }

            if (targetTilesByOrigin.Count == 0)
                return true;

            int minX = towerPosition.X - FARMING_ACCESS_SEARCH_MARGIN_TILES;
            int minY = towerPosition.Y - FARMING_ACCESS_SEARCH_MARGIN_TILES;
            int maxX = towerPosition.X + FARMING_ACCESS_SEARCH_MARGIN_TILES;
            int maxY = towerPosition.Y + FARMING_ACCESS_SEARCH_MARGIN_TILES;
            foreach (TerrainDesignation designation in designations)
            {
                Tile2i origin = designation.OriginTileCoord;
                minX = minX.Min(origin.X - FARMING_ACCESS_SEARCH_MARGIN_TILES);
                minY = minY.Min(origin.Y - FARMING_ACCESS_SEARCH_MARGIN_TILES);
                maxX = maxX.Max(origin.X + 3 + FARMING_ACCESS_SEARCH_MARGIN_TILES);
                maxY = maxY.Max(origin.Y + 3 + FARMING_ACCESS_SEARCH_MARGIN_TILES);
            }

            var visited = new HashSet<Tile2i>();
            var queue = new Queue<Tile2i>();
            visited.Add(start);
            queue.Enqueue(start);

            var reachableOrigins = new HashSet<Tile2i>();
            while (queue.Count > 0 && visited.Count < MAX_FARMING_ACCESS_SEARCH_TILES)
            {
                Tile2i current = queue.Dequeue();

                if (originsByTargetTile.TryGetValue(current, out List<Tile2i> reachedTargets))
                {
                    foreach (Tile2i reachedOrigin in reachedTargets)
                        reachableOrigins.Add(reachedOrigin);
                }

                if (reachableOrigins.Count == targetTilesByOrigin.Count)
                    break;

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

            foreach (TerrainDesignation designation in designations)
            {
                if (!reachableOrigins.Contains(designation.OriginTileCoord)
                    && !inaccessible.Contains(designation))
                {
                    inaccessible.Add(designation);
                }
            }

            return true;
        }

        private static bool IsFarmingDesignationReadyForVehicleWork(TerrainDesignation designation, bool isFilling)
        {
            return isFilling
                ? designation.IsReadyToDumpNonAmphibious()
                : designation.IsReadyToMineNonAmphibious();
        }

        private static HashSet<Tile2i> BuildFarmingAccessTargetTiles(
            Tile2i origin,
            IPathabilityProvider pathabilityProvider,
            VehiclePathFindingParams pfParams)
        {
            var targets = new HashSet<Tile2i>();
            for (int y = -1; y <= 4; y++)
            {
                for (int x = -1; x <= 4; x++)
                {
                    bool isPerimeter = x == -1 || x == 4 || y == -1 || y == 4;
                    if (!isPerimeter)
                        continue;

                    Tile2i tile = origin + new RelTile2i(x, y);
                    if (pathabilityProvider.IsPathable(tile, pfParams.PathabilityQueryMask))
                        targets.Add(tile);
                }
            }

            return targets;
        }
    }
}
