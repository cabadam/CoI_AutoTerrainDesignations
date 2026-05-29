// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
// Auto Terrain Designations - Farm Placement Assist (Phase 0 spike)
//
// SPIKE FILE — not production-ready. All [SPIKE] annotations mark things that must be
// verified or completed before this code ships. The spike goal is to confirm:
//   (S1) EntitiesCommandsProcessor.Invoke(CreateStaticEntityCmd) fires on player placement.
//   (S2) cmd.ProtoId resolves to a FarmProto; cmd.Transform has correct position.
//   (S3) GetFarmingTowerForTile correctly identifies a farming-enabled tower area.
//   (S4) ReplayFarmPlacement successfully re-places the farm via IInputScheduler.
//   (S5) The validator patches allow placement on uneven / infertile ground in a tower area.
//
// Enable / disable the spike by toggling FARM_PLACEMENT_ASSIST_SPIKE in project settings or:
#define FARM_PLACEMENT_ASSIST_SPIKE

using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Core;
using Mafi.Core.Buildings.Farms;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static.Commands;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Entities.Validators;
using Mafi.Core.Input;
using Mafi.Core.Terrain;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
#if FARM_PLACEMENT_ASSIST_SPIKE

        // ---------------------------------------------------------------------------------
        // Static state
        // ---------------------------------------------------------------------------------

        // s_inputScheduler is declared and wired in ATD.State.cs.

        // Sentinel error string used in the intercept to make createStaticEntityFromCmd
        // believe placement failed (and thus destroy the floating entity / set cmd error).
        // [SPIKE: verify this doesn't surface as a player-visible notification — if it does,
        // patch the notification path to suppress messages matching this constant.]
        private const string ATD_FARM_INTERCEPT_REASON = "ATD_FarmPlacementIntercepted";

        // Pending farm placements: list of intents waiting for site preparation.
        // [TODO Phase 3-E: replace with proper PlacementIntent class]
        // Z is taken from the original command's transform so we don't need to call GetHeight at intercept time.
        // At replay time we'll use the same Z; production code should re-read surface height after preparation.
        private static readonly List<(FarmProto Proto, Tile2i Position, int Z, Rotation90 Rotation, IAreaManagingTower Tower)>
            s_spikeIntents = new();

        // ---------------------------------------------------------------------------------
        // Intercept patch — Phase 3-A
        // Preferred intercept: EntitiesCommandsProcessor.Invoke(CreateStaticEntityCmd)
        // Called on the simulation thread BEFORE the entity is instantiated — cleanest hook.
        // ---------------------------------------------------------------------------------

        [HarmonyPatch(typeof(EntitiesCommandsProcessor), nameof(EntitiesCommandsProcessor.Invoke),
            new[] { typeof(CreateStaticEntityCmd) })]
        private static class Patch_EntitiesCommandsProcessor_Invoke_Farm
        {
            // [SPIKE S1] Confirm this prefix fires when the player places a farm.
            static bool Prefix(CreateStaticEntityCmd cmd)
            {
                if (!IsInitialized) return true;
                if (s_protosDb == null) return true;

                // Resolve the proto. [SPIKE S2] Verify FarmProto match.
                if (!s_protosDb.TryGetProto<FarmProto>(cmd.ProtoId, out var farmProto))
                    return true; // not a farm — let through

                Tile2i position = cmd.Transform.Position.Xy;

                // [SPIKE S3] Verify GetFarmingTowerForTile finds the correct tower.
                IAreaManagingTower? tower = GetFarmingTowerForTile(position);
                if (tower == null)
                    return true; // not inside a farming-enabled tower area — let through

                // Check if the site is already fully prepared — if so, let through.
                // [TODO Phase 3-B: implement AreCellsAlreadyFarmable]

                s_log.Info($"[ATD FarmPlacementAssist SPIKE] Intercepted farm placement: " +
                    $"proto={farmProto.Id}, pos={position}, rot={cmd.Transform.Rotation}. " +
                    $"Tower found: {tower.Id}. Deferring placement.");

                // Record the intent. [TODO Phase 3-E: replace with PlacementIntent class]
                s_spikeIntents.Add((farmProto, position, cmd.Transform.Position.Z, cmd.Transform.Rotation, tower));

                // Inject farming designations for the covered cells.
                // [TODO Phase 3-C/D: ComputeCoveredDesignationCells + EnsureFarmingDesignationForCell]

                // Mark the command as failed so the engine doesn't proceed.
                // [SPIKE: observe whether this shows a player-visible error notification.
                //  If it does, intercept the notification path to suppress ATD_FARM_INTERCEPT_REASON.]
                cmd.SetResultError(EntityId.Invalid, ATD_FARM_INTERCEPT_REASON);
                return false; // skip original Invoke
            }
        }

        // ---------------------------------------------------------------------------------
        // Validator suppression patches — Phase 2
        // Allow farm placement (ghost bypass) on uneven / infertile ground in tower areas.
        // Note: These patches fire at HOVER time too (validator CanAdd is used for preview).
        // ---------------------------------------------------------------------------------

        // Phase 2-A: FarmFertileGroundValidator.CanAdd
        // Takes concrete LayoutEntityAddRequest (not the interface).
        [HarmonyPatch(typeof(FarmFertileGroundValidator), nameof(FarmFertileGroundValidator.CanAdd))]
        private static class Patch_FarmFertileGroundValidator_CanAdd
        {
            // [SPIKE S5] Confirm green tiles appear over uneven ground in a tower area.
            static bool Prefix(LayoutEntityAddRequest addRequest, ref EntityValidationResult __result)
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
            static MethodBase TargetMethod()
            {
                var iface = typeof(IEntityAdditionValidator<ILayoutEntityAddRequest>);
                var map = typeof(LayoutEntityTerrainValidator).GetInterfaceMap(iface);
                // The interface has a single method: CanAdd(ILayoutEntityAddRequest)
                return map.TargetMethods[0];
            }

            static bool Prefix(ILayoutEntityAddRequest addRequest, ref EntityValidationResult __result)
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
            return null;
        }

        /// <summary>Returns the farming tower for a concrete add request, or null if outside all tower areas.
        /// Spike simplification: checks only the origin tile; production will check all occupied tiles.</summary>
        internal static IAreaManagingTower? GetFarmingTowerForRequest(LayoutEntityAddRequest addRequest)
        {
            if (addRequest.Proto is not FarmProto) return null;
            // [SPIKE S3] Origin check only. Production: check all addRequest.OccupiedTiles.
            return GetFarmingTowerForTile(addRequest.Transform.Position.Xy);
        }

        internal static IAreaManagingTower? GetFarmingTowerForRequest(ILayoutEntityAddRequest addRequest)
        {
            if (addRequest.Proto is not FarmProto) return null;
            // [SPIKE S3] Origin check only. Production: check all addRequest.OccupiedTiles.
            return GetFarmingTowerForTile(addRequest.Transform.Position.Xy);
        }

        // ---------------------------------------------------------------------------------
        // Replay path — Phase 4-C stub
        // [TODO Phase 4: verify s_inputScheduler is populated; test (S4) in-game]
        // ---------------------------------------------------------------------------------

        internal static void ReplayFarmPlacement(FarmProto proto, Tile2i position, int z, Rotation90 rotation)
        {
            if (s_inputScheduler == null)
            {
                s_log.Warning("[ATD FarmPlacementAssist] Cannot replay farm placement: IInputScheduler not available.");
                return;
            }

            // [SPIKE S4] Use Z from original intercept for spike. In production, re-read surface
            // height after preparation via TerrainManager.GetHeight(position).IntegerPart.
            var transform = new TileTransform(
                new Tile3i(position.X, position.Y, z),
                rotation,
                isReflected: false);

            var cmd = new CreateStaticEntityCmd(proto.Id, transform, isFree: false,
                allowValidationSuppression: false);
            s_inputScheduler.ScheduleInputCmd(cmd);
            s_log.Info($"[ATD FarmPlacementAssist SPIKE] Replayed farm placement: proto={proto.Id}, pos={position}.");
        }

        // ---------------------------------------------------------------------------------
        // Tick — Phase 4-A stub
        // Call from ATD.Ticker.cs tick loop once the real intent tracking is in place.
        // ---------------------------------------------------------------------------------

        internal static void TickFarmPlacementAssist()
        {
            if (s_spikeIntents.Count == 0) return;
            for (int i = s_spikeIntents.Count - 1; i >= 0; i--)
            {
                var (proto, pos, z, rot, tower) = s_spikeIntents[i];
                // [TODO Phase 4-B: implement AreCellsDone check]
                // For the spike, just log status each tick to observe state changes.
                s_log.Info($"[ATD FarmPlacementAssist SPIKE] Pending intent: proto={proto.Id}, pos={pos}. " +
                    $"(TODO: check covered cells done)");
                // Spike: remove after one tick to avoid log spam during testing.
                // Replace with real AreCellsDone gate before production.
                s_spikeIntents.RemoveAt(i);
            }
        }

#endif // FARM_PLACEMENT_ASSIST_SPIKE
    }
}
