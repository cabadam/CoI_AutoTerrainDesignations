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
using Mafi.Core.Products;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;
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
            public HashSet<Tile2i> PreparationAccessRampOrigins { get; } = new HashSet<Tile2i>();
            public HashSet<Tile2i> FillingAccessRampOrigins { get; } = new HashSet<Tile2i>();
        }

        private static readonly Dictionary<EntityId, FarmingPreparationSession> s_farmingPreparationSessions =
            new Dictionary<EntityId, FarmingPreparationSession>();
        private static readonly HashSet<EntityId> s_farmingAutomationDisabledTowerIds =
            new HashSet<EntityId>();
        private static int s_farmingAutomationTickIndex;
        private static bool s_farmingTowerBootstrapCompleted;
        private static bool s_farmingSaveRestorePending;
        private const float FARMING_FILLING_STABILIZATION_SECONDS = 3f;

        private static void ClearFarmingRuntimeState()
        {
            s_farmingDebugStoredDesignations.Clear();
            s_farmingPreparationSessions.Clear();
            s_farmingAutomationDisabledTowerIds.Clear();
            s_farmingAutomationTickIndex = 0;
            s_farmingTowerBootstrapCompleted = false;
            s_farmingSaveRestorePending = false;
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
                return;
            }

            StartFarmingPreparationForTower(tower, autoStartFilling: true);
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
                : true;
        }

        private static string StartFarmingPreparationForTower(IAreaManagingTower? tower, bool autoStartFilling)
        {
            if (tower == null)
                return "[ATD Farming] No tower selected.";

            if (s_desigManager == null)
                return "[ATD Farming] Terrain designation manager is unavailable.";

            if (s_levelingProto == null)
                return "[ATD Farming] Leveling prototype is unavailable.";

            if (!TryGetTowerEntityId(tower, out EntityId towerId) || !towerId.IsValid)
                return "[ATD Farming] Selected tower has no stable entity id.";

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

                return "[ATD Farming] Filling is waiting: all tracked tower designations must be ReadyForFilling or Done before tower dump rules can change.";
            }

            session.Active = true;
            session.LastReport = autoStartFilling
                ? "[ATD Farming] Farming automation enabled."
                : "[ATD Farming] Stage 3 preparation session started.";
            return session.LastReport;
        }

        internal static string RestoreFarmingPreparationForTower(IAreaManagingTower? tower)
        {
            if (tower == null)
                return "[ATD Farming] No tower selected.";

            if (s_desigManager == null)
                return "[ATD Farming] Terrain designation manager is unavailable.";

            if (s_levelingProto == null)
                return "[ATD Farming] Leveling prototype is unavailable.";

            if (!TryGetTowerEntityId(tower, out EntityId towerId) || !towerId.IsValid)
                return "[ATD Farming] Selected tower has no stable entity id.";

            if (!s_farmingPreparationSessions.TryGetValue(towerId, out FarmingPreparationSession session))
                return "[ATD Farming] Farming automation is already disabled for this tower.";

            session.Enabled = false;
            session.Active = false;
            session.Tower = tower;
            session.LastAccessRampRequestKey = string.Empty;
            session.FillingAllDoneSinceRealtime = null;
            RemoveOwnedFarmingAccessRamps(session, isFilling: false);
            RemoveOwnedFarmingAccessRamps(session, isFilling: true);
            RestoreTowerDumpRulesIfOwned(tower, session);
            int restored = 0;
            int failed = 0;
            foreach (FarmingOriginSession originState in session.Origins.Values.ToList())
            {
                if (s_desigManager.AddOrReplaceDesignation(s_levelingProto, originState.OriginalData))
                {
                    restored++;
                    originState.IsHiddenUntilFilling = false;
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
                session.LastReport = $"[ATD Farming] Restore attempted: restored={restored}, failed={failed}.";

            return failed == 0
                ? $"[ATD Farming] Restored {restored} Stage 3 origin(s) for this tower."
                : session.LastReport;
        }

        internal static string StartFarmingFillingForTower(IAreaManagingTower? tower)
        {
            if (tower == null)
                return "[ATD Farming] No tower selected.";

            if (s_desigManager == null)
                return "[ATD Farming] Terrain designation manager is unavailable.";

            if (s_levelingProto == null)
                return "[ATD Farming] Leveling prototype is unavailable.";

            if (!TryGetTowerEntityId(tower, out EntityId towerId) || !towerId.IsValid)
                return "[ATD Farming] Selected tower has no stable entity id.";

            if (!s_farmingPreparationSessions.TryGetValue(towerId, out FarmingPreparationSession session))
                return "[ATD Farming] No Stage 3 preparation session exists for this tower. Run preparation first.";

            session.Tower = tower;
            session.Enabled = true;
            session.AutoStartFilling = true;
            if (session.Active)
                return "[ATD Farming] A preparation/filling session is already active for this tower.";

            if (RunFarmingPreparationPass(tower, session))
            {
                session.Active = true;
                return "[ATD Farming] Filling is waiting: one or more tower designations still need leveling or preparation.";
            }

            return BeginFarmingFillingForSession(tower, towerId, session);
        }

        private static string BeginFarmingFillingForSession(
            IAreaManagingTower tower,
            EntityId towerId,
            FarmingPreparationSession session)
        {
            if (s_desigManager == null)
                return "[ATD Farming] Stage 4 blocked: designation manager unavailable.";

            if (s_levelingProto == null)
                return "[ATD Farming] Stage 4 blocked: leveling prototype unavailable.";

            if (!CanStartTowerLevelFilling(session))
            {
                session.LastReport = FormatFarmingPreparationReport(session);
                return "[ATD Farming] Filling is waiting: all tracked tower designations must be ReadyForFilling or Done before tower dump rules can change.";
            }

            List<FarmingOriginSession> fillingOrigins = session.Origins.Values
                .Where(origin => origin.Phase == FarmingOriginPhase.ReadyForFilling
                    || origin.Phase == FarmingOriginPhase.Done)
                .ToList();

            if (fillingOrigins.Count == 0)
            {
                session.LastReport = FormatFarmingPreparationReport(session);
                return "[ATD Farming] No origins are ready for filling/restoration. Run preparation first or wait for leveling/preparation to finish.";
            }

            List<LooseProductProto> farmableDumpProducts = GetFarmableDumpProducts();
            if (farmableDumpProducts.Count == 0)
            {
                session.LastReport = "[ATD Farming] Stage 4 blocked: no farmable dump products discovered.";
                return session.LastReport;
            }

            if (!TryApplyTowerFarmableDumpRules(tower, session, farmableDumpProducts, out string dumpRulesError))
            {
                session.LastReport = "[ATD Farming] Stage 4 blocked: " + dumpRulesError;
                return session.LastReport;
            }

            int restored = 0;
            int failed = 0;
            foreach (FarmingOriginSession originState in fillingOrigins)
            {
                if (s_desigManager.AddOrReplaceDesignation(s_levelingProto, originState.OriginalData))
                {
                    restored++;
                    originState.IsHiddenUntilFilling = false;
                    originState.Phase = FarmingOriginPhase.Filling;
                    originState.Detail = "original level designation restored; tower dump rules restricted to farmable products";
                    s_farmingDebugStoredDesignations.Remove(originState.Origin);
                }
                else
                {
                    failed++;
                    originState.Phase = FarmingOriginPhase.Blocked;
                    originState.Detail = "failed to restore original level designation for filling";
                }
            }

            if (restored == 0)
            {
                RestoreTowerDumpRulesIfOwned(tower, session);
                session.LastReport = $"[ATD Farming] Stage 4 blocked: failed to restore {failed} ready origin(s).";
                return session.LastReport;
            }

            session.Active = true;
            session.LastReport = $"[ATD Farming] Stage 4 filling started: restored={restored}, failed={failed}, farmableProducts={farmableDumpProducts.Count}.";
            return session.LastReport;
        }

        internal static string FormatFarmingPreparationStatusForTower(IAreaManagingTower? tower)
        {
            if (tower == null)
                return "[ATD Farming] No tower selected.";

            if (!TryGetTowerEntityId(tower, out EntityId towerId) || !towerId.IsValid)
                return "[ATD Farming] Selected tower has no stable entity id.";

            if (!s_farmingPreparationSessions.TryGetValue(towerId, out FarmingPreparationSession session))
                return "[ATD Farming] Farming automation: off.";

            return session.LastReport;
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

            foreach (var entry in s_farmingPreparationSessions.ToList())
            {
                EntityId towerId = entry.Key;
                FarmingPreparationSession session = entry.Value;
                IAreaManagingTower? tower = session.Tower;
                if (!session.Enabled || tower == null)
                    continue;

                bool fillingPass = session.Origins.Values.Any(origin => origin.Phase == FarmingOriginPhase.Filling);
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
            if (s_farmingTowerBootstrapCompleted || s_entitiesManager == null)
                return;

            try
            {
                foreach (MineTower tower in s_entitiesManager.GetAllEntitiesOfType<MineTower>())
                    EnsureFarmingAutomationDefaultEnabledForTower(tower);

                s_farmingTowerBootstrapCompleted = true;
            }
            catch (System.Exception ex)
            {
                Log.Warning("[ATD Farming] Failed to bootstrap farming automation for existing towers: " + ex.Message);
            }
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
            session.LastReport = FormatFarmingPreparationReport(session);

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
                    RestoreTowerDumpRulesIfOwned(tower, session);
                }

                session.LastAccessRampRequestKey = string.Empty;
                session.LastAccessCheckWorkKey = string.Empty;
                session.LastAccessCheckDetail = string.Empty;
                session.LastAccessCheckReady = true;
                session.LastAccessCheckTick = int.MinValue;
                session.FillingAllDoneSinceRealtime = null;

                foreach (FarmingOriginSession originState in session.Origins.Values)
                {
                    if (s_desigManager.AddOrReplaceDesignation(s_levelingProto, originState.OriginalData))
                    {
                        restored++;
                        originState.Phase = FarmingOriginPhase.AnalysisLeveling;
                        originState.IsHiddenUntilFilling = false;
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

            session.LastReport = FormatFarmingPreparationReport(session);
            bool hasFilling = session.Origins.Values.Any(origin => origin.Phase == FarmingOriginPhase.Filling);
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
                session.LastReport = FormatFarmingPreparationReport(session)
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
                        AdvanceAnalysisOrigin(originState, row);
                        break;
                }
            }

            foreach (Tile2i origin in droppedOrigins)
            {
                session.Origins.Remove(origin);
                s_farmingDebugStoredDesignations.Remove(origin);
            }
        }

        private static void AdvanceAnalysisOrigin(FarmingOriginSession originState, FarmingAnalysisRow row)
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
                    if (TryPlaceFarmingPreparationDesignation(originState.Origin, originState.OriginalData, originState.TargetHeight))
                    {
                        originState.Phase = FarmingOriginPhase.Preparing;
                        originState.Detail = $"placed temporary preparation target={originState.TargetHeight - 1}";
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
            int analysis = session.Origins.Values.Count(origin => origin.Phase == FarmingOriginPhase.AnalysisLeveling);
            int preparing = session.Origins.Values.Count(origin => origin.Phase == FarmingOriginPhase.Preparing);
            int ready = session.Origins.Values.Count(origin => origin.Phase == FarmingOriginPhase.ReadyForFilling);
            int filling = session.Origins.Values.Count(origin => origin.Phase == FarmingOriginPhase.Filling);
            int done = session.Origins.Values.Count(origin => origin.Phase == FarmingOriginPhase.Done);
            int blocked = session.Origins.Values.Count(origin => origin.Phase == FarmingOriginPhase.Blocked);

            var sb = new StringBuilder();
            sb.AppendLine(session.Enabled
                ? "[ATD Farming] Farming automation: on"
                : "[ATD Farming] Farming automation: off");
            sb.AppendLine($"  Active origins={session.Origins.Count}, Analysis/Leveling={analysis}, Preparing={preparing}, ReadyForFilling={ready}, Filling={filling}, Done={done}, Blocked={blocked}");
            if (session.TowerDumpRulesOwned)
                sb.AppendLine("  Tower dump rules are temporarily restricted to farmable products.");
            if (!string.IsNullOrEmpty(session.LastDroppedOriginDetail))
                sb.AppendLine("  " + session.LastDroppedOriginDetail);
            if (!string.IsNullOrEmpty(session.LastAccessRampDetail))
                sb.AppendLine("  " + session.LastAccessRampDetail);

            foreach (FarmingOriginSession originState in session.Origins.Values
                .OrderBy(origin => origin.Origin.Y)
                .ThenBy(origin => origin.Origin.X)
                .Take(40))
            {
                sb.AppendLine($"  {originState.Phase}: ({originState.Origin.X},{originState.Origin.Y}) target={originState.TargetHeight} {originState.Detail}".TrimEnd());
            }

            if (session.Origins.Count > 40)
                sb.AppendLine($"  ... {session.Origins.Count - 40} more origin(s) omitted.");

            return sb.ToString().TrimEnd();
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
