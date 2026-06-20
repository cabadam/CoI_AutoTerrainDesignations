using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.Terrain;

namespace AutoTerrainDesignations.Access
{
    public static class AccessReachability
    {
        private static readonly Tile2i[] s_cardinalDirections = new Tile2i[]
        {
            new Tile2i(4, 0),
            new Tile2i(-4, 0),
            new Tile2i(0, 4),
            new Tile2i(0, -4)
        };

        /// <summary>
        /// Evaluates the reachability of a set of clusters using a single fixpoint forward flood.
        /// </summary>
        public static Dictionary<AccessOriginCluster, AccessClusterState> EvaluateReachability(
            IReadOnlyList<AccessOriginCluster> clusters,
            IReadOnlyList<AccessProvider> existingProviders,
            IAreaManagingTower tower,
            TerrainManager terrMgr,
            Func<Tile2i, bool> isReachableFromTower,
            Func<Tile2i, Tile2i, bool> isEdgeTraversable)
        {
            var states = new Dictionary<AccessOriginCluster, AccessClusterState>();
            var clusterTileLookup = new Dictionary<Tile2i, AccessOriginCluster>();

            foreach (var cluster in clusters)
            {
                states[cluster] = AccessClusterState.AnalysisPending;
                foreach (var origin in cluster.Origins)
                {
                    clusterTileLookup[origin.Origin] = cluster;
                    if (states[cluster] != AccessClusterState.AccessibleDirect && isReachableFromTower(origin.Origin))
                    {
                        AccessDiagnostics.LogDebug($"[ATD Access Debug] Cluster {cluster.ClusterId} determined AccessibleDirect via origin tile {origin.Origin}");
                        states[cluster] = AccessClusterState.AccessibleDirect;
                    }
                }
            }

            var providerTiles = new HashSet<Tile2i>();
            foreach (var provider in existingProviders)
            {
                if (provider.ReachesGround)
                {
                    foreach (var tile in provider.Tiles)
                    {
                        providerTiles.Add(tile);
                    }
                }
            }

            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var cluster in clusters)
                {
                    if (states[cluster] != AccessClusterState.AnalysisPending)
                    {
                        continue;
                    }

                    if (CheckClusterConnection(cluster, clusterTileLookup, providerTiles, states, isEdgeTraversable))
                    {
                        states[cluster] = AccessClusterState.AccessibleViaProvider;
                        changed = true;
                    }
                }
            }

            foreach (var cluster in clusters)
            {
                if (states[cluster] == AccessClusterState.AnalysisPending)
                {
                    states[cluster] = AccessClusterState.NeedsAccessway;
                }
            }

            return states;
        }

        private static bool CheckClusterConnection(
            AccessOriginCluster cluster,
            Dictionary<Tile2i, AccessOriginCluster> clusterTileLookup,
            HashSet<Tile2i> providerTiles,
            Dictionary<AccessOriginCluster, AccessClusterState> states,
            Func<Tile2i, Tile2i, bool> isEdgeTraversable)
        {
            foreach (var origin in cluster.Origins)
            {
                foreach (var direction in s_cardinalDirections)
                {
                    Tile2i neighbor = new Tile2i(origin.Origin.X + direction.X, origin.Origin.Y + direction.Y);

                    // 1. Is it touching another tile in the same cluster? (Internal edge, skip)
                    if (clusterTileLookup.TryGetValue(neighbor, out var neighborCluster) && neighborCluster == cluster)
                    {
                        continue;
                    }

                    // For all external edges, we must verify the terrain heights match.
                    if (!isEdgeTraversable(origin.Origin, direction))
                    {
                        continue;
                    }

                    // 2. Is it touching a connected AccessProvider?
                    if (providerTiles.Contains(neighbor))
                    {
                        AccessDiagnostics.LogDebug($"[ATD Access Debug] Cluster {cluster.ClusterId} accessible via provider tile {neighbor} from origin {origin.Origin}");
                        return true;
                    }

                    // 3. Is it touching another accessible cluster?
                    if (neighborCluster != null)
                    {
                        if (states[neighborCluster] == AccessClusterState.AccessibleDirect || 
                            states[neighborCluster] == AccessClusterState.AccessibleViaProvider)
                        {
                            AccessDiagnostics.LogDebug($"[ATD Access Debug] Cluster {cluster.ClusterId} accessible via neighbor cluster {neighborCluster.ClusterId} at tile {neighbor} from origin {origin.Origin}");
                            return true; // Connected via another cluster
                        }
                    }
                }
            }

            return false;
        }
    }
}
