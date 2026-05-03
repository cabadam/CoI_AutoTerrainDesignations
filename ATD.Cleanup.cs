// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
// Auto Terrain Designations - Designation Cleanup
using System.Collections.Generic;
using System.Linq;
using Mafi;
using Mafi.Collections;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.Terrain.Designation;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        private static void ClearDesignationsInArea(IAreaManagingTower tower)
        {
            if (s_desigManager == null) return;

            var originsToRemove = new List<Tile2i>();
            foreach (TerrainDesignation designation in tower.ManagedDesignations)
            {
                if (IsMiningDesignation(designation))
                {
                    originsToRemove.Add(designation.OriginTileCoord);
                }
            }

            foreach (Tile2i origin in originsToRemove)
            {
                s_desigManager.RemoveDesignation(origin);
            }
        }

        private static bool IsMiningDesignation(TerrainDesignation designation)
        {
            if (s_miningProto != null && designation.Prototype == s_miningProto)
            {
                return true;
            }

            return designation.Prototype.Id.Value == "MiningDesignator";
        }

        private static bool HasTerrainDesignationAtOrigin(IAreaManagingTower tower, Tile2i origin)
        {
            foreach (TerrainDesignation designation in tower.ManagedDesignations)
            {
                if (designation.OriginTileCoord == origin)
                {
                    return true;
                }
            }

            return false;
        }

        internal static void ClearDesignationsForTower(IAreaManagingTower tower)
        {
            ClearDesignationsInArea(tower);
        }

        private static void RemoveFulfilledDesignationsForTower(IAreaManagingTower tower)
        {
            if (s_desigManager == null)
            {
                return;
            }

            var fulfilledOrigins = new List<Tile2i>();
            foreach (TerrainDesignation designation in tower.ManagedDesignations)
            {
                if (IsMiningDesignation(designation) && designation.IsFulfilled)
                {
                    fulfilledOrigins.Add(designation.OriginTileCoord);
                }
            }

            foreach (Tile2i origin in fulfilledOrigins)
            {
                s_desigManager.RemoveDesignation(origin);
            }
        }

        private static void CleanupIsolatedLeftoverDesignationsForTower(IAreaManagingTower tower, Dict<Tile2i, int> originalOreOrigins)
        {
            if (s_desigManager == null)
            {
                return;
            }

            var remainingOrigins = new HashSet<Tile2i>();
            foreach (TerrainDesignation designation in tower.ManagedDesignations)
            {
                if (IsMiningDesignation(designation) && !designation.IsFulfilled)
                {
                    remainingOrigins.Add(designation.OriginTileCoord);
                }
            }

            if (remainingOrigins.Count == 0)
            {
                return;
            }

            var originalOriginSet = new HashSet<Tile2i>(originalOreOrigins.Keys);
            var visited = new HashSet<Tile2i>();
            var originsToRemove = new List<Tile2i>();

            foreach (Tile2i origin in remainingOrigins)
            {
                if (visited.Contains(origin))
                {
                    continue;
                }

                var component = new List<Tile2i>();
                FloodFillOrigins(origin, remainingOrigins, visited, component);

                bool touchesOriginalOre = component.Any(originalOriginSet.Contains);
                if (!touchesOriginalOre)
                {
                    originsToRemove.AddRange(component);
                }
            }

            foreach (Tile2i origin in originsToRemove)
            {
                s_desigManager.RemoveDesignation(origin);
            }

            if (originsToRemove.Count > 0)
            {
                LogDebug(string.Format("Removed {0} isolated leftover designation tile(s) after ramp cleanup", originsToRemove.Count));
            }
        }
    }
}
