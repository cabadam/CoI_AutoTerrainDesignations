using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Mafi;
using Mafi.Collections;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.Entities;
using Mafi.Core.PathFinding;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;
using AutoTerrainDesignations.Access;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        private static readonly RelTile2i[] s_experimentalGroundDirections =
        {
            new RelTile2i(1, 0), new RelTile2i(-1, 0), new RelTile2i(0, 1), new RelTile2i(0, -1)
        };

        internal static AccessSearchResult? LastExperimentalAccessSearch { get; private set; }
        internal static AccessDesignationPlan? LastExperimentalAccessPlan { get; private set; }

        private readonly struct PlacedExperimentalDesignation
        {
            public Tile2i Origin { get; }
            public TerrainDesignationProto Proto { get; }

            public PlacedExperimentalDesignation(Tile2i origin, TerrainDesignationProto proto)
            {
                Origin = origin;
                Proto = proto;
            }
        }

        private static bool TryGetExperimentalOperation(TerrainDesignationProto proto, out bool isMining)
        {
            if (s_miningProto != null && proto == s_miningProto)
            {
                isMining = true;
                return true;
            }
            if (s_dumpingProto != null && proto == s_dumpingProto)
            {
                isMining = false;
                return true;
            }
            isMining = false;
            return false;
        }

        private static bool TryBuildExperimentalAccessSnapshot(
            IAreaManagingTower tower,
            Dict<Tile2i, int> tileDepths,
            Dict<Tile2i, int> cornerHeights,
            TerrainManager terrMgr,
            bool isMining,
            bool allowsMixedWork,
            out AccessSearchSnapshot snapshot,
            out string failureReason)
        {
            Stopwatch snapshotTimer = Stopwatch.StartNew();
            snapshot = null!;
            failureReason = string.Empty;
            if (!AccessPathSearch.ValidateCoreTransitions(out failureReason))
            {
                failureReason = "TransitionSelfTest: " + failureReason;
                return false;
            }
            if (s_desigManager == null || s_vehiclePathFindingManager == null || s_excavatorPathFindingParams == null)
            {
                failureReason = "PathfindingUnavailable";
                return false;
            }

            Tile2i boundsMin = tower.Area.BoundingBoxMin;
            Tile2i boundsMax = tower.Area.BoundingBoxMax;
            Tile2i towerCenter = tower is IEntityWithPosition positioned
                ? positioned.Position2f.Tile2i
                : new Tile2i((boundsMin.X + boundsMax.X) / 2, (boundsMin.Y + boundsMax.Y) / 2);

            var groundHeight2 = new Dictionary<Tile2i, int>();
            var terrainCenterHeight2 = new Dictionary<Tile2i, int>();
            var oceanTiles = new HashSet<Tile2i>();
            var fixedProfiles = new Dictionary<Tile2i, AccessHeightProfile>();
            var designatedOrigins = new HashSet<Tile2i>();

            foreach (TerrainDesignation designation in SelectDesignationsInAreaChunked(boundsMin, boundsMax))
            {
                Tile2i origin = designation.OriginTileCoord;
                designatedOrigins.Add(origin);
                fixedProfiles[origin] = ProfileFromDesignation(designation);
            }

            var workOrigins = new HashSet<Tile2i>();
            foreach (var pair in tileDepths)
            {
                Tile2i origin = pair.Key;
                workOrigins.Add(origin);
                int fallback = pair.Value;
                int nw = cornerHeights.TryGetValue(origin, out int value) ? value : fallback;
                int ne = cornerHeights.TryGetValue(origin + new RelTile2i(4, 0), out value) ? value : fallback;
                int se = cornerHeights.TryGetValue(origin + new RelTile2i(4, 4), out value) ? value : fallback;
                int sw = cornerHeights.TryGetValue(origin + new RelTile2i(0, 4), out value) ? value : fallback;
                fixedProfiles[origin] = new AccessHeightProfile(nw * 2, ne * 2, se * 2, sw * 2);
                designatedOrigins.Add(origin);
            }

            int minHeight2 = int.MaxValue;
            int maxHeight2 = int.MinValue;
            for (int x = boundsMin.X; x <= boundsMax.X; x++)
            {
                for (int y = boundsMin.Y; y <= boundsMax.Y; y++)
                {
                    Tile2i tile = new Tile2i(x, y);
                    if (!tower.Area.ContainsTile(tile)) continue;
                    int height2 = ToHeight2(terrMgr.GetHeight(tile).Value.ToFloat());
                    groundHeight2[tile] = height2;
                    if (terrMgr.IsOcean(tile)) oceanTiles.Add(tile);
                    minHeight2 = Math.Min(minHeight2, height2);
                    maxHeight2 = Math.Max(maxHeight2, height2);
                }
            }

            int firstOriginX = boundsMin.X & -4;
            int firstOriginY = boundsMin.Y & -4;
            for (int x = firstOriginX; x <= boundsMax.X; x += 4)
            {
                for (int y = firstOriginY; y <= boundsMax.Y; y += 4)
                {
                    Tile2i origin = new Tile2i(x, y);
                    if (!IsOriginInsideTower(tower, origin)) continue;
                    Tile2i center = origin + new RelTile2i(2, 2);
                    terrainCenterHeight2[origin] = groundHeight2.TryGetValue(center, out int h2)
                        ? h2
                        : ToHeight2(terrMgr.GetHeight(center).Value.ToFloat());
                }
            }

            foreach (AccessHeightProfile profile in fixedProfiles.Values)
            {
                minHeight2 = Math.Min(minHeight2, Math.Min(Math.Min(profile.Nw2, profile.Ne2), Math.Min(profile.Se2, profile.Sw2)));
                maxHeight2 = Math.Max(maxHeight2, Math.Max(Math.Max(profile.Nw2, profile.Ne2), Math.Max(profile.Se2, profile.Sw2)));
            }

            var durabilityCorners = BuildDurabilityCorners(fixedProfiles, s_buildingFixedHeights2ByTile);
            float landslideRunPerHeight = AutoTerrainDesignationsMod.AccessLandslideRunPerHeight;
            IPathabilityProvider provider = s_vehiclePathFindingManager.PathabilityProvider;
            VehiclePathFindingParams pathParams = s_excavatorPathFindingParams;
            try { provider.UpdateChangedTiles(); } catch { }

            var groundNodes = new HashSet<Tile2i>();
            foreach (var pair in groundHeight2)
            {
                Tile2i tile = pair.Key;
                if (pair.Value < 2 && oceanTiles.Contains(tile)) continue;
                Tile2i alignedOrigin = new Tile2i(tile.X & -4, tile.Y & -4);
                if (designatedOrigins.Contains(alignedOrigin)) continue;
                if (IsDurabilityBlocked(tile, pair.Value, durabilityCorners, landslideRunPerHeight)) continue;
                if (provider.IsPathable(tile, pathParams.PathabilityQueryMask)) groundNodes.Add(tile);
            }

            if (!TryBuildTowerReachableGround(tower, boundsMin, boundsMax,
                groundNodes, provider, pathParams,
                out HashSet<Tile2i> towerReachableGround, out Tile2i groundStart))
            {
                failureReason = "NoTowerGround";
                return false;
            }
            if (towerReachableGround.Count == 0)
            {
                failureReason = "NoTowerReachableGround";
                return false;
            }
            if (minHeight2 == int.MaxValue) { minHeight2 = 0; maxHeight2 = 0; }

            snapshot = new AccessSearchSnapshot(
                boundsMin,
                boundsMax,
                towerCenter,
                minHeight2 - 2,
                maxHeight2 + 2,
                isMining,
                allowsMixedWork,
                AutoTerrainDesignationsMod.ExperimentalAccessUseAStar,
                AutoTerrainDesignationsMod.AccessWorkDistanceScale,
                landslideRunPerHeight,
                groundHeight2,
                terrainCenterHeight2,
                fixedProfiles,
                workOrigins,
                groundNodes,
                towerReachableGround,
                s_buildingOccupiedTiles,
                oceanTiles,
                durabilityCorners,
                (origin, profile, predecessorOrigin, predecessorProfile) =>
                    BuildProspectiveWorkableHandoffs(
                        origin, profile, predecessorOrigin, predecessorProfile,
                        terrMgr, towerReachableGround));
            snapshotTimer.Stop();
            LogExperimentalAccessDebug(
                $"[ATD Experimental Access Timing] phase=snapshot algorithm={(snapshot.UseAStar ? "A*" : "Dijkstra")} " +
                $"elapsedMs={snapshotTimer.Elapsed.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)} " +
                $"goals={snapshot.GoalCount} towerGroundStart={groundStart} " +
                $"landslideSources={snapshot.LandslideSourceCount}");
            return true;
        }

        private static AccessSearchResult RunExperimentalAccessDryRun(
            AccessSearchSnapshot snapshot,
            AccessOriginCluster cluster)
        {
            Tile2i[] origins = cluster.Origins.Select(origin => origin.Origin).ToArray();
            Stopwatch searchTimer = Stopwatch.StartNew();
            AccessSearchResult result = AccessPathSearch.FindPath(snapshot, origins);
            searchTimer.Stop();
            LastExperimentalAccessSearch = result;
            string rejections = result.Rejections.Count == 0
                ? "none"
                : string.Join(",", result.Rejections
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}:{pair.Value}"));
            string reason = string.IsNullOrEmpty(result.FailureReason) ? "none" : result.FailureReason;
            string cost = result.Cost.ToString("0.##", CultureInfo.InvariantCulture);
            string searchMs = searchTimer.Elapsed.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture);
            string landslideRun = snapshot.LandslideRunPerHeight.ToString("0.##", CultureInfo.InvariantCulture);
            LogExperimentalAccessDebug($"[ATD Experimental Access] cluster={cluster.ClusterId} algorithm={(snapshot.UseAStar ? "A*" : "Dijkstra")} success={result.Success} reason={reason} start=({result.StartOrigin.X},{result.StartOrigin.Y}) goals={snapshot.GoalCount} landslideRun={landslideRun} landslideSources={snapshot.LandslideSourceCount} cost={cost} visited={result.VisitedNodes} pathNodes={result.Path.Count} searchMs={searchMs} rejections=[{rejections}]");
            if (result.Success)
            {
                LogExperimentalAccessDebug($"[ATD Experimental Access Path] cluster={cluster.ClusterId} {FormatExperimentalPath(result)}");
                Stopwatch materializeTimer = Stopwatch.StartNew();
                AccessDesignationPlan plan = AccessPathMaterializer.Materialize(snapshot, result);
                materializeTimer.Stop();
                LastExperimentalAccessPlan = plan;
                string materializeMs = materializeTimer.Elapsed.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture);
                LogExperimentalAccessDebug($"[ATD Experimental Access Plan] cluster={cluster.ClusterId} valid={plan.IsValid} reason={(string.IsNullOrEmpty(plan.FailureReason) ? "none" : plan.FailureReason)} designations={plan.Designations.Count} reused={plan.ReusedNodeCount} groundNodes={plan.GroundNodeCount} handoff=({plan.HandoffGround.X},{plan.HandoffGround.Y}) handoffOperation={plan.HandoffOperation} materializeMs={materializeMs}");
                if (plan.IsValid)
                    LogExperimentalAccessDebug($"[ATD Experimental Access Plan Tiles] cluster={cluster.ClusterId} {FormatExperimentalPlan(plan)}");
            }
            else
            {
                LastExperimentalAccessPlan = null;
                if (result.Path.Count > 0)
                    LogExperimentalAccessDebug($"[ATD Experimental Access Rejected Path] cluster={cluster.ClusterId} {FormatExperimentalPath(result)}");
            }
            return result;
        }

        private static IReadOnlyList<AccessGroundHandoff> BuildProspectiveWorkableHandoffs(
            Tile2i origin,
            AccessHeightProfile profile,
            Tile2i predecessorOrigin,
            AccessHeightProfile predecessorProfile,
            TerrainManager terrMgr,
            HashSet<Tile2i> towerReachableGround)
        {
            if (((profile.Nw2 | profile.Ne2 | profile.Se2 | profile.Sw2) & 1) != 0)
                return Array.Empty<AccessGroundHandoff>();

            var data = new DesignationData(origin,
                new HeightTilesI(profile.Nw2 / 2),
                new HeightTilesI(profile.Ne2 / 2),
                new HeightTilesI(profile.Se2 / 2),
                new HeightTilesI(profile.Sw2 / 2));
            Tile2i predecessorCenter = predecessorOrigin + new RelTile2i(2, 2);
            float predecessorGroundCenter = terrMgr.GetHeight(predecessorCenter).Value.ToFloat();
            AccessHandoffOperation operation = predecessorProfile.Center2 / 2f < predecessorGroundCenter
                ? AccessHandoffOperation.Mining
                : AccessHandoffOperation.Dumping;
            TerrainDesignationProto? proto = operation == AccessHandoffOperation.Mining
                ? s_miningProto
                : s_dumpingProto;
            if (proto == null || !TryBuildProspectiveFulfilledBitmap(
                proto, terrMgr, data, operation, out uint fulfilledBitmap))
                return Array.Empty<AccessGroundHandoff>();
            if (fulfilledBitmap == ALL_DESIGNATION_TILES_MASK
                || (fulfilledBitmap & READY_PERIMETER_MASK) == 0)
                return Array.Empty<AccessGroundHandoff>();

            var result = new List<AccessGroundHandoff>();
            for (int y = 0; y <= 4; y++)
            {
                for (int x = 0; x <= 4; x++)
                {
                    if (x != 0 && x != 4 && y != 0 && y != 4) continue;
                    uint mask = GetDesignationMask(x, y);
                    if ((fulfilledBitmap & mask) == 0) continue;
                    Tile2i tile = origin + new RelTile2i(x, y);
                    if (towerReachableGround.Contains(tile))
                        result.Add(new AccessGroundHandoff(tile, operation));
                }
            }
            return result;
        }

        private static EvaluatedAccessCandidate? EvaluateExperimentalAccessCandidate(
            AccessSearchResult result,
            AccessDesignationPlan? plan,
            Tile2i towerPosition,
            TerrainManager terrMgr)
        {
            if (!result.Success || plan == null || !plan.IsValid || plan.Designations.Count == 0)
                return null;

            var rampTiles = new List<RampTilePlan>(plan.Designations.Count);
            foreach (AccessPlannedDesignation item in plan.Designations)
            {
                if (((item.Profile.Nw2 | item.Profile.Ne2 | item.Profile.Se2 | item.Profile.Sw2) & 1) != 0)
                    return null;
                rampTiles.Add(new RampTilePlan(item.Origin,
                    item.Profile.Nw2 / 2,
                    item.Profile.Ne2 / 2,
                    item.Profile.Se2 / 2,
                    item.Profile.Sw2 / 2));
            }

            int dx = towerPosition.X - plan.HandoffGround.X;
            int dy = towerPosition.Y - plan.HandoffGround.Y;
            return new EvaluatedAccessCandidate(
                plan.HandoffGround,
                isValid: true,
                isReachableNow: true,
                mouthDistance: dx * dx + dy * dy,
                materialMoved: CalculateUselessMaterialMoved(rampTiles, terrMgr),
                designationCount: plan.Designations.Count,
                stableOrder: int.MaxValue,
                sourceCandidate: new ExperimentalAccessCandidate(result, plan));
        }

        private static bool TryPlaceExperimentalAccessCandidate(
            AccessSearchSnapshot snapshot,
            TerrainDesignationProto rampProto,
            ExperimentalAccessCandidate candidate,
            List<Tile2i>? placedRampOrigins,
            HashSet<Tile2i>? reservedRampTiles,
            out Tile2i topRowTile,
            out string failureReason)
        {
            topRowTile = default;
            failureReason = string.Empty;
            if (s_desigManager == null)
            {
                failureReason = "DesignationManagerUnavailable";
                return false;
            }

            AccessDesignationPlan placementPlan = AccessPathMaterializer.Materialize(snapshot, candidate.SearchResult);
            if (!placementPlan.IsValid || placementPlan.Designations.Count == 0)
            {
                failureReason = placementPlan.IsValid ? "EmptyPlan" : placementPlan.FailureReason;
                return false;
            }

            Tile2i terminalOrigin = default;
            bool hasGeneratedTerminal = TryGetGeneratedTerminal(
                candidate.SearchResult, out terminalOrigin);
            var placedNow = new List<PlacedExperimentalDesignation>(placementPlan.Designations.Count);
            foreach (AccessPlannedDesignation item in placementPlan.Designations)
            {
                if (((item.Profile.Nw2 | item.Profile.Ne2 | item.Profile.Se2 | item.Profile.Sw2) & 1) != 0)
                {
                    failureReason = "HalfLevelCorner";
                    RollBackExperimentalDesignations(placedNow, reservedRampTiles);
                    return false;
                }
                if (s_desigManager.GetDesignationAt(item.Origin).HasValue)
                {
                    failureReason = "DesignationAppeared";
                    RollBackExperimentalDesignations(placedNow, reservedRampTiles);
                    return false;
                }

                var data = new DesignationData(item.Origin,
                    new HeightTilesI(item.Profile.Nw2 / 2),
                    new HeightTilesI(item.Profile.Ne2 / 2),
                    new HeightTilesI(item.Profile.Se2 / 2),
                    new HeightTilesI(item.Profile.Sw2 / 2));
                TerrainDesignationProto itemProto = rampProto;
                if (hasGeneratedTerminal && item.Origin == terminalOrigin)
                {
                    TerrainDesignationProto? terminalProto = placementPlan.HandoffOperation == AccessHandoffOperation.Mining
                        ? s_miningProto
                        : placementPlan.HandoffOperation == AccessHandoffOperation.Dumping
                            ? s_dumpingProto
                            : null;
                    if (terminalProto == null)
                    {
                        failureReason = "MissingHandoffOperationProto";
                        RollBackExperimentalDesignations(placedNow, reservedRampTiles);
                        return false;
                    }
                    itemProto = terminalProto;
                }
                if (!s_desigManager.AddOrReplaceDesignation(itemProto, data))
                {
                    failureReason = "PlacementFailed";
                    RollBackExperimentalDesignations(placedNow, reservedRampTiles);
                    return false;
                }

                placedNow.Add(new PlacedExperimentalDesignation(item.Origin, itemProto));
                s_designationOriginsInArea.Add(item.Origin);
                reservedRampTiles?.Add(item.Origin);
                if (itemProto != rampProto)
                    LogExperimentalAccessDebug(
                        $"[ATD Experimental Access Terminal] origin={item.Origin} proto={itemProto.Id.Value}");
            }

            placedRampOrigins?.AddRange(placedNow.Select(item => item.Origin));
            topRowTile = placementPlan.Designations[placementPlan.Designations.Count - 1].Origin;
            LastExperimentalAccessPlan = placementPlan;
            return true;
        }

        private static void RollBackExperimentalDesignations(
            IReadOnlyList<PlacedExperimentalDesignation> designations,
            HashSet<Tile2i>? reservedRampTiles)
        {
            if (s_desigManager == null) return;
            foreach (PlacedExperimentalDesignation placed in designations)
            {
                Option<TerrainDesignation> designation = s_desigManager.GetDesignationAt(placed.Origin);
                if (designation.HasValue && designation.Value.Prototype == placed.Proto)
                    s_desigManager.RemoveDesignation(placed.Origin);
                s_designationOriginsInArea.Remove(placed.Origin);
                reservedRampTiles?.Remove(placed.Origin);
            }
        }

        private static void RollBackExperimentalDesignations(
            IReadOnlyList<Tile2i> origins,
            TerrainDesignationProto rampProto,
            HashSet<Tile2i>? reservedRampTiles)
        {
            if (s_desigManager == null) return;
            foreach (Tile2i origin in origins)
            {
                Option<TerrainDesignation> designation = s_desigManager.GetDesignationAt(origin);
                if (designation.HasValue && IsAccesswayDesignationProto(designation.Value.Prototype, rampProto))
                    s_desigManager.RemoveDesignation(origin);
                s_designationOriginsInArea.Remove(origin);
                reservedRampTiles?.Remove(origin);
            }
        }

        private static bool TryGetGeneratedTerminal(
            AccessSearchResult result,
            out Tile2i terminalOrigin)
        {
            terminalOrigin = default;
            if (result.Path.Count < 2) return false;
            AccessSearchNode terminal = result.Path[result.Path.Count - 2];
            if (terminal.IsGround || terminal.Mode == AccessSearchMode.Existing)
                return false;
            terminalOrigin = terminal.Position;
            return true;
        }

        private static string FormatExperimentalPath(AccessSearchResult result)
        {
            var parts = new List<string>(result.Path.Count + 1)
            {
                $"S@({result.StartOrigin.X},{result.StartOrigin.Y})"
            };
            foreach (AccessSearchNode node in result.Path)
            {
                string height = (node.Height2 / 2f).ToString("0.#", CultureInfo.InvariantCulture);
                parts.Add($"{FormatSearchMode(node.Mode)}@({node.Position.X},{node.Position.Y},h={height})");
            }
            return string.Join(" -> ", parts);
        }

        private static string FormatSearchMode(AccessSearchMode mode)
        {
            switch (mode)
            {
                case AccessSearchMode.Ground: return "G";
                case AccessSearchMode.Flat: return "F";
                case AccessSearchMode.XPositive: return "X+";
                case AccessSearchMode.XNegative: return "X-";
                case AccessSearchMode.YPositive: return "Y+";
                case AccessSearchMode.YNegative: return "Y-";
                case AccessSearchMode.Existing: return "Existing";
                default: return mode.ToString();
            }
        }

        private static string FormatExperimentalPlan(AccessDesignationPlan plan)
        {
            return string.Join(" -> ", plan.Designations.Select(item =>
                $"{FormatSearchMode(item.Mode)}@({item.Origin.X},{item.Origin.Y})" +
                $"[{item.Profile.Nw2 / 2},{item.Profile.Ne2 / 2},{item.Profile.Se2 / 2},{item.Profile.Sw2 / 2}]"));
        }

        private static AccessHeightProfile ProfileFromDesignation(TerrainDesignation designation)
        {
            DesignationData data = designation.Data;
            return new AccessHeightProfile(
                data.OriginTargetHeight.Value * 2,
                data.PlusXTargetHeight.Value * 2,
                data.PlusXyTargetHeight.Value * 2,
                data.PlusYTargetHeight.Value * 2);
        }

        private static List<AccessDurabilityCorner> BuildDurabilityCorners(
            Dictionary<Tile2i, AccessHeightProfile> profiles,
            IReadOnlyDictionary<Tile2i, HashSet<int>> buildingFixedHeights2ByTile)
        {
            var designationHeightsByPosition = new Dictionary<Tile2i, HashSet<int>>();
            foreach (KeyValuePair<Tile2i, AccessHeightProfile> pair in profiles)
            {
                pair.Value.AddWorldCorners(pair.Key, (position, height2) =>
                {
                    if (!designationHeightsByPosition.TryGetValue(position, out HashSet<int> heights))
                    {
                        heights = new HashSet<int>();
                        designationHeightsByPosition[position] = heights;
                    }
                    heights.Add(height2);
                });
            }

            var heightsByPosition = new Dictionary<Tile2i, HashSet<int>>();
            foreach (KeyValuePair<Tile2i, HashSet<int>> pair in designationHeightsByPosition)
            {
                // Four surrounding compatible origins make this a strictly interior
                // corner. Their stable profiles contain its exclusion envelope.
                if (pair.Value.Count == 1 && IsStrictlyInteriorDesignationCorner(pair.Key, profiles)) continue;
                heightsByPosition[pair.Key] = new HashSet<int>(pair.Value);
            }

            foreach (KeyValuePair<Tile2i, HashSet<int>> pair in buildingFixedHeights2ByTile)
            {
                if (!heightsByPosition.TryGetValue(pair.Key, out HashSet<int> heights))
                {
                    heights = new HashSet<int>();
                    heightsByPosition[pair.Key] = heights;
                }
                foreach (int height2 in pair.Value)
                    heights.Add(height2);
            }

            return heightsByPosition
                .SelectMany(pair => pair.Value.Select(height2 => new AccessDurabilityCorner(pair.Key, height2)))
                .ToList();
        }

        private static bool IsStrictlyInteriorDesignationCorner(Tile2i corner,
            IReadOnlyDictionary<Tile2i, AccessHeightProfile> profiles)
        {
            if (!profiles.TryGetValue(corner + new RelTile2i(-4, -4), out AccessHeightProfile nw)
                || !profiles.TryGetValue(corner + new RelTile2i(0, -4), out AccessHeightProfile ne)
                || !profiles.TryGetValue(corner + new RelTile2i(-4, 0), out AccessHeightProfile sw)
                || !profiles.TryGetValue(corner, out AccessHeightProfile se))
                return false;

            return ProfilesShareEdge(nw, ne, new Tile2i(1, 0))
                && ProfilesShareEdge(nw, sw, new Tile2i(0, 1))
                && ProfilesShareEdge(ne, se, new Tile2i(0, 1))
                && ProfilesShareEdge(sw, se, new Tile2i(1, 0));
        }

        private static bool ProfilesShareEdge(AccessHeightProfile first,
            AccessHeightProfile second, Tile2i direction)
        {
            first.GetEdge(direction, out int firstA, out int firstB);
            second.GetEdge(new Tile2i(-direction.X, -direction.Y), out int secondA, out int secondB);
            return firstA == secondA && firstB == secondB;
        }

        private static bool IsDurabilityBlocked(Tile2i position, int height2,
            IReadOnlyList<AccessDurabilityCorner> durabilityCorners,
            float landslideRunPerHeight)
        {
            foreach (AccessDurabilityCorner corner in durabilityCorners)
            {
                if (corner.Blocks(position, height2, landslideRunPerHeight))
                    return true;
            }
            return false;
        }

        private static bool IsOriginInsideTower(IAreaManagingTower tower, Tile2i origin)
            => tower.Area.ContainsTile(origin)
                && tower.Area.ContainsTile(origin + new RelTile2i(3, 0))
                && tower.Area.ContainsTile(origin + new RelTile2i(0, 3))
                && tower.Area.ContainsTile(origin + new RelTile2i(3, 3));

        private static bool TryBuildTowerReachableGround(
            IAreaManagingTower tower,
            Tile2i boundsMin,
            Tile2i boundsMax,
            HashSet<Tile2i> groundNodes,
            IPathabilityProvider provider,
            VehiclePathFindingParams pathParams,
            out HashSet<Tile2i> reachedGround,
            out Tile2i start)
        {
            reachedGround = new HashSet<Tile2i>();
            Tile2i towerPosition = GetTowerPosition(tower, boundsMin, boundsMax);
            if (!TryFindNearestPathableTile(provider, pathParams, towerPosition, out start))
                return false;

            int minX = Math.Min(boundsMin.X, towerPosition.X) - RAMP_ACCESS_SEARCH_MARGIN_TILES;
            int minY = Math.Min(boundsMin.Y, towerPosition.Y) - RAMP_ACCESS_SEARCH_MARGIN_TILES;
            int maxX = Math.Max(boundsMax.X, towerPosition.X) + RAMP_ACCESS_SEARCH_MARGIN_TILES;
            int maxY = Math.Max(boundsMax.Y, towerPosition.Y) + RAMP_ACCESS_SEARCH_MARGIN_TILES;

            var visited = new HashSet<Tile2i> { start };
            var queue = new Queue<Tile2i>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                Tile2i current = queue.Dequeue();
                if (groundNodes.Contains(current)) reachedGround.Add(current);
                foreach (RelTile2i direction in s_experimentalGroundDirections)
                {
                    Tile2i next = current + direction;
                    if (next.X < minX || next.X > maxX || next.Y < minY || next.Y > maxY
                        || visited.Contains(next))
                        continue;

                    bool insideManagedArea = tower.Area.ContainsTile(next);
                    if (insideManagedArea)
                    {
                        if (!groundNodes.Contains(next)) continue;
                    }
                    else if (!provider.IsPathable(next, pathParams.PathabilityQueryMask))
                    {
                        continue;
                    }

                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }
            return true;
        }

        private static int ToHeight2(float height)
            => (int)Math.Round(height * 2f, MidpointRounding.AwayFromZero);
    }
}
