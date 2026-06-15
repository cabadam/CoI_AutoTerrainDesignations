using System.Collections.Generic;
using Mafi;
using Mafi.Core.Terrain;

namespace AutoTerrainDesignations.Access
{
    public enum AccessClusterState
    {
        CompleteNoAction,
        AccessibleDirect,
        AccessibleViaProvider,
        NeedsAccessway,
        AccessProvided,
        WaitingForProviderCompletion,
        Blocked,
        AnalysisPending
    }

    public enum BlockedReason
    {
        None,
        NoCandidate,
        MouthUnreachable,
        EdgeMismatch,
        Obstructed,
        UnsupportedGeometry
    }

    public enum AccessNeedType
    {
        NeedsAccessNow,
        NeverNeedsAccess
    }

    public struct AccessNeed
    {
        public AccessNeedType Type;
        public bool NeedsExcavator;
        public bool NeedsHauler;
        public bool TowerAssignmentRequired;

        public AccessNeed(AccessNeedType type, bool needsExcavator, bool needsHauler, bool towerAssignmentRequired)
        {
            Type = type;
            NeedsExcavator = needsExcavator;
            NeedsHauler = needsHauler;
            TowerAssignmentRequired = towerAssignmentRequired;
        }

        public static AccessNeed Never => new AccessNeed(AccessNeedType.NeverNeedsAccess, false, false, false);
        public static AccessNeed Mining => new AccessNeed(AccessNeedType.NeedsAccessNow, true, true, true);
        public static AccessNeed Filling => new AccessNeed(AccessNeedType.NeedsAccessNow, false, true, false);
    }

    public abstract class AccessWorkIntent
    {
        public abstract string IntentId { get; }
    }

    public class GenericWorkIntent : AccessWorkIntent
    {
        public override string IntentId { get; }

        public GenericWorkIntent(string intentId)
        {
            IntentId = intentId;
        }
    }

    public class AccessWorkOrigin
    {
        public Tile2i Origin { get; }
        public AccessWorkIntent SourceIntent { get; }
        public bool RequiresSoilTopLayer { get; }

        public AccessWorkOrigin(Tile2i origin, AccessWorkIntent sourceIntent, bool requiresSoilTopLayer)
        {
            Origin = origin;
            SourceIntent = sourceIntent;
            RequiresSoilTopLayer = requiresSoilTopLayer;
        }
    }

    public class AccessOriginCluster
    {
        public int ClusterId { get; }
        public IReadOnlyList<AccessWorkOrigin> Origins { get; }
        public IReadOnlyList<AccessWorkIntent> SourceIntents { get; }

        public AccessOriginCluster(int clusterId, IReadOnlyList<AccessWorkOrigin> origins, IReadOnlyList<AccessWorkIntent> sourceIntents)
        {
            ClusterId = clusterId;
            Origins = origins;
            SourceIntents = sourceIntents;
        }
    }

    public class AccessProvider
    {
        public IReadOnlyList<Tile2i> Tiles { get; }
        public bool ReachesGround { get; }

        public AccessProvider(IReadOnlyList<Tile2i> tiles, bool reachesGround)
        {
            Tiles = tiles;
            ReachesGround = reachesGround;
        }
    }

    public struct AccessEdge
    {
        public Tile2i A { get; }
        public Tile2i B { get; }
        public bool IsSlopeTraversable { get; }
        public bool IsFlatConnected { get; }

        public AccessEdge(Tile2i a, Tile2i b, bool isSlopeTraversable, bool isFlatConnected)
        {
            A = a;
            B = b;
            IsSlopeTraversable = isSlopeTraversable;
            IsFlatConnected = isFlatConnected;
        }
    }

    public class AccessCandidate
    {
        public Tile2i Mouth { get; }
        public IReadOnlyList<Tile2i> CandidateTiles { get; }
        public int VolumeMoved { get; }

        public AccessCandidate(Tile2i mouth, IReadOnlyList<Tile2i> candidateTiles, int volumeMoved)
        {
            Mouth = mouth;
            CandidateTiles = candidateTiles;
            VolumeMoved = volumeMoved;
        }
    }

    public class EvaluatedAccessCandidate
    {
        public Tile2i Mouth { get; }
        public bool IsValid { get; }
        public bool IsReachableNow { get; }
        public int MouthDistance { get; }
        public int MaterialMoved { get; }
        public int DesignationCount { get; }
        public object SourceCandidate { get; }

        public EvaluatedAccessCandidate(
            Tile2i mouth,
            bool isValid,
            bool isReachableNow,
            int mouthDistance,
            int materialMoved,
            int designationCount,
            object sourceCandidate)
        {
            Mouth = mouth;
            IsValid = isValid;
            IsReachableNow = isReachableNow;
            MouthDistance = mouthDistance;
            MaterialMoved = materialMoved;
            DesignationCount = designationCount;
            SourceCandidate = sourceCandidate;
        }

        public static int Compare(EvaluatedAccessCandidate left, EvaluatedAccessCandidate right)
        {
            if (left.IsValid != right.IsValid)
            {
                return left.IsValid ? -1 : 1;
            }

            if (left.IsReachableNow != right.IsReachableNow)
            {
                return left.IsReachableNow ? -1 : 1;
            }

            if (left.MouthDistance != right.MouthDistance)
            {
                return left.MouthDistance.CompareTo(right.MouthDistance);
            }

            if (left.MaterialMoved != right.MaterialMoved)
            {
                return left.MaterialMoved.CompareTo(right.MaterialMoved);
            }

            if (left.DesignationCount != right.DesignationCount)
            {
                return left.DesignationCount.CompareTo(right.DesignationCount);
            }

            return 0;
        }

        public static string GetDecidedBy(EvaluatedAccessCandidate best, EvaluatedAccessCandidate other)
        {
            if (best.IsValid != other.IsValid)
            {
                return "validity";
            }
            if (best.IsReachableNow != other.IsReachableNow)
            {
                return "reachable-now";
            }
            if (best.MouthDistance != other.MouthDistance)
            {
                return "mouth-distance";
            }
            if (best.MaterialMoved != other.MaterialMoved)
            {
                return "material-moved";
            }
            if (best.DesignationCount != other.DesignationCount)
            {
                return "designation-count";
            }
            return "default";
        }
    }

    public class AccessAnalysisResult
    {
        public AccessOriginCluster Cluster { get; }
        public AccessClusterState State { get; }
        public AccessNeed Need { get; }
        public AccessProvider? SelectedProvider { get; }
        public AccessCandidate? SelectedCandidate { get; }
        public BlockedReason BlockedReason { get; }
        public float CompletionPercentage { get; }

        public AccessAnalysisResult(
            AccessOriginCluster cluster, 
            AccessClusterState state, 
            AccessNeed need, 
            AccessProvider? selectedProvider, 
            AccessCandidate? selectedCandidate, 
            BlockedReason blockedReason, 
            float completionPercentage)
        {
            Cluster = cluster;
            State = state;
            Need = need;
            SelectedProvider = selectedProvider;
            SelectedCandidate = selectedCandidate;
            BlockedReason = blockedReason;
            CompletionPercentage = completionPercentage;
        }
    }
}
