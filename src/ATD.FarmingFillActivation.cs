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
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        private const float FARMING_RIM_ALIGNMENT_HEIGHT_TOLERANCE = 0.2f;

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

        /// <summary>
        /// After committing fill designations, inspects each rim tile (one designation step outward
        /// from the boundary of the filled area). If the probe tile one further step out has a surface
        /// height within <see cref="FARMING_RIM_ALIGNMENT_HEIGHT_TOLERANCE"/> of the adjacent origin's
        /// target height, a flat fill designation is placed on the rim tile to repair ramps or other
        /// disturbances left by the preparation phase.
        /// </summary>
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

        private static int PlaceFarmingRimAlignmentDesignations(
            FarmingPreparationSession session,
            TerrainManager terrMgr)
        {
            if (s_desigManager == null || s_levelingProto == null)
                return 0;

            session.RimAlignmentOrigins.Clear();
            HashSet<Tile2i> originSet = new HashSet<Tile2i>(session.Origins.Keys);

            // Collect origins tracked by other farming sessions so rim placement does not
            // overwrite their active leveling designations.
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

            int placed = 0;
            foreach (KeyValuePair<Tile2i, FarmingOriginSession> kvp in session.Origins)
            {
                Tile2i origin = kvp.Key;
                int targetHeight = kvp.Value.TargetHeight;

                for (int d = 0; d < 4; d++)
                {
                    Tile2i rimOrigin = new Tile2i(origin.X + dx[d], origin.Y + dy[d]);
                    if (originSet.Contains(rimOrigin))
                        continue;  // neighbor is part of the designation area, not a rim

                    if (!rimSeen.Add(rimOrigin))
                        continue;  // already evaluated from another boundary origin

                    var existingAtRim = s_desigManager.GetDesignationAt(rimOrigin);
                    if (existingAtRim.HasValue)
                    {
                        // Only override leveling designations (our own rim or a preparation-phase
                        // ramp). Non-leveling designations (mining, dumping, etc.) are left alone.
                        if (existingAtRim.Value.Prototype != s_levelingProto)
                            continue;

                        // Don't overwrite a leveling designation that belongs to another session's
                        // farming origin — it would corrupt that session's origin tracking.
                        if (otherSessionOrigins.Contains(rimOrigin))
                            continue;

                        // Our own rim designation is already in place — re-track it without
                        // replacing. Calling AddOrReplaceDesignation removes the existing
                        // designation first, which cancels haul jobs for trucks already en route.
                        session.RimAlignmentOrigins.Add(rimOrigin);
                        continue;
                    }

                    Tile2i probeOrigin = new Tile2i(rimOrigin.X + dx[d], rimOrigin.Y + dy[d]);
                    if (!ProbePassesRimCriteria(probeOrigin, d, targetHeight, terrMgr))
                        continue;

                    DesignationData rimData = BuildFlatLevelDesignationData(rimOrigin, targetHeight);
                    if (s_desigManager.AddOrReplaceDesignation(s_levelingProto, rimData))
                    {
                        placed++;
                        session.RimAlignmentOrigins.Add(rimOrigin);
                        MarkPendingFillingAreaDirty(session);
                    }
                }
            }

            // Corner rim placement: diagonal tiles where two cardinal rims meet.
            // A corner rim is placed only when both adjacent cardinal rims were placed and
            // both probe tiles in the X and Y directions from the corner pass the probe criteria.
            // xi indexes dx for the X component, yi indexes dy for the Y component.
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
                    if (originSet.Contains(cornerRim))
                        continue;

                    if (!rimSeen.Add(cornerRim))
                        continue;

                    // Both adjacent cardinal rims must have been placed this pass.
                    Tile2i cardinalX = new Tile2i(origin.X + dxc, origin.Y);
                    Tile2i cardinalY = new Tile2i(origin.X, origin.Y + dyc);
                    if (!session.RimAlignmentOrigins.Contains(cardinalX) || !session.RimAlignmentOrigins.Contains(cardinalY))
                        continue;

                    var existingAtCorner = s_desigManager.GetDesignationAt(cornerRim);
                    if (existingAtCorner.HasValue)
                    {
                        if (existingAtCorner.Value.Prototype != s_levelingProto)
                            continue;
                        if (otherSessionOrigins.Contains(cornerRim))
                            continue;

                        // Our own corner rim designation is already in place — re-track it
                        // without replacing, for the same reason as cardinal rims above.
                        session.RimAlignmentOrigins.Add(cornerRim);
                        continue;
                    }

                    // Both probe tiles (one in the X direction, one in the Y direction from the corner) must pass.
                    Tile2i probeX = new Tile2i(cornerRim.X + dxc, cornerRim.Y);
                    Tile2i probeY = new Tile2i(cornerRim.X, cornerRim.Y + dyc);
                    if (!ProbePassesRimCriteria(probeX, xi, targetHeight, terrMgr))
                        continue;
                    if (!ProbePassesRimCriteria(probeY, yi, targetHeight, terrMgr))
                        continue;

                    DesignationData cornerData = BuildFlatLevelDesignationData(cornerRim, targetHeight);
                    if (s_desigManager.AddOrReplaceDesignation(s_levelingProto, cornerData))
                    {
                        placed++;
                        session.RimAlignmentOrigins.Add(cornerRim);
                        MarkPendingFillingAreaDirty(session);
                    }
                }
            }

            return placed;
        }

        /// <summary>
        /// Returns true if the probe tile at <paramref name="probeOrigin"/> passes the rim
        /// placement criteria in direction <paramref name="probeDir"/>: either its designation
        /// corners facing the rim are at <paramref name="targetHeight"/>, or its average terrain
        /// height is within <see cref="FARMING_RIM_ALIGNMENT_HEIGHT_TOLERANCE"/>.
        /// </summary>
        private static bool ProbePassesRimCriteria(Tile2i probeOrigin, int probeDir, int targetHeight, TerrainManager terrMgr)
        {
            if (s_desigManager == null)
                return false;

            var existing = s_desigManager.GetDesignationAt(probeOrigin);
            if (existing.HasValue)
            {
                // When the probe tile has a designation, check that both corners on its edge
                // facing the rim tile are at the target height — terrain may not reflect the
                // designation target yet (work in progress).
                return ProbeEdgeFacingRimIsAtTargetHeight(existing.Value.Data, probeDir, targetHeight);
            }

            float heightSum = 0f;
            int cellCount = 0;
            try
            {
                foreach (Tile2i cell in EnumerateDesignatableTileCells(probeOrigin))
                {
                    heightSum += terrMgr.GetHeight(cell).Value.ToFloat();
                    cellCount++;
                }
            }
            catch
            {
                return false;  // probe tile out of bounds or otherwise inaccessible
            }

            if (cellCount == 0)
                return false;

            return Math.Abs(heightSum / cellCount - targetHeight) <= FARMING_RIM_ALIGNMENT_HEIGHT_TOLERANCE;
        }

        /// <summary>
        /// Returns true if the two corners on the edge of <paramref name="data"/> that face back
        /// toward the rim tile (i.e. the edge opposite to the direction traveled) are both equal to
        /// <paramref name="targetHeight"/>.
        /// Direction mapping (dx/dy arrays, index → movement direction):
        ///   0 = -X (probe is West  of rim) → probe's East  edge: PlusX,   PlusXy
        ///   1 = +X (probe is East  of rim) → probe's West  edge: Origin,   PlusY
        ///   2 = -Y (probe is South of rim) → probe's North edge: Origin,   PlusX
        ///   3 = +Y (probe is North of rim) → probe's South edge: PlusY,    PlusXy
        /// </summary>
        private static bool ProbeEdgeFacingRimIsAtTargetHeight(DesignationData data, int dir, int targetHeight)
        {
            switch (dir)
            {
                case 0: return data.PlusXTargetHeight.Value  == targetHeight && data.PlusXyTargetHeight.Value == targetHeight;
                case 1: return data.OriginTargetHeight.Value == targetHeight && data.PlusYTargetHeight.Value  == targetHeight;
                case 2: return data.OriginTargetHeight.Value == targetHeight && data.PlusXTargetHeight.Value  == targetHeight;
                case 3: return data.PlusYTargetHeight.Value  == targetHeight && data.PlusXyTargetHeight.Value == targetHeight;
                default: return false;
            }
        }
    }
}
