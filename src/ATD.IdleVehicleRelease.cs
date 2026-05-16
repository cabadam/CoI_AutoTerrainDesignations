// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
// Auto Terrain Designations - Idle Vehicle Release
using System.Collections.Generic;
using Mafi;
using Mafi.Core;
using Mafi.Core.Buildings.Mine;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Prototypes;
using Mafi.Core.Terrain.Designation;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        // Key present → vehicles for that tower are currently in the "released" state.
        // Value is the list of vehicles we released (may be empty if the tower had no vehicles at release time).
        private static readonly Dictionary<EntityId, List<Vehicle>> s_idleReleasedVehiclesByTower =
            new Dictionary<EntityId, List<Vehicle>>();

        /// <summary>
        /// Clears all idle-release tracking state (called from ResetWorldRuntimeState).
        /// Does NOT attempt to restore vehicles — the world is being torn down.
        /// </summary>
        private static void ClearIdleVehicleReleaseState()
        {
            s_idleReleasedVehiclesByTower.Clear();
        }

        /// <summary>
        /// Returns true if any designation managed by <paramref name="tower"/> still has
        /// pending excavation work: a mining or leveling designation where at least one
        /// tile's terrain is still above the designation's target height.
        /// </summary>
        private static bool HasPendingExcavationJobs(MineTower tower)
        {
            if (s_miningProto == null || s_levelingProto == null)
                return true; // can't determine → assume yes, keep vehicles

            foreach (TerrainDesignation desig in tower.ManagedDesignations)
            {
                Proto.ID protoId = desig.Prototype.Id;
                if (protoId != s_miningProto.Id && protoId != s_levelingProto.Id)
                    continue;

                if (desig.IsMiningNotFulfilled)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Main per-tick entry point. Called by the ticker on the same 1-second game-time
        /// interval as farming preparation sessions.
        /// </summary>
        internal static void TickIdleVehicleRelease()
        {
            if (s_entitiesManager == null)
                return;

            foreach (MineTower tower in s_entitiesManager.GetAllEntitiesOfType<MineTower>())
            {
                if (tower.IsDestroyed)
                    continue;

                if (!TryGetTowerEntityId(tower, out EntityId towerId))
                    continue;

                if (!IsIdleVehicleReleaseEnabledForId(towerId))
                {
                    // Feature disabled — if we previously released vehicles for this tower, restore them.
                    if (s_idleReleasedVehiclesByTower.ContainsKey(towerId))
                        RestoreIdleReleasedVehicles(tower, towerId);
                    continue;
                }

                bool hasPending = HasPendingExcavationJobs(tower);

                if (hasPending)
                {
                    // Work returned — restore any previously released vehicles.
                    if (s_idleReleasedVehiclesByTower.ContainsKey(towerId))
                        RestoreIdleReleasedVehicles(tower, towerId);
                }
                else
                {
                    // No pending excavation — release vehicles if not already in released state.
                    if (!s_idleReleasedVehiclesByTower.ContainsKey(towerId))
                    {
                        ReleaseIdleVehicles(tower, towerId);
                    }
                    else
                    {
                        // Already in released state, but the player may have manually assigned new
                        // vehicles since the last release pass. Evict any that aren't in our list.
                        ReleaseStrayVehicles(tower, towerId);
                    }
                }
            }
        }

        /// <summary>
        /// If vehicles were released for the given tower (e.g. because the setting was turned
        /// off), restores them immediately. Safe to call when tower is not a MineTower.
        /// </summary>
        private static void TryRestoreIdleReleasedVehiclesForTower(IAreaManagingTower tower)
        {
            if (!TryGetTowerEntityId(tower, out EntityId towerId))
                return;
            if (!s_idleReleasedVehiclesByTower.ContainsKey(towerId))
                return;
            if (tower is MineTower mineTower)
                RestoreIdleReleasedVehicles(mineTower, towerId);
            else
                s_idleReleasedVehiclesByTower.Remove(towerId);
        }

        private static bool IsIdleVehicleReleaseEnabledForId(EntityId towerId)
        {
            if (s_towerSettingsByEntityId.TryGetValue(towerId, out ATDTowerSettings settings))
                return settings.AutoReleaseVehiclesWhenIdle;
            return AutoTerrainDesignationsMod.AutoReleaseVehiclesWhenIdle;
        }

        private static void ReleaseIdleVehicles(MineTower tower, EntityId towerId)
        {
            var released = new List<Vehicle>();
            var snapshot = new List<Vehicle>();
            foreach (Vehicle v in tower.AllVehicles)
                snapshot.Add(v);

            int releasedCount = 0;
            int failedCount = 0;
            foreach (Vehicle vehicle in snapshot)
            {
                if (vehicle == null || vehicle.IsDestroyed)
                    continue;

                try
                {
                    tower.UnassignVehicle(vehicle, true);
                    released.Add(vehicle);
                    releasedCount++;
                }
                catch
                {
                    failedCount++;
                }
            }

            // Record the released state even if no vehicles were released (so we don't re-enter this branch).
            s_idleReleasedVehiclesByTower[towerId] = released;

            if (releasedCount > 0 || failedCount > 0)
                LogDebug($"[IdleRelease] Tower {towerId}: released {releasedCount} vehicle(s) (failed={failedCount}).");
            else
                LogDebug($"[IdleRelease] Tower {towerId}: no pending excavation, no vehicles to release.");
        }

        /// <summary>
        /// Called when the tower is already in released state but may have vehicles that were
        /// manually assigned after the initial release. Evicts any vehicle not already tracked
        /// in the released list and adds it to the list.
        /// </summary>
        private static void ReleaseStrayVehicles(MineTower tower, EntityId towerId)
        {
            if (!s_idleReleasedVehiclesByTower.TryGetValue(towerId, out List<Vehicle> released))
                return;

            var snapshot = new List<Vehicle>();
            foreach (Vehicle v in tower.AllVehicles)
                snapshot.Add(v);

            int releasedCount = 0;
            foreach (Vehicle vehicle in snapshot)
            {
                if (vehicle == null || vehicle.IsDestroyed)
                    continue;
                if (released.Contains(vehicle))
                    continue; // already tracked

                try
                {
                    tower.UnassignVehicle(vehicle, true);
                    released.Add(vehicle);
                    releasedCount++;
                }
                catch { }
            }

            if (releasedCount > 0)
                LogDebug($"[IdleRelease] Tower {towerId}: evicted {releasedCount} stray vehicle(s) assigned while idle.");
        }

        /// <summary>
        /// Called by <c>beforeSave</c>: re-assigns all currently-released vehicles back to their
        /// towers so the save file captures the assignment. The in-memory tracking dict is left
        /// intact; <see cref="ReReleaseIdleVehiclesAfterSave"/> unassigns them again afterward.
        /// </summary>
        internal static void RestoreIdleReleasedVehiclesForSave()
        {
            if (s_entitiesManager == null || s_idleReleasedVehiclesByTower.Count == 0)
                return;

            foreach (MineTower tower in s_entitiesManager.GetAllEntitiesOfType<MineTower>())
            {
                if (tower.IsDestroyed)
                    continue;

                if (!TryGetTowerEntityId(tower, out EntityId towerId))
                    continue;

                if (!s_idleReleasedVehiclesByTower.TryGetValue(towerId, out List<Vehicle> released))
                    continue;

                int restored = 0;
                foreach (Vehicle vehicle in released)
                {
                    if (vehicle == null || vehicle.IsDestroyed)
                        continue;
                    if (tower.AllVehicles.Contains(vehicle))
                        continue;
                    if (vehicle.AssignedTo.HasValue)
                        continue;

                    try
                    {
                        tower.AssignVehicle(vehicle, true);
                        restored++;
                    }
                    catch { }
                }

                if (restored > 0)
                    LogDebug($"[IdleRelease] Tower {towerId}: re-assigned {restored} vehicle(s) before save.");
            }
        }

        /// <summary>
        /// Called by <c>onSaveDone</c>: unassigns the vehicles that were temporarily re-assigned
        /// by <see cref="RestoreIdleReleasedVehiclesForSave"/>, restoring the released state.
        /// </summary>
        internal static void ReReleaseIdleVehiclesAfterSave()
        {
            if (s_entitiesManager == null || s_idleReleasedVehiclesByTower.Count == 0)
                return;

            foreach (MineTower tower in s_entitiesManager.GetAllEntitiesOfType<MineTower>())
            {
                if (tower.IsDestroyed)
                    continue;

                if (!TryGetTowerEntityId(tower, out EntityId towerId))
                    continue;

                if (!s_idleReleasedVehiclesByTower.TryGetValue(towerId, out List<Vehicle> released))
                    continue;

                int rereleased = 0;
                foreach (Vehicle vehicle in released)
                {
                    if (vehicle == null || vehicle.IsDestroyed)
                        continue;
                    if (!tower.AllVehicles.Contains(vehicle))
                        continue;

                    try
                    {
                        tower.UnassignVehicle(vehicle, true);
                        rereleased++;
                    }
                    catch { }
                }

                if (rereleased > 0)
                    LogDebug($"[IdleRelease] Tower {towerId}: re-released {rereleased} vehicle(s) after save.");
            }
        }

        private static void RestoreIdleReleasedVehicles(MineTower tower, EntityId towerId)
        {
            if (!s_idleReleasedVehiclesByTower.TryGetValue(towerId, out List<Vehicle> released))
                return;

            int restored = 0;
            int skipped = 0;
            int failed = 0;
            foreach (Vehicle vehicle in released)
            {
                if (vehicle == null || vehicle.IsDestroyed)
                {
                    skipped++;
                    continue;
                }

                // Skip if the vehicle was already re-assigned (e.g. by the player or farming session).
                if (tower.AllVehicles.Contains(vehicle))
                {
                    skipped++;
                    continue;
                }

                if (vehicle.AssignedTo.HasValue)
                {
                    skipped++;
                    continue;
                }

                try
                {
                    tower.AssignVehicle(vehicle, true);
                    restored++;
                }
                catch
                {
                    failed++;
                }
            }

            s_idleReleasedVehiclesByTower.Remove(towerId);

            if (restored > 0 || skipped > 0 || failed > 0)
                LogDebug($"[IdleRelease] Tower {towerId}: restored {restored} vehicle(s) (skipped={skipped}, failed={failed}).");
        }
    }
}
