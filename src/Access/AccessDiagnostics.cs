using Mafi;

namespace AutoTerrainDesignations.Access
{
    public static class AccessDiagnostics
    {
        // Reserved for a future diagnostics toggle. Legacy access-planning traces
        // are intentionally silent by default; warnings remain unconditional.
        internal static bool VerboseLoggingEnabled { get; set; }

        internal static void LogDebug(string message)
        {
            if (VerboseLoggingEnabled)
                Log.Info(message);
        }

        public static void LogClusterState(AccessAnalysisResult result)
        {
            if (!VerboseLoggingEnabled) return;

            Log.Info($"[ATD Access] originCluster={result.Cluster.ClusterId} origins={result.Cluster.Origins.Count} " +
                     $"need={FormatNeed(result.Need.Type)} state={result.State} {FormatBlockedReason(result.BlockedReason)}");
        }

        public static void LogCandidateSelected(AccessAnalysisResult result)
        {
            if (!VerboseLoggingEnabled) return;

            if (result.SelectedCandidate != null)
            {
                Log.Info($"[ATD Access] originCluster={result.Cluster.ClusterId} action=SelectedCandidate " +
                         $"mouth={result.SelectedCandidate.Mouth} tiles={result.SelectedCandidate.CandidateTiles.Count} " +
                         $"volume={result.SelectedCandidate.VolumeMoved}");
            }
        }

        public static void LogProviderBuilt(int clusterId, AccessProvider provider)
        {
            if (!VerboseLoggingEnabled) return;

            Log.Info($"[ATD Access] originCluster={clusterId} action=BuiltProvider tiles={provider.Tiles.Count}");
        }

        public static void LogAccessProvided(int clusterId, string providerName, string protoName, Tile2i origin, string direction, string decidedBy)
        {
            if (!VerboseLoggingEnabled) return;

            Log.Info($"[ATD Access] originCluster={clusterId} state=AccessProvided provider={providerName} proto={protoName} origin={origin} direction={direction} decidedBy={decidedBy}");
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
