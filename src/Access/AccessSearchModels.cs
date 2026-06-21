using System;
using System.Collections.Generic;
using Mafi;

namespace AutoTerrainDesignations.Access
{
    internal enum AccessSearchMode
    {
        Ground,
        Flat,
        XPositive,
        XNegative,
        YPositive,
        YNegative,
        Existing
    }

    internal enum AccessHandoffOperation
    {
        None,
        Mining,
        Dumping
    }

    internal readonly struct AccessGroundHandoff
    {
        public Tile2i Tile { get; }
        public AccessHandoffOperation Operation { get; }

        public AccessGroundHandoff(Tile2i tile, AccessHandoffOperation operation)
        {
            Tile = tile;
            Operation = operation;
        }
    }

    internal readonly struct AccessSearchNode : IEquatable<AccessSearchNode>
    {
        public Tile2i Position { get; }
        public int Height2 { get; }
        public AccessSearchMode Mode { get; }
        public AccessHandoffOperation HandoffOperation { get; }

        public AccessSearchNode(Tile2i position, int height2, AccessSearchMode mode,
            AccessHandoffOperation handoffOperation = AccessHandoffOperation.None)
        {
            Position = position;
            Height2 = height2;
            Mode = mode;
            HandoffOperation = handoffOperation;
        }

        public bool IsGround => Mode == AccessSearchMode.Ground;
        public Tile2i CostPosition => IsGround ? Position : Position + new RelTile2i(2, 2);

        public bool Equals(AccessSearchNode other)
            => Position == other.Position && Height2 == other.Height2 && Mode == other.Mode
                && HandoffOperation == other.HandoffOperation;

        public override bool Equals(object? obj) => obj is AccessSearchNode other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Position.GetHashCode();
                hash = (hash * 397) ^ Height2;
                hash = (hash * 397) ^ (int)Mode;
                hash = (hash * 397) ^ (int)HandoffOperation;
                return hash;
            }
        }

        public override string ToString() => $"{Mode}@{Position}/h2={Height2}/handoff={HandoffOperation}";
    }

    internal readonly struct AccessHeightProfile
    {
        public int Nw2 { get; }
        public int Ne2 { get; }
        public int Se2 { get; }
        public int Sw2 { get; }

        public AccessHeightProfile(int nw2, int ne2, int se2, int sw2)
        {
            Nw2 = nw2;
            Ne2 = ne2;
            Se2 = se2;
            Sw2 = sw2;
        }

        public int Center2 => (Nw2 + Ne2 + Se2 + Sw2) / 4;

        public int GetHeight2NumeratorAt(int x, int y)
        {
            // Bilinear target height over a 4x4 designation. The denominator is 16;
            // retaining the numerator avoids rounding away operation incompatibilities.
            return Nw2 * (4 - x) * (4 - y)
                + Ne2 * x * (4 - y)
                + Sw2 * (4 - x) * y
                + Se2 * x * y;
        }

        public static bool TryForMode(AccessSearchMode mode, int center2, out AccessHeightProfile profile)
        {
            switch (mode)
            {
                case AccessSearchMode.Flat when (center2 & 1) == 0:
                    profile = new AccessHeightProfile(center2, center2, center2, center2);
                    return true;
                case AccessSearchMode.XPositive when (center2 & 1) != 0:
                    profile = new AccessHeightProfile(center2 - 1, center2 + 1, center2 + 1, center2 - 1);
                    return true;
                case AccessSearchMode.XNegative when (center2 & 1) != 0:
                    profile = new AccessHeightProfile(center2 + 1, center2 - 1, center2 - 1, center2 + 1);
                    return true;
                case AccessSearchMode.YPositive when (center2 & 1) != 0:
                    profile = new AccessHeightProfile(center2 - 1, center2 - 1, center2 + 1, center2 + 1);
                    return true;
                case AccessSearchMode.YNegative when (center2 & 1) != 0:
                    profile = new AccessHeightProfile(center2 + 1, center2 + 1, center2 - 1, center2 - 1);
                    return true;
                default:
                    profile = default;
                    return false;
            }
        }

        public void GetEdge(Tile2i direction, out int first2, out int second2)
        {
            if (direction.X > 0) { first2 = Ne2; second2 = Se2; return; }
            if (direction.X < 0) { first2 = Nw2; second2 = Sw2; return; }
            if (direction.Y > 0) { first2 = Sw2; second2 = Se2; return; }
            first2 = Nw2; second2 = Ne2;
        }

        public void AddWorldCorners(Tile2i origin, Action<Tile2i, int> add)
        {
            add(origin, Nw2);
            add(origin + new RelTile2i(4, 0), Ne2);
            add(origin + new RelTile2i(4, 4), Se2);
            add(origin + new RelTile2i(0, 4), Sw2);
        }
    }

    internal readonly struct AccessDurabilityCorner
    {
        public Tile2i Position { get; }
        public int Height2 { get; }

        public AccessDurabilityCorner(Tile2i position, int height2)
        {
            Position = position;
            Height2 = height2;
        }

        public bool Blocks(Tile2i position, int height2, float horizontalRunPerHeight)
        {
            int delta2 = Math.Abs(height2 - Height2);
            return delta2 > 0
                && Math.Abs(position.X - Position.X) * 2 < delta2 * horizontalRunPerHeight
                && Math.Abs(position.Y - Position.Y) * 2 < delta2 * horizontalRunPerHeight;
        }
    }

    internal sealed class AccessSearchSnapshot
    {
        private readonly Dictionary<Tile2i, int> m_groundHeight2;
        private readonly Dictionary<Tile2i, int> m_terrainCenterHeight2;
        private readonly Dictionary<Tile2i, AccessHeightProfile> m_fixedProfiles;
        private readonly HashSet<Tile2i> m_workOrigins;
        private readonly HashSet<Tile2i> m_groundNodes;
        private readonly HashSet<Tile2i> m_goalGroundNodes;
        private readonly HashSet<Tile2i> m_occupiedTiles;
        private readonly HashSet<Tile2i> m_oceanTiles;
        private readonly HashSet<Tile2i> m_validOrigins;
        private readonly Dictionary<int, int[]> m_goalDistancesByHeight2;
        private readonly int m_goalDistanceWidth;
        private readonly int m_goalDistanceHeight;
        private readonly AccessDurabilityCorner[] m_durabilityCorners;
        private readonly Func<Tile2i, AccessHeightProfile, Tile2i, AccessHeightProfile,
            IReadOnlyList<AccessGroundHandoff>>? m_workableHandoffs;

        public Tile2i BoundsMin { get; }
        public Tile2i BoundsMax { get; }
        public Tile2i TowerCenter { get; }
        public int MinHeight2 { get; }
        public int MaxHeight2 { get; }
        public bool IsMining { get; }
        public bool AllowsMixedWork { get; }
        public bool UseAStar { get; }
        public float WorkDistanceScale { get; }
        public float LandslideRunPerHeight { get; }
        public int GoalCount => m_goalGroundNodes.Count;
        public int LandslideSourceCount => m_durabilityCorners.Length;

        public AccessSearchSnapshot(
            Tile2i boundsMin,
            Tile2i boundsMax,
            Tile2i towerCenter,
            int minHeight2,
            int maxHeight2,
            bool isMining,
            bool allowsMixedWork,
            bool useAStar,
            float workDistanceScale,
            float landslideRunPerHeight,
            IDictionary<Tile2i, int> groundHeight2,
            IDictionary<Tile2i, int> terrainCenterHeight2,
            IDictionary<Tile2i, AccessHeightProfile> fixedProfiles,
            IEnumerable<Tile2i> workOrigins,
            IEnumerable<Tile2i> groundNodes,
            IEnumerable<Tile2i> goalGroundNodes,
            IEnumerable<Tile2i> occupiedTiles,
            IEnumerable<Tile2i> oceanTiles,
            IEnumerable<AccessDurabilityCorner> durabilityCorners,
            Func<Tile2i, AccessHeightProfile, Tile2i, AccessHeightProfile,
                IReadOnlyList<AccessGroundHandoff>>? workableHandoffs = null)
        {
            BoundsMin = boundsMin;
            BoundsMax = boundsMax;
            TowerCenter = towerCenter;
            MinHeight2 = minHeight2;
            MaxHeight2 = maxHeight2;
            IsMining = isMining;
            AllowsMixedWork = allowsMixedWork;
            UseAStar = useAStar;
            WorkDistanceScale = workDistanceScale;
            LandslideRunPerHeight = landslideRunPerHeight;
            m_groundHeight2 = new Dictionary<Tile2i, int>(groundHeight2);
            m_terrainCenterHeight2 = new Dictionary<Tile2i, int>(terrainCenterHeight2);
            m_fixedProfiles = new Dictionary<Tile2i, AccessHeightProfile>(fixedProfiles);
            m_workOrigins = new HashSet<Tile2i>(workOrigins);
            m_groundNodes = new HashSet<Tile2i>(groundNodes);
            m_goalGroundNodes = new HashSet<Tile2i>(goalGroundNodes);
            m_occupiedTiles = new HashSet<Tile2i>(occupiedTiles);
            m_oceanTiles = new HashSet<Tile2i>(oceanTiles);
            m_validOrigins = new HashSet<Tile2i>(m_terrainCenterHeight2.Keys);
            m_goalDistanceWidth = boundsMax.X - boundsMin.X + 1;
            m_goalDistanceHeight = boundsMax.Y - boundsMin.Y + 1;
            m_goalDistancesByHeight2 = useAStar
                ? BuildGoalDistancesByHeight(boundsMin, boundsMax, m_goalGroundNodes, m_groundHeight2)
                : new Dictionary<int, int[]>();
            m_durabilityCorners = new List<AccessDurabilityCorner>(durabilityCorners).ToArray();
            m_workableHandoffs = workableHandoffs;
        }

        public bool IsOriginInside(Tile2i origin) => m_validOrigins.Contains(origin);

        public bool IsTileInside(Tile2i tile)
            => tile.X >= BoundsMin.X && tile.Y >= BoundsMin.Y
                && tile.X <= BoundsMax.X && tile.Y <= BoundsMax.Y;

        public bool IsWorkOrigin(Tile2i origin) => m_workOrigins.Contains(origin);
        public bool TryGetFixedProfile(Tile2i origin, out AccessHeightProfile profile) => m_fixedProfiles.TryGetValue(origin, out profile);
        public bool IsGroundNode(Tile2i tile) => m_groundNodes.Contains(tile);
        public bool IsGoalGroundNode(Tile2i tile) => m_goalGroundNodes.Contains(tile);
        public int GetGoalTravelLowerBound(Tile2i tile, int height2)
        {
            int x = tile.X - BoundsMin.X;
            int y = tile.Y - BoundsMin.Y;
            if (x < 0 || x >= m_goalDistanceWidth || y < 0 || y >= m_goalDistanceHeight) return 0;
            int index = y * m_goalDistanceWidth + x;
            int best = int.MaxValue;
            foreach (KeyValuePair<int, int[]> entry in m_goalDistancesByHeight2)
            {
                int horizontalDistance = entry.Value[index];
                if (horizontalDistance < 0) continue;
                int verticalDistance = Math.Abs(height2 - entry.Key);
                best = Math.Min(best, Math.Max(horizontalDistance, verticalDistance));
            }
            return best == int.MaxValue ? 0 : best;
        }
        public bool TryGetGroundHeight2(Tile2i tile, out int height2) => m_groundHeight2.TryGetValue(tile, out height2);
        public int GetTerrainCenterHeight2(Tile2i origin) => m_terrainCenterHeight2.TryGetValue(origin, out int h2) ? h2 : 0;
        public bool HasWorkableHandoffEvaluator => m_workableHandoffs != null;
        public IReadOnlyList<AccessGroundHandoff> GetWorkableHandoffs(
            Tile2i origin, AccessHeightProfile profile,
            Tile2i predecessorOrigin, AccessHeightProfile predecessorProfile)
            => m_workableHandoffs?.Invoke(origin, profile, predecessorOrigin, predecessorProfile)
                ?? Array.Empty<AccessGroundHandoff>();

        public bool IsProfileOceanBlocked(Tile2i origin, AccessHeightProfile profile)
        {
            const int minOceanHeight2Numerator = 2 * 16;
            for (int y = 0; y <= 4; y++)
                for (int x = 0; x <= 4; x++)
                    if (m_oceanTiles.Contains(origin + new RelTile2i(x, y))
                        && profile.GetHeight2NumeratorAt(x, y) < minOceanHeight2Numerator)
                        return true;
            return false;
        }

        public bool IsCandidateProfileFeasible(Tile2i origin, AccessHeightProfile profile, out string reason)
            => IsCandidateProfileFeasible(origin, profile, default, default, false, out reason);

        public bool IsCandidateProfileFeasibleFromValidatedPredecessor(
            Tile2i origin, AccessHeightProfile profile, Tile2i predecessorOrigin,
            Tile2i direction, out string reason)
            => IsCandidateProfileFeasible(origin, profile, predecessorOrigin, direction, true, out reason);

        private bool IsCandidateProfileFeasible(Tile2i origin, AccessHeightProfile profile,
            Tile2i predecessorOrigin, Tile2i direction, bool directionalDurabilityCheck,
            out string reason)
        {
            if (!IsOriginInside(origin)) { reason = "HorizontalBounds"; return false; }
            if (m_workOrigins.Contains(origin)) { reason = "WorkOrigin"; return false; }
            if (m_fixedProfiles.ContainsKey(origin)) { reason = "ExistingDesignation"; return false; }
            if (profile.Center2 < MinHeight2 || profile.Center2 > MaxHeight2) { reason = "VerticalBounds"; return false; }
            if (IsProfileOceanBlocked(origin, profile)) { reason = "OceanBelowMinimum"; return false; }

            string? operationMismatch = null;
            for (int y = 0; !AllowsMixedWork && y <= 4 && operationMismatch == null; y++)
            {
                for (int x = 0; x <= 4; x++)
                {
                    Tile2i sample = origin + new RelTile2i(x, y);
                    if (!m_groundHeight2.TryGetValue(sample, out int terrainHeight2)) continue;
                    int targetHeight2Numerator = profile.GetHeight2NumeratorAt(x, y);
                    int terrainHeight2Numerator = terrainHeight2 * 16;
                    if (IsMining && targetHeight2Numerator > terrainHeight2Numerator)
                    {
                        operationMismatch = "RequiresDumping";
                        break;
                    }
                    if (!IsMining && targetHeight2Numerator < terrainHeight2Numerator)
                    {
                        operationMismatch = "RequiresMining";
                        break;
                    }
                }
            }
            if (operationMismatch != null) { reason = operationMismatch; return false; }

            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    if (m_occupiedTiles.Contains(origin + new RelTile2i(x, y)))
                    { reason = "Building"; return false; }

            bool durabilityBlocked = false;
            profile.AddWorldCorners(origin, (corner, height2) =>
            {
                if (durabilityBlocked) return;
                durabilityBlocked = directionalDurabilityCheck
                    ? IsDurabilityBlockedAhead(corner, height2, predecessorOrigin, direction)
                    : IsDurabilityBlocked(corner, height2);
            });
            if (durabilityBlocked) { reason = "Durability"; return false; }

            if (!MatchesFixedNeighbors(origin, profile)) { reason = "FightInvariant"; return false; }
            reason = string.Empty;
            return true;
        }

        public bool IsDurabilityBlocked(Tile2i position, int height2)
        {
            foreach (AccessDurabilityCorner corner in m_durabilityCorners)
            {
                if (corner.Blocks(position, height2, LandslideRunPerHeight))
                    return true;
            }
            return false;
        }

        private bool IsDurabilityBlockedAhead(Tile2i position, int height2,
            Tile2i predecessorOrigin, Tile2i direction)
        {
            foreach (AccessDurabilityCorner corner in m_durabilityCorners)
            {
                bool ahead = direction.X > 0 ? corner.Position.X > predecessorOrigin.X
                    : direction.X < 0 ? corner.Position.X < predecessorOrigin.X
                    : direction.Y > 0 ? corner.Position.Y > predecessorOrigin.Y
                    : corner.Position.Y < predecessorOrigin.Y;
                if (ahead && corner.Blocks(position, height2, LandslideRunPerHeight)) return true;
            }
            return false;
        }

        private bool MatchesFixedNeighbors(Tile2i origin, AccessHeightProfile profile)
        {
            var candidateCorners = new Dictionary<Tile2i, int>();
            profile.AddWorldCorners(origin, (p, h) => candidateCorners[p] = h);
            for (int dy = -4; dy <= 4; dy += 4)
            {
                for (int dx = -4; dx <= 4; dx += 4)
                {
                    if (dx == 0 && dy == 0) continue;
                    Tile2i neighbor = origin + new RelTile2i(dx, dy);
                    if (!m_fixedProfiles.TryGetValue(neighbor, out AccessHeightProfile fixedProfile)) continue;
                    bool mismatch = false;
                    fixedProfile.AddWorldCorners(neighbor, (p, h) =>
                    {
                        if (candidateCorners.TryGetValue(p, out int own) && own != h) mismatch = true;
                    });
                    if (mismatch) return false;
                }
            }
            return true;
        }

        private static Dictionary<int, int[]> BuildGoalDistancesByHeight(
            Tile2i boundsMin, Tile2i boundsMax, HashSet<Tile2i> goals,
            Dictionary<Tile2i, int> groundHeight2)
        {
            var goalsByHeight2 = new Dictionary<int, HashSet<Tile2i>>();
            foreach (Tile2i goal in goals)
            {
                if (!groundHeight2.TryGetValue(goal, out int height2)) continue;
                if (!goalsByHeight2.TryGetValue(height2, out HashSet<Tile2i>? sameHeightGoals))
                {
                    sameHeightGoals = new HashSet<Tile2i>();
                    goalsByHeight2.Add(height2, sameHeightGoals);
                }
                sameHeightGoals.Add(goal);
            }

            var result = new Dictionary<int, int[]>();
            foreach (KeyValuePair<int, HashSet<Tile2i>> entry in goalsByHeight2)
                result.Add(entry.Key, BuildGoalDistance(boundsMin, boundsMax, entry.Value));
            return result;
        }

        private static int[] BuildGoalDistance(Tile2i boundsMin, Tile2i boundsMax,
            HashSet<Tile2i> goals)
        {
            int width = boundsMax.X - boundsMin.X + 1;
            int height = boundsMax.Y - boundsMin.Y + 1;
            var result = new int[width * height];
            for (int i = 0; i < result.Length; i++) result[i] = -1;
            var queue = new Queue<int>();
            foreach (Tile2i goal in goals)
            {
                int index = (goal.Y - boundsMin.Y) * width + goal.X - boundsMin.X;
                result[index] = 0;
                queue.Enqueue(index);
            }
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                int x = current % width;
                int y = current / width;
                int nextDistance = result[current] + 1;
                if (x > 0 && result[current - 1] < 0)
                { result[current - 1] = nextDistance; queue.Enqueue(current - 1); }
                if (x + 1 < width && result[current + 1] < 0)
                { result[current + 1] = nextDistance; queue.Enqueue(current + 1); }
                if (y > 0 && result[current - width] < 0)
                { result[current - width] = nextDistance; queue.Enqueue(current - width); }
                if (y + 1 < height && result[current + width] < 0)
                { result[current + width] = nextDistance; queue.Enqueue(current + width); }
            }
            return result;
        }
    }

    internal sealed class AccessSearchResult
    {
        public bool Success { get; }
        public string FailureReason { get; }
        public Tile2i StartOrigin { get; }
        public IReadOnlyList<AccessSearchNode> Path { get; }
        public float Cost { get; }
        public int VisitedNodes { get; }
        public IReadOnlyDictionary<string, int> Rejections { get; }

        public AccessSearchResult(bool success, string failureReason, Tile2i startOrigin,
            IReadOnlyList<AccessSearchNode> path, float cost, int visitedNodes,
            IReadOnlyDictionary<string, int> rejections)
        {
            Success = success;
            FailureReason = failureReason;
            StartOrigin = startOrigin;
            Path = path;
            Cost = cost;
            VisitedNodes = visitedNodes;
            Rejections = rejections;
        }
    }
}
