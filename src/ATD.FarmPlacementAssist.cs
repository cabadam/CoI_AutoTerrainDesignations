// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
// Auto Terrain Designations - Farm Placement Assist
// Phase 0 spike: all S1-S5 confirmed 2026-05-30.
// Phase 1-3 production: intercept, designation injection, AreCellsDone-gated replay.
//
// Enable / disable via FARM_PLACEMENT_ASSIST_SPIKE in csproj Debug DefineConstants.

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Buildings.Farms;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static.Commands;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Entities.Validators;
using Mafi.Core.Input;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
#if FARM_PLACEMENT_ASSIST_SPIKE

        // ---------------------------------------------------------------------------------
        // Static state
        // ---------------------------------------------------------------------------------

        // s_inputScheduler is declared and wired in ATD.State.cs.

        private static readonly List<PlacementIntent> s_pendingFarmPlacements = new();

        private sealed class PlacementIntent
        {
            public readonly FarmProto Proto;
            public readonly Tile2i Position;
            public readonly int Z;
            public readonly Rotation90 Rotation;
            public readonly IAreaManagingTower Tower;
            public readonly IReadOnlyList<Tile2i> RequiredCells;
            public readonly HashSet<Tile2i> AtdInjectedCells = new();

            public PlacementIntent(FarmProto proto, Tile2i position, int z, Rotation90 rotation,
                IAreaManagingTower tower, IReadOnlyList<Tile2i> requiredCells)
            {
                Proto = proto; Position = position; Z = z; Rotation = rotation;
                Tower = tower; RequiredCells = requiredCells;
            }
        }

        // ---------------------------------------------------------------------------------
        // Intercept patch — Phase 3-A (corrected)
        // Farms use BatchCreateStaticEntitiesCmd, NOT CreateStaticEntityCmd.
        // Confirmed by S1 probe: Invoke(CreateStaticEntityCmd) never fires for farms.
        // ---------------------------------------------------------------------------------

        private static class Patch_EntitiesCommandsProcessor_Invoke_Batch_Farm
        {
            // [SPIKE S1] Confirmed: this is the real path for farm placement.
            internal static bool Prefix(BatchCreateStaticEntitiesCmd cmd)
            {
                if (!IsInitialized) return true;
                if (s_protosDb == null) return true;

                // Scan the batch for any farm proto in a known tower area.
                // Spike simplification: blocks the whole batch if any farm is intercepted.
                // Production: split batch — remove farm entries and re-dispatch the rest.
                bool anyIntercepted = false;
                foreach (EntityConfigData item in cmd.ConfigData)
                {
                    if (item.Prototype.ValueOrNull is not FarmProto farmProto) continue;

                    TileTransform? transform = item.Transform;
                    if (!transform.HasValue) continue;

                    Tile2i position = transform.Value.Position.Xy;
                    IAreaManagingTower? tower = GetFarmingTowerForTile(position);
                    if (tower == null) continue;

                    s_log.Info($"[ATD FarmPlacementAssist] Intercepted farm placement: " +
                        $"proto={farmProto.Id}, pos={position}, rot={transform.Value.Rotation}, tower={tower.Id}.");

                    OnFarmPlacementIntercepted(farmProto, position, transform.Value.Position.Z,
                        transform.Value.Rotation, tower);
                    anyIntercepted = true;
                }

                if (!anyIntercepted) return true;

                // Report success so the engine doesn't play the error sound/toast.
                // No entity is created because we returned false (skipped original Invoke).
                cmd.SetResultSuccess();
                return false;
            }
        }

        // ---------------------------------------------------------------------------------
        // Validator suppression patches — Phase 2
        // Allow farm placement (ghost bypass) on uneven / infertile ground in tower areas.
        // Note: These patches fire at HOVER time too (validator CanAdd is used for preview).
        // ---------------------------------------------------------------------------------

        // Phase 2-A: FarmFertileGroundValidator.CanAdd
        // Takes concrete LayoutEntityAddRequest (not the interface).
        private static class Patch_FarmFertileGroundValidator_CanAdd
        {
            // [SPIKE S5] Confirm green tiles appear over uneven ground in a tower area.
            internal static bool Prefix(LayoutEntityAddRequest addRequest, ref EntityValidationResult __result)
            {
                if (!IsInitialized) return true;
                if (GetFarmingTowerForRequest(addRequest) == null) return true;
                s_log.Info("[ATD FarmPlacementAssist SPIKE] FarmFertileGroundValidator suppressed.");
                __result = EntityValidationResult.Success;
                return false;
            }
        }

        // Phase 2-B: LayoutEntityTerrainValidator.CanAdd
        // Explicit interface implementation — located via interface map, not string name.
        private static class Patch_LayoutEntityTerrainValidator_CanAdd_Farm
        {
            // [SPIKE: confirm this TargetMethod approach compiles and binds correctly;
            //  verify the interface map has exactly one CanAdd entry for this interface.]
            internal static MethodBase TargetMethod()
            {
                var iface = typeof(IEntityAdditionValidator<ILayoutEntityAddRequest>);
                var map = typeof(LayoutEntityTerrainValidator).GetInterfaceMap(iface);
                // The interface has a single method: CanAdd(ILayoutEntityAddRequest)
                return map.TargetMethods[0];
            }

            internal static bool Prefix(ILayoutEntityAddRequest addRequest, ref EntityValidationResult __result)
            {
                if (!IsInitialized) return true;
                if (addRequest.Proto is not FarmProto) return true;
                if (GetFarmingTowerForRequest(addRequest) == null) return true;
                s_log.Info("[ATD FarmPlacementAssist SPIKE] LayoutEntityTerrainValidator suppressed.");
                __result = EntityValidationResult.Success;
                return false;
            }
        }

        // ---------------------------------------------------------------------------------
        // Tower-area helper — Phase 1-A stub
        // [TODO Phase 1: move to ATD.FarmingPreparationSession.cs as internal static]
        // ---------------------------------------------------------------------------------

        /// <summary>Returns the first farming-enabled tower whose area contains tile, or null.</summary>
        internal static IAreaManagingTower? GetFarmingTowerForTile(Tile2i tile)
        {
            foreach (var kvp in s_farmingPreparationSessions)
            {
                IAreaManagingTower? tower = kvp.Value.Tower;
                if (tower != null && tower.Area.ContainsTile(tile)) return tower;
            }
#if FARM_PLACEMENT_ASSIST_SPIKE
            // Spike fallback: no active sessions → check all mine towers directly so testing
            // doesn't require manually starting farming preparation on a tower first.
            if (s_farmingPreparationSessions.Count == 0 && s_entitiesManager != null)
            {
                foreach (Mafi.Core.Buildings.Mine.MineTower tower in
                    s_entitiesManager.GetAllEntitiesOfType<Mafi.Core.Buildings.Mine.MineTower>())
                {
                    if (tower.Area.ContainsTile(tile)) return tower;
                }
            }
#endif
            return null;
        }

        /// <summary>Returns the farming tower if all 4 corners of the farm's bounding rect are inside it, or null.</summary>
        internal static IAreaManagingTower? GetFarmingTowerForRequest(LayoutEntityAddRequest addRequest)
            => GetFarmingTowerForRequest((ILayoutEntityAddRequest)addRequest);

        internal static IAreaManagingTower? GetFarmingTowerForRequest(ILayoutEntityAddRequest addRequest)
        {
            if (addRequest.Proto is not FarmProto) return null;
            var r = addRequest.BoundingRect;
            IAreaManagingTower? tower = GetFarmingTowerForTile(r.Origin);
            if (tower == null) return null;
            if (!tower.Area.ContainsTile(r.PlusXTileIncl)) return null;
            if (!tower.Area.ContainsTile(r.PlusYTileIncl)) return null;
            if (!tower.Area.ContainsTile(new Tile2i(r.Origin.X + r.Size.X - 1, r.Origin.Y + r.Size.Y - 1))) return null;
            return tower;
        }

        // ---------------------------------------------------------------------------------
        // Core Phase 3 — covered cell computation, designation injection, intent tracking
        // ---------------------------------------------------------------------------------

        /// <summary>Returns the unique 4×4 designation-grid origins covered by the farm footprint.</summary>
        internal static List<Tile2i> ComputeCoveredDesignationCells(FarmProto proto, Tile2i position, Rotation90 rotation)
        {
            // Build a zero-Z TileTransform so GetOccupiedTilesRelative handles rotation.
            var transform = new TileTransform(new Tile3i(position.X, position.Y, 0), rotation, isReflected: false);
            ImmutableArray<OccupiedTileRelative> relTiles = proto.Layout.GetOccupiedTilesRelative(transform);
            var origins = new HashSet<Tile2i>();
            foreach (OccupiedTileRelative rel in relTiles)
                origins.Add(SnapToDesignationGrid(position + rel.RelCoord));
            return new List<Tile2i>(origins);
        }

        /// <summary>Returns true if every cell is already in Done phase inside an active farming session.</summary>
        private static bool AreCellsAlreadyFarmable(IReadOnlyList<Tile2i> cells)
        {
            foreach (Tile2i origin in cells)
            {
                bool done = false;
                foreach (var kvp in s_farmingPreparationSessions)
                {
                    if (kvp.Value.Origins.TryGetValue(origin, out var os) && os.Phase == FarmingOriginPhase.Done)
                    { done = true; break; }
                }
                if (!done) return false;
            }
            return cells.Count > 0;
        }

        /// <summary>Returns true when all required cells are Done in an active farming session.</summary>
        private static bool AreCellsDone(PlacementIntent intent)
            => AreCellsAlreadyFarmable(intent.RequiredCells);

        /// <summary>
        /// Ensures a flat leveling designation exists at <paramref name="origin"/> at
        /// <paramref name="targetHeight"/>. Records the origin in <paramref name="intent"/>.AtdInjectedCells
        /// if a new designation was created so it can be cleaned up if the player cancels.
        /// </summary>
        private static void EnsureFarmingDesignationForCell(Tile2i origin, int targetHeight, PlacementIntent intent)
        {
            if (s_desigManager == null || s_levelingProto == null) return;

            // Skip if a designation already exists at this origin.
            if (s_desigManager.GetDesignationAt(origin).HasValue) return;

            var data = new DesignationData(origin, new HeightTilesI(targetHeight));
            if (s_desigManager.AddOrReplaceDesignation(s_levelingProto, data))
                intent.AtdInjectedCells.Add(origin);
            else
                s_log.Warning($"[ATD FarmPlacementAssist] Failed to add leveling designation at {origin}.");
        }

        /// <summary>
        /// Called when a farm placement is intercepted. Either replays immediately (site ready)
        /// or injects leveling designations and registers a pending intent.
        /// </summary>
        private static void OnFarmPlacementIntercepted(
            FarmProto proto, Tile2i position, int z, Rotation90 rotation, IAreaManagingTower tower)
        {
            List<Tile2i> cells = ComputeCoveredDesignationCells(proto, position, rotation);

            // If the site is already fully prepared, replay immediately — no deferral needed.
            if (AreCellsAlreadyFarmable(cells))
            {
                s_log.Info($"[ATD FarmPlacementAssist] Site already farmable — replaying immediately.");
                ReplayFarmPlacement(proto, position, z, rotation);
                return;
            }

            var intent = new PlacementIntent(proto, position, z, rotation, tower, cells);

            // Determine target height from the farm's origin tile.
            int targetHeight = s_desigManager != null
                ? (int)Math.Floor(s_desigManager.TerrainManager.GetHeight(position).Value.ToFloat())
                : z;

            // Inject a flat leveling designation for every cell that doesn't already have one.
            foreach (Tile2i origin in cells)
                EnsureFarmingDesignationForCell(origin, targetHeight, intent);

            s_pendingFarmPlacements.Add(intent);
            s_log.Info($"[ATD FarmPlacementAssist] Deferred. {cells.Count} cells queued, " +
                $"{intent.AtdInjectedCells.Count} new designations injected.");
        }

        // ---------------------------------------------------------------------------------
        // Replay path (Phase 4-C — S4 confirmed 2026-05-30)
        // ---------------------------------------------------------------------------------

        internal static void ReplayFarmPlacement(FarmProto proto, Tile2i position, int z, Rotation90 rotation)
        {
            if (s_inputScheduler == null)
            {
                s_log.Warning("[ATD FarmPlacementAssist] Cannot replay: IInputScheduler not available.");
                return;
            }
            var transform = new TileTransform(new Tile3i(position.X, position.Y, z), rotation, isReflected: false);
            var cmd = new CreateStaticEntityCmd(proto.Id, transform, isFree: false, allowValidationSuppression: false);
            s_inputScheduler.ScheduleInputCmd(cmd);
            s_log.Info($"[ATD FarmPlacementAssist] Replayed farm placement: proto={proto.Id}, pos={position}.");
        }

        // ---------------------------------------------------------------------------------
        // Tick — AreCellsDone-gated replay (Phase 4-A)
        // ---------------------------------------------------------------------------------

        internal static void TickFarmPlacementAssist()
        {
            if (s_pendingFarmPlacements.Count == 0) return;
            for (int i = s_pendingFarmPlacements.Count - 1; i >= 0; i--)
            {
                PlacementIntent intent = s_pendingFarmPlacements[i];
                if (!AreCellsDone(intent)) continue;
                s_pendingFarmPlacements.RemoveAt(i);
                ReplayFarmPlacement(intent.Proto, intent.Position, intent.Z, intent.Rotation);
            }
        }

        // ---------------------------------------------------------------------------------
        // Patch registration — ATD does not use PatchAll(); every patch must be registered
        // explicitly. Call from ATD.Mod.cs alongside the other Apply*Patches calls.
        // ---------------------------------------------------------------------------------

        internal static void ApplyFarmPlacementAssistPatches(Harmony harmony)
        {
            try
            {
                // --- Intercept: EntitiesCommandsProcessor.Invoke(BatchCreateStaticEntitiesCmd) ---
                var invokeTarget = AccessTools.Method(
                    typeof(EntitiesCommandsProcessor), "Invoke",
                    new[] { typeof(BatchCreateStaticEntitiesCmd) });
                var invokePrefix = AccessTools.Method(
                    typeof(Patch_EntitiesCommandsProcessor_Invoke_Batch_Farm), "Prefix");
                if (invokeTarget == null || invokePrefix == null)
                    s_log.Warning("[ATD FPA] EntitiesCommandsProcessor.Invoke(Batch) patch target not found.");
                else
                    harmony.Patch(invokeTarget, prefix: new HarmonyMethod(invokePrefix));
            }
            catch (Exception ex) { s_log.Warning("[ATD FPA] Failed to patch EntitiesCommandsProcessor.Invoke(Batch): " + ex.Message); }

            try
            {
                // --- FarmFertileGroundValidator.CanAdd ---
                var fertileTarget = AccessTools.Method(
                    typeof(FarmFertileGroundValidator), "CanAdd");
                var fertilePrefix = AccessTools.Method(
                    typeof(Patch_FarmFertileGroundValidator_CanAdd), "Prefix");
                if (fertileTarget == null || fertilePrefix == null)
                    s_log.Warning("[ATD FPA] FarmFertileGroundValidator.CanAdd patch target not found.");
                else
                    harmony.Patch(fertileTarget, prefix: new HarmonyMethod(fertilePrefix));
            }
            catch (Exception ex) { s_log.Warning("[ATD FPA] Failed to patch FarmFertileGroundValidator.CanAdd: " + ex.Message); }

            try
            {
                // --- LayoutEntityTerrainValidator.CanAdd (explicit interface impl) ---
                var terrainTarget = Patch_LayoutEntityTerrainValidator_CanAdd_Farm.TargetMethod();
                var terrainPrefix = AccessTools.Method(
                    typeof(Patch_LayoutEntityTerrainValidator_CanAdd_Farm), "Prefix");
                if (terrainTarget == null || terrainPrefix == null)
                    s_log.Warning("[ATD FPA] LayoutEntityTerrainValidator.CanAdd patch target not found.");
                else
                    harmony.Patch(terrainTarget, prefix: new HarmonyMethod(terrainPrefix));
            }
            catch (Exception ex) { s_log.Warning("[ATD FPA] Failed to patch LayoutEntityTerrainValidator.CanAdd: " + ex.Message); }
        }

#endif // FARM_PLACEMENT_ASSIST_SPIKE
    }
}
