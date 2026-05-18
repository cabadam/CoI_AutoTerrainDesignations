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

                    if (!RimPassesFarCornerCriteria(rimOrigin, d, targetHeight, terrMgr))
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
            // the corner rim tile's far 3 corners pass the far-corner height criteria.
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

                    if (!CornerRimPassesFarCornerCriteria(cornerRim, xi, yi, targetHeight, terrMgr))
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
        /// above <paramref name="targetHeight"/> - <see cref="FARMING_RIM_ALIGNMENT_HEIGHT_TOLERANCE"/>.
        /// Cardinal direction index matches the dx/dy arrays: 0=-X, 1=+X, 2=-Y, 3=+Y.
        /// </summary>
        private static bool RimPassesFarCornerCriteria(Tile2i rimOrigin, int dir, int targetHeight, TerrainManager terrMgr)
        {
            float threshold = targetHeight - FARMING_RIM_ALIGNMENT_HEIGHT_TOLERANCE;
            try
            {
                switch (dir)
                {
                    case 0: // West rim: far edge = West → Origin(NW), PlusY(SW)
                        return terrMgr.GetHeight(rimOrigin).Value.ToFloat()          > threshold
                            && terrMgr.GetHeight(rimOrigin.AddY(4)).Value.ToFloat()  > threshold;
                    case 1: // East rim: far edge = East → PlusX(NE), PlusXy(SE)
                        return terrMgr.GetHeight(rimOrigin.AddX(4)).Value.ToFloat()  > threshold
                            && terrMgr.GetHeight(rimOrigin.AddXy(4)).Value.ToFloat() > threshold;
                    case 2: // North rim: far edge = North → Origin(NW), PlusX(NE)
                        return terrMgr.GetHeight(rimOrigin).Value.ToFloat()          > threshold
                            && terrMgr.GetHeight(rimOrigin.AddX(4)).Value.ToFloat()  > threshold;
                    case 3: // South rim: far edge = South → PlusY(SW), PlusXy(SE)
                        return terrMgr.GetHeight(rimOrigin.AddY(4)).Value.ToFloat()  > threshold
                            && terrMgr.GetHeight(rimOrigin.AddXy(4)).Value.ToFloat() > threshold;
                    default: return false;
                }
            }
            catch { return false; }
        }

        /// <summary>
        /// Returns true if the far 3 corners of <paramref name="cornerRim"/> (all corners except
        /// the one facing back toward the farming area) each have terrain height above
        /// <paramref name="targetHeight"/> - <see cref="FARMING_RIM_ALIGNMENT_HEIGHT_TOLERANCE"/>.
        /// <paramref name="xi"/> and <paramref name="yi"/> are the direction indices into dx/dy.
        /// </summary>
        private static bool CornerRimPassesFarCornerCriteria(Tile2i cornerRim, int xi, int yi, int targetHeight, TerrainManager terrMgr)
        {
            float threshold = targetHeight - FARMING_RIM_ALIGNMENT_HEIGHT_TOLERANCE;
            try
            {
                float nw = terrMgr.GetHeight(cornerRim).Value.ToFloat();
                float ne = terrMgr.GetHeight(cornerRim.AddX(4)).Value.ToFloat();
                float se = terrMgr.GetHeight(cornerRim.AddXy(4)).Value.ToFloat();
                float sw = terrMgr.GetHeight(cornerRim.AddY(4)).Value.ToFloat();
                // Exclude the single inner corner facing the farming area; the other 3 must pass.
                if (xi == 0 && yi == 2) return nw > threshold && ne > threshold && sw > threshold; // NW rim: inner=SE
                if (xi == 1 && yi == 2) return nw > threshold && ne > threshold && se > threshold; // NE rim: inner=SW
                if (xi == 0 && yi == 3) return nw > threshold && se > threshold && sw > threshold; // SW rim: inner=NE
                if (xi == 1 && yi == 3) return ne > threshold && se > threshold && sw > threshold; // SE rim: inner=NW
                return false;
            }
            catch { return false; }
        }
    }
}
