using System;
using System.Collections.Generic;
using Mafi;

namespace AutoTerrainDesignations.Access
{
    internal static class AccessPathSearch
    {
        private static readonly Tile2i[] s_originDirections =
        {
            new Tile2i(4, 0), new Tile2i(-4, 0), new Tile2i(0, 4), new Tile2i(0, -4)
        };

        private static readonly RelTile2i[] s_tileDirections =
        {
            new RelTile2i(1, 0), new RelTile2i(-1, 0), new RelTile2i(0, 1), new RelTile2i(0, -1)
        };

        private static readonly AccessSearchMode[] s_vModes =
        {
            AccessSearchMode.Flat,
            AccessSearchMode.XPositive,
            AccessSearchMode.XNegative,
            AccessSearchMode.YPositive,
            AccessSearchMode.YNegative
        };

        private const int MAX_VISITED_NODES = 250000;

        public static bool ValidateCoreTransitions(out string failure)
        {
            AccessHeightProfile.TryForMode(AccessSearchMode.Flat, 0, out AccessHeightProfile flat);
            AccessHeightProfile.TryForMode(AccessSearchMode.XPositive, 1, out AccessHeightProfile xPositive);

            if (!TrySolveSuccessor(flat, new Tile2i(4, 0), AccessSearchMode.XPositive, out AccessHeightProfile rise)
                || rise.Center2 != 1)
            { failure = "F-to-X+ should rise by half a level"; return false; }
            if (!TrySolveSuccessor(xPositive, new Tile2i(4, 0), AccessSearchMode.XPositive, out AccessHeightProfile continueRise)
                || continueRise.Center2 != 3)
            { failure = "X+ continuation should rise by one level"; return false; }
            if (!TrySolveSuccessor(xPositive, new Tile2i(4, 0), AccessSearchMode.Flat, out AccessHeightProfile landing)
                || landing.Center2 != 2)
            { failure = "X+ should terminate on a flat landing"; return false; }
            if (!TrySolveSuccessor(xPositive, new Tile2i(0, 4), AccessSearchMode.XPositive, out AccessHeightProfile strafe)
                || strafe.Center2 != xPositive.Center2)
            { failure = "perpendicular X+ strafe should preserve height"; return false; }
            if (TrySolveSuccessor(xPositive, new Tile2i(0, 4), AccessSearchMode.XNegative, out _))
            { failure = "opposite signed perpendicular slopes must fight"; return false; }
            if (TrySolveSuccessor(xPositive, new Tile2i(4, 0), AccessSearchMode.YPositive, out _))
            { failure = "axis turn must require a flat landing"; return false; }

            var groundHeights = new Dictionary<Tile2i, int>();
            for (int x = 0; x <= 20; x++)
                for (int y = 0; y <= 20; y++)
                    groundHeights[new Tile2i(x, y)] = 0;
            var terrainCenters = new Dictionary<Tile2i, int>();
            for (int x = 0; x <= 16; x += 4)
                for (int y = 0; y <= 16; y += 4)
                    terrainCenters[new Tile2i(x, y)] = 0;
            Tile2i fixtureStart = new Tile2i(4, 4);
            Tile2i fixtureWorkNeighbor = new Tile2i(8, 4);
            Tile2i fixtureGoal = new Tile2i(10, 6);
            var fixture = new AccessSearchSnapshot(
                new Tile2i(0, 0), new Tile2i(20, 20), new Tile2i(18, 18),
                -2, 2, true, false, 1f, 1f,
                groundHeights,
                terrainCenters,
                new Dictionary<Tile2i, AccessHeightProfile>
                {
                    [fixtureStart] = flat,
                    [fixtureWorkNeighbor] = flat,
                },
                new[] { fixtureStart, fixtureWorkNeighbor },
                new[] { fixtureGoal },
                new[] { fixtureGoal },
                Array.Empty<Tile2i>(),
                new[] { new AccessDurabilityCorner(new Tile2i(16, 16), 0) });
            if (!fixture.IsDurabilityBlocked(new Tile2i(17, 17), 4))
            { failure = "nearby higher point should be durability-blocked"; return false; }
            if (!fixture.IsDurabilityBlocked(new Tile2i(17, 17), -4))
            { failure = "nearby lower point should be durability-blocked"; return false; }
            if (fixture.IsDurabilityBlocked(new Tile2i(16, 0), 4))
            { failure = "distant same-axis point must not be durability-blocked"; return false; }
            if (fixture.IsDurabilityBlocked(new Tile2i(17, 17), 0))
            { failure = "equal-height point must not be durability-blocked"; return false; }
            var scaleSource = new AccessDurabilityCorner(new Tile2i(16, 16), 0);
            if (scaleSource.Blocks(new Tile2i(18, 18), 4, 1f)
                || !scaleSource.Blocks(new Tile2i(18, 18), 4, 2f))
            { failure = "landslide horizontal-run scale should widen the exclusion envelope"; return false; }
            AccessSearchResult fixtureResult = FindPath(fixture, new[] { fixtureStart });
            if (!fixtureResult.Success || fixtureResult.Path.Count < 2
                || fixtureResult.Path[0].Position != fixtureWorkNeighbor
                || fixtureResult.Path[0].Mode != AccessSearchMode.Existing
                || fixtureResult.Path[fixtureResult.Path.Count - 1].Mode != AccessSearchMode.Ground)
            { failure = "synthetic work-origin traversal and V-to-G Dijkstra fixture failed"; return false; }
            AccessDesignationPlan reusedPlan = AccessPathMaterializer.Materialize(fixture, fixtureResult);
            if (!reusedPlan.IsValid || reusedPlan.Designations.Count != 0 || reusedPlan.ReusedNodeCount != 1)
            { failure = "synthetic reused-path materialization fixture failed"; return false; }

            Tile2i generatedOrigin = new Tile2i(4, 8);
            Tile2i generatedGoal = new Tile2i(6, 10);
            var generatedFixture = new AccessSearchSnapshot(
                new Tile2i(0, 0), new Tile2i(20, 20), new Tile2i(18, 18),
                -2, 2, true, false, 1f, 1f,
                groundHeights,
                terrainCenters,
                new Dictionary<Tile2i, AccessHeightProfile> { [fixtureStart] = flat },
                new[] { fixtureStart },
                new[] { generatedGoal },
                new[] { generatedGoal },
                Array.Empty<Tile2i>(),
                Array.Empty<AccessDurabilityCorner>());
            var generatedResult = new AccessSearchResult(true, string.Empty, fixtureStart,
                new AccessSearchNode[]
                {
                    new AccessSearchNode(generatedOrigin, 0, AccessSearchMode.Flat),
                    new AccessSearchNode(generatedGoal, 0, AccessSearchMode.Ground),
                }, 6f, 2, new Dictionary<string, int>());
            AccessDesignationPlan generatedPlan = AccessPathMaterializer.Materialize(generatedFixture, generatedResult);
            if (!generatedPlan.IsValid || generatedPlan.Designations.Count != 1
                || generatedPlan.Designations[0].Origin != generatedOrigin
                || generatedPlan.Designations[0].Mode != AccessSearchMode.Flat)
            { failure = "synthetic generated-path materialization fixture failed"; return false; }

            Tile2i turnStart = new Tile2i(4, 12);
            var turnFixture = new AccessSearchSnapshot(
                new Tile2i(0, 0), new Tile2i(20, 20), new Tile2i(18, 18),
                -2, 2, true, false, 1f, 1f,
                groundHeights,
                terrainCenters,
                new Dictionary<Tile2i, AccessHeightProfile> { [turnStart] = flat },
                new[] { turnStart },
                new[] { fixtureGoal },
                new[] { fixtureGoal },
                Array.Empty<Tile2i>(),
                Array.Empty<AccessDurabilityCorner>());
            var turnResult = new AccessSearchResult(true, string.Empty, turnStart,
                new AccessSearchNode[]
                {
                    new AccessSearchNode(new Tile2i(4, 8), 0, AccessSearchMode.Flat),
                    new AccessSearchNode(new Tile2i(4, 4), 0, AccessSearchMode.Flat),
                    new AccessSearchNode(new Tile2i(8, 4), 0, AccessSearchMode.Flat),
                    new AccessSearchNode(fixtureGoal, 0, AccessSearchMode.Ground),
                }, 14f, 4, new Dictionary<string, int>());
            AccessDesignationPlan turnPlan = AccessPathMaterializer.Materialize(turnFixture, turnResult);
            if (!turnPlan.IsValid || turnPlan.Designations.Count != 3)
            { failure = "legal diagonal self-contact at flat turn should materialize"; return false; }

            failure = string.Empty;
            return true;
        }

