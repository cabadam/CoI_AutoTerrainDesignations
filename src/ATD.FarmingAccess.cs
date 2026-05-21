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

        private sealed class FarmingAccessCluster
        {
            public int DebugId { get; set; }
            public List<TerrainDesignation> Designations { get; } = new List<TerrainDesignation>();
            public HashSet<Tile2i> Origins { get; } = new HashSet<Tile2i>();
            public bool NeedsAccess { get; set; }
            public bool HasAccess { get; set; }

            public Tile2i Anchor => Designations.Count > 0
                ? Designations[0].OriginTileCoord
                : default;

            public int Count => Designations.Count;

            public void Add(TerrainDesignation designation)
            {
                Designations.Add(designation);
                Origins.Add(designation.OriginTileCoord);
            }
        }

        private static bool EnsureFarmingAccessForCurrentPhase(
            IAreaManagingTower tower,
            FarmingPreparationSession session,
            bool isFilling)
        {
            session.LastAccessRampDetail = string.Empty;

            if (s_desigManager == null)
                return true;

            TerrainDesignationProto? defaultRampProto = isFilling ? s_dumpingProto : s_miningProto;
            if (defaultRampProto == null)
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
            if (!TryFindInaccessibleFarmingAccessClusters(tower, currentWork, isFilling, out List<FarmingAccessCluster> inaccessibleClusters))
            {
                accessSw.Stop();
                LogFarmingPerfIfSlow(session, tower, "access check", accessSw.ElapsedMilliseconds, $"mode={(isFilling ? "filling" : "preparation")}, work={currentWork.Count}, inaccessible=unknown");
                SetFarmingAccessCache(session, workKey, ready: true, string.Empty);
                return true;
            }
            accessSw.Stop();
            int inaccessibleCount = inaccessibleClusters.Sum(cluster => cluster.Count);
            LogFarmingPerfIfSlow(session, tower, "access check", accessSw.ElapsedMilliseconds, $"mode={(isFilling ? "filling" : "preparation")}, work={currentWork.Count}, inaccessible={inaccessibleCount}, clusters={inaccessibleClusters.Count}");
            LogDebug($"[ATD Farming Access] mode={(isFilling ? "filling" : "preparation")} work={currentWork.Count} inaccessibleClusters={inaccessibleClusters.Count} inaccessibleOrigins={inaccessibleCount}.");

            if (inaccessibleClusters.Count == 0)
            {
                LogDebug($"[ATD Farming Access] mode={(isFilling ? "filling" : "preparation")} all clusters have access.");
                // Proactively remove any stale filling ramps now that the fill area is accessible.
                if (isFilling)
                    RemoveOwnedFarmingAccessRamps(session, isFilling: true);
                SetFarmingAccessCache(session, workKey, ready: true, string.Empty);
                return true;
            }

            var towerSettings = GetOrCreateTowerSettings(tower);
            if (towerSettings.RampWidth <= 0)
            {
                session.LastAccessRampDetail = $"Access ramp needed for {inaccessibleCount} origin(s), but ramp generation is disabled.";
                SetFarmingAccessCache(session, workKey, ready: false, session.LastAccessRampDetail);
                return false;
            }

            string requestKey = BuildFarmingAccessRampRequestKey(inaccessibleClusters, isFilling);
            if (session.LastAccessRampRequestKey == requestKey)
            {
                string waitMode = isFilling ? "dumping" : "excavation";
                session.LastAccessRampDetail =
                    $"Access ramp already requested for {inaccessibleCount} unreachable {waitMode} origin(s); waiting for terrain/designation state to change.";
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

            // Place one ramp per non-red connected cluster so all clusters are unblocked
            // in the same tick instead of one per tick.
            string mode = isFilling ? "dumping" : "excavation";
            HashSet<Tile2i> ownedRamps = GetOwnedFarmingAccessRamps(session, isFilling);
            // Also reserve ramps already placed in previous ticks so we never double-stack.
            foreach (Tile2i existingRamp in ownedRamps)
                reservedRampTiles.Add(existingRamp);
            int clustersPlaced = 0;
            int clustersFailed = 0;
            var clusterDetails = new List<string>();

            foreach (FarmingAccessCluster cluster in inaccessibleClusters)
            {
                Tile2i anchor = cluster.Anchor;
                TerrainDesignationProto? clusterRampProto = GetFarmingAccessRampProtoForCluster(cluster.Designations, isFilling, defaultRampProto);
                if (clusterRampProto == null)
                {
                    clustersFailed++;
                    clusterDetails.Add($"({anchor.X},{anchor.Y})+{cluster.Count}: ramp proto unavailable");
                    LogDebug($"[ATD Farming Access] cluster#{cluster.DebugId} ramp skipped: proto unavailable. {FormatFarmingAccessClusterSummary(cluster)}");
                    continue;
                }

                var attachDesignations = new List<TerrainDesignation>(cluster.Designations);
                var tileDepths = new Dict<Tile2i, int>();
                var cornerHeights = new Dict<Tile2i, int>();
                foreach (TerrainDesignation designation in cluster.Designations)
                {
                    AddFarmingRampPlanTile(designation, tileDepths, cornerHeights);
                }

                if (!isFilling && s_dumpingProto != null && clusterRampProto == s_dumpingProto)
                    AddConnectedPreparationShouldersToRampPlan(session, cluster.Designations, tileDepths, cornerHeights, attachDesignations);

                LogDebug($"[ATD Farming Access] cluster#{cluster.DebugId} planning ramp proto={clusterRampProto.Id.Value} width={(cluster.Count < towerSettings.RampWidth ? 1 : towerSettings.RampWidth)} attach={attachDesignations.Count} reserved={reservedRampTiles.Count}. {FormatFarmingAccessClusterSummary(cluster)}");

                // If a previous-tick owned ramp is still present adjacent to the current attach
                // surface and snapped toward it (non-red edge), skip placing a new ramp. For
                // dumping-prep, the attach surface includes the owned shoulder ring, not just the
                // original farming origins.
                if (AttachSurfaceAlreadyHasOwnedRamp(attachDesignations, ownedRamps, clusterRampProto))
                {
                    clusterDetails.Add($"({anchor.X},{anchor.Y})+{cluster.Count}: existing ramp pending");
                    LogDebug($"[ATD Farming Access] cluster#{cluster.DebugId} ramp skipped: existing owned ramp pending.");
                    continue;
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
                    clusterRampProto,
                    placedRampOrigins,
                    reservedRampTiles,
                    useLocalSurfaceReference: isFilling || (s_dumpingProto != null && clusterRampProto == s_dumpingProto),
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
                    LogDebug($"[ATD Farming Access] cluster#{cluster.DebugId} ramp failed.");
                }
                else
                {
                    clustersPlaced++;
                    clusterDetails.Add($"({anchor.X},{anchor.Y})+{cluster.Count}: {outcome} at ({rampTopTile.X},{rampTopTile.Y})");
                    LogDebug($"[ATD Farming Access] cluster#{cluster.DebugId} ramp {outcome} at ({rampTopTile.X},{rampTopTile.Y}); placedOrigins={placedRampOrigins.Count}.");
                }
            }

            session.LastAccessRampRequestKey = requestKey;
            session.LastAccessRampDetail = inaccessibleClusters.Count == 1
                ? (clustersFailed > 0
                    ? $"Access ramp failed for {inaccessibleCount} unreachable {mode} origin(s)."
                    : $"Access ramp placed for {inaccessibleCount} unreachable {mode} origin(s): {clusterDetails[0].Split(':')[1].Trim()}.")
                : $"Access ramps for {inaccessibleClusters.Count} {mode} clusters: {clustersPlaced} placed, {clustersFailed} failed. [{string.Join("; ", clusterDetails)}]";
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
                    .Append(designation.Prototype.Id.Value).Append('@')
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
            List<FarmingAccessCluster> inaccessibleClusters,
            bool isFilling)
        {
            var sb = new StringBuilder();
            sb.Append(isFilling ? "fill" : "prep");
            foreach (TerrainDesignation designation in inaccessibleClusters
                .SelectMany(cluster => cluster.Designations)
                .OrderBy(designation => designation.OriginTileCoord.Y)
                .ThenBy(designation => designation.OriginTileCoord.X))
            {
                DesignationData data = designation.Data;
                sb.Append('|')
                    .Append(designation.Prototype.Id.Value).Append('@')
                    .Append(data.OriginTile.X).Append(',').Append(data.OriginTile.Y)
                    .Append(':')
                    .Append(data.OriginTargetHeight.Value).Append(',')
                    .Append(data.PlusXTargetHeight.Value).Append(',')
                    .Append(data.PlusXyTargetHeight.Value).Append(',')
                    .Append(data.PlusYTargetHeight.Value);
            }

            return sb.ToString();
        }

        private static List<FarmingAccessCluster> BuildFarmingAccessClusters(
            List<TerrainDesignation> designations)
        {
            var clusters = new List<FarmingAccessCluster>();
            var remaining = new HashSet<int>();
            for (int i = 0; i < designations.Count; i++)
                remaining.Add(i);

            while (remaining.Count > 0)
            {
                var cluster = new FarmingAccessCluster();
                var queue = new Queue<int>();
                int seed = -1;
                foreach (int i in remaining) { seed = i; break; }
                remaining.Remove(seed);
                queue.Enqueue(seed);
                cluster.Add(designations[seed]);

                while (queue.Count > 0)
                {
                    int idx = queue.Dequeue();
                    var toExpand = new List<int>();
                    foreach (int other in remaining)
                    {
                        if (AreFarmingDesignationsConnectedByNonRedEdge(designations[idx], designations[other]))
                            toExpand.Add(other);
                    }

                    foreach (int other in toExpand)
                    {
                        remaining.Remove(other);
                        queue.Enqueue(other);
                        cluster.Add(designations[other]);
                    }
                }

                cluster.DebugId = clusters.Count + 1;
                clusters.Add(cluster);
            }

            LogDebug($"[ATD Farming Access] built {clusters.Count} non-red cluster(s): {FormatFarmingAccessClusterList(clusters)}");
            return clusters;
        }

        private static bool AreFarmingDesignationsConnectedByNonRedEdge(
            TerrainDesignation first,
            TerrainDesignation second)
        {
            Tile2i a = first.OriginTileCoord;
            Tile2i b = second.OriginTileCoord;
            int dx = b.X - a.X;
            int dy = b.Y - a.Y;

            if (dx == 4 && dy == 0)
            {
                return first.Data.PlusXTargetHeight == second.Data.OriginTargetHeight
                    && first.Data.PlusXyTargetHeight == second.Data.PlusYTargetHeight;
            }

            if (dx == -4 && dy == 0)
            {
                return first.Data.OriginTargetHeight == second.Data.PlusXTargetHeight
                    && first.Data.PlusYTargetHeight == second.Data.PlusXyTargetHeight;
            }

            if (dx == 0 && dy == 4)
            {
                return first.Data.PlusYTargetHeight == second.Data.OriginTargetHeight
                    && first.Data.PlusXyTargetHeight == second.Data.PlusXTargetHeight;
            }

            if (dx == 0 && dy == -4)
            {
                return first.Data.OriginTargetHeight == second.Data.PlusYTargetHeight
                    && first.Data.PlusXTargetHeight == second.Data.PlusXyTargetHeight;
            }

            return false;
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
                return IsLevelingDesignation(designation)
                    || (originState.Phase == FarmingOriginPhase.Preparing && IsDumpingDesignation(designation));

            return IsDumpingDesignation(designation);
        }

        private static TerrainDesignationProto? GetFarmingAccessRampProtoForCluster(
            List<TerrainDesignation> cluster,
            bool isFilling,
            TerrainDesignationProto defaultRampProto)
        {
            if (isFilling)
                return s_dumpingProto;

            foreach (TerrainDesignation designation in cluster)
            {
                if (IsDumpingDesignation(designation))
                    return s_dumpingProto;
            }

            return defaultRampProto;
        }

        private static void AddFarmingRampPlanTile(
            TerrainDesignation designation,
            Dict<Tile2i, int> tileDepths,
            Dict<Tile2i, int> cornerHeights)
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

        private static void AddConnectedPreparationShouldersToRampPlan(
            FarmingPreparationSession session,
            List<TerrainDesignation> cluster,
            Dict<Tile2i, int> tileDepths,
            Dict<Tile2i, int> cornerHeights,
            List<TerrainDesignation> attachDesignations)
        {
            if (s_desigManager == null || session.PreparationShoulderOrigins.Count == 0)
                return;

            var queue = new Queue<Tile2i>();
            var seen = new HashSet<Tile2i>();
            foreach (TerrainDesignation designation in cluster)
            {
                Tile2i origin = designation.OriginTileCoord;
                if (seen.Add(origin))
                    queue.Enqueue(origin);
            }

            int added = 0;
            while (queue.Count > 0)
            {
                Tile2i current = queue.Dequeue();
                foreach (Tile2i direction in s_cardinalDirections)
                {
                    Tile2i neighbor = Offset(current, direction);
                    if (!seen.Add(neighbor))
                        continue;

                    if (!session.PreparationShoulderOrigins.Contains(neighbor))
                        continue;

                    var shoulder = s_desigManager.GetDesignationAt(neighbor);
                    if (!shoulder.HasValue || !IsDumpingDesignation(shoulder.Value))
                        continue;

                    AddFarmingRampPlanTile(shoulder.Value, tileDepths, cornerHeights);
                    attachDesignations.Add(shoulder.Value);
                    queue.Enqueue(neighbor);
                    added++;
                }
            }

            if (added > 0)
                LogDebug($"Farming dumping-prep access: included {added} connected preparation shoulder designation(s) in ramp planning.");
        }

        private static bool AttachSurfaceAlreadyHasOwnedRamp(
            List<TerrainDesignation> attachDesignations,
            HashSet<Tile2i> ownedRamps,
            TerrainDesignationProto rampProto)
        {
            if (s_desigManager == null || ownedRamps.Count == 0)
                return false;

            foreach (TerrainDesignation attachDesignation in attachDesignations)
            {
                Tile2i origin = attachDesignation.OriginTileCoord;
                foreach (NeighborCoord dir in NeighborCoord.All4Neighbors)
                {
                    Tile2i neighbor = origin + new RelTile2i(dir.Dx * 4, dir.Dy * 4);
                    if (!ownedRamps.Contains(neighbor))
                        continue;

                    Option<TerrainDesignation> existing = s_desigManager.GetDesignationAt(neighbor);
                    if (existing.HasValue
                        && existing.Value.Prototype == rampProto
                        && attachDesignation.IsSnappedTowards(dir))
                        return true;
                }
            }

            return false;
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
                if (currentDesignation.HasValue
                    && (currentDesignation.Value.Prototype == rampProto
                        || (!isFilling && s_dumpingProto != null && currentDesignation.Value.Prototype == s_dumpingProto)))
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

        private static bool TryFindInaccessibleFarmingAccessClusters(
            IAreaManagingTower tower,
            List<TerrainDesignation> designations,
            bool isFilling,
            out List<FarmingAccessCluster> inaccessibleClusters)
        {
            inaccessibleClusters = new List<FarmingAccessCluster>();

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
                inaccessibleClusters.AddRange(BuildFarmingAccessClusters(designations));
                LogDebug($"[ATD Farming Access] no pathable start near tower; treating all clusters inaccessible: {FormatFarmingAccessClusterList(inaccessibleClusters)}");
                return true;
            }

            List<FarmingAccessCluster> clusters = BuildFarmingAccessClusters(designations);
            var clusterByOrigin = new Dictionary<Tile2i, FarmingAccessCluster>(designations.Count);
            foreach (FarmingAccessCluster cluster in clusters)
            {
                cluster.NeedsAccess = true;
                foreach (Tile2i origin in cluster.Origins)
                    clusterByOrigin[origin] = cluster;
            }

            var targetTilesByOrigin = new Dictionary<Tile2i, HashSet<Tile2i>>();
            var originsByTargetTile = new Dictionary<Tile2i, List<Tile2i>>();
            var designationsByOrigin = new Dictionary<Tile2i, TerrainDesignation>(designations.Count);
            bool anyReadyDesignation = false;
            foreach (TerrainDesignation designation in designations)
            {
                if (!IsFarmingDesignationReadyForVehicleWork(designation, isFilling))
                {
                    LogDebug($"[ATD Farming Access] origin=({designation.OriginTileCoord.X},{designation.OriginTileCoord.Y}) proto={designation.Prototype.Id.Value} not ready for {(isFilling ? "filling" : "preparation")} vehicle work.");
                    continue;
                }

                anyReadyDesignation = true;
                HashSet<Tile2i> targets = BuildFarmingAccessTargetTiles(designation.OriginTileCoord, pathabilityProvider, pfParams);
                if (targets.Count > 0)
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
                else
                {
                    LogDebug($"[ATD Farming Access] origin=({designation.OriginTileCoord.X},{designation.OriginTileCoord.Y}) has no adjacent pathable target tiles.");
                }
            }

            if (targetTilesByOrigin.Count == 0 && !anyReadyDesignation)
            {
                // No designation is currently ready for vehicle work. Instead of immediately
                // concluding the cluster is inaccessible, perform a physical accessibility
                // check by collecting perimeter target tiles for ALL designations (ignoring
                // readiness state). This distinguishes two situations:
                //   - Cluster at surface level (perimeter tiles ARE pathable) → the BFS will
                //     find them reachable from the tower → no ramp generated.
                //   - Elevated pad with steep/unpathable perimeter tiles → no target tiles
                //     survive → cluster correctly marked as inaccessible → ramp generated.
                foreach (TerrainDesignation designation in designations)
                {
                    HashSet<Tile2i> physTargets = BuildFarmingAccessTargetTiles(
                        designation.OriginTileCoord, pathabilityProvider, pfParams);
                    if (physTargets.Count == 0)
                        continue;

                    targetTilesByOrigin[designation.OriginTileCoord] = physTargets;
                    designationsByOrigin[designation.OriginTileCoord] = designation;
                    foreach (Tile2i target in physTargets)
                    {
                        if (!originsByTargetTile.TryGetValue(target, out List<Tile2i> physOrigins))
                        {
                            physOrigins = new List<Tile2i>();
                            originsByTargetTile[target] = physOrigins;
                        }
                        physOrigins.Add(designation.OriginTileCoord);
                    }
                }

                LogDebug($"[ATD Farming Access] no designations ready; physical fallback found {targetTilesByOrigin.Count} target origin(s) for accessibility check.");
            }

            if (targetTilesByOrigin.Count == 0)
            {
                inaccessibleClusters.AddRange(clusters.Where(cluster => cluster.NeedsAccess));
                LogDebug($"[ATD Farming Access] no target tiles for any cluster; inaccessible={FormatFarmingAccessClusterList(inaccessibleClusters)}");
                return true;
            }

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
                    {
                        reachableOrigins.Add(reachedOrigin);
                        if (clusterByOrigin.TryGetValue(reachedOrigin, out FarmingAccessCluster cluster))
                        {
                            if (!cluster.HasAccess)
                                LogDebug($"[ATD Farming Access] cluster#{cluster.DebugId} reached by path target at origin=({reachedOrigin.X},{reachedOrigin.Y}).");
                            cluster.HasAccess = true;
                        }
                    }
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
                { MarkFarmingAccessOriginReachable(nbr.OriginTileCoord, clusterByOrigin, reachableOrigins); spreadQueue.Enqueue(nbr); }

                // West (-4, 0): curr.Origin == nbr.PlusX AND curr.PlusY == nbr.PlusXy
                if (designationsByOrigin.TryGetValue(o + new RelTile2i(-4, 0), out nbr)
                    && !reachableOrigins.Contains(nbr.OriginTileCoord)
                    && cd.OriginTargetHeight == nbr.Data.PlusXTargetHeight
                    && cd.PlusYTargetHeight == nbr.Data.PlusXyTargetHeight)
                { MarkFarmingAccessOriginReachable(nbr.OriginTileCoord, clusterByOrigin, reachableOrigins); spreadQueue.Enqueue(nbr); }

                // PlusY (+4, 0): curr.PlusY == nbr.Origin AND curr.PlusXy == nbr.PlusX
                if (designationsByOrigin.TryGetValue(o + new RelTile2i(0, 4), out nbr)
                    && !reachableOrigins.Contains(nbr.OriginTileCoord)
                    && cd.PlusYTargetHeight == nbr.Data.OriginTargetHeight
                    && cd.PlusXyTargetHeight == nbr.Data.PlusXTargetHeight)
                { MarkFarmingAccessOriginReachable(nbr.OriginTileCoord, clusterByOrigin, reachableOrigins); spreadQueue.Enqueue(nbr); }

                // MinusY (0, -4): curr.Origin == nbr.PlusY AND curr.PlusX == nbr.PlusXy
                if (designationsByOrigin.TryGetValue(o + new RelTile2i(0, -4), out nbr)
                    && !reachableOrigins.Contains(nbr.OriginTileCoord)
                    && cd.OriginTargetHeight == nbr.Data.PlusYTargetHeight
                    && cd.PlusXTargetHeight == nbr.Data.PlusXyTargetHeight)
                { MarkFarmingAccessOriginReachable(nbr.OriginTileCoord, clusterByOrigin, reachableOrigins); spreadQueue.Enqueue(nbr); }
            }

            inaccessibleClusters.AddRange(clusters.Where(cluster => cluster.NeedsAccess && !cluster.HasAccess));
            LogDebug($"[ATD Farming Access] reachability result reachableOrigins={reachableOrigins.Count}/{targetTilesByOrigin.Count}, visited={visited.Count}, inaccessible={FormatFarmingAccessClusterList(inaccessibleClusters)}");

            return true;
        }

        private static void MarkFarmingAccessOriginReachable(
            Tile2i origin,
            Dictionary<Tile2i, FarmingAccessCluster> clusterByOrigin,
            HashSet<Tile2i> reachableOrigins)
        {
            reachableOrigins.Add(origin);
            if (clusterByOrigin.TryGetValue(origin, out FarmingAccessCluster cluster))
            {
                if (!cluster.HasAccess)
                    LogDebug($"[ATD Farming Access] cluster#{cluster.DebugId} reached through non-red edge at origin=({origin.X},{origin.Y}).");
                cluster.HasAccess = true;
            }
        }

        private static string FormatFarmingAccessClusterList(IEnumerable<FarmingAccessCluster> clusters)
        {
            var parts = clusters
                .OrderBy(cluster => cluster.DebugId)
                .Select(FormatFarmingAccessClusterSummary)
                .ToList();
            return parts.Count == 0 ? "none" : string.Join("; ", parts);
        }

        private static string FormatFarmingAccessClusterSummary(FarmingAccessCluster cluster)
        {
            Tile2i anchor = cluster.Anchor;
            string origins = string.Join(
                " ",
                cluster.Origins
                    .OrderBy(origin => origin.Y)
                    .ThenBy(origin => origin.X)
                    .Take(6)
                    .Select(origin => $"({origin.X},{origin.Y})"));
            if (cluster.Origins.Count > 6)
                origins += $" ...+{cluster.Origins.Count - 6}";

            return $"#{cluster.DebugId}@({anchor.X},{anchor.Y}) count={cluster.Count} needs={cluster.NeedsAccess} has={cluster.HasAccess} [{origins}]";
        }

        private static bool IsFarmingDesignationReadyForVehicleWork(TerrainDesignation designation, bool isFilling)
        {
            return IsDumpingDesignation(designation)
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
