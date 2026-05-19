// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
// Auto Terrain Designations - Farming Fill Activation
using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        private const float FARMING_RIM_ALIGNMENT_HEIGHT_TOLERANCE = 0.1f;

        private static bool HasQueuedFarmingFillingOrigins(FarmingPreparationSession session)
        {
            return session.Origins.Values.Any(IsQueuedForFarmingFilling);
        }

        private static bool IsQueuedForFarmingFilling(FarmingOriginSession origin)
        {
            return origin.Phase == FarmingOriginPhase.ReadyForFilling
                || (origin.Phase == FarmingOriginPhase.Done && !origin.IsFillingActivated);
        }

        private static void ClearFarmingFillingActivation(FarmingPreparationSession session)
        {
            session.LastFillingActivationDetail = string.Empty;
            RemoveFarmingRimAlignmentDesignations(session);
            RemoveOwnedFarmingFutureRimDebrisCleanup(session);
            foreach (FarmingOriginSession origin in session.Origins.Values)
                origin.IsFillingActivated = false;
            MarkPendingFillingAreaDirty(session);
        }

        private static int ActivateFarmingFillingOrigins(
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
            if (queued.Count == 0)
            {
                session.LastFillingActivationDetail = "Filling activation: no queued origins remain.";
                return 0;
            }

            int activated = 0;
            foreach (FarmingOriginSession originState in queued)
            {
                if (s_desigManager.AddOrReplaceDesignation(s_levelingProto, originState.OriginalData))
                {
                    activated++;
                    originState.IsHiddenUntilFilling = false;
                    originState.IsFillingActivated = true;
                    originState.Phase = FarmingOriginPhase.Filling;
                    MarkPendingFillingAreaDirty(session);
                    originState.Detail = "activated final fill designation";
                    s_farmingDebugStoredDesignations.Remove(originState.Origin);
                }
                else
                {
                    failed++;
                    originState.Phase = FarmingOriginPhase.Blocked;
                    MarkPendingFillingAreaDirty(session);
                    originState.Detail = "failed to restore original level designation for filling";
                }
            }

            session.LastFillingActivationDetail =
                $"Filling activation: activated queued fill origins={activated}, failed={failed}.";
            return activated;
        }

        private static void EnsureFutureRimDebrisCleanupForPreparation(
            IAreaManagingTower tower,
            FarmingPreparationSession session,
            TerrainManager terrMgr)
        {
            if (session.FutureRimDebrisCleanupInitialized)
                return;

            session.FutureRimDebrisCleanupInitialized = true;
            session.LastFutureRimDebrisCleanupDetail = string.Empty;

            if (s_desigManager == null || s_miningProto == null || s_terrainPropsManager == null)
                return;

            if (session.Origins.Count == 0)
                return;

            Dictionary<Tile2i, int> futureRimTargets = CollectFarmingRimAlignmentCandidates(
                session,
                terrMgr,
                includeExistingLevelingDesignations: false,
                requireHeightCriteria: false);
            if (futureRimTargets.Count == 0)
                return;

            HashSet<Tile2i> debrisOrigins = CollectDebrisDesignationOrigins(tower, tower.Area, terrMgr);
            if (debrisOrigins.Count == 0)
                return;

            int placed = 0;
            foreach (KeyValuePair<Tile2i, int> rim in futureRimTargets
                .OrderBy(kvp => kvp.Key.Y)
                .ThenBy(kvp => kvp.Key.X))
            {
                Tile2i rimOrigin = rim.Key;
                if (!debrisOrigins.Contains(rimOrigin))
                    continue;

                var existing = s_desigManager.GetDesignationAt(rimOrigin);
                bool isOwnedCleanup = session.FutureRimDebrisCleanupOrigins.Contains(rimOrigin);
                if (existing.HasValue && !isOwnedCleanup)
                    continue;

                DesignationData cleanupData = BuildFlatLevelDesignationData(rimOrigin, rim.Value + 1);
                if (s_desigManager.AddOrReplaceDesignation(s_miningProto, cleanupData))
                {
                    placed++;
                    session.FutureRimDebrisCleanupOrigins.Add(rimOrigin);
                }
            }

            if (placed > 0)
            {
                session.LastFutureRimDebrisCleanupDetail =
                    $"Future rim debris cleanup: placed {placed} temporary mining designation(s).";
            }
        }

        private static void PruneFulfilledFutureRimDebrisCleanup(FarmingPreparationSession session)
        {
            if (s_desigManager == null || session.FutureRimDebrisCleanupOrigins.Count == 0)
                return;

            int removed = 0;
            foreach (Tile2i origin in session.FutureRimDebrisCleanupOrigins.ToList())
            {
                var current = s_desigManager.GetDesignationAt(origin);
                if (!current.HasValue)
                {
                    session.FutureRimDebrisCleanupOrigins.Remove(origin);
                    continue;
                }

                if (s_miningProto == null || current.Value.Prototype != s_miningProto)
                    continue;

                if (!current.Value.IsFulfilled)
                    continue;

                s_desigManager.RemoveDesignation(origin);
                session.FutureRimDebrisCleanupOrigins.Remove(origin);
                removed++;
            }

            if (removed > 0)
            {
                session.LastFutureRimDebrisCleanupDetail =
                    $"Future rim debris cleanup: completed {removed}, remaining={session.FutureRimDebrisCleanupOrigins.Count}.";
            }
        }

        private static bool HasFutureRimDebrisCleanupWork(FarmingPreparationSession session)
        {
            if (s_desigManager == null || session.FutureRimDebrisCleanupOrigins.Count == 0)
                return false;

            foreach (Tile2i origin in session.FutureRimDebrisCleanupOrigins.ToList())
            {
                var current = s_desigManager.GetDesignationAt(origin);
                if (!current.HasValue)
                {
                    session.FutureRimDebrisCleanupOrigins.Remove(origin);
                    continue;
                }

                if (s_miningProto != null && current.Value.Prototype == s_miningProto && !current.Value.IsFulfilled)
                    return true;
            }

            return false;
        }

        private static int RemoveOwnedFarmingFutureRimDebrisCleanup(FarmingPreparationSession session)
        {
            if (s_desigManager == null || session.FutureRimDebrisCleanupOrigins.Count == 0)
            {
                session.FutureRimDebrisCleanupOrigins.Clear();
                return 0;
            }

            int removed = 0;
            foreach (Tile2i origin in session.FutureRimDebrisCleanupOrigins.ToList())
            {
                var current = s_desigManager.GetDesignationAt(origin);
                if (current.HasValue && s_miningProto != null && current.Value.Prototype == s_miningProto)
                {
                    s_desigManager.RemoveDesignation(origin);
                    removed++;
                }

                session.FutureRimDebrisCleanupOrigins.Remove(origin);
            }

            return removed;
        }

        private static void RemoveFarmingRimAlignmentDesignations(FarmingPreparationSession session)
        {
            if (s_desigManager == null || session.RimAlignmentOrigins.Count == 0)
                return;

            foreach (Tile2i origin in new List<Tile2i>(session.RimAlignmentOrigins))
            {
                var current = s_desigManager.GetDesignationAt(origin);
                if (current.HasValue && s_levelingProto != null && current.Value.Prototype == s_levelingProto)
                    s_desigManager.RemoveDesignation(origin);
            }

            session.RimAlignmentOrigins.Clear();
            MarkPendingFillingAreaDirty(session);
        }

        /// <summary>
        /// After committing fill designations, inspects each rim tile (one designation step outward
        /// from the boundary of the filled area). A flat leveling designation is placed when the
        /// rim tile's far corners are all above
        /// <paramref name="targetHeight"/> - <see cref="FARMING_RIM_ALIGNMENT_HEIGHT_TOLERANCE"/>:
        /// the far 2 corners for cardinal rims, or the far 3 corners for diagonal corner rims.
        /// These designations repair terrain disturbances left by the preparation phase.
        /// </summary>
        private static int PlaceFarmingRimAlignmentDesignations(
            FarmingPreparationSession session,
            TerrainManager terrMgr)
        {
            if (s_desigManager == null || s_levelingProto == null)
                return 0;

            session.RimAlignmentOrigins.Clear();
            Dictionary<Tile2i, int> rimTargets = CollectFarmingRimAlignmentCandidates(
                session,
                terrMgr,
                includeExistingLevelingDesignations: true,
                requireHeightCriteria: true);

            int placed = 0;
            foreach (KeyValuePair<Tile2i, int> rim in rimTargets)
            {
                Tile2i rimOrigin = rim.Key;
                var existingAtRim = s_desigManager.GetDesignationAt(rimOrigin);
                if (existingAtRim.HasValue && existingAtRim.Value.Prototype == s_levelingProto)
                {
                    session.RimAlignmentOrigins.Add(rimOrigin);
                    continue;
                }

                DesignationData rimData = BuildFlatLevelDesignationData(rimOrigin, rim.Value);
                if (s_desigManager.AddOrReplaceDesignation(s_levelingProto, rimData))
                {
                    placed++;
                    session.RimAlignmentOrigins.Add(rimOrigin);
                    MarkPendingFillingAreaDirty(session);
                }
            }

            return placed;
        }

        private static Dictionary<Tile2i, int> CollectFarmingRimAlignmentCandidates(
            FarmingPreparationSession session,
            TerrainManager terrMgr,
            bool includeExistingLevelingDesignations,
            bool requireHeightCriteria)
        {
            var rimTargets = new Dictionary<Tile2i, int>();
            if (s_desigManager == null)
                return rimTargets;

            HashSet<Tile2i> originSet = new HashSet<Tile2i>(session.Origins.Keys);
            var otherSessionOrigins = new HashSet<Tile2i>();
            foreach (FarmingPreparationSession otherSession in s_farmingPreparationSessions.Values)
            {
                if (otherSession == session)
                    continue;
                foreach (Tile2i otherOrigin in otherSession.Origins.Keys)
                    otherSessionOrigins.Add(otherOrigin);
            }

            HashSet<Tile2i> rimSeen = new HashSet<Tile2i>();
            int[] dx = { -4, 4, 0, 0 };
            int[] dy = {  0, 0, -4, 4 };

            foreach (KeyValuePair<Tile2i, FarmingOriginSession> kvp in session.Origins)
            {
                Tile2i origin = kvp.Key;
                int targetHeight = kvp.Value.TargetHeight;

                for (int d = 0; d < 4; d++)
                {
                    Tile2i rimOrigin = new Tile2i(origin.X + dx[d], origin.Y + dy[d]);
                    if (originSet.Contains(rimOrigin) || !rimSeen.Add(rimOrigin))
                        continue;

                    if (!ShouldIncludeFarmingRimCandidate(rimOrigin, terrMgr, includeExistingLevelingDesignations))
                        continue;

                    if (otherSessionOrigins.Contains(rimOrigin))
                        continue;

                    if (!requireHeightCriteria
                        || IsExistingFarmingRimLevelingCandidate(rimOrigin, includeExistingLevelingDesignations)
                        || RimPassesFarCornerCriteria(rimOrigin, d, targetHeight, terrMgr))
                        rimTargets[rimOrigin] = targetHeight;
                }
            }

            int[,] cornerDirPairs = { { 0, 2 }, { 1, 2 }, { 0, 3 }, { 1, 3 } };
            foreach (KeyValuePair<Tile2i, FarmingOriginSession> kvp in session.Origins)
            {
                Tile2i origin = kvp.Key;
                int targetHeight = kvp.Value.TargetHeight;

                for (int c = 0; c < 4; c++)
                {
                    int xi = cornerDirPairs[c, 0];
                    int yi = cornerDirPairs[c, 1];
                    int dxc = dx[xi];
                    int dyc = dy[yi];
                    Tile2i cornerRim = new Tile2i(origin.X + dxc, origin.Y + dyc);
                    if (originSet.Contains(cornerRim) || !rimSeen.Add(cornerRim))
                        continue;

                    Tile2i cardinalX = new Tile2i(origin.X + dxc, origin.Y);
                    Tile2i cardinalY = new Tile2i(origin.X, origin.Y + dyc);
                    if (!rimTargets.ContainsKey(cardinalX) || !rimTargets.ContainsKey(cardinalY))
                        continue;

                    if (!ShouldIncludeFarmingRimCandidate(cornerRim, terrMgr, includeExistingLevelingDesignations))
                        continue;

                    if (otherSessionOrigins.Contains(cornerRim))
                        continue;

                    if (!requireHeightCriteria
                        || IsExistingFarmingRimLevelingCandidate(cornerRim, includeExistingLevelingDesignations)
                        || CornerRimPassesFarCornerCriteria(cornerRim, xi, yi, targetHeight, terrMgr))
                        rimTargets[cornerRim] = targetHeight;
                }
            }

            return rimTargets;
        }

        private static bool ShouldIncludeFarmingRimCandidate(
            Tile2i rimOrigin,
            TerrainManager terrMgr,
            bool includeExistingLevelingDesignations)
        {
            if (!IsFarmingDesignationOriginValid(terrMgr, rimOrigin))
                return false;

            var existing = s_desigManager!.GetDesignationAt(rimOrigin);
            if (!existing.HasValue)
                return true;

            if (!includeExistingLevelingDesignations || s_levelingProto == null)
                return false;

            return existing.Value.Prototype == s_levelingProto;
        }

        private static bool IsExistingFarmingRimLevelingCandidate(
            Tile2i rimOrigin,
            bool includeExistingLevelingDesignations)
        {
            if (!includeExistingLevelingDesignations || s_desigManager == null || s_levelingProto == null)
                return false;

            var existing = s_desigManager.GetDesignationAt(rimOrigin);
            return existing.HasValue && existing.Value.Prototype == s_levelingProto;
        }

        /// <summary>
        /// Returns true if any rim alignment designation currently has pending excavation work
        /// (i.e. terrain above its target height). When this is true, trucks must remain
        /// assigned to the tower so they can haul debris away from those leveling sites.
        /// </summary>
        private static bool HasRimExcavationWork(FarmingPreparationSession session)
        {
            if (s_desigManager == null || session.RimAlignmentOrigins.Count == 0)
                return false;
            foreach (Tile2i rimOrigin in session.RimAlignmentOrigins)
            {
                var desig = s_desigManager.GetDesignationAt(rimOrigin);
                if (desig.HasValue && desig.Value.IsMiningNotFulfilled)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true if the far 2 corners of <paramref name="rimOrigin"/> (the edge furthest
        /// from the farming area in direction <paramref name="dir"/>) each have terrain height
        /// within <see cref="FARMING_RIM_ALIGNMENT_HEIGHT_TOLERANCE"/> of <paramref name="targetHeight"/>
        /// (i.e. neither too low nor too high).
        /// Cardinal direction index matches the dx/dy arrays: 0=-X, 1=+X, 2=-Y, 3=+Y.
        /// </summary>
        private static bool RimPassesFarCornerCriteria(Tile2i rimOrigin, int dir, int targetHeight, TerrainManager terrMgr)
        {
            float lower = targetHeight - FARMING_RIM_ALIGNMENT_HEIGHT_TOLERANCE;
            float upper = targetHeight + FARMING_RIM_ALIGNMENT_HEIGHT_TOLERANCE;
            try
            {
                switch (dir)
                {
                    case 0: // West rim: far edge = West → Origin(NW), PlusY(SW)
                    {
                        float h0 = terrMgr.GetHeight(rimOrigin).Value.ToFloat();
                        float h1 = terrMgr.GetHeight(rimOrigin.AddY(4)).Value.ToFloat();
                        return h0 > lower && h0 < upper && h1 > lower && h1 < upper;
                    }
                    case 1: // East rim: far edge = East → PlusX(NE), PlusXy(SE)
                    {
                        float h0 = terrMgr.GetHeight(rimOrigin.AddX(4)).Value.ToFloat();
                        float h1 = terrMgr.GetHeight(rimOrigin.AddXy(4)).Value.ToFloat();
                        return h0 > lower && h0 < upper && h1 > lower && h1 < upper;
                    }
                    case 2: // North rim: far edge = North → Origin(NW), PlusX(NE)
                    {
                        float h0 = terrMgr.GetHeight(rimOrigin).Value.ToFloat();
                        float h1 = terrMgr.GetHeight(rimOrigin.AddX(4)).Value.ToFloat();
                        return h0 > lower && h0 < upper && h1 > lower && h1 < upper;
                    }
                    case 3: // South rim: far edge = South → PlusY(SW), PlusXy(SE)
                    {
                        float h0 = terrMgr.GetHeight(rimOrigin.AddY(4)).Value.ToFloat();
                        float h1 = terrMgr.GetHeight(rimOrigin.AddXy(4)).Value.ToFloat();
                        return h0 > lower && h0 < upper && h1 > lower && h1 < upper;
                    }
                    default: return false;
                }
            }
            catch { return false; }
        }

        /// <summary>
        /// Returns true if the far 3 corners of <paramref name="cornerRim"/> (all corners except
        /// the one facing back toward the farming area) each have terrain height within
        /// <see cref="FARMING_RIM_ALIGNMENT_HEIGHT_TOLERANCE"/> of <paramref name="targetHeight"/>
        /// (i.e. neither too low nor too high).
        /// <paramref name="xi"/> and <paramref name="yi"/> are the direction indices into dx/dy.
        /// </summary>
        private static bool CornerRimPassesFarCornerCriteria(Tile2i cornerRim, int xi, int yi, int targetHeight, TerrainManager terrMgr)
        {
            float lower = targetHeight - FARMING_RIM_ALIGNMENT_HEIGHT_TOLERANCE;
            float upper = targetHeight + FARMING_RIM_ALIGNMENT_HEIGHT_TOLERANCE;
            try
            {
                float nw = terrMgr.GetHeight(cornerRim).Value.ToFloat();
                float ne = terrMgr.GetHeight(cornerRim.AddX(4)).Value.ToFloat();
                float se = terrMgr.GetHeight(cornerRim.AddXy(4)).Value.ToFloat();
                float sw = terrMgr.GetHeight(cornerRim.AddY(4)).Value.ToFloat();
                bool nwOk = nw > lower && nw < upper;
                bool neOk = ne > lower && ne < upper;
                bool seOk = se > lower && se < upper;
                bool swOk = sw > lower && sw < upper;
                // Exclude the single inner corner facing the farming area; the other 3 must pass.
                if (xi == 0 && yi == 2) return nwOk && neOk && swOk; // NW rim: inner=SE
                if (xi == 1 && yi == 2) return nwOk && neOk && seOk; // NE rim: inner=SW
                if (xi == 0 && yi == 3) return nwOk && seOk && swOk; // SW rim: inner=NE
                if (xi == 1 && yi == 3) return neOk && seOk && swOk; // SE rim: inner=NW
                return false;
            }
            catch { return false; }
        }
    }
}