        public static AccessSearchResult FindPath(AccessSearchSnapshot snapshot, IReadOnlyList<Tile2i> clusterOrigins)
        {
            var rejections = new Dictionary<string, int>(StringComparer.Ordinal);
            Tile2i startOrigin = SelectStart(clusterOrigins);
            if (clusterOrigins.Count == 0)
                return Failed("NoStart", startOrigin, 0, rejections);
            if (snapshot.GoalCount == 0)
                return Failed("NoGoalGround", startOrigin, 0, rejections);
            if (!snapshot.TryGetFixedProfile(startOrigin, out AccessHeightProfile startProfile))
                return Failed("NoStartProfile", startOrigin, 0, rejections);

            var distance = new Dictionary<AccessSearchNode, float>();
            var previous = new Dictionary<AccessSearchNode, AccessSearchNode>();
            var queue = new MinQueue();

            foreach (Tile2i direction in s_originDirections)
            {
                Tile2i nextOrigin = new Tile2i(startOrigin.X + direction.X, startOrigin.Y + direction.Y);
                AddOriginSuccessors(snapshot, startOrigin, startProfile, nextOrigin, direction,
                    current: default, hasCurrent: false, baseCost: 0f, distance, previous, queue, rejections);
            }

            if (queue.Count == 0)
                return Failed("NoInitialSuccessor", startOrigin, 0, rejections);

            int visited = 0;
            while (queue.Count > 0 && visited < MAX_VISITED_NODES)
            {
                QueueEntry entry = queue.Pop();
                if (!distance.TryGetValue(entry.Node, out float known) || entry.PathCost > known + 0.0001f)
                    continue;

                AccessSearchNode current = entry.Node;
                visited++;
                if (current.IsGround && snapshot.IsGoalGroundNode(current.Position))
                {
                    List<AccessSearchNode> path = Reconstruct(current, previous);
                    if (!ValidateGeneratedPath(path, snapshot, out string validationReason))
                        return new AccessSearchResult(false, validationReason, startOrigin, path, known, visited, rejections);
                    return new AccessSearchResult(true, string.Empty, startOrigin, path, known, visited, rejections);
                }

                if (current.IsGround)
                    ExpandGround(snapshot, current, known, distance, previous, queue, rejections);
                else if (TryGetProfile(snapshot, current, out AccessHeightProfile currentProfile))
                    ExpandOrigin(snapshot, current, currentProfile, known, distance, previous, queue, rejections);
                else
                    Reject(rejections, "MissingProfile");
            }

            return Failed(visited >= MAX_VISITED_NODES ? "VisitedLimit" : "NoPath", startOrigin, visited, rejections);
        }

