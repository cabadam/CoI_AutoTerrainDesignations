using Mafi;

namespace AutoTerrainDesignations.Access
{
    public static class AccessDiagnostics
    {
        public static void LogClusterState(AccessAnalysisResult result)
        {
            Log.Info($"[ATD Access] originCluster={result.Cluster.ClusterId} origins={result.Cluster.Origins.Count} " +
                     $"need={FormatNeed(result.Need.Type)} state={result.State} {FormatBlockedReason(result.BlockedReason)}");
        }

        public static void LogCandidateSelected(AccessAnalysisResult result)
        {
            if (result.SelectedCandidate != null)
            {
                Log.Info($"[ATD Access] originCluster={result.Cluster.ClusterId} action=SelectedCandidate " +
                         $"mouth={result.SelectedCandidate.Mouth} tiles={result.SelectedCandidate.CandidateTiles.Count} " +
                         $"volume={result.SelectedCandidate.VolumeMoved}");
            }
        }

        public static void LogProviderBuilt(int clusterId, AccessProvider provider)
        {
            Log.Info($"[ATD Access] originCluster={clusterId} action=BuiltProvider tiles={provider.Tiles.Count}");
        }

        private static string FormatNeed(AccessNeedType type)
        {
            return type == AccessNeedType.NeedsAccessNow ? "now" : "never";
        }

        private static string FormatBlockedReason(BlockedReason reason)
        {
            return reason == BlockedReason.None ? string.Empty : $"blockedReason={reason}";
        }
    }
}
