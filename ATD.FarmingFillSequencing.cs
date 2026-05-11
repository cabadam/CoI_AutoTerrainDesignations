// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
// Auto Terrain Designations - Farming Fill Sequencing
using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.Terrain.Designation;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        private const int FARMING_DESIGNATION_ORIGIN_STEP = 4;
        private const int FARMING_FILL_SEQUENCE_RECHECK_TICKS = 3;
        private const int FARMING_FILL_SEQUENCE_DISTANCE_BAND = 1;
        private const int FARMING_FILL_SEQUENCE_MIN_ACTIVE_ORIGINS = 16;

        private static readonly RelTile2i[] s_farmingOriginDirections =
        {
            new RelTile2i(FARMING_DESIGNATION_ORIGIN_STEP, 0),
            new RelTile2i(-FARMING_DESIGNATION_ORIGIN_STEP, 0),
            new RelTile2i(0, FARMING_DESIGNATION_ORIGIN_STEP),
            new RelTile2i(0, -FARMING_DESIGNATION_ORIGIN_STEP)
        };

        private static bool HasQueuedFarmingFillingOrigins(FarmingPreparationSession session)
        {
            return session.Origins.Values.Any(IsQueuedForFarmingFilling);
        }

        private static bool IsQueuedForFarmingFilling(FarmingOriginSession origin)
        {
            return origin.Phase == FarmingOriginPhase.ReadyForFilling
                || (origin.Phase == FarmingOriginPhase.Done && !origin.IsFillingActivated);
        }

        private static void ClearFarmingFillingSequence(FarmingPreparationSession session)
        {
            session.LastFillingSequenceDetail = string.Empty;
            session.LastFillingSequenceActivationTick = int.MinValue;
            session.LastFillingCorridorOrigins.Clear();
            foreach (FarmingOriginSession origin in session.Origins.Values)
            {
                origin.IsFillingActivated = false;
                origin.IsFillingRampActive = false;
            }
        }

        private static bool ShouldRefreshFarmingFillingRim(FarmingPreparationSession session)
        {
            int activeFillingCount = session.Origins.Values.Count(origin => origin.Phase == FarmingOriginPhase.Filling);
            if (activeFillingCount < FARMING_FILL_SEQUENCE_MIN_ACTIVE_ORIGINS)
                return true;

            int ticksSinceActivation = s_farmingAutomationTickIndex - session.LastFillingSequenceActivationTick;
            return ticksSinceActivation < 0 || ticksSinceActivation >= FARMING_FILL_SEQUENCE_RECHECK_TICKS;
        }

        private static int ActivateNextFarmingFillingRim(
            IAreaManagingTower tower,
            FarmingPreparationSession session,
            out int failed)
        {
            failed = 0;
            if (s_desigManager == null || s_levelingProto == null)
                return 0;

            List<FarmingOriginSession> queued = session.Origins.Values
                .Where(IsQueuedForFarmingFilling)
                .OrderBy(origin => origin.Origin.Y)
                .ThenBy(origin => origin.Origin.X)
                .ToList();
            int activeFillingCount = session.Origins.Values.Count(origin => origin.Phase == FarmingOriginPhase.Filling);
            bool hasActiveFilling = activeFillingCount > 0;
            if (queued.Count == 0)
            {
                session.LastFillingSequenceDetail = "Filling sequence: no queued rim origins remain.";
                session.LastFillingCorridorOrigins.Clear();
                session.LastFillingSequenceActivationTick = s_farmingAutomationTickIndex;
                return 0;
            }

            HashSet<Tile2i> queuedOrigins = new HashSet<Tile2i>(queued.Select(origin => origin.Origin));
            HashSet<Tile2i> sequenceArea = BuildFarmingFillSequenceArea(session, queuedOrigins);
            HashSet<Tile2i> rim = FindFarmingFillRim(sequenceArea);
            Tile2i accessSeed = FindFarmingFillAccessSeed(tower, session, sequenceArea);
            HashSet<Tile2i> rampMouthGuard = BuildFarmingFillRampMouthGuard(session, sequenceArea);
            Dictionary<Tile2i, int> accessDistances = ComputeFarmingFillAccessDistances(accessSeed, sequenceArea);

            HashSet<Tile2i> activeOrigins = new HashSet<Tile2i>(rim);
            activeOrigins.IntersectWith(queuedOrigins);
            activeOrigins.ExceptWith(rampMouthGuard);
            activeOrigins = SelectFarthestFarmingFillBand(
                activeOrigins,
                accessDistances,
                accessSeed,
                Math.Max(0, FARMING_FILL_SEQUENCE_MIN_ACTIVE_ORIGINS - activeFillingCount));

            bool corridorOnly = activeOrigins.Count == 0;
            if (corridorOnly && hasActiveFilling)
            {
                session.LastFillingSequenceDetail =
                    $"Filling sequence: waiting for active far rim to expose more work; queued={queued.Count}, rim={rim.Count}, ramp mouth={rampMouthGuard.Count}, refresh={FARMING_FILL_SEQUENCE_RECHECK_TICKS} ticks.";
                session.LastFillingSequenceActivationTick = s_farmingAutomationTickIndex;
                return 0;
            }

            if (corridorOnly)
            {
                HashSet<Tile2i> fallbackOrigins = new HashSet<Tile2i>(queuedOrigins);
                fallbackOrigins.ExceptWith(rampMouthGuard);
                activeOrigins = SelectFarthestFarmingFillBand(
                    fallbackOrigins,
                    accessDistances,
                    accessSeed,
                    Math.Max(1, FARMING_FILL_SEQUENCE_MIN_ACTIVE_ORIGINS - activeFillingCount));
                if (activeOrigins.Count == 0)
                {
                    activeOrigins = SelectFarthestFarmingFillBand(
                        queuedOrigins,
                        accessDistances,
                        accessSeed,
                        Math.Max(1, FARMING_FILL_SEQUENCE_MIN_ACTIVE_ORIGINS - activeFillingCount));
                }
            }

            session.LastFillingCorridorOrigins.Clear();
            foreach (Tile2i origin in rampMouthGuard)
                session.LastFillingCorridorOrigins.Add(origin);

            int activated = 0;
            foreach (FarmingOriginSession originState in queued.Where(origin => activeOrigins.Contains(origin.Origin)))
            {
                DesignationData activationData = BuildFarmingFillActivationData(
                    originState,
                    activeOrigins,
                    sequenceArea);
                bool isRamp = !IsSameFarmingDesignationShape(activationData, originState.OriginalData);
                TerrainDesignationProto? activationProto = isRamp ? s_dumpingProto : s_levelingProto;
                if (activationProto == null)
                {
                    failed++;
                    originState.Phase = FarmingOriginPhase.Blocked;
                    originState.Detail = isRamp
                        ? "failed to place temporary inward dumping ramp: dumping prototype unavailable"
                        : "failed to restore final level designation: leveling prototype unavailable";
                    continue;
                }

                if (s_desigManager.AddOrReplaceDesignation(activationProto, activationData))
                {
                    activated++;
                    originState.IsHiddenUntilFilling = false;
                    originState.IsFillingActivated = true;
                    originState.IsFillingRampActive = isRamp;
                    originState.Phase = FarmingOriginPhase.Filling;
                    if (isRamp)
                    {
                        originState.Detail = corridorOnly
                            ? "activated protected access-side inward fill ramp"
                            : "activated far rim as inward fill ramp";
                    }
                    else
                    {
                        originState.Detail = corridorOnly
                            ? "activated protected access-side final fill; converging toward ramp"
                            : "activated far fill rim; remaining work converges toward ramp";
                    }

                    s_farmingDebugStoredDesignations.Remove(originState.Origin);
                }
                else
                {
                    failed++;
                    originState.Phase = FarmingOriginPhase.Blocked;
                    originState.Detail = "failed to restore original level designation for sequenced filling";
                }
            }

            int farthestDistance = activeOrigins.Count == 0
                ? 0
                : activeOrigins.Max(origin => GetFarmingFillAccessDistance(origin, accessDistances, accessSeed));
            session.LastFillingSequenceDetail =
                $"Filling sequence: active far rim={activated}, live={activeFillingCount + activated}, queued={queued.Count}, rim={rim.Count}, ramp mouth={rampMouthGuard.Count}, accessDistance={farthestDistance}, refresh={FARMING_FILL_SEQUENCE_RECHECK_TICKS} ticks.";
            session.LastFillingSequenceActivationTick = s_farmingAutomationTickIndex;
            return activated;
        }

        private static DesignationData BuildFarmingFillActivationData(
            FarmingOriginSession originState,
            HashSet<Tile2i> activeOrigins,
            HashSet<Tile2i> sequenceArea)
        {
            int nw = originState.TargetHeight;
            int ne = originState.TargetHeight;
            int se = originState.TargetHeight;
            int sw = originState.TargetHeight;
            int low = originState.TargetHeight - 1;
            bool hasInwardFace = false;

            foreach (RelTile2i direction in s_farmingOriginDirections)
            {
                Tile2i neighbor = originState.Origin + direction;
                if (!sequenceArea.Contains(neighbor) || activeOrigins.Contains(neighbor))
                    continue;

                hasInwardFace = true;
                if (direction.X > 0)
                {
                    ne = low;
                    se = low;
                }
                else if (direction.X < 0)
                {
                    nw = low;
                    sw = low;
                }
                else if (direction.Y > 0)
                {
                    sw = low;
                    se = low;
                }
                else if (direction.Y < 0)
                {
                    nw = low;
                    ne = low;
                }
            }

            return hasInwardFace
                ? new DesignationData(
                    originState.Origin,
                    new HeightTilesI(nw),
                    new HeightTilesI(ne),
                    new HeightTilesI(se),
                    new HeightTilesI(sw))
                : originState.OriginalData;
        }

        private static bool IsSameFarmingDesignationShape(DesignationData left, DesignationData right)
        {
            return left.OriginTargetHeight.Value == right.OriginTargetHeight.Value
                && left.PlusXTargetHeight.Value == right.PlusXTargetHeight.Value
                && left.PlusXyTargetHeight.Value == right.PlusXyTargetHeight.Value
                && left.PlusYTargetHeight.Value == right.PlusYTargetHeight.Value;
        }

        private static HashSet<Tile2i> BuildFarmingFillSequenceArea(
            FarmingPreparationSession session,
            HashSet<Tile2i> queuedOrigins)
        {
            var area = new HashSet<Tile2i>(queuedOrigins);
            foreach (FarmingOriginSession origin in session.Origins.Values)
            {
                if (origin.Phase == FarmingOriginPhase.Filling)
                    area.Add(origin.Origin);
            }

            return area;
        }

        private static HashSet<Tile2i> FindFarmingFillRim(HashSet<Tile2i> origins)
        {
            var rim = new HashSet<Tile2i>();
            foreach (Tile2i origin in origins)
            {
                foreach (RelTile2i direction in s_farmingOriginDirections)
                {
                    if (!origins.Contains(origin + direction))
                    {
                        rim.Add(origin);
                        break;
                    }
                }
            }

            return rim;
        }

        private static Tile2i FindFarmingFillAccessSeed(
            IAreaManagingTower tower,
            FarmingPreparationSession session,
            HashSet<Tile2i> queuedOrigins)
        {
            var accessPoints = new List<Tile2i>();
            if (session.FillingAccessRampOrigins.Count > 0)
                accessPoints.AddRange(session.FillingAccessRampOrigins);

            accessPoints.Add(GetTowerPosition(tower, tower.Area.BoundingBoxMin, tower.Area.BoundingBoxMax));

            Tile2i bestOrigin = queuedOrigins.First();
            int bestDistance = int.MaxValue;
            foreach (Tile2i origin in queuedOrigins)
            {
                int distance = accessPoints.Min(point => ManhattanDistance(origin, point));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestOrigin = origin;
                }
            }

            return bestOrigin;
        }

        private static List<Tile2i> FindFarmingFillCorridorPath(Tile2i accessSeed, HashSet<Tile2i> origins)
        {
            var parent = new Dictionary<Tile2i, Tile2i>();
            var distance = new Dictionary<Tile2i, int>();
            var queue = new Queue<Tile2i>();
            parent[accessSeed] = accessSeed;
            distance[accessSeed] = 0;
            queue.Enqueue(accessSeed);

            while (queue.Count > 0)
            {
                Tile2i current = queue.Dequeue();
                foreach (RelTile2i direction in s_farmingOriginDirections)
                {
                    Tile2i next = current + direction;
                    if (!origins.Contains(next) || parent.ContainsKey(next))
                        continue;

                    parent[next] = current;
                    distance[next] = distance[current] + 1;
                    queue.Enqueue(next);
                }
            }

            Dictionary<Tile2i, int> shellDepths = ComputeFarmingFillShellDepths(origins);
            Tile2i target = parent.Keys
                .OrderByDescending(origin => shellDepths.TryGetValue(origin, out int depth) ? depth : 0)
                .ThenByDescending(origin => distance[origin])
                .ThenByDescending(origin => ManhattanDistance(origin, accessSeed))
                .First();

            var path = new List<Tile2i>();
            Tile2i cursor = target;
            while (true)
            {
                path.Add(cursor);
                if (cursor == accessSeed)
                    break;
                cursor = parent[cursor];
            }

            path.Reverse();
            return path;
        }

        private static Dictionary<Tile2i, int> ComputeFarmingFillAccessDistances(
            Tile2i accessSeed,
            HashSet<Tile2i> origins)
        {
            var distances = new Dictionary<Tile2i, int>();
            var queue = new Queue<Tile2i>();
            distances[accessSeed] = 0;
            queue.Enqueue(accessSeed);

            while (queue.Count > 0)
            {
                Tile2i current = queue.Dequeue();
                foreach (RelTile2i direction in s_farmingOriginDirections)
                {
                    Tile2i next = current + direction;
                    if (!origins.Contains(next) || distances.ContainsKey(next))
                        continue;

                    distances[next] = distances[current] + 1;
                    queue.Enqueue(next);
                }
            }

            return distances;
        }

        private static HashSet<Tile2i> SelectFarthestFarmingFillBand(
            HashSet<Tile2i> candidates,
            Dictionary<Tile2i, int> accessDistances,
            Tile2i accessSeed,
            int minCount)
        {
            var selected = new HashSet<Tile2i>();
            if (candidates.Count == 0)
                return selected;

            int maxDistance = candidates.Max(origin => GetFarmingFillAccessDistance(origin, accessDistances, accessSeed));
            int minDistance = Math.Max(0, maxDistance - FARMING_FILL_SEQUENCE_DISTANCE_BAND);
            while (true)
            {
                selected.Clear();
                foreach (Tile2i origin in candidates)
                {
                    if (GetFarmingFillAccessDistance(origin, accessDistances, accessSeed) >= minDistance)
                        selected.Add(origin);
                }

                if (selected.Count >= minCount || selected.Count == candidates.Count || minDistance == 0)
                    return selected;

                minDistance--;
            }
        }

        private static int GetFarmingFillAccessDistance(
            Tile2i origin,
            Dictionary<Tile2i, int> accessDistances,
            Tile2i accessSeed)
        {
            return accessDistances.TryGetValue(origin, out int distance)
                ? distance
                : 100000 + ManhattanDistance(origin, accessSeed) / FARMING_DESIGNATION_ORIGIN_STEP;
        }

        private static Dictionary<Tile2i, int> ComputeFarmingFillShellDepths(HashSet<Tile2i> origins)
        {
            HashSet<Tile2i> rim = FindFarmingFillRim(origins);
            var depths = new Dictionary<Tile2i, int>();
            var queue = new Queue<Tile2i>();
            foreach (Tile2i origin in rim)
            {
                depths[origin] = 0;
                queue.Enqueue(origin);
            }

            while (queue.Count > 0)
            {
                Tile2i current = queue.Dequeue();
                foreach (RelTile2i direction in s_farmingOriginDirections)
                {
                    Tile2i next = current + direction;
                    if (!origins.Contains(next) || depths.ContainsKey(next))
                        continue;

                    depths[next] = depths[current] + 1;
                    queue.Enqueue(next);
                }
            }

            return depths;
        }

        private static HashSet<Tile2i> BuildFarmingFillCorridor(
            List<Tile2i> path,
            HashSet<Tile2i> origins,
            bool preferWide,
            out int corridorWidth)
        {
            var oneWide = new HashSet<Tile2i>(path);
            corridorWidth = 1;
            if (!preferWide || path.Count == 0)
                return oneWide;

            HashSet<Tile2i> leftWide = ExpandFarmingFillCorridor(path, origins, rotateLeft: true);
            HashSet<Tile2i> rightWide = ExpandFarmingFillCorridor(path, origins, rotateLeft: false);
            HashSet<Tile2i> bestWide = leftWide.Count >= rightWide.Count ? leftWide : rightWide;
            if (bestWide.Count > oneWide.Count)
            {
                corridorWidth = 2;
                return bestWide;
            }

            return oneWide;
        }

        private static HashSet<Tile2i> ExpandFarmingFillCorridor(
            List<Tile2i> path,
            HashSet<Tile2i> origins,
            bool rotateLeft)
        {
            var corridor = new HashSet<Tile2i>(path);
            for (int i = 0; i < path.Count; i++)
            {
                RelTile2i direction = GetFarmingFillPathDirection(path, i);
                RelTile2i side = rotateLeft
                    ? new RelTile2i(-direction.Y, direction.X)
                    : new RelTile2i(direction.Y, -direction.X);
                Tile2i widened = path[i] + side;
                if (origins.Contains(widened))
                    corridor.Add(widened);
            }

            return corridor;
        }

        private static HashSet<Tile2i> BuildFarmingFillRampMouthGuard(
            FarmingPreparationSession session,
            HashSet<Tile2i> origins)
        {
            var guard = new HashSet<Tile2i>();
            if (session.FillingAccessRampOrigins.Count == 0)
                return guard;

            foreach (Tile2i origin in origins)
            {
                foreach (Tile2i rampOrigin in session.FillingAccessRampOrigins)
                {
                    int dx = Math.Abs(origin.X - rampOrigin.X);
                    int dy = Math.Abs(origin.Y - rampOrigin.Y);
                    if (dx <= FARMING_DESIGNATION_ORIGIN_STEP * 2
                        && dy <= FARMING_DESIGNATION_ORIGIN_STEP * 2)
                    {
                        guard.Add(origin);
                        break;
                    }
                }
            }

            return guard;
        }

        private static RelTile2i GetFarmingFillPathDirection(List<Tile2i> path, int index)
        {
            if (path.Count <= 1)
                return new RelTile2i(FARMING_DESIGNATION_ORIGIN_STEP, 0);

            Tile2i current = path[index];
            Tile2i other = index + 1 < path.Count ? path[index + 1] : path[index - 1];
            return new RelTile2i(
                Math.Sign(other.X - current.X) * FARMING_DESIGNATION_ORIGIN_STEP,
                Math.Sign(other.Y - current.Y) * FARMING_DESIGNATION_ORIGIN_STEP);
        }

        private static HashSet<Tile2i> FindFarmingFillCorridorEndBand(
            List<Tile2i> corridorPath,
            HashSet<Tile2i> queuedOrigins)
        {
            var band = new HashSet<Tile2i>();
            for (int i = corridorPath.Count - 1; i >= 0; i--)
            {
                if (queuedOrigins.Contains(corridorPath[i]))
                {
                    band.Add(corridorPath[i]);
                    break;
                }
            }

            return band;
        }

        private static int ManhattanDistance(Tile2i left, Tile2i right)
        {
            return Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y);
        }
    }
}