        private static Tile2i SelectStart(IReadOnlyList<Tile2i> origins)
        {
            if (origins.Count == 0) return default;
            long sumX = 0, sumY = 0;
            foreach (Tile2i origin in origins) { sumX += origin.X + 2; sumY += origin.Y + 2; }
            Tile2i best = origins[0];
            long bestDistance = long.MaxValue;
            foreach (Tile2i origin in origins)
            {
                long dx = Math.Abs((long)(origin.X + 2) * origins.Count - sumX);
                long dy = Math.Abs((long)(origin.Y + 2) * origins.Count - sumY);
                long candidate = dx + dy;
                if (candidate < bestDistance
                    || (candidate == bestDistance && (origin.X < best.X || (origin.X == best.X && origin.Y < best.Y))))
                { best = origin; bestDistance = candidate; }
            }
            return best;
        }

        private static void ExpandOrigin(AccessSearchSnapshot snapshot, AccessSearchNode current,
            AccessHeightProfile currentProfile, float currentCost,
            Dictionary<AccessSearchNode, float> distance,
            Dictionary<AccessSearchNode, AccessSearchNode> previous,
            MinQueue queue, Dictionary<string, int> rejections)
        {
            foreach (Tile2i groundTile in GetHandoffTiles(snapshot, current.Position, currentProfile))
            {
                if (!snapshot.TryGetGroundHeight2(groundTile, out int groundHeight2)) continue;
                var ground = new AccessSearchNode(groundTile, groundHeight2, AccessSearchMode.Ground);
                Relax(snapshot, current, ground, currentCost + Manhattan(current.CostPosition, groundTile),
                    distance, previous, queue);
            }

            foreach (Tile2i direction in s_originDirections)
            {
                Tile2i nextOrigin = new Tile2i(current.Position.X + direction.X, current.Position.Y + direction.Y);
                AddOriginSuccessors(snapshot, current.Position, currentProfile, nextOrigin, direction,
                    current, true, currentCost, distance, previous, queue, rejections);
            }
        }

