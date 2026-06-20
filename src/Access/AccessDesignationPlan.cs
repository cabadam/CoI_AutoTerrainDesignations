using System;
using System.Collections.Generic;
using Mafi;

namespace AutoTerrainDesignations.Access
{
    internal readonly struct AccessPlannedDesignation
    {
        public Tile2i Origin { get; }
        public AccessSearchMode Mode { get; }
        public AccessHeightProfile Profile { get; }

        public AccessPlannedDesignation(Tile2i origin, AccessSearchMode mode, AccessHeightProfile profile)
        {
            Origin = origin;
            Mode = mode;
            Profile = profile;
        }
    }

    internal sealed class AccessDesignationPlan
    {
        public bool IsValid { get; }
        public string FailureReason { get; }
        public Tile2i StartOrigin { get; }
        public Tile2i HandoffGround { get; }
        public IReadOnlyList<AccessPlannedDesignation> Designations { get; }
        public int ReusedNodeCount { get; }
        public int GroundNodeCount { get; }

        public AccessDesignationPlan(bool isValid, string failureReason, Tile2i startOrigin,
            Tile2i handoffGround, IReadOnlyList<AccessPlannedDesignation> designations,
            int reusedNodeCount, int groundNodeCount)
        {
            IsValid = isValid;
            FailureReason = failureReason;
            StartOrigin = startOrigin;
            HandoffGround = handoffGround;
            Designations = designations;
            ReusedNodeCount = reusedNodeCount;
            GroundNodeCount = groundNodeCount;
        }

        public static AccessDesignationPlan Invalid(string reason, Tile2i startOrigin)
            => new AccessDesignationPlan(false, reason, startOrigin, default,
                Array.Empty<AccessPlannedDesignation>(), 0, 0);
    }

    internal sealed class ExperimentalAccessCandidate
    {
        public AccessSearchResult SearchResult { get; }
        public AccessDesignationPlan Plan { get; }

        public ExperimentalAccessCandidate(AccessSearchResult searchResult, AccessDesignationPlan plan)
        {
            SearchResult = searchResult;
            Plan = plan;
        }
    }
}
