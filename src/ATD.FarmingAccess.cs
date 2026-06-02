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

            // Merge spatially adjacent inaccessible clusters into super-clusters so that one
            // ramp serves the entire contiguous group. Without this, each sub-cluster would
            // get its own ramp, which often points into an adjacent cluster's footprint — an
            // area also being prepared — rather than to a stable external surface.
            inaccessibleClusters = MergeAdjacentInaccessibleClusters(inaccessibleClusters);

            // Order clusters greedily by Manhattan distance from cluster anchor to tower's
            // bounding-box center, ascending. The closest cluster is processed first; it will
            // typically have to ramp up to the surrounding tower-area surface (no other cluster
            // is connected yet, so no bridge target exists). Once placed, its origins are
            // removed from the "forbidden approach" set, so subsequent clusters can produce
            // bridge candidates that connect through it. The geometric Score inside
            // TryPlaceRampCandidates already prefers shorter accessways, so a short ramp wins
            // over a longer bridge naturally; bridges are only chosen when distance is similar.
            Tile2i towerCenterForOrdering = new Tile2i(
                (tower.Area.BoundingBoxMin.X + tower.Area.BoundingBoxMax.X) / 2,
                (tower.Area.BoundingBoxMin.Y + tower.Area.BoundingBoxMax.Y) / 2);
            inaccessibleClusters.Sort((a, b) =>
            {
                int da = System.Math.Abs(a.Anchor.X - towerCenterForOrdering.X) + System.Math.Abs(a.Anchor.Y - towerCenterForOrdering.Y);
                int db = System.Math.Abs(b.Anchor.X - towerCenterForOrdering.X) + System.Math.Abs(b.Anchor.Y - towerCenterForOrdering.Y);
                return da.CompareTo(db);
            });

            int inaccessibleCount = inaccessibleClusters.Sum(cluster => cluster.Count);
            LogFarmingPerfIfSlow(session, tower, "access check", accessSw.ElapsedMilliseconds, $"mode={(isFilling ? "filling" : "preparation")}, work={currentWork.Count}, inaccessible={inaccessibleCount}, clusters={inaccessibleClusters.Count}");
            LogDebug($"[ATD Farming Access] mode={(isFilling ? "filling" : "preparation")} work={currentWork.Count} inaccessibleClusters={inaccessibleClusters.Count} inaccessibleOrigins={inaccessibleCount} (after adjacency merge).");

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
            // Purge owned ramps that are still designated but are no longer reachable from the
            // tower. This catches ramps that became stranded after adjacent excavation lowered
            // the approach slope (changing the terrain since the ramp was placed). Clearing the
            // request key forces a fresh placement attempt next tick.
            if (PurgeUnreachableOwnedRamps(ownedRamps, tower, isFilling))
                session.LastAccessRampRequestKey = string.Empty;
            // Also reserve ramps already placed in previous ticks so we never double-stack.
            foreach (Tile2i existingRamp in ownedRamps)
                reservedRampTiles.Add(existingRamp);

            // Build the requestKey AFTER reservedRampTiles is populated so that the bucket term
            // reflects how many surrounding active-phase tiles remain. When an inaccessible cluster
            // is geometrically enclosed by an accessible one, all ramp candidates exit through the
            // accessible cluster's reserved tiles — NotAccessible is returned. As the accessible
            // cluster advances (reserved count shrinks), the bucket changes, allowing a retry every
            // ~50 tile completions instead of waiting for the inaccessible cluster itself to change.
            string requestKey = BuildFarmingAccessRampRequestKey(inaccessibleClusters, isFilling)
                + "|r=" + (reservedRampTiles.Count / 50);
            if (session.LastAccessRampRequestKey == requestKey)
            {
                string waitMode = isFilling ? "dumping" : "excavation";
                session.LastAccessRampDetail =
                    $"Access ramp already requested for {inaccessibleCount} unreachable {waitMode} origin(s); waiting for terrain/designation state to change.";
                SetFarmingAccessCache(session, workKey, ready: false, session.LastAccessRampDetail);
                return false;
            }
            int clustersPlaced = 0;
            int clustersFailed = 0;
            var clusterDetails = new List<string>();

            // Forbidden-approach set: origins of inaccessible-cluster designations that haven't
            // yet been successfully placed in this tick. On each fixpoint pass we attempt every
            // still-pending cluster; any that succeed (Crested/Truncated) are removed from the
            // set, allowing the NEXT pass's bridge candidates to approach through them. We loop
            // until a pass produces no successes — that's the natural propagation order, which
            // the simple distance-based ordering can't capture (a closer cluster may need a
            // farther one to be placed first to enable a bridge).
            var forbiddenApproachOrigins = new HashSet<Tile2i>();
            foreach (FarmingAccessCluster c in inaccessibleClusters)
                foreach (Tile2i o in c.Origins)
                    forbiddenApproachOrigins.Add(o);

            // Per-cluster final outcome — captures the last pass that touched the cluster so we
            // emit one detail line per cluster regardless of how many passes ran.
            var lastOutcomeByDebugId = new Dictionary<int, (RampPlacementOutcome outcome, Tile2i topTile, Tile2i anchor, int count)>();
            // Clusters that succeeded in any pass (don't retry them).
            var succeededDebugIds = new HashSet<int>();
            // Per-cluster previous-pass placed origins, kept so a retry can clean up the prior
            // NotAccessible fallback designation before placing a fresh one. The final pass's
            // placement (whatever it is) is left in place.
            var prevPlacedByDebugId = new Dictionary<int, List<Tile2i>>();

            for (int pass = 0; pass < inaccessibleClusters.Count; pass++)
            {
                bool anySuccessThisPass = false;
                foreach (FarmingAccessCluster cluster in inaccessibleClusters)
                {
                    if (succeededDebugIds.Contains(cluster.DebugId)) continue;

                    Tile2i anchor = cluster.Anchor;
                    TerrainDesignationProto? clusterRampProto = GetFarmingAccessRampProtoForCluster(cluster.Designations, isFilling, defaultRampProto);
                    if (clusterRampProto == null)
                    {
                        if (pass == 0)
                        {
                            clustersFailed++;
                            clusterDetails.Add($"({anchor.X},{anchor.Y})+{cluster.Count}: ramp proto unavailable");
                            LogDebug($"[ATD Farming Access] cluster#{cluster.DebugId} ramp skipped: proto unavailable. {FormatFarmingAccessClusterSummary(cluster)}");
                            succeededDebugIds.Add(cluster.DebugId); // mark done so we don't retry
                        }
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

                    LogDebug($"[ATD Farming Access] cluster#{cluster.DebugId} planning ramp pass={pass} proto={clusterRampProto.Id.Value} width={(cluster.Count < towerSettings.RampWidth ? 1 : towerSettings.RampWidth)} attach={attachDesignations.Count} reserved={reservedRampTiles.Count} forbiddenOrigins={forbiddenApproachOrigins.Count}. {FormatFarmingAccessClusterSummary(cluster)}");

                    if (AttachSurfaceAlreadyHasOwnedRamp(attachDesignations, ownedRamps, clusterRampProto))
                    {
                        if (pass == 0)
                        {
                            clusterDetails.Add($"({anchor.X},{anchor.Y})+{cluster.Count}: existing ramp pending");
                            LogDebug($"[ATD Farming Access] cluster#{cluster.DebugId} ramp skipped: existing owned ramp pending.");
                            succeededDebugIds.Add(cluster.DebugId);
                        }
                        continue;
                    }

                    // If the previous pass placed a NotAccessible fallback for this cluster, remove
                    // it now before retrying. Otherwise the next CreateAccessRamp call would stack
                    // a second ramp on top of the first (the fallback is in the world but not in
                    // ownedRamps, so the duplicate-prevention code wouldn't catch it).
                    if (prevPlacedByDebugId.TryGetValue(cluster.DebugId, out List<Tile2i>? prevOrigins) && prevOrigins != null)
                    {
                        foreach (Tile2i prev in prevOrigins)
                        {
                            ownedRamps.Remove(prev);
                            reservedRampTiles.Remove(prev);
                            Option<TerrainDesignation> placed = s_desigManager.GetDesignationAt(prev);
                            if (placed.HasValue && placed.Value.Prototype == clusterRampProto)
                                s_desigManager.RemoveDesignation(prev);
                        }
                        prevOrigins.Clear();
                    }

                    int configuredRampWidth = cluster.Count < towerSettings.RampWidth
                        ? 1
                        : towerSettings.RampWidth;

                    var placedRampOrigins = new List<Tile2i>();
                    // Temporarily exclude this cluster's own origins so its ramp candidates can
                    // attach to its own designations. Restored after the call below if placement
                    // didn't yield tower-reachable access.
                    foreach (Tile2i o in cluster.Origins)
                        forbiddenApproachOrigins.Remove(o);
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
                        out Tile2i rampTopTile,
                        forbiddenApproachClusterOrigins: forbiddenApproachOrigins);
                    bool clusterIsNowConnected =
                        outcome != RampPlacementOutcome.Failed
                        && outcome != RampPlacementOutcome.NotAccessible;
                    if (!clusterIsNowConnected)
                    {
                        foreach (Tile2i o in cluster.Origins)
                            forbiddenApproachOrigins.Add(o);
                    }

                    foreach (Tile2i origin in placedRampOrigins)
                    {
                        ownedRamps.Add(origin);
                        reservedRampTiles.Add(origin);
                    }

                    if (outcome == RampPlacementOutcome.NotAccessible)
                    {
                        // Vanilla behaviour: NotAccessible places a designation in the world even
                        // though the mouth isn't yet vehicle-reachable. Don't track in ownedRamps
                        // (so the duplicate-prevention code doesn't block a future placement once
                        // the surroundings advance). Record the placement so the NEXT pass — if
                        // any — can remove it before laying down a fresh attempt.
                        foreach (Tile2i origin in placedRampOrigins)
                            ownedRamps.Remove(origin);
                        prevPlacedByDebugId[cluster.DebugId] = new List<Tile2i>(placedRampOrigins);
                        LogDebug($"[ATD Farming Access] cluster#{cluster.DebugId} pass={pass} ramp not accessible; mouth unreachable, will retry as surrounding work advances.");
                    }
                    else if (outcome == RampPlacementOutcome.Failed)
                    {
                        LogDebug($"[ATD Farming Access] cluster#{cluster.DebugId} pass={pass} ramp failed.");
                    }
                    else
                    {
                        anySuccessThisPass = true;
                        succeededDebugIds.Add(cluster.DebugId);
                        LogDebug($"[ATD Farming Access] cluster#{cluster.DebugId} pass={pass} ramp {outcome} at ({rampTopTile.X},{rampTopTile.Y}); placedOrigins={placedRampOrigins.Count}.");
                    }

                    lastOutcomeByDebugId[cluster.DebugId] = (outcome, rampTopTile, anchor, cluster.Count);
                }

                if (!anySuccessThisPass) break;
            }

            // Emit one detail line per cluster from the final outcome.
            foreach (FarmingAccessCluster cluster in inaccessibleClusters)
            {
                if (!lastOutcomeByDebugId.TryGetValue(cluster.DebugId, out var info)) continue;
                Tile2i a = info.anchor;
                if (info.outcome == RampPlacementOutcome.NotAccessible)
                    clusterDetails.Add($"({a.X},{a.Y})+{info.count}: not accessible");
                else if (info.outcome == RampPlacementOutcome.Failed)
                {
                    clustersFailed++;
                    clusterDetails.Add($"({a.X},{a.Y})+{info.count}: failed");
                }
                else
                {
                    clustersPlaced++;
                    clusterDetails.Add($"({a.X},{a.Y})+{info.count}: {info.outcome} at ({info.topTile.X},{info.topTile.Y})");
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

        /// <summary>
        /// Scans <paramref name="ownedRamps"/> and removes any entry whose ramp designation is
        /// still present in the world but is no longer vehicle-reachable from the tower (terrain
        /// changed after placement — e.g. adjacent excavation lowered the approach slope). The
        /// designation is deleted so the tile is freed for a better placement on the next tick.
        /// Returns true if at least one ramp was purged.
        /// </summary>
        private static bool PurgeUnreachableOwnedRamps(
            HashSet<Tile2i> ownedRamps, IAreaManagingTower tower, bool isFilling)
        {
            if (s_desigManager == null || ownedRamps.Count == 0) return false;

            TerrainDesignationProto? rampProto = isFilling ? s_dumpingProto : s_miningProto;
            if (rampProto == null) return false;

            // Ensure the pathability bitmap is up-to-date before running BFS checks.
            if (s_vehiclePathFindingManager != null)
            {
                try { s_vehiclePathFindingManager.PathabilityProvider.UpdateChangedTiles(); }
                catch { }
            }

            bool anyPurged = false;
            foreach (Tile2i origin in ownedRamps.ToList())
            {
                Option<TerrainDesignation> desig = s_desigManager.GetDesignationAt(origin);
                if (!desig.HasValue || desig.Value.Prototype != rampProto)
                {
                    // Designation already gone (fulfilled or replaced) — just drop the tracking entry.
                    ownedRamps.Remove(origin);
                    continue;
                }

                if (!IsRampMouthReachableFromTower(tower, origin))
                {
                    // Ramp was accessible when placed but terrain has since changed.
                    // Delete the designation so a better placement can be found next tick.
                    s_desigManager.RemoveDesignation(origin);
                    ownedRamps.Remove(origin);
                    anyPurged = true;
                    LogDebug($"[ATD Farming Access] Purged unreachable owned ramp at ({origin.X},{origin.Y}); terrain changed after placement.");
                }
            }
            return anyPurged;
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
            int notReadyCount = 0;
            foreach (TerrainDesignation designation in designations)
            {
                if (!IsFarmingDesignationReadyForVehicleWork(designation, isFilling))
                {
                    notReadyCount++;
                    continue;
                }

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

            if (notReadyCount > 0)
                LogDebug($"[ATD Farming Access] skipped {notReadyCount} designation(s) not ready for {(isFilling ? "filling" : "preparation")} vehicle work.");

            // If no designation passed the vanilla readiness check (IsReadyToMineNonAmphibious /
            // IsReadyToDumpNonAmphibious), vanilla will not assign any vehicle to this cluster
            // regardless of whether the perimeter tiles are physically reachable. Treat it as
            // inaccessible so a ramp is placed and excavation can actually begin.

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

        /// <summary>
        /// Merges spatially adjacent inaccessible clusters into super-clusters.
        /// Two clusters are adjacent if any designation origin in one is exactly 4 tiles
        /// (one designation width) from any designation origin in the other. The merged
        /// super-cluster's full footprint is used for ramp generation, which causes the
        /// ramp candidate search to exit at the true outer boundary of the group rather
        /// than pointing inward toward a neighbour that is also being prepared.
        /// </summary>
        private static List<FarmingAccessCluster> MergeAdjacentInaccessibleClusters(
            List<FarmingAccessCluster> inaccessibleClusters)
        {
            if (inaccessibleClusters.Count <= 1)
                return inaccessibleClusters;

            // Union-Find: parent[i] is the representative of cluster i.
            int[] parent = new int[inaccessibleClusters.Count];
            for (int i = 0; i < parent.Length; i++)
                parent[i] = i;

            int Find(int x)
            {
                while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
                return x;
            }

            void Union(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra != rb) parent[ra] = rb;
            }

            // Build a lookup: origin tile → cluster index.
            var indexByOrigin = new Dictionary<Tile2i, int>();
            for (int i = 0; i < inaccessibleClusters.Count; i++)
                foreach (Tile2i origin in inaccessibleClusters[i].Origins)
                    indexByOrigin[origin] = i;

            // Merge clusters that share a designation boundary (4 tiles apart, cardinal).
            var designationOffsets = new RelTile2i[]
            {
                new RelTile2i(4, 0), new RelTile2i(-4, 0),
                new RelTile2i(0, 4), new RelTile2i(0, -4)
            };

            for (int i = 0; i < inaccessibleClusters.Count; i++)
            {
                foreach (Tile2i origin in inaccessibleClusters[i].Origins)
                {
                    foreach (RelTile2i offset in designationOffsets)
                    {
                        if (indexByOrigin.TryGetValue(origin + offset, out int j) && j != i)
                            Union(i, j);
                    }
                }
            }

            // Build merged super-clusters, preserving insertion order of the first member.
            var merged = new Dictionary<int, FarmingAccessCluster>();
            var mergedOrder = new List<int>();
            for (int i = 0; i < inaccessibleClusters.Count; i++)
            {
                int root = Find(i);
                if (!merged.TryGetValue(root, out FarmingAccessCluster mc))
                {
                    mc = new FarmingAccessCluster { DebugId = inaccessibleClusters[root].DebugId, NeedsAccess = true };
                    merged[root] = mc;
                    mergedOrder.Add(root);
                }
                foreach (TerrainDesignation d in inaccessibleClusters[i].Designations)
                    mc.Add(d);
            }

            if (merged.Count < inaccessibleClusters.Count)
                LogDebug($"[ATD Farming Access] merged {inaccessibleClusters.Count} adjacent inaccessible clusters into {merged.Count} super-cluster(s).");

            return mergedOrder.Select(root => merged[root]).ToList();
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
            // Mirror the vanilla job assignment gates exactly:
            //   Filling pass  → trucks use TryFindClosestReadyToDump  → IsReadyToDumpNonAmphibious()
            //   Prep pass     → excavators use TryFindClosestReadyToMine → IsReadyToMineNonAmphibious()
            // A LevelDesignator has both mining and dumping fulfillment functions,
            // so the isFilling flag selects which vanilla gate to match.
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