        private static void AddOriginSuccessors(AccessSearchSnapshot snapshot,
            Tile2i currentOrigin, AccessHeightProfile currentProfile, Tile2i nextOrigin, Tile2i direction,
            AccessSearchNode current, bool hasCurrent, float baseCost,
            Dictionary<AccessSearchNode, float> distance,
            Dictionary<AccessSearchNode, AccessSearchNode> previous,
            MinQueue queue, Dictionary<string, int> rejections)
        {
            if (!snapshot.IsOriginInside(nextOrigin)) { Reject(rejections, "HorizontalBounds"); return; }

            if (snapshot.TryGetFixedProfile(nextOrigin, out AccessHeightProfile fixedProfile))
            {
                if (!EdgesMatch(currentProfile, fixedProfile, direction)) { Reject(rejections, "FixedEdgeMismatch"); return; }
                var existing = new AccessSearchNode(nextOrigin, fixedProfile.Center2, AccessSearchMode.Existing);
                Relax(snapshot, current, existing, baseCost + 4f, distance, previous, queue, hasCurrent);
                return;
            }

            foreach (AccessSearchMode mode in s_vModes)
            {
                if (!TrySolveSuccessor(currentProfile, direction, mode, out AccessHeightProfile nextProfile))
                { Reject(rejections, "EdgeProfile"); continue; }
                if (!snapshot.IsCandidateProfileFeasible(nextOrigin, nextProfile, out string reason))
                { Reject(rejections, reason); continue; }

                var next = new AccessSearchNode(nextOrigin, nextProfile.Center2, mode);
                float work = Math.Abs(nextProfile.Center2 - snapshot.GetTerrainCenterHeight2(nextOrigin)) / 2f;
                float nextCost = baseCost + 4f + snapshot.WorkDistanceScale * work;
                Relax(snapshot, current, next, nextCost, distance, previous, queue, hasCurrent);
            }
        }

        private static void ExpandGround(AccessSearchSnapshot snapshot, AccessSearchNode current, float currentCost,
            Dictionary<AccessSearchNode, float> distance,
            Dictionary<AccessSearchNode, AccessSearchNode> previous,
            MinQueue queue, Dictionary<string, int> rejections)
        {
            foreach (RelTile2i direction in s_tileDirections)
            {
                Tile2i nextTile = current.Position + direction;
                if (!snapshot.IsGroundNode(nextTile) || !snapshot.TryGetGroundHeight2(nextTile, out int height2)) continue;
                var next = new AccessSearchNode(nextTile, height2, AccessSearchMode.Ground);
                Relax(snapshot, current, next, currentCost + 1f, distance, previous, queue);
            }

            foreach (Tile2i origin in CandidateOriginsAtGroundTile(current.Position))
            {
                foreach (AccessSearchMode mode in s_vModes)
                {
                    int center2 = snapshot.GetTerrainCenterHeight2(origin);
                    for (int delta = -3; delta <= 3; delta++)
                    {
                        if (!AccessHeightProfile.TryForMode(mode, center2 + delta, out AccessHeightProfile profile)) continue;
                        if (!snapshot.IsCandidateProfileFeasible(origin, profile, out string reason))
                        { Reject(rejections, reason); continue; }
                        if (!ContainsHandoffTile(snapshot, origin, profile, current.Position)) continue;
                        var next = new AccessSearchNode(origin, profile.Center2, mode);
                        float work = Math.Abs(profile.Center2 - center2) / 2f;
                        float cost = currentCost + Manhattan(current.Position, next.CostPosition)
                            + snapshot.WorkDistanceScale * work;
                        Relax(snapshot, current, next, cost, distance, previous, queue);
                    }
                }
            }
        }

