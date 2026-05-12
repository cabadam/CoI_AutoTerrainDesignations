// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
// Auto Terrain Designations - Farming Preparation Sessions
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Mafi;
using Mafi.Core;
using Mafi.Core.Buildings.Mine;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Products;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Vehicles.Trucks;
using UnityEngine;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        private enum FarmingOriginPhase
        {
            AnalysisLeveling,
            Preparing,
            ReadyForFilling,
            Filling,
            Done,
            Blocked
        }

        private sealed class FarmingOriginSession
        {
            public Tile2i Origin { get; }
            public DesignationData OriginalData { get; }
            public int TargetHeight { get; }
            public FarmingOriginPhase Phase { get; set; }
            public string Detail { get; set; }
            public bool IsHiddenUntilFilling { get; set; }
            public bool IsFillingActivated { get; set; }

            public FarmingOriginSession(Tile2i origin, DesignationData originalData, int targetHeight)
            {
                Origin = origin;
                OriginalData = originalData;
                TargetHeight = targetHeight;
                Phase = FarmingOriginPhase.AnalysisLeveling;
                Detail = string.Empty;
            }
        }

        private sealed class FarmingPreparationSession
        {
            public Dictionary<Tile2i, FarmingOriginSession> Origins { get; } =
                new Dictionary<Tile2i, FarmingOriginSession>();
            public bool Enabled { get; set; }
            public bool Active { get; set; }
            public string LastReport { get; set; } = "[ATD Farming] No preparation pass has run yet.";
            public string LastDroppedOriginDetail { get; set; } = string.Empty;
            public List<LooseProductProto>? TowerDumpRulesSnapshot { get; set; }
            public bool TowerDumpRulesOwned { get; set; }
            public bool AutoStartFilling { get; set; }
            public IAreaManagingTower? Tower { get; set; }
            public string LastAccessRampDetail { get; set; } = string.Empty;
            public string LastAccessRampRequestKey { get; set; } = string.Empty;
            public string LastAccessCheckWorkKey { get; set; } = string.Empty;
            public string LastAccessCheckDetail { get; set; } = string.Empty;
            public bool LastAccessCheckReady { get; set; } = true;
            public int LastAccessCheckTick { get; set; } = int.MinValue;
            public float? FillingAllDoneSinceRealtime { get; set; }
            public bool FillingVehicleClearOutPending { get; set; }
            public bool FillingVehicleClearOutHasMovementCheck { get; set; }
            public int FillingVehicleClearOutStartTick { get; set; } = int.MinValue;
            public List<Vehicle> FillingVehicleClearOutVehicles { get; } = new List<Vehicle>();
            public Dictionary<Vehicle, Tile2i> FillingVehicleClearOutLastPositions { get; } =
                new Dictionary<Vehicle, Tile2i>();
            public HashSet<Vehicle> FillingVehicleClearOutStuckVehicles { get; } = new HashSet<Vehicle>();
            public HashSet<Tile2i> FillingVehicleClearOutArea { get; } = new HashSet<Tile2i>();
            // Runtime-only by design: if the player saves/exits during filling, released trucks may not be reassigned on load.
            // This is acceptable for now, but save persistence must include this list if we persist farming sessions later.
            public List<Truck> ReleasedFillingTrucks { get; } = new List<Truck>();
            public bool FillingTruckAssignmentsReleased { get; set; }
            public string LastFillingActivationDetail { get; set; } = string.Empty;
            public string LastVehicleClearOutDetail { get; set; } = string.Empty;
            public string LastTruckAssignmentDetail { get; set; } = string.Empty;
            public HashSet<Tile2i> PreparationShoulderOrigins { get; } = new HashSet<Tile2i>();
            public HashSet<Tile2i> PreparationAccessRampOrigins { get; } = new HashSet<Tile2i>();
            public HashSet<Tile2i> FillingAccessRampOrigins { get; } = new HashSet<Tile2i>();
        }

        private static readonly Dictionary<EntityId, FarmingPreparationSession> s_farmingPreparationSessions =
            new Dictionary<EntityId, FarmingPreparationSession>();
        private static readonly HashSet<EntityId> s_farmingAutomationDisabledTowerIds =
            new HashSet<EntityId>();
        private static int s_farmingAutomationTickIndex;
        private static bool s_farmingTowerBootstrapCompleted;
        private static bool s_farmingReEnableOnLoadPending;
        private static bool s_farmingSaveRestorePending;
        private const float FARMING_FILLING_STABILIZATION_SECONDS = 3f;

        private static string FarmingTr(string key, string englishDefault)
        {
            return AtdLocalization.Tr("farming." + key, englishDefault);
        }

        private static string FarmingTrFormat(string key, string englishDefault, params object[] args)
        {
            return AtdLocalization.TrFormat("farming." + key, englishDefault, args);
        }

        private static void ClearFarmingRuntimeState()
        {
            s_farmingDebugStoredDesignations.Clear();
            s_farmingPreparationSessions.Clear();
            s_farmingAutomationDisabledTowerIds.Clear();
            s_farmingAutomationTickIndex = 0;
            s_farmingTowerBootstrapCompleted = false;
            s_farmingReEnableOnLoadPending = false;
            s_farmingSaveRestorePending = false;
        }

        internal static void RequestFarmingReEnableOnLoad(bool gameWasLoaded)
        {
            s_farmingReEnableOnLoadPending = gameWasLoaded && AutoTerrainDesignationsMod.ReEnableFarmingOnLoad;
        }

        internal static string StartFarmingPreparationForTower(IAreaManagingTower? tower)
        {
            return SetFarmingAutomationEnabledForTower(tower, enabled: true);
        }

        internal static string StartFarmingFullCycleForTower(IAreaManagingTower? tower)
        {
            return SetFarmingAutomationEnabledForTower(tower, enabled: true);
        }

        internal static string SetFarmingAutomationEnabledForTower(IAreaManagingTower? tower, bool enabled)
        {
            if (tower != null
                && TryGetTowerEntityId(tower, out EntityId towerId)
                && towerId.IsValid)
            {
                if (enabled)
                    s_farmingAutomationDisabledTowerIds.Remove(towerId);
                else
                    s_farmingAutomationDisabledTowerIds.Add(towerId);
            }

            if (!enabled)
                return RestoreFarmingPreparationForTower(tower);

            return StartFarmingPreparationForTower(tower, autoStartFilling: true);
        }

        internal static void EnsureFarmingAutomationDefaultEnabledForTower(IAreaManagingTower? tower)
        {
            if (tower == null)
                return;

            if (!TryGetTowerEntityId(tower, out EntityId towerId) || !towerId.IsValid)
                return;

            if (s_farmingAutomationDisabledTowerIds.Contains(towerId))
                return;

            if (s_farmingPreparationSessions.TryGetValue(towerId, out FarmingPreparationSession session)
                && session.Enabled)
            {
                session.Tower = tower;
            }
        }

        internal static bool IsFarmingAutomationEnabledForTower(IAreaManagingTower? tower)
        {
            if (tower == null)
                return false;

            if (!TryGetTowerEntityId(tower, out EntityId towerId) || !towerId.IsValid)
                return false;

            if (s_farmingAutomationDisabledTowerIds.Contains(towerId))
                return false;

            return s_farmingPreparationSessions.TryGetValue(towerId, out FarmingPreparationSession session)
                ? session.Enabled
                : false;
        }

        private static string StartFarmingPreparationForTower(IAreaManagingTower? tower, bool autoStartFilling)
        {
            if (tower == null)
                return FarmingTr("no_tower", "[ATD Farming] No tower selected.");

            if (s_desigManager == null)
                return FarmingTr("designation_manager_unavailable", "[ATD Farming] Terrain designation manager is unavailable.");

            if (s_levelingProto == null)
                return FarmingTr("leveling_proto_unavailable", "[ATD Farming] Leveling prototype is unavailable.");

            if (!TryGetTowerEntityId(tower, out EntityId towerId) || !towerId.IsValid)
                return FarmingTr("no_stable_entity_id", "[ATD Farming] Selected tower has no stable entity id.");

            FarmingPreparationSession session = GetOrCreateFarmingPreparationSession(towerId);
            session.Tower = tower;
            session.Enabled = true;
            session.AutoStartFilling = autoStartFilling;
            if (session.Active)
            {
                return session.LastReport;
            }

            if (autoStartFilling
                && session.Origins.Values.Any(origin => origin.Phase == FarmingOriginPhase.ReadyForFilling))
            {
                session.AutoStartFilling = true;
                if (RunFarmingPreparationPass(tower, session))
                {
                    session.Active = true;
                    return session.LastReport;
                }

                if (CanStartTowerLevelFilling(session))
                    return BeginFarmingFillingForSession(tower, towerId, session);

                return FarmingTr(
                    "filling_waiting_all_tracked_ready",
                    "[ATD Farming] Filling is waiting: all tracked tower designations must be ReadyForFilling or Done before tower dump rules can change.");
            }

            session.Active = true;
            session.LastReport = autoStartFilling
                ? FarmingTr("automation_enabled", "[ATD Farming] Farming automation enabled.")
                : FarmingTr("stage3_started", "[ATD Farming] Stage 3 preparation session started.");
            return session.LastReport;
        }

        internal static string RestoreFarmingPreparationForTower(IAreaManagingTower? tower)
        {
            if (tower == null)
                return FarmingTr("no_tower", "[ATD Farming] No tower selected.");

            if (s_desigManager == null)
                return FarmingTr("designation_manager_unavailable", "[ATD Farming] Terrain designation manager is unavailable.");

            if (s_levelingProto == null)
                return FarmingTr("leveling_proto_unavailable", "[ATD Farming] Leveling prototype is unavailable.");

            if (!TryGetTowerEntityId(tower, out EntityId towerId) || !towerId.IsValid)
                return FarmingTr("no_stable_entity_id", "[ATD Farming] Selected tower has no stable entity id.");

            if (!s_farmingPreparationSessions.TryGetValue(towerId, out FarmingPreparationSession session))
                return FarmingTr("already_disabled", "[ATD Farming] Farming automation is already disabled for this tower.");

            session.Enabled = false;
            session.Active = false;
            session.Tower = tower;
            session.LastAccessRampRequestKey = string.Empty;
            session.FillingAllDoneSinceRealtime = null;
            ClearFarmingFillingVehicleClearOut(session);
            ClearFarmingFillingActivation(session);
            RemoveOwnedFarmingPreparationShoulders(session);
            RemoveOwnedFarmingAccessRamps(session, isFilling: false);
            RemoveOwnedFarmingAccessRamps(session, isFilling: true);
            RestoreTowerDumpRulesIfOwned(tower, session);
            RestoreTowerTrucksReleasedForFilling(tower, session);
            int restored = 0;
            int failed = 0;
            foreach (FarmingOriginSession originState in session.Origins.Values.ToList())
            {
                if (s_desigManager.AddOrReplaceDesignation(s_levelingProto, originState.OriginalData))
                {
                    restored++;
                    originState.IsHiddenUntilFilling = false;
                    originState.IsFillingActivated = false;
                    s_farmingDebugStoredDesignations.Remove(originState.Origin);
                }
                else
                {
                    failed++;
                }
            }

            if (failed == 0)
                s_farmingPreparationSessions.Remove(towerId);
            else
                session.LastReport = FarmingTrFormat(
                    "restore_attempted",
                    "[ATD Farming] Restore attempted: restored={0}, failed={1}.",
                    restored,
                    failed);

            return failed == 0
                ? FarmingTrFormat(
                    "restored_origins",
                    "[ATD Farming] Restored {0} Stage 3 origin(s) for this tower.",
                    restored)
                : session.LastReport;
        }

        internal static string StartFarmingFillingForTower(IAreaManagingTower? tower)
        {
            if (tower == null)
                return FarmingTr("no_tower", "[ATD Farming] No tower selected.");

            if (s_desigManager == null)
                return FarmingTr("designation_manager_unavailable", "[ATD Farming] Terrain designation manager is unavailable.");

            if (s_levelingProto == null)
                return FarmingTr("leveling_proto_unavailable", "[ATD Farming] Leveling prototype is unavailable.");

            if (!TryGetTowerEntityId(tower, out EntityId towerId) || !towerId.IsValid)
                return FarmingTr("no_stable_entity_id", "[ATD Farming] Selected tower has no stable entity id.");

            if (!s_farmingPreparationSessions.TryGetValue(towerId, out FarmingPreparationSession session))
                return FarmingTr(
                    "no_stage3_session",
                    "[ATD Farming] No Stage 3 preparation session exists for this tower. Run preparation first.");

            session.Tower = tower;
            session.Enabled = true;
            session.AutoStartFilling = true;
            if (session.Active)
                return FarmingTr(
                    "session_already_active",
                    "[ATD Farming] A preparation/filling session is already active for this tower.");

            if (RunFarmingPreparationPass(tower, session))
            {
                session.Active = true;
                return FarmingTr(
                    "filling_waiting_preparation",
                    "[ATD Farming] Filling is waiting: one or more tower designations still need leveling or preparation.");
            }

            return BeginFarmingFillingForSession(tower, towerId, session);
        }

        private static string BeginFarmingFillingForSession(
            IAreaManagingTower tower,
            EntityId towerId,
            FarmingPreparationSession session)
        {
            if (s_desigManager == null)
                return FarmingTr("stage4_designation_manager_unavailable", "[ATD Farming] Stage 4 blocked: designation manager unavailable.");

            if (s_levelingProto == null)
                return FarmingTr("stage4_leveling_proto_unavailable", "[ATD Farming] Stage 4 blocked: leveling prototype unavailable.");

            if (!CanStartTowerLevelFilling(session))
            {
                session.LastReport = FormatFarmingPreparationSummary(session);
                return FarmingTr(
                    "filling_waiting_all_tracked_ready",
                    "[ATD Farming] Filling is waiting: all tracked tower designations must be ReadyForFilling or Done before tower dump rules can change.");
            }

            if (!HasQueuedFarmingFillingOrigins(session))
            {
                session.LastReport = FormatFarmingPreparationSummary(session);
                return FarmingTr(
                    "no_origins_ready",
                    "[ATD Farming] No origins are ready for filling/restoration. Run preparation first or wait for leveling/preparation to finish.");
            }

            ReleaseEmptyTowerTrucksForFilling(tower, session);
            if (TryStartFarmingFillingVehicleClearOut(tower, session, out int vehiclesInside))
            {
                session.Active = true;
                session.LastReport = FarmingTrFormat(
                    "stage4_waiting_vehicles",
                    "[ATD Farming] Stage 4 filling started: waiting briefly for {0} vehicle(s) to leave the fill area before committing fill designations.",
                    vehiclesInside);
                return session.LastReport;
            }

            if (!TrySwitchTowerToFarmableDumpRulesForFilling(tower, session, out int farmableProductCount))
                return session.LastReport;

            ClearFarmingFillingActivation(session);
            int restored = ActivateFarmingFillingOrigins(session, out int failed);

            if (restored == 0)
            {
                RestoreTowerDumpRulesIfOwned(tower, session);
                RestoreTowerTrucksReleasedForFilling(tower, session);
                session.LastReport = failed > 0
                    ? FarmingTrFormat(
                        "stage4_failed_restore",
                        "[ATD Farming] Stage 4 blocked: failed to restore {0} ready origin(s).",
                        failed)
                    : FarmingTr(
                        "stage4_no_fill_origins",
                        "[ATD Farming] Stage 4 blocked: no fill origins could be activated.");
                return session.LastReport;
            }

            session.Active = true;
            session.LastReport = FarmingTrFormat(
                "stage4_started",
                "[ATD Farming] Stage 4 filling started: activated={0}, failed={1}, farmableProducts={2}.",
                restored,
                failed,
                farmableProductCount);
            return session.LastReport;
        }

        internal static string FormatFarmingPreparationStatusForTower(IAreaManagingTower? tower)
        {
            return FormatFarmingPreparationStatusForTower(tower, 20);
        }

        private static string FormatFarmingPreparationStatusForTower(IAreaManagingTower? tower, int maxRows)
        {
            if (tower == null)
                return FarmingTr("no_tower", "[ATD Farming] No tower selected.");

            if (!TryGetTowerEntityId(tower, out EntityId towerId) || !towerId.IsValid)
                return FarmingTr("no_stable_entity_id", "[ATD Farming] Selected tower has no stable entity id.");

            if (!s_farmingPreparationSessions.TryGetValue(towerId, out FarmingPreparationSession session))
                return FarmingTr("automation_off_period", "[ATD Farming] Farming automation: off.");

            return FormatFarmingPreparationReport(session, maxRows);
        }

        private static FarmingPreparationSession GetOrCreateFarmingPreparationSession(EntityId towerId)
        {
            if (!s_farmingPreparationSessions.TryGetValue(towerId, out FarmingPreparationSession session))
            {
                session = new FarmingPreparationSession();
                s_farmingPreparationSessions[towerId] = session;
            }

            return session;
        }

        internal static void TickFarmingPreparationSessions()
        {
            if (s_desigManager == null)
                return;

            s_farmingAutomationTickIndex++;
            BootstrapFarmingAutomationForExistingTowers();
            ReEnableFarmingAutomationForLoadedFarmingTowers();

            foreach (var entry in s_farmingPreparationSessions.ToList())
            {
                EntityId towerId = entry.Key;
                FarmingPreparationSession session = entry.Value;
                IAreaManagingTower? tower = session.Tower;
                if (!session.Enabled || tower == null)
                    continue;

                bool fillingPass = session.TowerDumpRulesOwned
                    || session.Origins.Values.Any(origin => origin.Phase == FarmingOriginPhase.Filling);
                if (fillingPass)
                {
                    bool keepFilling = RunFarmingFillingPass(tower, session);
                    if (keepFilling)
                    {
                        session.Active = true;
                        continue;
                    }

                    session.Active = false;
                    RemoveOwnedFarmingAccessRamps(session, isFilling: true);
                    RestoreTowerDumpRulesIfOwned(tower, session);
                    RestoreTowerTrucksReleasedForFilling(tower, session);
                    if (IsFarmingPreparationComplete(session))
                        AddFarmingCompleteNotification(tower);
                    continue;
                }

                bool keepPreparing = RunFarmingPreparationPass(tower, session);
                if (keepPreparing)
                {
                    session.Active = true;
                    continue;
                }

                session.Active = false;
                if (CanStartTowerLevelFilling(session))
                {
                    BeginFarmingFillingForSession(tower, towerId, session);
                }

                if (s_farmingPreparationSessions.TryGetValue(towerId, out FarmingPreparationSession activeSession)
                    && activeSession == session
                    && !session.Enabled
                    && session.Origins.Count == 0)
                {
                    s_farmingPreparationSessions.Remove(towerId);
                }
            }
        }

        private static void BootstrapFarmingAutomationForExistingTowers()
        {
            if (s_farmingTowerBootstrapCompleted)
                return;

            try
            {
                foreach (FarmingPreparationSession session in s_farmingPreparationSessions.Values)
                    session.Active = session.Enabled;
                s_farmingTowerBootstrapCompleted = true;
            }
            catch (System.Exception ex)
            {
                Log.Warning("[ATD Farming] Failed to bootstrap farming automation for existing towers: " + ex.Message);
            }
        }

        private static void ReEnableFarmingAutomationForLoadedFarmingTowers()
        {
            if (!s_farmingReEnableOnLoadPending)
                return;

            s_farmingReEnableOnLoadPending = false;
            if (s_entitiesManager == null || s_desigManager == null || s_levelingProto == null)
                return;

            int enabled = 0;
            try
            {
                foreach (MineTower tower in s_entitiesManager.GetAllEntitiesOfType<MineTower>())
                {
                    if (!TryGetTowerEntityId(tower, out EntityId towerId) || !towerId.IsValid)
                        continue;

                    if (s_farmingAutomationDisabledTowerIds.Contains(towerId))
                        continue;

                    if (s_farmingPreparationSessions.TryGetValue(towerId, out FarmingPreparationSession existingSession)
                        && existingSession.Enabled)
                    {
                        existingSession.Tower = tower;
                        continue;
                    }

                    if (!LooksLikeLoadedFarmingTower(tower))
                        continue;

                    FarmingPreparationSession session = GetOrCreateFarmingPreparationSession(towerId);
                    session.Tower = tower;
                    session.Enabled = true;
                    session.Active = true;
                    session.AutoStartFilling = true;
                    session.LastReport = "[ATD Farming] Farming automation re-enabled on load for apparent farmland designations.";
                    enabled++;
                }

                if (enabled > 0)
                    Log.Info($"[ATD Farming] Re-enabled farming automation on load for {enabled} tower(s).");
            }
            catch (System.Exception ex)
            {
                Log.Warning("[ATD Farming] Failed to re-enable farming automation on load: " + ex.Message);
            }
        }

        private static bool LooksLikeLoadedFarmingTower(IAreaManagingTower tower)
        {
            bool hasDesignation = false;
            foreach (TerrainDesignation designation in tower.ManagedDesignations)
            {
                hasDesignation = true;
                if (!IsLevelingDesignation(designation))
                    return false;

                if (!TryGetFlatTargetHeight(designation.Data, out _))
                    return false;
            }

            return hasDesignation;
        }

        private static bool CanStartTowerLevelFilling(FarmingPreparationSession session)
        {
            bool hasReadyOrigin = false;
            foreach (FarmingOriginSession origin in session.Origins.Values)
            {
                if (origin.Phase == FarmingOriginPhase.ReadyForFilling)
                {
                    hasReadyOrigin = true;
                    continue;
                }

                if (origin.Phase != FarmingOriginPhase.Done)
                    return false;
            }

            return hasReadyOrigin;
        }

        private static bool IsFarmingPreparationComplete(FarmingPreparationSession session)
        {
            return session.Origins.Count > 0
                && session.Origins.Values.All(origin => origin.Phase == FarmingOriginPhase.Done);
        }

        private static bool RunFarmingPreparationPass(IAreaManagingTower tower, FarmingPreparationSession session)
        {
            if (s_desigManager == null || s_levelingProto == null)
            {
                session.LastReport = "[ATD Farming] Stage 3 stopped: designation manager or leveling proto unavailable.";
                return false;
            }

            TerrainManager terrMgr = s_desigManager.TerrainManager;
            CaptureCurrentFlatFarmingDesignations(tower, session);
            AdvanceCapturedFarmingOrigins(session, terrMgr);
            bool accessReady = EnsureFarmingAccessForCurrentPhase(tower, session, isFilling: false);
            session.LastReport = FormatFarmingPreparationSummary(session);

            return !accessReady || session.Origins.Values.Any(origin =>
                origin.Phase == FarmingOriginPhase.AnalysisLeveling ||
                origin.Phase == FarmingOriginPhase.Preparing);
        }

        internal static void RestoreFarmingRuntimeForSave()
        {
            if (s_desigManager == null || s_levelingProto == null)
                return;

            int restored = 0;
            int failed = 0;
            foreach (var storedEntry in s_farmingDebugStoredDesignations.ToList())
            {
                if (s_desigManager.AddOrReplaceDesignation(s_levelingProto, storedEntry.Value.OriginalData))
                {
                    restored++;
                    s_farmingDebugStoredDesignations.Remove(storedEntry.Key);
                }
                else
                {
                    failed++;
                }
            }

            foreach (FarmingPreparationSession session in s_farmingPreparationSessions.Values.ToList())
            {
                IAreaManagingTower? tower = session.Tower;
                if (tower != null)
                {
                    RemoveOwnedFarmingAccessRamps(session, isFilling: false);
                    RemoveOwnedFarmingAccessRamps(session, isFilling: true);
                    RemoveOwnedFarmingPreparationShoulders(session);
                    RestoreTowerDumpRulesIfOwned(tower, session);
                    RestoreTowerTrucksReleasedForFilling(tower, session);
                }

                session.LastAccessRampRequestKey = string.Empty;
                session.LastAccessCheckWorkKey = string.Empty;
                session.LastAccessCheckDetail = string.Empty;
                session.LastAccessCheckReady = true;
                session.LastAccessCheckTick = int.MinValue;
                session.FillingAllDoneSinceRealtime = null;
                ClearFarmingFillingVehicleClearOut(session);
                ClearFarmingFillingActivation(session);

                foreach (FarmingOriginSession originState in session.Origins.Values)
                {
                    if (s_desigManager.AddOrReplaceDesignation(s_levelingProto, originState.OriginalData))
                    {
                        restored++;
                        originState.Phase = FarmingOriginPhase.AnalysisLeveling;
                        originState.IsHiddenUntilFilling = false;
                        originState.IsFillingActivated = false;
                        originState.Detail = "restored original designation for save; pending re-analysis";
                        s_farmingDebugStoredDesignations.Remove(originState.Origin);
                    }
                    else
                    {
                        failed++;
                        originState.Phase = FarmingOriginPhase.Blocked;
                        originState.Detail = "failed to restore original designation before save";
                    }
                }

                session.Active = session.Enabled;
                session.LastReport = failed == 0
                    ? $"[ATD Farming] Save hook restored {restored} original designation(s)."
                    : $"[ATD Farming] Save hook restored {restored} original designation(s), failed={failed}.";
            }

            s_farmingTowerBootstrapCompleted = false;
            s_farmingSaveRestorePending = true;
        }

        internal static void ResumeFarmingRuntimeAfterSave()
        {
            if (!s_farmingSaveRestorePending)
                return;

            s_farmingSaveRestorePending = false;
            s_farmingTowerBootstrapCompleted = false;
            TickFarmingPreparationSessions();
        }

        private static bool RunFarmingFillingPass(IAreaManagingTower tower, FarmingPreparationSession session)
        {
            if (s_desigManager == null)
            {
                session.LastReport = "[ATD Farming] Stage 4 stopped: designation manager unavailable.";
                return false;
            }

            TerrainManager terrMgr = s_desigManager.TerrainManager;
            if (session.FillingVehicleClearOutPending)
            {
                int remainingVehicles = PruneAndCountVehiclesInPendingFillingArea(session, out int stuckVehicles);
                if (stuckVehicles > 0)
                {
                    session.LastVehicleClearOutDetail =
                        $"Filling vehicle clear-out detected {stuckVehicles} stuck/paused vehicle(s); committing fill designations.";
                    ClearFarmingFillingVehicleClearOut(session, keepStuckCommitAllowance: true);
                }
                else if (remainingVehicles > 0)
                {
                    session.LastReport = FormatFarmingPreparationSummary(session)
                        + $"\n  Filling vehicle clear-out: waiting for {remainingVehicles} cached vehicle(s) to leave the fill area.";
                    return true;
                }

                if (session.FillingVehicleClearOutStuckVehicles.Count == 0)
                {
                    session.LastVehicleClearOutDetail = "Filling vehicle clear-out complete; rechecking before committing fill designations.";
                    ClearFarmingFillingVehicleClearOut(session);
                }
            }

            ReleaseEmptyTowerTrucksForFilling(tower, session);
            EnsureFarmingAccessForCurrentPhase(tower, session, isFilling: true);
            var droppedOrigins = new List<Tile2i>();
            session.LastDroppedOriginDetail = string.Empty;

            foreach (FarmingOriginSession originState in session.Origins.Values)
            {
                if (originState.Phase != FarmingOriginPhase.Filling
                    && originState.Phase != FarmingOriginPhase.Done)
                    continue;

                var currentDesignation = s_desigManager.GetDesignationAt(originState.Origin);
                if (!currentDesignation.HasValue)
                {
                    droppedOrigins.Add(originState.Origin);
                    session.LastDroppedOriginDetail = $"Dropped ({originState.Origin.X},{originState.Origin.Y}): filling designation was removed.";
                    continue;
                }

                if (!IsLevelingDesignation(currentDesignation.Value))
                {
                    droppedOrigins.Add(originState.Origin);
                    session.LastDroppedOriginDetail = $"Dropped ({originState.Origin.X},{originState.Origin.Y}): filling designation was replaced with a non-leveling designation.";
                    continue;
                }

                FarmingAnalysisRow row = AnalyzeFarmingFillingDesignation(
                    currentDesignation.Value,
                    originState.TargetHeight,
                    terrMgr);
                if (row.State == FarmingAnalysisState.Done)
                {
                    originState.Phase = FarmingOriginPhase.Done;
                    originState.Detail = row.Detail;
                }
                else if (row.State == FarmingAnalysisState.NeedsPreparation)
                {
                    originState.Phase = FarmingOriginPhase.Blocked;
                    originState.Detail = "non-farmable material reappeared in target band during filling";
                }
                else
                {
                    originState.Phase = FarmingOriginPhase.Filling;
                    originState.Detail = row.Detail;
                }
            }

            foreach (Tile2i origin in droppedOrigins)
            {
                session.Origins.Remove(origin);
                s_farmingDebugStoredDesignations.Remove(origin);
            }

            bool hasFilling = session.Origins.Values.Any(origin => origin.Phase == FarmingOriginPhase.Filling);
            if (HasQueuedFarmingFillingOrigins(session) && !hasFilling)
            {
                if (TryStartFarmingFillingVehicleClearOut(tower, session, out int vehiclesInside))
                {
                    session.LastReport = FormatFarmingPreparationSummary(session)
                        + $"\n  Filling vehicle clear-out safeguard: reissued evacuation for {vehiclesInside} vehicle(s) before committing fill designations.";
                    session.FillingAllDoneSinceRealtime = null;
                    return true;
                }

                if (!TrySwitchTowerToFarmableDumpRulesForFilling(tower, session, out _))
                    return true;

                int activated = ActivateFarmingFillingOrigins(session, out int failed);
                session.LastReport = FormatFarmingPreparationSummary(session);
                session.FillingAllDoneSinceRealtime = null;
                if (activated > 0)
                    return true;

                if (failed > 0)
                    return session.Origins.Values.Any(origin => origin.Phase == FarmingOriginPhase.Filling);
            }

            session.LastReport = FormatFarmingPreparationSummary(session);
            hasFilling = session.Origins.Values.Any(origin => origin.Phase == FarmingOriginPhase.Filling);
            bool allDone = session.Origins.Count > 0
                && session.Origins.Values.All(origin => origin.Phase == FarmingOriginPhase.Done);

            if (hasFilling || !allDone)
            {
                session.FillingAllDoneSinceRealtime = null;
                return hasFilling;
            }

            float now = Time.realtimeSinceStartup;
            if (!session.FillingAllDoneSinceRealtime.HasValue)
                session.FillingAllDoneSinceRealtime = now;

            float stableFor = now - session.FillingAllDoneSinceRealtime.Value;
            if (stableFor < FARMING_FILLING_STABILIZATION_SECONDS)
            {
                session.LastReport = FormatFarmingPreparationSummary(session)
                    + $"\n  Filling stabilization: {stableFor:F1}/{FARMING_FILLING_STABILIZATION_SECONDS:F0}s; keeping farmable dump rules active.";
                return true;
            }

            session.FillingAllDoneSinceRealtime = null;
            return false;
        }

        private static void CaptureCurrentFlatFarmingDesignations(
            IAreaManagingTower tower,
            FarmingPreparationSession session)
        {
            foreach (TerrainDesignation designation in tower.ManagedDesignations)
            {
                if (!IsLevelingDesignation(designation))
                    continue;

                Tile2i origin = designation.OriginTileCoord;
                if (session.Origins.ContainsKey(origin))
                    continue;

                if (s_farmingDebugStoredDesignations.TryGetValue(origin, out FarmingDebugStoredDesignation stored))
                {
                    session.Origins[origin] = new FarmingOriginSession(origin, stored.OriginalData, stored.TargetHeight)
                    {
                        Phase = FarmingOriginPhase.Preparing,
                        Detail = "captured existing Stage 2 temporary preparation designation"
                    };
                    continue;
                }

                if (!TryGetFlatTargetHeight(designation.Data, out int targetHeight))
                    continue;

                session.Origins[origin] = new FarmingOriginSession(origin, designation.Data, targetHeight);
            }
        }

        private static void AdvanceCapturedFarmingOrigins(FarmingPreparationSession session, TerrainManager terrMgr)
        {
            var droppedOrigins = new List<Tile2i>();
            session.LastDroppedOriginDetail = string.Empty;

            foreach (FarmingOriginSession originState in session.Origins.Values)
            {
                var currentDesignation = s_desigManager!.GetDesignationAt(originState.Origin);
                if (!currentDesignation.HasValue)
                {
                    if (originState.IsHiddenUntilFilling)
                        continue;

                    droppedOrigins.Add(originState.Origin);
                    session.LastDroppedOriginDetail = $"Dropped ({originState.Origin.X},{originState.Origin.Y}): designation was removed.";
                    continue;
                }

                if (!IsLevelingDesignation(currentDesignation.Value))
                {
                    droppedOrigins.Add(originState.Origin);
                    session.LastDroppedOriginDetail = $"Dropped ({originState.Origin.X},{originState.Origin.Y}): designation was replaced with a non-leveling designation.";
                    continue;
                }

                FarmingAnalysisRow row = AnalyzeFarmingDesignation(originState.Origin, originState.TargetHeight, terrMgr);
                switch (originState.Phase)
                {
                    case FarmingOriginPhase.Preparing:
                        AdvancePreparingOrigin(originState, row);
                        break;
                    case FarmingOriginPhase.ReadyForFilling:
                    case FarmingOriginPhase.Filling:
                    case FarmingOriginPhase.Done:
                    case FarmingOriginPhase.Blocked:
                        break;
                    default:
                        AdvanceAnalysisOrigin(session, originState, row);
                        break;
                }
            }

            foreach (Tile2i origin in droppedOrigins)
            {
                session.Origins.Remove(origin);
                s_farmingDebugStoredDesignations.Remove(origin);
            }
        }

        private static void AdvanceAnalysisOrigin(
            FarmingPreparationSession session,
            FarmingOriginSession originState,
            FarmingAnalysisRow row)
        {
            switch (row.State)
            {
                case FarmingAnalysisState.Done:
                    originState.Phase = FarmingOriginPhase.Done;
                    HideFarmingDesignationUntilFilling(originState, "already done; original designation hidden until tower-level filling");
                    break;
                case FarmingAnalysisState.ReadyForFilling:
                    originState.Phase = FarmingOriginPhase.ReadyForFilling;
                    HideFarmingDesignationUntilFilling(originState, "ready for filling; original designation hidden until tower-level filling");
                    break;
                case FarmingAnalysisState.NeedsLeveling:
                    originState.Phase = FarmingOriginPhase.AnalysisLeveling;
                    originState.Detail = row.Detail;
                    break;
                case FarmingAnalysisState.NeedsPreparation:
                    if (TryPlaceFarmingPreparationDesignation(
                        originState.Origin,
                        originState.OriginalData,
                        originState.TargetHeight,
                        session,
                        out int shoulderCount))
                    {
                        originState.Phase = FarmingOriginPhase.Preparing;
                        originState.Detail = shoulderCount > 0
                            ? $"placed temporary preparation target={originState.TargetHeight - 1}, support shoulders={shoulderCount}"
                            : $"placed temporary preparation target={originState.TargetHeight - 1}";
                    }
                    else
                    {
                        originState.Phase = FarmingOriginPhase.Blocked;
                        originState.Detail = "failed to place temporary preparation designation";
                    }
                    break;
                default:
                    originState.Phase = FarmingOriginPhase.Blocked;
                    originState.Detail = row.Detail;
                    break;
            }
        }

        private static void HideFarmingDesignationUntilFilling(
            FarmingOriginSession originState,
            string detail)
        {
            if (s_desigManager == null)
            {
                originState.Detail = detail;
                return;
            }

            var currentDesignation = s_desigManager.GetDesignationAt(originState.Origin);
            if (currentDesignation.HasValue && IsLevelingDesignation(currentDesignation.Value))
                s_desigManager.RemoveDesignation(originState.Origin);

            originState.IsHiddenUntilFilling = true;
            originState.Detail = detail;
        }

        private static bool TryStartFarmingFillingVehicleClearOut(
            IAreaManagingTower tower,
            FarmingPreparationSession session,
            out int vehiclesInside)
        {
            vehiclesInside = OrderVehiclesOutOfFillingAreaForTransition(tower, session);
            if (vehiclesInside <= 0)
                return false;

            session.FillingVehicleClearOutPending = true;
            session.FillingVehicleClearOutStartTick = s_farmingAutomationTickIndex;
            return true;
        }

        private static int OrderVehiclesOutOfFillingAreaForTransition(
            IAreaManagingTower tower,
            FarmingPreparationSession session)
        {
            ClearFarmingFillingVehicleClearOut(session, keepStuckCommitAllowance: true);
            session.LastVehicleClearOutDetail = string.Empty;

            if (!(tower is MineTower mineTower))
                return 0;

            if (s_parkAndWaitJobFactory == null)
            {
                session.LastVehicleClearOutDetail = "Filling transition vehicle clear-out skipped: parking job factory unavailable.";
                return 0;
            }

            if (s_entitiesManager == null)
            {
                session.LastVehicleClearOutDetail = "Filling transition vehicle clear-out skipped: entity manager unavailable.";
                return 0;
            }

            HashSet<Tile2i> fillingArea = BuildPendingFarmingFillingArea(session);
            if (fillingArea.Count == 0)
                return 0;

            foreach (Tile2i tile in fillingArea)
                session.FillingVehicleClearOutArea.Add(tile);

            int total = 0;
            int inside = 0;
            int enqueued = 0;
            int notEnqueued = 0;
            int assumedStuck = 0;
            int failed = 0;

            foreach (Vehicle vehicle in s_entitiesManager.GetAllEntitiesOfType<Vehicle>())
            {
                total++;
                try
                {
                    if (vehicle == null || vehicle.IsDestroyed || !vehicle.IsSpawned)
                    {
                        notEnqueued++;
                        continue;
                    }

                    if (!fillingArea.Contains(vehicle.GroundPositionTile2i))
                        continue;

                    if (session.FillingVehicleClearOutStuckVehicles.Contains(vehicle))
                        continue;

                    inside++;
                    vehicle.CancelAllJobsAndResetState();
                    if (s_parkAndWaitJobFactory.TryEnqueueParkingJobIfNeeded(vehicle, mineTower))
                    {
                        enqueued++;
                        session.FillingVehicleClearOutVehicles.Add(vehicle);
                        session.FillingVehicleClearOutLastPositions[vehicle] = vehicle.GroundPositionTile2i;
                    }
                    else
                    {
                        notEnqueued++;
                        assumedStuck++;
                        session.FillingVehicleClearOutStuckVehicles.Add(vehicle);
                    }
                }
                catch
                {
                    failed++;
                    session.FillingVehicleClearOutStuckVehicles.Add(vehicle);
                    session.FillingVehicleClearOutVehicles.Remove(vehicle);
                    session.FillingVehicleClearOutLastPositions.Remove(vehicle);
                }
            }

            session.LastVehicleClearOutDetail =
                $"Filling transition ordered vehicles out of fill area: scanned={total}, inside={inside}, enqueued={enqueued}, assumedStuck={assumedStuck}, alreadyNearOrSkipped={notEnqueued}, failed={failed}.";
            LogDebug(session.LastVehicleClearOutDetail);
            return enqueued;
        }

        private static int PruneAndCountVehiclesInPendingFillingArea(
            FarmingPreparationSession session,
            out int stuckVehicles)
        {
            stuckVehicles = 0;
            if (session.FillingVehicleClearOutVehicles.Count == 0
                || session.FillingVehicleClearOutArea.Count == 0)
                return 0;

            int count = 0;
            for (int i = session.FillingVehicleClearOutVehicles.Count - 1; i >= 0; i--)
            {
                Vehicle vehicle = session.FillingVehicleClearOutVehicles[i];
                Tile2i currentPosition = vehicle?.GroundPositionTile2i ?? default;
                if (vehicle == null
                    || vehicle.IsDestroyed
                    || !vehicle.IsSpawned
                    || !session.FillingVehicleClearOutArea.Contains(currentPosition))
                {
                    session.FillingVehicleClearOutVehicles.RemoveAt(i);
                    if (vehicle != null)
                        session.FillingVehicleClearOutLastPositions.Remove(vehicle);
                    continue;
                }

                if (session.FillingVehicleClearOutHasMovementCheck
                    && session.FillingVehicleClearOutLastPositions.TryGetValue(vehicle, out Tile2i lastPosition)
                    && currentPosition == lastPosition)
                {
                    stuckVehicles++;
                    session.FillingVehicleClearOutStuckVehicles.Add(vehicle);
                }

                session.FillingVehicleClearOutLastPositions[vehicle] = currentPosition;
                count++;
            }

            session.FillingVehicleClearOutHasMovementCheck = true;
            return count;
        }

        private static void ClearFarmingFillingVehicleClearOut(
            FarmingPreparationSession session,
            bool keepStuckCommitAllowance = false)
        {
            session.FillingVehicleClearOutPending = false;
            session.FillingVehicleClearOutHasMovementCheck = false;
            if (!keepStuckCommitAllowance)
                session.FillingVehicleClearOutStuckVehicles.Clear();
            session.FillingVehicleClearOutStartTick = int.MinValue;
            session.FillingVehicleClearOutVehicles.Clear();
            session.FillingVehicleClearOutLastPositions.Clear();
            session.FillingVehicleClearOutArea.Clear();
        }

        private static void ReleaseEmptyTowerTrucksForFilling(
            IAreaManagingTower tower,
            FarmingPreparationSession session)
        {
            session.LastTruckAssignmentDetail = string.Empty;
            if (!(tower is MineTower mineTower))
                return;

            int released = 0;
            int loaded = 0;
            int failed = 0;
            int assignedTrucks = 0;
            var assignedVehicles = new List<Vehicle>();
            foreach (Vehicle vehicle in mineTower.AllVehicles)
                assignedVehicles.Add(vehicle);

            foreach (Vehicle vehicle in assignedVehicles)
            {
                if (vehicle is Truck truck && !truck.IsDestroyed)
                    assignedTrucks++;
            }

            foreach (Vehicle vehicle in assignedVehicles)
            {
                if (!(vehicle is Truck truck))
                    continue;

                if (truck.IsDestroyed)
                    continue;

                if (truck.IsNotEmpty)
                {
                    loaded++;
                    continue;
                }

                if (session.ReleasedFillingTrucks.Contains(truck))
                    continue;

                if (assignedTrucks <= 1)
                    continue;

                session.ReleasedFillingTrucks.Add(truck);
                try
                {
                    mineTower.UnassignVehicle(truck, true);
                    assignedTrucks--;
                    released++;
                }
                catch
                {
                    failed++;
                }
            }

            session.FillingTruckAssignmentsReleased = session.ReleasedFillingTrucks.Count > 0;
            if (released > 0 || loaded > 0 || failed > 0)
            {
                session.LastTruckAssignmentDetail =
                    $"Filling truck assignments: releasedNow={released}, releasedTotal={session.ReleasedFillingTrucks.Count}, waitingLoaded={loaded}, failed={failed}.";
                LogDebug(session.LastTruckAssignmentDetail);
            }
        }

        private static void RestoreTowerTrucksReleasedForFilling(
            IAreaManagingTower tower,
            FarmingPreparationSession session)
        {
            if (!session.FillingTruckAssignmentsReleased && session.ReleasedFillingTrucks.Count == 0)
                return;

            if (!(tower is MineTower mineTower))
            {
                session.ReleasedFillingTrucks.Clear();
                session.FillingTruckAssignmentsReleased = false;
                return;
            }

            int restored = 0;
            int skipped = 0;
            int failed = 0;
            foreach (Truck truck in session.ReleasedFillingTrucks.ToList())
            {
                if (truck == null || truck.IsDestroyed)
                {
                    skipped++;
                    continue;
                }

                if (mineTower.AllVehicles.Contains(truck))
                {
                    skipped++;
                    continue;
                }

                var assignedTo = truck.AssignedTo;
                if (assignedTo.HasValue)
                {
                    skipped++;
                    continue;
                }

                try
                {
                    mineTower.AssignVehicle(truck, true);
                    restored++;
                }
                catch
                {
                    failed++;
                }
            }

            session.ReleasedFillingTrucks.Clear();
            session.FillingTruckAssignmentsReleased = false;
            session.LastTruckAssignmentDetail =
                $"Filling truck assignments restored: restored={restored}, skipped={skipped}, failed={failed}.";
            LogDebug(session.LastTruckAssignmentDetail);
        }

        private static HashSet<Tile2i> BuildPendingFarmingFillingArea(FarmingPreparationSession session)
        {
            const int designationSize = 4;
            const int margin = 2;
            var area = new HashSet<Tile2i>();
            foreach (FarmingOriginSession originState in session.Origins.Values)
            {
                if (!IsQueuedForFarmingFilling(originState))
                    continue;

                for (int x = originState.Origin.X - margin; x < originState.Origin.X + designationSize + margin; x++)
                {
                    for (int y = originState.Origin.Y - margin; y < originState.Origin.Y + designationSize + margin; y++)
                        area.Add(new Tile2i(x, y));
                }
            }

            return area;
        }

        private static bool TrySwitchTowerToFarmableDumpRulesForFilling(
            IAreaManagingTower tower,
            FarmingPreparationSession session,
            out int farmableProductCount)
        {
            farmableProductCount = 0;
            List<LooseProductProto> farmableDumpProducts = GetFarmableDumpProducts();
            if (farmableDumpProducts.Count == 0)
            {
                session.LastReport = "[ATD Farming] Stage 4 blocked: no farmable dump products discovered.";
                return false;
            }

            if (!TryApplyTowerFarmableDumpRules(tower, session, farmableDumpProducts, out string dumpRulesError))
            {
                session.LastReport = "[ATD Farming] Stage 4 blocked: " + dumpRulesError;
                return false;
            }

            farmableProductCount = farmableDumpProducts.Count;
            return true;
        }

        private static void AdvancePreparingOrigin(FarmingOriginSession originState, FarmingAnalysisRow row)
        {
            if (row.State == FarmingAnalysisState.NeedsPreparation)
            {
                originState.Detail = row.Detail;
                return;
            }

            if (row.State == FarmingAnalysisState.ReadyForFilling)
            {
                originState.Phase = FarmingOriginPhase.ReadyForFilling;
                originState.Detail = "preparation complete; keeping target-1 designation active until tower-level filling";
                return;
            }

            if (row.State == FarmingAnalysisState.Done)
            {
                originState.Detail = "preparation hold remains active until all preparation is complete";
                return;
            }

            originState.Detail = row.Detail;
        }

        private static string FormatFarmingPreparationReport(FarmingPreparationSession session)
        {
            return FormatFarmingPreparationReport(session, 20);
        }

        private static string FormatFarmingPreparationSummary(FarmingPreparationSession session)
        {
            CountFarmingOriginPhases(
                session,
                out int analysis,
                out int preparing,
                out int ready,
                out int filling,
                out int done,
                out int blocked);

            var sb = new StringBuilder();
            sb.AppendLine(session.Enabled
                ? FarmingTr("automation_on", "[ATD Farming] Farming automation: on")
                : FarmingTr("automation_off", "[ATD Farming] Farming automation: off"));
            sb.AppendLine(FarmingTrFormat(
                "summary_counts",
                "  Active origins={0}, Analysis/Leveling={1}, Preparing={2}, ReadyForFilling={3}, Filling={4}, Done={5}, Blocked={6}",
                session.Origins.Count,
                analysis,
                preparing,
                ready,
                filling,
                done,
                blocked));
            if (session.TowerDumpRulesOwned)
                sb.AppendLine(FarmingTr(
                    "tower_dump_rules_restricted",
                    "  Tower dump rules are temporarily restricted to farmable products."));
            if (!string.IsNullOrEmpty(session.LastFillingActivationDetail))
                sb.AppendLine("  " + session.LastFillingActivationDetail);
            if (!string.IsNullOrEmpty(session.LastVehicleClearOutDetail))
                sb.AppendLine("  " + session.LastVehicleClearOutDetail);
            if (!string.IsNullOrEmpty(session.LastTruckAssignmentDetail))
                sb.AppendLine("  " + session.LastTruckAssignmentDetail);
            if (!string.IsNullOrEmpty(session.LastDroppedOriginDetail))
                sb.AppendLine("  " + session.LastDroppedOriginDetail);
            if (!string.IsNullOrEmpty(session.LastAccessRampDetail))
                sb.AppendLine("  " + session.LastAccessRampDetail);

            return sb.ToString().TrimEnd();
        }

        private static string FormatFarmingPreparationReport(FarmingPreparationSession session, int? maxRows)
        {
            CountFarmingOriginPhases(
                session,
                out int analysis,
                out int preparing,
                out int ready,
                out int filling,
                out int done,
                out int blocked);

            var sb = new StringBuilder();
            sb.AppendLine(session.Enabled
                ? FarmingTr("automation_on", "[ATD Farming] Farming automation: on")
                : FarmingTr("automation_off", "[ATD Farming] Farming automation: off"));
            sb.AppendLine(FarmingTrFormat(
                "summary_counts",
                "  Active origins={0}, Analysis/Leveling={1}, Preparing={2}, ReadyForFilling={3}, Filling={4}, Done={5}, Blocked={6}",
                session.Origins.Count,
                analysis,
                preparing,
                ready,
                filling,
                done,
                blocked));
            if (session.TowerDumpRulesOwned)
                sb.AppendLine(FarmingTr(
                    "tower_dump_rules_restricted",
                    "  Tower dump rules are temporarily restricted to farmable products."));
            if (!string.IsNullOrEmpty(session.LastFillingActivationDetail))
                sb.AppendLine("  " + session.LastFillingActivationDetail);
            if (!string.IsNullOrEmpty(session.LastVehicleClearOutDetail))
                sb.AppendLine("  " + session.LastVehicleClearOutDetail);
            if (!string.IsNullOrEmpty(session.LastTruckAssignmentDetail))
                sb.AppendLine("  " + session.LastTruckAssignmentDetail);
            if (!string.IsNullOrEmpty(session.LastDroppedOriginDetail))
                sb.AppendLine("  " + session.LastDroppedOriginDetail);
            if (!string.IsNullOrEmpty(session.LastAccessRampDetail))
                sb.AppendLine("  " + session.LastAccessRampDetail);

            IEnumerable<FarmingOriginSession> orderedOrigins = session.Origins.Values
                .OrderBy(origin => origin.Origin.Y)
                .ThenBy(origin => origin.Origin.X);
            if (maxRows.HasValue)
                orderedOrigins = orderedOrigins.Take(maxRows.Value);

            if (!maxRows.HasValue || maxRows.Value > 0)
            {
                foreach (FarmingOriginSession originState in orderedOrigins)
                {
                    sb.AppendLine($"  {originState.Phase}: ({originState.Origin.X},{originState.Origin.Y}) target={originState.TargetHeight} {originState.Detail}".TrimEnd());
                }

                if (maxRows.HasValue && session.Origins.Count > maxRows.Value)
                    sb.AppendLine(FarmingTrFormat(
                        "more_origins_omitted",
                        "  ... {0} more origin(s) omitted.",
                        session.Origins.Count - maxRows.Value));
            }

            return sb.ToString().TrimEnd();
        }

        private static void CountFarmingOriginPhases(
            FarmingPreparationSession session,
            out int analysis,
            out int preparing,
            out int ready,
            out int filling,
            out int done,
            out int blocked)
        {
            analysis = 0;
            preparing = 0;
            ready = 0;
            filling = 0;
            done = 0;
            blocked = 0;

            foreach (FarmingOriginSession origin in session.Origins.Values)
            {
                switch (origin.Phase)
                {
                    case FarmingOriginPhase.AnalysisLeveling:
                        analysis++;
                        break;
                    case FarmingOriginPhase.Preparing:
                        preparing++;
                        break;
                    case FarmingOriginPhase.ReadyForFilling:
                        ready++;
                        break;
                    case FarmingOriginPhase.Filling:
                        filling++;
                        break;
                    case FarmingOriginPhase.Done:
                        done++;
                        break;
                    case FarmingOriginPhase.Blocked:
                        blocked++;
                        break;
                }
            }
        }

        private static bool TryApplyTowerFarmableDumpRules(
            IAreaManagingTower tower,
            FarmingPreparationSession session,
            List<LooseProductProto> farmableDumpProducts,
            out string error)
        {
            error = string.Empty;
            if (!TryGetTowerDumpableProducts(tower, out List<LooseProductProto> currentProducts, out error))
                return false;

            if (!session.TowerDumpRulesOwned)
                session.TowerDumpRulesSnapshot = currentProducts.ToList();
            session.FillingAllDoneSinceRealtime = null;

            var farmableSet = new HashSet<LooseProductProto>(farmableDumpProducts);
            foreach (LooseProductProto product in currentProducts)
            {
                if (!farmableSet.Contains(product) && !TryInvokeTowerDumpRuleMethod(tower, "RemoveProductToDump", product, out error))
                    return false;
            }

            foreach (LooseProductProto product in farmableDumpProducts)
            {
                if (!TryInvokeTowerDumpRuleMethod(tower, "AddProductToDump", product, out error))
                    return false;
            }

            session.TowerDumpRulesOwned = true;
            return true;
        }

        private static void RestoreTowerDumpRulesIfOwned(IAreaManagingTower tower, FarmingPreparationSession session)
        {
            if (!session.TowerDumpRulesOwned || session.TowerDumpRulesSnapshot == null)
                return;

            if (!TryGetTowerDumpableProducts(tower, out List<LooseProductProto> currentProducts, out _))
                return;

            var snapshotSet = new HashSet<LooseProductProto>(session.TowerDumpRulesSnapshot);
            foreach (LooseProductProto product in currentProducts)
            {
                if (!snapshotSet.Contains(product))
                    TryInvokeTowerDumpRuleMethod(tower, "RemoveProductToDump", product, out _);
            }

            foreach (LooseProductProto product in session.TowerDumpRulesSnapshot)
            {
                TryInvokeTowerDumpRuleMethod(tower, "AddProductToDump", product, out _);
            }

            session.TowerDumpRulesOwned = false;
            session.TowerDumpRulesSnapshot = null;
        }

        private static bool TryGetTowerDumpableProducts(
            IAreaManagingTower tower,
            out List<LooseProductProto> products,
            out string error)
        {
            products = new List<LooseProductProto>();
            error = string.Empty;

            PropertyInfo? prop = tower.GetType().GetProperty(
                "DumpableProducts",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop == null)
            {
                error = "tower DumpableProducts property not found";
                return false;
            }

            if (!(prop.GetValue(tower) is IEnumerable enumerable))
            {
                error = "tower DumpableProducts is not enumerable";
                return false;
            }

            foreach (object item in enumerable)
            {
                if (item is LooseProductProto product)
                    products.Add(product);
            }

            return true;
        }

        private static bool TryInvokeTowerDumpRuleMethod(
            IAreaManagingTower tower,
            string methodName,
            LooseProductProto product,
            out string error)
        {
            error = string.Empty;
            MethodInfo? method = tower.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(LooseProductProto) },
                null);
            if (method == null)
            {
                error = $"tower {methodName}(LooseProductProto) method not found";
                return false;
            }

            try
            {
                method.Invoke(tower, new object[] { product });
                return true;
            }
            catch (System.Exception ex)
            {
                error = $"tower {methodName} failed for {product.Id}: {ex.Message}";
                return false;
            }
        }
    }
}
