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
using System.Diagnostics;
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
        private const int FARMING_ACCESS_MEDIUM_WORK_THRESHOLD = 250;
        private const int FARMING_ACCESS_LARGE_WORK_THRESHOLD = 1000;
        private const int FARMING_ACCESS_MEDIUM_RECHECK_TICKS = 30;
        private const int FARMING_ACCESS_LARGE_RECHECK_TICKS = 90;

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

            // Rim alignment designations were placed this tick but the terrain they target has not
            // been raised yet. The BFS uses actual terrain pathability, so it cannot see the future
            // path through the rim and may route a filling ramp in the wrong direction (e.g. into
            // the sea on the cliff side). Wait for the rim to be built before placing any ramp.
            if (isFilling && session.RimAlignmentOrigins.Count > 0)
            {
                session.LastAccessRampDetail =
                    "Filling access: waiting for rim alignment designations to be built before placing ramps.";
                return false;
            }

            string workKey = BuildFarmingAccessWorkKey(currentWork, isFilling);
            if (TryUseCachedFarmingAccessResult(session, workKey, currentWork.Count, out bool cachedReady))
                return cachedReady;

            Stopwatch accessSw = Stopwatch.StartNew();
            if (!TryFindInaccessibleFarmingDesignations(tower, currentWork, isFilling, out List<TerrainDesignation> inaccessible))
            {
                accessSw.Stop();
                LogFarmingPerfIfSlow(session, tower, "access check", accessSw.ElapsedMilliseconds, $"mode={(isFilling ? "filling" : "preparation")}, work={currentWork.Count}, inaccessible=unknown");
                SetFarmingAccessCache(session, workKey, ready: true, string.Empty);
                return true;
            }
            accessSw.Stop();
            LogFarmingPerfIfSlow(session, tower, "access check", accessSw.ElapsedMilliseconds, $"mode={(isFilling ? "filling" : "preparation")}, work={currentWork.Count}, inaccessible={inaccessible.Count}");

            if (inaccessible.Count == 0)
            {
                // Proactively remove any stale filling ramps now that the fill area is accessible.
                if (isFilling)
                    RemoveOwnedFarmingAccessRamps(session, isFilling: true);
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

            // Reserve this session's active work-phase origins so ramps don't overwrite designations
            // currently being prepared. Hidden origins (ReadyForFilling/Done) are intentionally NOT
            // reserved — ramps must be allowed to pass through already-completed tiles to reach an
            // inaccessible cluster that is surrounded by finished neighbours.
            // All origins from other sessions are reserved regardless of phase to prevent ramps from
            // corrupting another session's farming tracking.
            var reservedRampTiles = new HashSet<Tile2i>(
                session.Origins
                    .Where(kvp => IsFarmingAccessWorkPhase(kvp.Value.Phase, isFilling))
                    .Select(kvp => kvp.Key));
            foreach (Tile2i rimOrigin in session.RimAlignmentOrigins)
                reservedRampTiles.Add(rimOrigin);
            foreach (Tile2i cleanupOrigin in session.FutureRimDebrisCleanupOrigins)
                reservedRampTiles.Add(cleanupOrigin);
            foreach (FarmingPreparationSession otherSession in s_farmingPreparationSessions.Values)
            {
                if (otherSession == session)
                    continue;
                foreach (Tile2i otherOrigin in otherSession.Origins.Keys)
                    reservedRampTiles.Add(otherOrigin);
            }

            // Place one ramp per spatially disconnected cluster so all clusters are unblocked
            // in the same tick instead of one per tick.
            List<List<TerrainDesignation>> clusters = ClusterFarmingDesignationsByAdjacency(inaccessible);
            string mode = isFilling ? "dumping" : "excavation";
            HashSet<Tile2i> ownedRamps = GetOwnedFarmingAccessRamps(session, isFilling);
            // Also reserve ramps already placed in previous ticks so we never double-stack.
            foreach (Tile2i existingRamp in ownedRamps)
                reservedRampTiles.Add(existingRamp);
            int clustersPlaced = 0;
            int clustersFailed = 0;
            var clusterDetails = new List<string>();

            foreach (List<TerrainDesignation> cluster in clusters)
            {
                Tile2i anchor = cluster[0].OriginTileCoord;

                // If a previous-tick owned ramp is still present adjacent to this cluster and
                // snapped toward it (non-red edge), skip placing a new ramp — the existing one
                // just hasn't been excavated yet; adding another would cause ramps to accumulate.
                bool clusterAlreadyHasRamp = false;
                {
                    foreach (TerrainDesignation d in cluster)
                    {
                        Tile2i o = d.OriginTileCoord;
                        foreach (NeighborCoord dir in NeighborCoord.All4Neighbors)
                        {
                            Tile2i neighbor = o + new RelTile2i(dir.Dx * 4, dir.Dy * 4);
                            if (!ownedRamps.Contains(neighbor))
                                continue;
                            Option<TerrainDesignation> existing = s_desigManager.GetDesignationAt(neighbor);
                            if (existing.HasValue && existing.Value.Prototype == rampProto
                                && d.IsSnappedTowards(dir))
                            {
                                clusterAlreadyHasRamp = true;
                                break;
                            }
                        }
                        if (clusterAlreadyHasRamp)
                            break;
                    }
                }

                if (clusterAlreadyHasRamp)
                {
                    clusterDetails.Add($"({anchor.X},{anchor.Y})+{cluster.Count}: existing ramp pending");
                    continue;
                }

                var tileDepths = new Dict<Tile2i, int>();
                var cornerHeights = new Dict<Tile2i, int>();
                foreach (TerrainDesignation designation in cluster)
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

                int configuredRampWidth = cluster.Count < towerSettings.RampWidth
                    ? 1
                    : towerSettings.RampWidth;

                var placedRampOrigins = new List<Tile2i>();
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
                    allowExistingPlannedRampShortcut: false,
                    out Tile2i rampTopTile);

                foreach (Tile2i origin in placedRampOrigins)
                {
                    ownedRamps.Add(origin);
                    reservedRampTiles.Add(origin);
                }

                if (outcome == RampPlacementOutcome.Failed)
                {
                    clustersFailed++;
                    clusterDetails.Add($"({anchor.X},{anchor.Y})+{cluster.Count}: failed");
                }
                else
                {
                    clustersPlaced++;
                    clusterDetails.Add($"({anchor.X},{anchor.Y})+{cluster.Count}: {outcome} at ({rampTopTile.X},{rampTopTile.Y})");
                }
            }

            session.LastAccessRampRequestKey = requestKey;
            session.LastAccessRampDetail = clusters.Count == 1
                ? (clustersFailed > 0
                    ? $"Access ramp failed for {inaccessible.Count} unreachable {mode} origin(s)."
                    : $"Access ramp placed for {inaccessible.Count} unreachable {mode} origin(s): {clusterDetails[0].Split(':')[1].Trim()}.")
                : $"Access ramps for {clusters.Count} {mode} clusters: {clustersPlaced} placed, {clustersFailed} failed. [{string.Join("; ", clusterDetails)}]";
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
            int workCount,
            out bool ready)
        {
            ready = true;
            if (session.LastAccessCheckWorkKey != workKey)
                return false;

            int ticksSinceCheck = s_farmingAutomationTickIndex - session.LastAccessCheckTick;
            int recheckTicks = GetFarmingAccessRecheckTicks(workCount);
            if (ticksSinceCheck < 0 || ticksSinceCheck >= recheckTicks)
                return false;

            ready = session.LastAccessCheckReady;
            session.LastAccessRampDetail = session.LastAccessCheckDetail;
            return true;
        }

        private static int GetFarmingAccessRecheckTicks(int workCount)
        {
            if (workCount >= FARMING_ACCESS_LARGE_WORK_THRESHOLD)
                return FARMING_ACCESS_LARGE_RECHECK_TICKS;
            if (workCount >= FARMING_ACCESS_MEDIUM_WORK_THRESHOLD)
                return FARMING_ACCESS_MEDIUM_RECHECK_TICKS;
            return FARMING_ACCESS_RECHECK_TICKS;
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

        /// <summary>
        /// Groups <paramref name="designations"/> into connected clusters where two designations
        /// are considered adjacent when their origins are exactly 4 tiles apart on one axis
        /// (the standard farming designation grid spacing).
        /// </summary>
        private static List<List<TerrainDesignation>> ClusterFarmingDesignationsByAdjacency(
            List<TerrainDesignation> designations)
        {
            var clusters = new List<List<TerrainDesignation>>();
            var remaining = new HashSet<int>();
            for (int i = 0; i < designations.Count; i++)
                remaining.Add(i);

            while (remaining.Count > 0)
            {
                var cluster = new List<TerrainDesignation>();
                var queue = new Queue<int>();
                int seed = -1;
                foreach (int i in remaining) { seed = i; break; }
                remaining.Remove(seed);
                queue.Enqueue(seed);
                cluster.Add(designations[seed]);

                while (queue.Count > 0)
                {
                    int idx = queue.Dequeue();
                    Tile2i origin = designations[idx].OriginTileCoord;
                    var toExpand = new List<int>();
                    foreach (int other in remaining)
                    {
                        Tile2i otherOrigin = designations[other].OriginTileCoord;
                        int dx = origin.X - otherOrigin.X;
                        int dy = origin.Y - otherOrigin.Y;
                        if (dx < 0) dx = -dx;
                        if (dy < 0) dy = -dy;
                        if ((dx == 4 && dy == 0) || (dx == 0 && dy == 4))
                            toExpand.Add(other);
                    }

                    foreach (int other in toExpand)
                    {
                        remaining.Remove(other);
                        queue.Enqueue(other);
                        cluster.Add(designations[other]);
                    }
                }

                clusters.Add(cluster);
            }

            return clusters;
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

            if (removed > 0)
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
            var designationsByOrigin = new Dictionary<Tile2i, TerrainDesignation>(designations.Count);
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
                    designationsByOrigin[designation.OriginTileCoord] = designation;
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

            // Propagate reachability through designation-adjacency with matching target heights
            // ("non-red" edges). If designation A is BFS-reachable and shares a compatible edge
            // with neighbouring farming designation B, B is also reachable — a vehicle working
            // on A can cross the matching-height edge into B once A is fulfilled.
            var spreadQueue = new Queue<TerrainDesignation>();
            foreach (TerrainDesignation d in designationsByOrigin.Values)
                if (reachableOrigins.Contains(d.OriginTileCoord))
                    spreadQueue.Enqueue(d);

            while (spreadQueue.Count > 0)
            {
                TerrainDesignation curr = spreadQueue.Dequeue();
                DesignationData cd = curr.Data;
                Tile2i o = curr.OriginTileCoord;
                TerrainDesignation nbr;

                // East (+4, 0): curr.PlusX == nbr.Origin AND curr.PlusXy == nbr.PlusY
                if (designationsByOrigin.TryGetValue(o + new RelTile2i(4, 0), out nbr)
                    && !reachableOrigins.Contains(nbr.OriginTileCoord)
                    && cd.PlusXTargetHeight == nbr.Data.OriginTargetHeight
                    && cd.PlusXyTargetHeight == nbr.Data.PlusYTargetHeight)
                { reachableOrigins.Add(nbr.OriginTileCoord); spreadQueue.Enqueue(nbr); }

                // West (-4, 0): curr.Origin == nbr.PlusX AND curr.PlusY == nbr.PlusXy
                if (designationsByOrigin.TryGetValue(o + new RelTile2i(-4, 0), out nbr)
                    && !reachableOrigins.Contains(nbr.OriginTileCoord)
                    && cd.OriginTargetHeight == nbr.Data.PlusXTargetHeight
                    && cd.PlusYTargetHeight == nbr.Data.PlusXyTargetHeight)
                { reachableOrigins.Add(nbr.OriginTileCoord); spreadQueue.Enqueue(nbr); }

                // PlusY (+4, 0): curr.PlusY == nbr.Origin AND curr.PlusXy == nbr.PlusX
                if (designationsByOrigin.TryGetValue(o + new RelTile2i(0, 4), out nbr)
                    && !reachableOrigins.Contains(nbr.OriginTileCoord)
                    && cd.PlusYTargetHeight == nbr.Data.OriginTargetHeight
                    && cd.PlusXyTargetHeight == nbr.Data.PlusXTargetHeight)
                { reachableOrigins.Add(nbr.OriginTileCoord); spreadQueue.Enqueue(nbr); }

                // MinusY (0, -4): curr.Origin == nbr.PlusY AND curr.PlusX == nbr.PlusXy
                if (designationsByOrigin.TryGetValue(o + new RelTile2i(0, -4), out nbr)
                    && !reachableOrigins.Contains(nbr.OriginTileCoord)
                    && cd.OriginTargetHeight == nbr.Data.PlusYTargetHeight
                    && cd.PlusXTargetHeight == nbr.Data.PlusXyTargetHeight)
                { reachableOrigins.Add(nbr.OriginTileCoord); spreadQueue.Enqueue(nbr); }
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