        private static IEnumerable<Tile2i> CandidateOriginsAtGroundTile(Tile2i tile)
        {
            var seen = new HashSet<Tile2i>();
            int baseX = tile.X & -4;
            int baseY = tile.Y & -4;
            Tile2i[] candidates =
            {
                new Tile2i(baseX, baseY), new Tile2i(baseX - 4, baseY),
                new Tile2i(baseX, baseY - 4), new Tile2i(baseX - 4, baseY - 4),
                new Tile2i(tile.X - 2, tile.Y - 2)
            };
            foreach (Tile2i candidate in candidates)
                if ((candidate.X & 3) == 0 && (candidate.Y & 3) == 0 && seen.Add(candidate))
                    yield return candidate;
        }

        private static IEnumerable<Tile2i> GetHandoffTiles(AccessSearchSnapshot snapshot, Tile2i origin, AccessHeightProfile profile)
        {
            Tile2i center = origin + new RelTile2i(2, 2);
            Tile2i[] corners =
            {
                origin, origin + new RelTile2i(4, 0), origin + new RelTile2i(4, 4), origin + new RelTile2i(0, 4)
            };
            int[] heights = { profile.Nw2, profile.Ne2, profile.Se2, profile.Sw2 };
            bool centerReached = Reached(snapshot, center, profile.Center2);
            int reachedCorners = 0;
            for (int i = 0; i < corners.Length; i++) if (Reached(snapshot, corners[i], heights[i])) reachedCorners++;
            if (!centerReached && reachedCorners < 2) yield break;

            if (snapshot.IsGroundNode(center)) yield return center;
            for (int i = 0; i < corners.Length; i++)
                if (snapshot.IsGroundNode(corners[i])) yield return corners[i];
        }

        internal static bool ContainsHandoffTile(AccessSearchSnapshot snapshot, Tile2i origin,
            AccessHeightProfile profile, Tile2i tile)
        {
            foreach (Tile2i candidate in GetHandoffTiles(snapshot, origin, profile))
                if (candidate == tile) return true;
            return false;
        }

        private static bool Reached(AccessSearchSnapshot snapshot, Tile2i tile, int targetHeight2)
        {
            if (!snapshot.TryGetGroundHeight2(tile, out int groundHeight2)) return false;
            return snapshot.IsMining ? targetHeight2 >= groundHeight2 : targetHeight2 <= groundHeight2;
        }

        private static bool TrySolveSuccessor(AccessHeightProfile current, Tile2i direction,
            AccessSearchMode mode, out AccessHeightProfile successor)
        {
            current.GetEdge(direction, out int currentFirst2, out int currentSecond2);
            int templateCenter2 = mode == AccessSearchMode.Flat ? 0 : 1;
            if (!AccessHeightProfile.TryForMode(mode, templateCenter2, out AccessHeightProfile template))
            { successor = default; return false; }
            template.GetEdge(new Tile2i(-direction.X, -direction.Y), out int templateFirst2, out int templateSecond2);
            int firstOffset2 = templateFirst2 - templateCenter2;
            int secondOffset2 = templateSecond2 - templateCenter2;
            int center2 = currentFirst2 - firstOffset2;
            if (currentSecond2 - secondOffset2 != center2
                || !AccessHeightProfile.TryForMode(mode, center2, out successor))
            { successor = default; return false; }
            return true;
        }

        internal static bool EdgesMatch(AccessHeightProfile current, AccessHeightProfile next, Tile2i direction)
        {
            current.GetEdge(direction, out int a, out int b);
            next.GetEdge(new Tile2i(-direction.X, -direction.Y), out int c, out int d);
            return a == c && b == d;
        }

        internal static bool TryGetProfile(AccessSearchSnapshot snapshot, AccessSearchNode node, out AccessHeightProfile profile)
        {
            if (node.Mode == AccessSearchMode.Existing)
                return snapshot.TryGetFixedProfile(node.Position, out profile);
            return AccessHeightProfile.TryForMode(node.Mode, node.Height2, out profile);
        }

        private static void Relax(AccessSearchSnapshot snapshot, AccessSearchNode current, AccessSearchNode next,
            float nextCost, Dictionary<AccessSearchNode, float> distance,
            Dictionary<AccessSearchNode, AccessSearchNode> previous, MinQueue queue, bool hasCurrent = true)
        {
            if (distance.TryGetValue(next, out float existing) && existing <= nextCost + 0.0001f) return;
            distance[next] = nextCost;
            if (hasCurrent) previous[next] = current;
            float heuristic = snapshot.UseAStar ? snapshot.GetGoalManhattanDistance(next.CostPosition) : 0f;
            queue.Push(new QueueEntry(next, nextCost, nextCost + heuristic));
        }

