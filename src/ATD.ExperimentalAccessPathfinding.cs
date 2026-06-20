using System;
using System.Collections.Generic;
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

            foreach (TerrainDesignation designation in s_desigManager.SelectDesignationsInArea(boundsMin, boundsMax))
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

            if (!TryFindNearestTile(groundNodes, towerCenter, out Tile2i groundStart))
            {
                failureReason = "NoTowerGround";
                return false;
            }

            HashSet<Tile2i> towerReachableGround = FloodGround(groundNodes, groundStart);
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
                durabilityCorners);
            return true;
        }

        private static AccessSearchResult RunExperimentalAccessDryRun(
            AccessSearchSnapshot snapshot,
            AccessOriginCluster cluster)
        {
            Tile2i[] origins = cluster.Origins.Select(origin => origin.Origin).ToArray();
            AccessSearchResult result = AccessPathSearch.FindPath(snapshot, origins);
            LastExperimentalAccessSearch = result;
            string rejections = result.Rejections.Count == 0
                ? "none"
                : string.Join(",", result.Rejections
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}:{pair.Value}"));
            string reason = string.IsNullOrEmpty(result.FailureReason) ? "none" : result.FailureReason;
            string cost = result.Cost.ToString("0.##", CultureInfo.InvariantCulture);
            string landslideRun = snapshot.LandslideRunPerHeight.ToString("0.##", CultureInfo.InvariantCulture);
            LogExperimentalAccessDebug($"[ATD Experimental Access] cluster={cluster.ClusterId} algorithm={(snapshot.UseAStar ? "A*" : "Dijkstra")} success={result.Success} reason={reason} start=({result.StartOrigin.X},{result.StartOrigin.Y}) goals={snapshot.GoalCount} landslideRun={landslideRun} landslideSources={snapshot.LandslideSourceCount} cost={cost} visited={result.VisitedNodes} pathNodes={result.Path.Count} rejections=[{rejections}]");
            if (result.Success)
            {
                LogExperimentalAccessDebug($"[ATD Experimental Access Path] cluster={cluster.ClusterId} {FormatExperimentalPath(result)}");
                AccessDesignationPlan plan = AccessPathMaterializer.Materialize(snapshot, result);
                LastExperimentalAccessPlan = plan;
                LogExperimentalAccessDebug($"[ATD Experimental Access Plan] cluster={cluster.ClusterId} valid={plan.IsValid} reason={(string.IsNullOrEmpty(plan.FailureReason) ? "none" : plan.FailureReason)} designations={plan.Designations.Count} reused={plan.ReusedNodeCount} groundNodes={plan.GroundNodeCount} handoff=({plan.HandoffGround.X},{plan.HandoffGround.Y})");
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
            IAreaManagingTower tower,
            Dict<Tile2i, int> tileDepths,
            Dict<Tile2i, int> cornerHeights,
            TerrainManager terrMgr,
            TerrainDesignationProto rampProto,
            bool isMining,
            bool allowsMixedWork,
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
            if (!TryBuildExperimentalAccessSnapshot(tower, tileDepths, cornerHeights, terrMgr, isMining, allowsMixedWork,
                out AccessSearchSnapshot freshSnapshot, out failureReason))
                return false;

            AccessDesignationPlan freshPlan = AccessPathMaterializer.Materialize(freshSnapshot, candidate.SearchResult);
            if (!freshPlan.IsValid || freshPlan.Designations.Count == 0)
            {
                failureReason = freshPlan.IsValid ? "EmptyPlan" : freshPlan.FailureReason;
                return false;
            }

            Tile2i terminalOrigin = default;
            Tile2i terminalIncomingDirection = default;
            bool hasGeneratedTerminal = TryGetGeneratedTerminal(
                candidate.SearchResult, out terminalOrigin, out terminalIncomingDirection);
            var placedNow = new List<PlacedExperimentalDesignation>(freshPlan.Designations.Count);
            foreach (AccessPlannedDesignation item in freshPlan.Designations)
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
                TerrainDesignationProto itemProto = hasGeneratedTerminal && item.Origin == terminalOrigin
                    ? SelectTerminalDesignationProto(
                        rampProto, item.Profile, terminalIncomingDirection, terrMgr, data)
                    : rampProto;
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
            topRowTile = freshPlan.Designations[freshPlan.Designations.Count - 1].Origin;
            LastExperimentalAccessPlan = freshPlan;
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
            out Tile2i terminalOrigin,
            out Tile2i incomingDirection)
        {
            terminalOrigin = default;
            incomingDirection = default;
            if (result.Path.Count < 3) return false;
            AccessSearchNode terminal = result.Path[result.Path.Count - 2];
            AccessSearchNode predecessor = result.Path[result.Path.Count - 3];
            if (terminal.IsGround || terminal.Mode == AccessSearchMode.Existing || predecessor.IsGround)
                return false;
            terminalOrigin = terminal.Position;
            incomingDirection = new Tile2i(
                predecessor.Position.X - terminal.Position.X,
                predecessor.Position.Y - terminal.Position.Y);
            return Math.Abs(incomingDirection.X) + Math.Abs(incomingDirection.Y) == 4;
        }

        private static TerrainDesignationProto SelectTerminalDesignationProto(
            TerrainDesignationProto levelingProto,
            AccessHeightProfile profile,
            Tile2i incomingDirection,
            TerrainManager terrMgr,
            DesignationData data)
        {
            GetProfileEdgeSamples(data.OriginTile, profile, incomingDirection,
                out Tile2i firstPosition, out int firstTarget2,
                out Tile2i secondPosition, out int secondTarget2);
            float firstDelta = firstTarget2 / 2f - terrMgr.GetHeight(firstPosition).Value.ToFloat();
            float secondDelta = secondTarget2 / 2f - terrMgr.GetHeight(secondPosition).Value.ToFloat();

            const float tolerance = 0.01f;
            TerrainDesignationProto? candidateProto;
            if (firstDelta > tolerance && secondDelta > tolerance)
                candidateProto = s_dumpingProto;
            else if (firstDelta < -tolerance && secondDelta < -tolerance)
                candidateProto = s_miningProto;
            else
            {
                Tile2i center = data.OriginTile + new RelTile2i(2, 2);
                float centerDelta = profile.Center2 / 2f - terrMgr.GetHeight(center).Value.ToFloat();
                candidateProto = centerDelta > tolerance
                    ? s_dumpingProto
                    : centerDelta < -tolerance ? s_miningProto : null;
            }

            if (candidateProto == null) return levelingProto;
            bool isReady = candidateProto == s_miningProto
                ? IsProspectiveMiningDesignationReady(candidateProto, terrMgr, data)
                : candidateProto == s_dumpingProto
                    && IsProspectiveDumpingDesignationReady(candidateProto, terrMgr, data);
            return isReady ? candidateProto : levelingProto;
        }

        private static void GetProfileEdgeSamples(
            Tile2i origin,
            AccessHeightProfile profile,
            Tile2i direction,
            out Tile2i firstPosition,
            out int firstTarget2,
            out Tile2i secondPosition,
            out int secondTarget2)
        {
            if (direction.X > 0)
            {
                firstPosition = origin + new RelTile2i(4, 0); firstTarget2 = profile.Ne2;
                secondPosition = origin + new RelTile2i(4, 4); secondTarget2 = profile.Se2;
            }
            else if (direction.X < 0)
            {
                firstPosition = origin; firstTarget2 = profile.Nw2;
                secondPosition = origin + new RelTile2i(0, 4); secondTarget2 = profile.Sw2;
            }
            else if (direction.Y > 0)
            {
                firstPosition = origin + new RelTile2i(0, 4); firstTarget2 = profile.Sw2;
                secondPosition = origin + new RelTile2i(4, 4); secondTarget2 = profile.Se2;
            }
            else
            {
                firstPosition = origin; firstTarget2 = profile.Nw2;
                secondPosition = origin + new RelTile2i(4, 0); secondTarget2 = profile.Ne2;
            }
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
            var heightsByPosition = new Dictionary<Tile2i, HashSet<int>>();
            foreach (KeyValuePair<Tile2i, AccessHeightProfile> pair in profiles)
            {
                pair.Value.AddWorldCorners(pair.Key, (position, height2) =>
                {
                    if (!heightsByPosition.TryGetValue(position, out HashSet<int> heights))
                    {
                        heights = new HashSet<int>();
                        heightsByPosition[position] = heights;
                    }
                    heights.Add(height2);
                });
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

        private static bool TryFindNearestTile(HashSet<Tile2i> tiles, Tile2i target, out Tile2i nearest)
        {
            nearest = default;
            int best = int.MaxValue;
            foreach (Tile2i tile in tiles)
            {
                int distance = Math.Abs(tile.X - target.X) + Math.Abs(tile.Y - target.Y);
                if (distance < best || (distance == best && (tile.X < nearest.X || (tile.X == nearest.X && tile.Y < nearest.Y))))
                { nearest = tile; best = distance; }
            }
            return best != int.MaxValue;
        }

        private static HashSet<Tile2i> FloodGround(HashSet<Tile2i> groundNodes, Tile2i start)
        {
            var reached = new HashSet<Tile2i> { start };
            var queue = new Queue<Tile2i>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                Tile2i current = queue.Dequeue();
                foreach (RelTile2i direction in s_experimentalGroundDirections)
                {
                    Tile2i next = current + direction;
                    if (groundNodes.Contains(next) && reached.Add(next)) queue.Enqueue(next);
                }
            }
            return reached;
        }

        private static int ToHeight2(float height)
            => (int)Math.Round(height * 2f, MidpointRounding.AwayFromZero);
    }
}
