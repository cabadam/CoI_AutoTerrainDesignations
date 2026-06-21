using System;
using System.Collections.Generic;
using Mafi;

namespace AutoTerrainDesignations.Access
{
    internal static class AccessPathMaterializer
    {
        public static AccessDesignationPlan Materialize(AccessSearchSnapshot snapshot, AccessSearchResult result)
        {
            if (!result.Success) return AccessDesignationPlan.Invalid("SearchFailed", result.StartOrigin);
            if (result.Path.Count == 0) return AccessDesignationPlan.Invalid("EmptyPath", result.StartOrigin);
            if (!snapshot.TryGetFixedProfile(result.StartOrigin, out AccessHeightProfile previousProfile))
                return AccessDesignationPlan.Invalid("MissingStartProfile", result.StartOrigin);

            var designations = new List<AccessPlannedDesignation>();
            var generatedByOrigin = new Dictionary<Tile2i, AccessPlannedDesignation>();
            var cornerHeights = new Dictionary<Tile2i, int>();
            Tile2i previousPosition = result.StartOrigin;
            Tile2i previousVPredecessorPosition = result.StartOrigin;
            AccessHeightProfile previousVPredecessorProfile = previousProfile;
            bool previousWasGround = false;
            int reusedNodes = 0;
            int groundNodes = 0;
            Tile2i handoffGround = default;
            AccessHandoffOperation handoffOperation = AccessHandoffOperation.None;

            for (int pathIndex = 0; pathIndex < result.Path.Count; pathIndex++)
            {
                AccessSearchNode node = result.Path[pathIndex];
                if (node.IsGround)
                {
                    if (!snapshot.IsGroundNode(node.Position))
                        return Invalid("PlanGroundUnavailable", result, designations, reusedNodes, groundNodes);
                    if (previousWasGround)
                    {
                        if (Manhattan(previousPosition, node.Position) != 1)
                            return Invalid("PlanGroundDiscontinuity", result, designations, reusedNodes, groundNodes);
                    }
                    else if (!AccessPathSearch.ContainsHandoff(
                        snapshot, previousPosition, previousProfile,
                        previousVPredecessorPosition, previousVPredecessorProfile,
                        node.Position, node.HandoffOperation))
                    {
                        return Invalid("PlanVToGHandoff", result, designations, reusedNodes, groundNodes);
                    }

                    previousWasGround = true;
                    previousPosition = node.Position;
                    handoffGround = node.Position;
                    handoffOperation = node.HandoffOperation;
                    groundNodes++;
                    continue;
                }

                if (!AccessPathSearch.TryGetProfile(snapshot, node, out AccessHeightProfile profile))
                    return Invalid("PlanMissingProfile", result, designations, reusedNodes, groundNodes);

                if (previousWasGround)
                {
                    if (!AccessPathSearch.ContainsHandoffTile(snapshot, node.Position, profile, previousPosition))
                        return Invalid("PlanGToVHandoff", result, designations, reusedNodes, groundNodes);
                }
                else
                {
                    Tile2i direction = new Tile2i(node.Position.X - previousPosition.X, node.Position.Y - previousPosition.Y);
                    if (!IsOriginStep(direction) || !AccessPathSearch.EdgesMatch(previousProfile, profile, direction))
                        return Invalid("PlanEdgeMismatch", result, designations, reusedNodes, groundNodes);
                }

                if (node.Mode == AccessSearchMode.Existing)
                {
                    if (!snapshot.TryGetFixedProfile(node.Position, out _))
                        return Invalid("PlanExistingMissing", result, designations, reusedNodes, groundNodes);
                    reusedNodes++;
                }
                else
                {
                    if (!snapshot.IsCandidateProfileFeasible(node.Position, profile, out string reason))
                        return Invalid("Plan" + reason, result, designations, reusedNodes, groundNodes);

                    var planned = new AccessPlannedDesignation(node.Position, node.Mode, profile);
                    if (generatedByOrigin.TryGetValue(node.Position, out AccessPlannedDesignation existing))
                    {
                        if (!ProfilesEqual(existing.Profile, profile))
                            return Invalid("PlanDuplicateConflict", result, designations, reusedNodes, groundNodes);
                        return Invalid("PlanDuplicateOrigin", result, designations, reusedNodes, groundNodes);
                    }
                    else
                    {
                        // Nonconsecutive side/diagonal contact is legal when every
                        // shared corner agrees. Compact flat-landed turns require it.
                        bool cornerMismatch = false;
                        profile.AddWorldCorners(node.Position, (corner, height2) =>
                        {
                            if (cornerHeights.TryGetValue(corner, out int oldHeight2) && oldHeight2 != height2)
                                cornerMismatch = true;
                            else
                                cornerHeights[corner] = height2;
                        });
                        if (cornerMismatch)
                            return Invalid("PlanCornerFight", result, designations, reusedNodes, groundNodes);

                        generatedByOrigin[node.Position] = planned;
                        designations.Add(planned);
                    }
                }

                if (!previousWasGround)
                {
                    previousVPredecessorPosition = previousPosition;
                    previousVPredecessorProfile = previousProfile;
                }
                previousWasGround = false;
                previousPosition = node.Position;
                previousProfile = profile;
            }

            AccessSearchNode end = result.Path[result.Path.Count - 1];
            if (!end.IsGround || !snapshot.IsGoalGroundNode(end.Position))
                return Invalid("PlanGoalMissing", result, designations, reusedNodes, groundNodes);

            return new AccessDesignationPlan(true, string.Empty, result.StartOrigin, handoffGround,
                handoffOperation,
                designations, reusedNodes, groundNodes);
        }

        private static AccessDesignationPlan Invalid(string reason, AccessSearchResult result,
            IReadOnlyList<AccessPlannedDesignation> designations, int reusedNodes, int groundNodes)
            => new AccessDesignationPlan(false, reason, result.StartOrigin, default,
                AccessHandoffOperation.None,
                designations, reusedNodes, groundNodes);

        private static bool IsOriginStep(Tile2i direction)
            => (Math.Abs(direction.X) == 4 && direction.Y == 0)
                || (Math.Abs(direction.Y) == 4 && direction.X == 0);

        private static bool ProfilesEqual(AccessHeightProfile left, AccessHeightProfile right)
            => left.Nw2 == right.Nw2 && left.Ne2 == right.Ne2
                && left.Se2 == right.Se2 && left.Sw2 == right.Sw2;

        private static int Manhattan(Tile2i left, Tile2i right)
            => Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y);
    }
}