        private static List<AccessSearchNode> Reconstruct(AccessSearchNode end,
            Dictionary<AccessSearchNode, AccessSearchNode> previous)
        {
            var path = new List<AccessSearchNode> { end };
            while (previous.TryGetValue(end, out AccessSearchNode parent))
            { path.Add(parent); end = parent; }
            path.Reverse();
            return path;
        }

        private static bool ValidateGeneratedPath(IReadOnlyList<AccessSearchNode> path,
            AccessSearchSnapshot snapshot, out string reason)
        {
            var profilesByOrigin = new Dictionary<Tile2i, AccessHeightProfile>();
            var cornerHeights = new Dictionary<Tile2i, int>();
            foreach (AccessSearchNode node in path)
            {
                if (node.IsGround || node.Mode == AccessSearchMode.Existing) continue;
                if (!TryGetProfile(snapshot, node, out AccessHeightProfile profile)) continue;
                if (profilesByOrigin.TryGetValue(node.Position, out AccessHeightProfile existing)
                    && (existing.Nw2 != profile.Nw2 || existing.Ne2 != profile.Ne2
                        || existing.Se2 != profile.Se2 || existing.Sw2 != profile.Sw2))
                { reason = "FinalSelfContact"; return false; }
                profilesByOrigin[node.Position] = profile;
                bool mismatch = false;
                profile.AddWorldCorners(node.Position, (p, h) =>
                {
                    if (cornerHeights.TryGetValue(p, out int old) && old != h) mismatch = true;
                    else cornerHeights[p] = h;
                });
                if (mismatch) { reason = "FinalSelfContact"; return false; }
            }
            reason = string.Empty;
            return true;
        }

        private static int Manhattan(Tile2i a, Tile2i b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

        private static void Reject(Dictionary<string, int> rejections, string reason)
            => rejections[reason] = rejections.TryGetValue(reason, out int count) ? count + 1 : 1;

        private static AccessSearchResult Failed(string reason, Tile2i start, int visited,
            Dictionary<string, int> rejections)
            => new AccessSearchResult(false, reason, start, Array.Empty<AccessSearchNode>(), 0f, visited, rejections);

        private readonly struct QueueEntry
        {
            public AccessSearchNode Node { get; }
            public float PathCost { get; }
            public float Priority { get; }
            public QueueEntry(AccessSearchNode node, float pathCost, float priority)
            { Node = node; PathCost = pathCost; Priority = priority; }
        }

        private sealed class MinQueue
        {
            private readonly List<QueueEntry> m_items = new List<QueueEntry>();
            public int Count => m_items.Count;
            public void Push(QueueEntry entry)
            {
                m_items.Add(entry);
                int i = m_items.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (!Less(m_items[i], m_items[parent])) break;
                    (m_items[i], m_items[parent]) = (m_items[parent], m_items[i]);
                    i = parent;
                }
            }
            public QueueEntry Pop()
            {
                QueueEntry result = m_items[0];
                int last = m_items.Count - 1;
                m_items[0] = m_items[last];
                m_items.RemoveAt(last);
                int i = 0;
                while (i < m_items.Count)
                {
                    int left = i * 2 + 1, right = left + 1, smallest = i;
                    if (left < m_items.Count && Less(m_items[left], m_items[smallest])) smallest = left;
                    if (right < m_items.Count && Less(m_items[right], m_items[smallest])) smallest = right;
                    if (smallest == i) break;
                    (m_items[i], m_items[smallest]) = (m_items[smallest], m_items[i]);
                    i = smallest;
                }
                return result;
            }
            private static bool Less(QueueEntry a, QueueEntry b)
            {
                int priority = a.Priority.CompareTo(b.Priority);
                if (priority != 0) return priority < 0;
                int path = a.PathCost.CompareTo(b.PathCost);
                if (path != 0) return path < 0;
                int x = a.Node.Position.X.CompareTo(b.Node.Position.X);
                if (x != 0) return x < 0;
                int y = a.Node.Position.Y.CompareTo(b.Node.Position.Y);
                if (y != 0) return y < 0;
                int height = a.Node.Height2.CompareTo(b.Node.Height2);
                if (height != 0) return height < 0;
                return (int)a.Node.Mode < (int)b.Node.Mode;
            }
        }
    }
}
