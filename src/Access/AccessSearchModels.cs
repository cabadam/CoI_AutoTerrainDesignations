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

    internal readonly struct AccessSearchNode : IEquatable<AccessSearchNode>
    {
        public Tile2i Position { get; }
        public int Height2 { get; }
        public AccessSearchMode Mode { get; }

        public AccessSearchNode(Tile2i position, int height2, AccessSearchMode mode)
        {
            Position = position;
            Height2 = height2;
            Mode = mode;
        }

        public bool IsGround => Mode == AccessSearchMode.Ground;
        public Tile2i CostPosition => IsGround ? Position : Position + new RelTile2i(2, 2);

        public bool Equals(AccessSearchNode other)
            => Position == other.Position && Height2 == other.Height2 && Mode == other.Mode;

        public override bool Equals(object? obj) => obj is AccessSearchNode other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Position.GetHashCode();
                hash = (hash * 397) ^ Height2;
                hash = (hash * 397) ^ (int)Mode;
                return hash;
            }
        }

        public override string ToString() => $"{Mode}@{Position}/h2={Height2}";
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
        private readonly HashSet<Tile2i> m_validOrigins;
        private readonly Dictionary<Tile2i, int> m_goalDistance;
        private readonly AccessDurabilityCorner[] m_durabilityCorners;

        public Tile2i BoundsMin { get; }
        public Tile2i BoundsMax { get; }
        public Tile2i TowerCenter { get; }
        public int MinHeight2 { get; }
        public int MaxHeight2 { get; }
        public bool IsMining { get; }
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
            IEnumerable<AccessDurabilityCorner> durabilityCorners)
        {
            BoundsMin = boundsMin;
            BoundsMax = boundsMax;
            TowerCenter = towerCenter;
            MinHeight2 = minHeight2;
            MaxHeight2 = maxHeight2;
            IsMining = isMining;
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
            m_validOrigins = new HashSet<Tile2i>(m_terrainCenterHeight2.Keys);
            m_goalDistance = useAStar ? BuildGoalDistance(boundsMin, boundsMax, m_goalGroundNodes) : new Dictionary<Tile2i, int>();
            m_durabilityCorners = new List<AccessDurabilityCorner>(durabilityCorners).ToArray();
        }

        public bool IsOriginInside(Tile2i origin) => m_validOrigins.Contains(origin);

        public bool IsTileInside(Tile2i tile)
            => tile.X >= BoundsMin.X && tile.Y >= BoundsMin.Y
                && tile.X <= BoundsMax.X && tile.Y <= BoundsMax.Y;

        public bool IsWorkOrigin(Tile2i origin) => m_workOrigins.Contains(origin);
        public bool TryGetFixedProfile(Tile2i origin, out AccessHeightProfile profile) => m_fixedProfiles.TryGetValue(origin, out profile);
        public bool IsGroundNode(Tile2i tile) => m_groundNodes.Contains(tile);
        public bool IsGoalGroundNode(Tile2i tile) => m_goalGroundNodes.Contains(tile);
        public int GetGoalManhattanDistance(Tile2i tile)
        {
            return m_goalDistance.TryGetValue(tile, out int distance) ? distance : 0;
        }
        public bool TryGetGroundHeight2(Tile2i tile, out int height2) => m_groundHeight2.TryGetValue(tile, out height2);
        public int GetTerrainCenterHeight2(Tile2i origin) => m_terrainCenterHeight2.TryGetValue(origin, out int h2) ? h2 : 0;

        public bool IsCandidateProfileFeasible(Tile2i origin, AccessHeightProfile profile, out string reason)
        {
            if (!IsOriginInside(origin)) { reason = "HorizontalBounds"; return false; }
            if (m_workOrigins.Contains(origin)) { reason = "WorkOrigin"; return false; }
            if (m_fixedProfiles.ContainsKey(origin)) { reason = "ExistingDesignation"; return false; }
            if (profile.Center2 < MinHeight2 || profile.Center2 > MaxHeight2) { reason = "VerticalBounds"; return false; }

            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    if (m_occupiedTiles.Contains(origin + new RelTile2i(x, y)))
                    { reason = "Building"; return false; }

            bool durabilityBlocked = false;
            profile.AddWorldCorners(origin, (corner, height2) =>
            {
                if (!durabilityBlocked && IsDurabilityBlocked(corner, height2)) durabilityBlocked = true;
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

        private static Dictionary<Tile2i, int> BuildGoalDistance(Tile2i boundsMin, Tile2i boundsMax,
            HashSet<Tile2i> goals)
        {
            var result = new Dictionary<Tile2i, int>();
            var queue = new Queue<Tile2i>();
            foreach (Tile2i goal in goals)
            {
                result[goal] = 0;
                queue.Enqueue(goal);
            }
            RelTile2i[] directions =
            {
                new RelTile2i(1, 0), new RelTile2i(-1, 0), new RelTile2i(0, 1), new RelTile2i(0, -1)
            };
            while (queue.Count > 0)
            {
                Tile2i current = queue.Dequeue();
                int nextDistance = result[current] + 1;
                foreach (RelTile2i direction in directions)
                {
                    Tile2i next = current + direction;
                    if (next.X < boundsMin.X || next.X > boundsMax.X
                        || next.Y < boundsMin.Y || next.Y > boundsMax.Y
                        || result.ContainsKey(next)) continue;
                    result[next] = nextDistance;
                    queue.Enqueue(next);
                }
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
