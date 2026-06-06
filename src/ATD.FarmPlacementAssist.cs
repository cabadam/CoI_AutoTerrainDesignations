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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Buildings.Farms;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Static.Commands;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Entities.Validators;
using Mafi.Core.Input;
using Mafi.Core.Prototypes;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;
using Mafi.Serialization;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        // ---------------------------------------------------------------------------------
        // Static state
        // ---------------------------------------------------------------------------------

        // s_inputScheduler is declared and wired in ATD.State.cs.

        private static readonly List<PlacementIntentBatch> s_pendingFarmPlacementBatches = new();

        // Positions registered here are replays issued by ATD itself. The intercept prefix
        // checks this set and lets matching commands through without re-intercepting them.
        private static readonly HashSet<Tile2i> s_farmPlacementReplayPositions = new();

        private sealed class PlacementIntentBatch
        {
            // Full original batch from BatchCreateStaticEntitiesCmd. During the same game
            // session this preserves every config bag exactly; save/load uses the compact
            // JSON projection below.
            public readonly ImmutableArray<EntityConfigData> Items;
            public readonly bool OriginalApplyConfiguration;
            public readonly IReadOnlyList<Tile2i> RequiredCells;
            public readonly HashSet<Tile2i> AtdInjectedCells = new();

            public PlacementIntentBatch(ImmutableArray<EntityConfigData> items, bool originalApplyConfiguration,
                IReadOnlyList<Tile2i> requiredCells)
            {
                Items = items; OriginalApplyConfiguration = originalApplyConfiguration;
                RequiredCells = requiredCells;
            }
        }

        private sealed class FarmPlacementItemRecord
        {
            public string ProtoId = string.Empty;
            public int X;
            public int Y;
            public int Z;
            public int Rotation;
            public bool IsReflected;
            public int? FertilityTargetRaw;
            public string?[]? CropSchedule;
        }

        private sealed class FarmPlacementBatchRecord
        {
            public bool ApplyConfiguration;
            public readonly List<FarmPlacementItemRecord> Items = new();
        }

        // ---------------------------------------------------------------------------------
        // Intercept patch — Phase 3-A (corrected)
        // Farms use BatchCreateStaticEntitiesCmd, NOT CreateStaticEntityCmd.
        // Confirmed by S1 probe: Invoke(CreateStaticEntityCmd) never fires for farms.
        // ---------------------------------------------------------------------------------

        private static class Patch_EntitiesCommandsProcessor_Invoke_Batch_Farm
        {
            internal static bool Prefix(BatchCreateStaticEntitiesCmd cmd)
            {
                if (!IsInitialized) return true;
                if (s_protosDb == null) return true;

                var assistedFarmItems = new List<Tuple<EntityConfigData, FarmProto, TileTransform, IAreaManagingTower>>();
                var replayPositionsToConsume = new List<Tile2i>();

                foreach (EntityConfigData item in cmd.ConfigData)
                {
                    if (item.Prototype.ValueOrNull is not FarmProto farmProto) continue;

                    TileTransform? transform = item.Transform;
                    if (!transform.HasValue) continue;

                    Tile2i position = transform.Value.Position.Xy;
                    IAreaManagingTower? tower = GetFarmingTowerForFarm(farmProto, transform.Value);
                    if (tower == null) continue;

                    if (s_farmPlacementReplayPositions.Contains(position))
                    {
                        replayPositionsToConsume.Add(position);
                        continue;
                    }

                    assistedFarmItems.Add(Tuple.Create(item, farmProto, transform.Value, tower));
                }

                if (assistedFarmItems.Count == 0)
                {
                    foreach (Tile2i replayPosition in replayPositionsToConsume)
                        s_farmPlacementReplayPositions.Remove(replayPosition);
                    return true;
                }

                foreach (var assisted in assistedFarmItems)
                {
                    FarmProto farmProto = assisted.Item2;
                    TileTransform transform = assisted.Item3;
                    IAreaManagingTower tower = assisted.Item4;
                    s_log.Info($"[ATD FarmPlacementAssist] Intercepted farm placement: " +
                        $"proto={farmProto.Id}, pos={transform.Position.Xy}, rot={transform.Rotation}, " +
                        $"reflected={transform.IsReflected}, tower={tower.Id}.");
                }

                OnFarmPlacementBatchIntercepted(cmd.ConfigData, assistedFarmItems, cmd.ApplyConfiguration);

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
            internal static bool Prefix(LayoutEntityAddRequest addRequest, ref EntityValidationResult __result)
            {
                if (!IsInitialized) return true;
                if (GetFarmingTowerForRequest(addRequest) == null) return true;
                __result = EntityValidationResult.Success;
                return false;
            }
        }

        // Phase 2-B: LayoutEntityTerrainValidator.CanAdd
        // Explicit interface implementation — located via interface map, not string name.
        // Suppresses map-bounds and ocean checks only; does NOT handle terrain height.
        private static class Patch_LayoutEntityTerrainValidator_CanAdd_Farm
        {
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
                __result = EntityValidationResult.Success;
                return false;
            }
        }

        // Phase 2-C: StaticEntitiesTerrainInteractionManager.CanAdd
        // This is the validator that emits "Terrain too high" / "Terrain too low" errors.
        // IEntityWithOccupiedTilesAddRequest exposes Proto (as ILayoutEntityProto) and Origin.
        private static class Patch_StaticEntitiesTerrainInteractionManager_CanAdd_Farm
        {
            internal static bool Prefix(IEntityWithOccupiedTilesAddRequest addRequest, ref EntityValidationResult __result)
            {
                if (!IsInitialized) return true;
                if (addRequest.Proto is not FarmProto) return true;
                if (GetFarmingTowerForRequest(addRequest) == null) return true;
                __result = EntityValidationResult.Success;
                return false;
            }
        }

        // ---------------------------------------------------------------------------------
        // Tower-area helper
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

        private static IAreaManagingTower? GetFarmingTowerForFarm(FarmProto proto, TileTransform transform)
        {
            Tile2i position = transform.Position.Xy;
            IAreaManagingTower? tower = null;
            ImmutableArray<OccupiedTileRelative> relTiles = proto.Layout.GetOccupiedTilesRelative(transform);
            foreach (OccupiedTileRelative rel in relTiles)
            {
                Tile2i tile = position + rel.RelCoord;
                IAreaManagingTower? tileTower = GetFarmingTowerForTile(tile);
                if (tileTower == null) return null;
                if (tower == null)
                    tower = tileTower;
                else if (!ReferenceEquals(tower, tileTower))
                    return null;
            }

            return tower;
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

        internal static IAreaManagingTower? GetFarmingTowerForRequest(IEntityWithOccupiedTilesAddRequest addRequest)
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
        internal static List<Tile2i> ComputeCoveredDesignationCells(FarmProto proto, TileTransform transform)
        {
            Tile2i position = transform.Position.Xy;
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
        private static bool AreCellsDone(PlacementIntentBatch intent)
            => AreCellsAlreadyFarmable(intent.RequiredCells);

        /// <summary>
        /// Ensures a flat leveling designation exists at <paramref name="origin"/> at
        /// <paramref name="targetHeight"/>. Records the origin in <paramref name="intent"/>.AtdInjectedCells
        /// if a new designation was created so it can be cleaned up if the player cancels.
        /// </summary>
        private static void EnsureFarmingDesignationForCell(Tile2i origin, int targetHeight, PlacementIntentBatch intent)
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
        /// Called when a farm-containing batch is intercepted. Either replays immediately
        /// (site ready) or injects leveling designations and registers the entire batch as
        /// one pending intent.
        /// </summary>
        private static void OnFarmPlacementBatchIntercepted(
            ImmutableArray<EntityConfigData> items,
            List<Tuple<EntityConfigData, FarmProto, TileTransform, IAreaManagingTower>> assistedFarmItems,
            bool applyConfiguration)
        {
            var requiredCells = new HashSet<Tile2i>();
            foreach (var assisted in assistedFarmItems)
            {
                FarmProto farmProto = assisted.Item2;
                TileTransform transform = assisted.Item3;
                foreach (Tile2i cell in ComputeCoveredDesignationCells(farmProto, transform))
                    requiredCells.Add(cell);
            }

            var cells = new List<Tile2i>(requiredCells);

            // If the site is already fully prepared, replay immediately — no deferral needed.
            if (AreCellsAlreadyFarmable(cells))
            {
                s_log.Info($"[ATD FarmPlacementAssist] Site already farmable — replaying batch immediately.");
                ReplayFarmPlacementBatch(items, applyConfiguration);
                return;
            }

            var intent = new PlacementIntentBatch(items, applyConfiguration, cells);

            foreach (var assisted in assistedFarmItems)
            {
                FarmProto farmProto = assisted.Item2;
                TileTransform transform = assisted.Item3;
                Tile2i position = transform.Position.Xy;
                int targetHeight = s_desigManager != null
                    ? (int)Math.Floor(s_desigManager.TerrainManager.GetHeight(position).Value.ToFloat())
                    : transform.Position.Z;

                foreach (Tile2i origin in ComputeCoveredDesignationCells(farmProto, transform))
                    EnsureFarmingDesignationForCell(origin, targetHeight, intent);
            }

            s_pendingFarmPlacementBatches.Add(intent);
            s_log.Info($"[ATD FarmPlacementAssist] Deferred batch. items={items.Length}, " +
                $"cells={cells.Count}, injected={intent.AtdInjectedCells.Count}.");
        }

        // ---------------------------------------------------------------------------------
        // Replay path (Phase 4-C — S4 confirmed 2026-05-30)
        // ---------------------------------------------------------------------------------

        internal static void ReplayFarmPlacementBatch(ImmutableArray<EntityConfigData> items, bool applyConfiguration)
        {
            if (s_inputScheduler == null)
            {
                s_log.Warning("[ATD FarmPlacementAssist] Cannot replay: IInputScheduler not available.");
                return;
            }

            foreach (EntityConfigData item in items)
            {
                if (item.Prototype.ValueOrNull is FarmProto && item.Transform.HasValue)
                    s_farmPlacementReplayPositions.Add(item.Transform.Value.Position.Xy);
            }

            var cmd = new BatchCreateStaticEntitiesCmd(
                items,
                BuildMiniZippersMode.DeferToProto,
                isFree: false,
                allowValidationSuppression: false,
                applyConfiguration: applyConfiguration);
            s_inputScheduler.ScheduleInputCmd(cmd);
            s_log.Info($"[ATD FarmPlacementAssist] Replayed farm placement batch: items={items.Length}.");
        }

        // ---------------------------------------------------------------------------------
        // Tick — AreCellsDone-gated replay (Phase 4-A)
        // ---------------------------------------------------------------------------------

        internal static void TickFarmPlacementAssist()
        {
            if (s_pendingFarmPlacementBatches.Count == 0) return;
            for (int i = s_pendingFarmPlacementBatches.Count - 1; i >= 0; i--)
            {
                PlacementIntentBatch intent = s_pendingFarmPlacementBatches[i];
                if (!AreCellsDone(intent)) continue;
                s_pendingFarmPlacementBatches.RemoveAt(i);
                ReplayFarmPlacementBatch(intent.Items, intent.OriginalApplyConfiguration);
            }
        }

        internal static void ClearFarmPlacementAssistRuntimeState()
        {
            s_pendingFarmPlacementBatches.Clear();
            s_farmPlacementReplayPositions.Clear();
        }

        internal static int GetPendingFarmPlacementBatchCount() => s_pendingFarmPlacementBatches.Count;

        internal static void AppendPendingFarmPlacementBatchesJson(StringBuilder sb)
        {
            sb.Append(",\"pendingFarmPlacementBatches\":[");
            bool firstBatch = true;
            foreach (PlacementIntentBatch intent in s_pendingFarmPlacementBatches)
            {
                FarmPlacementBatchRecord record = CreatePendingFarmPlacementBatchRecord(intent);
                if (record.Items.Count == 0) continue;
                if (!firstBatch) sb.Append(',');
                firstBatch = false;
                AppendPendingFarmPlacementBatchJson(sb, record);
            }
            sb.Append(']');
        }

        internal static void RestorePendingFarmPlacementBatchesFromJsonEntries(object[] entries)
        {
            s_pendingFarmPlacementBatches.Clear();
            foreach (object rawEntry in entries)
            {
                if (rawEntry is not Mafi.Collections.Dict<string, object> entry)
                {
                    s_log.Warning($"[ATD FarmPlacementAssist] Skipping unreadable pending batch entry: {rawEntry}");
                    continue;
                }

                if (!TryGetBool(entry, "applyConfiguration", out bool applyConfiguration))
                    applyConfiguration = true;

                if (!entry.TryGetValue("items", out object rawItems) || rawItems is not object[] itemEntries)
                {
                    s_log.Warning("[ATD FarmPlacementAssist] Pending batch entry missing items array; skipped.");
                    continue;
                }

                ImmutableArray<EntityConfigData> items = RebuildPendingFarmPlacementItems(itemEntries);
                if (items.Length == 0)
                    continue;

                var requiredCells = new HashSet<Tile2i>();
                foreach (EntityConfigData item in items)
                {
                    if (item.Prototype.ValueOrNull is FarmProto farmProto && item.Transform.HasValue)
                    {
                        foreach (Tile2i cell in ComputeCoveredDesignationCells(farmProto, item.Transform.Value))
                            requiredCells.Add(cell);
                    }
                }

                if (requiredCells.Count == 0)
                    continue;

                s_pendingFarmPlacementBatches.Add(new PlacementIntentBatch(
                    items,
                    applyConfiguration,
                    new List<Tile2i>(requiredCells)));
            }

            if (s_pendingFarmPlacementBatches.Count > 0)
                s_log.Info($"[ATD FarmPlacementAssist] Restored {s_pendingFarmPlacementBatches.Count} pending farm placement batch(es) from save state.");
        }

        private static FarmPlacementBatchRecord CreatePendingFarmPlacementBatchRecord(PlacementIntentBatch intent)
        {
            var record = new FarmPlacementBatchRecord { ApplyConfiguration = intent.OriginalApplyConfiguration };
            foreach (EntityConfigData item in intent.Items)
            {
                if (TryCreatePendingFarmPlacementItemRecord(item, out FarmPlacementItemRecord? itemRecord) && itemRecord != null)
                    record.Items.Add(itemRecord);
            }
            return record;
        }

        private static bool TryCreatePendingFarmPlacementItemRecord(EntityConfigData item, out FarmPlacementItemRecord? record)
        {
            record = null;
            if (item.Prototype.ValueOrNull is not Proto proto) return false;
            if (!item.Transform.HasValue) return false;

            TileTransform transform = item.Transform.Value;
            record = new FarmPlacementItemRecord
            {
                ProtoId = proto.Id.Value,
                X = transform.Position.X,
                Y = transform.Position.Y,
                Z = transform.Position.Z,
                Rotation = transform.Rotation.AngleIndex,
                IsReflected = transform.IsReflected
            };

            if (proto is FarmProto)
            {
                Percent? fertilityTarget = item.GetFertilityTarget();
                if (fertilityTarget.HasValue)
                    record.FertilityTargetRaw = fertilityTarget.Value.RawValue;

                ImmutableArray<Option<CropProto>>? cropSchedule = item.GetCropSchedule();
                if (cropSchedule.HasValue)
                {
                    record.CropSchedule = new string?[cropSchedule.Value.Length];
                    for (int i = 0; i < cropSchedule.Value.Length; i++)
                        record.CropSchedule[i] = cropSchedule.Value[i].ValueOrNull?.Id.Value;
                }
            }

            return true;
        }

        private static void AppendPendingFarmPlacementBatchJson(StringBuilder sb, FarmPlacementBatchRecord record)
        {
            sb.Append("{\"applyConfiguration\":");
            AppendJsonBool(sb, record.ApplyConfiguration);
            sb.Append(",\"items\":[");
            for (int i = 0; i < record.Items.Count; i++)
            {
                if (i > 0) sb.Append(',');
                AppendPendingFarmPlacementItemJson(sb, record.Items[i]);
            }
            sb.Append("]}");
        }

        private static void AppendPendingFarmPlacementItemJson(StringBuilder sb, FarmPlacementItemRecord item)
        {
            sb.Append("{\"protoId\":\"");
            sb.Append(JsonWriter.JsonEscapeString(item.ProtoId));
            sb.Append("\",\"x\":");
            sb.Append(item.X.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"y\":");
            sb.Append(item.Y.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"z\":");
            sb.Append(item.Z.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"rotation\":");
            sb.Append(item.Rotation.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"isReflected\":");
            AppendJsonBool(sb, item.IsReflected);

            if (item.FertilityTargetRaw.HasValue)
            {
                sb.Append(",\"fertilityTargetRaw\":");
                sb.Append(item.FertilityTargetRaw.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (item.CropSchedule != null)
            {
                sb.Append(",\"cropSchedule\":[");
                for (int i = 0; i < item.CropSchedule.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    string? cropId = item.CropSchedule[i];
                    if (string.IsNullOrEmpty(cropId))
                    {
                        sb.Append("null");
                    }
                    else
                    {
                        sb.Append('"');
                        sb.Append(JsonWriter.JsonEscapeString(cropId));
                        sb.Append('"');
                    }
                }
                sb.Append(']');
            }

            sb.Append('}');
        }

        private static ImmutableArray<EntityConfigData> RebuildPendingFarmPlacementItems(object[] itemEntries)
        {
            var rebuilt = new List<EntityConfigData>();
            foreach (object rawItem in itemEntries)
            {
                if (rawItem is not Mafi.Collections.Dict<string, object> item)
                    continue;
                if (!TryRebuildPendingFarmPlacementItem(item, out EntityConfigData? configData))
                    continue;
                if (configData != null)
                    rebuilt.Add(configData);
            }

            var builder = new ImmutableArrayBuilder<EntityConfigData>(rebuilt.Count);
            for (int i = 0; i < rebuilt.Count; i++)
                builder[i] = rebuilt[i];
            return builder.GetImmutableArrayAndClear();
        }

        private static bool TryRebuildPendingFarmPlacementItem(Mafi.Collections.Dict<string, object> item, out EntityConfigData? configData)
        {
            configData = null;
            if (s_protosDb == null || s_configSerializationContext == null)
                return false;
            if (!TryGetString(item, "protoId", out string protoId) || string.IsNullOrEmpty(protoId))
                return false;
            if (!s_protosDb.TryGetProto(new Proto.ID(protoId), out Proto proto))
            {
                s_log.Warning($"[ATD FarmPlacementAssist] Pending placement proto '{protoId}' not found; item skipped.");
                return false;
            }
            if (!TryGetInt(item, "x", out int x)
                || !TryGetInt(item, "y", out int y)
                || !TryGetInt(item, "z", out int z))
                return false;

            if (!TryGetInt(item, "rotation", out int rotation))
                rotation = 0;
            if (!TryGetBool(item, "isReflected", out bool isReflected))
                isReflected = false;

            configData = new EntityConfigData(proto, s_configSerializationContext);
            configData.Transform = new TileTransform(new Tile3i(x, y, z), new Rotation90(rotation), isReflected);

            if (proto is FarmProto)
            {
                if (TryGetInt(item, "fertilityTargetRaw", out int fertilityTargetRaw))
                    configData.SetFertilityTarget(Percent.FromRaw(fertilityTargetRaw));

                if (item.TryGetValue("cropSchedule", out object rawCropSchedule) && rawCropSchedule is object[] cropEntries)
                {
                    var cropBuilder = new ImmutableArrayBuilder<Option<CropProto>>(cropEntries.Length);
                    for (int i = 0; i < cropEntries.Length; i++)
                    {
                        if (cropEntries[i] is string cropId
                            && s_protosDb.TryGetProto(new Proto.ID(cropId), out CropProto cropProto))
                            cropBuilder[i] = Option.Some(cropProto);
                        else
                            cropBuilder[i] = Option<CropProto>.None;
                    }
                    configData.SetCropSchedule(cropBuilder.GetImmutableArrayAndClear());
                }
            }

            return true;
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

            try
            {
                // --- StaticEntitiesTerrainInteractionManager.CanAdd (terrain too high/low) ---
                var terrainInteractionTarget = AccessTools.Method(
                    typeof(StaticEntitiesTerrainInteractionManager), "CanAdd",
                    new[] { typeof(IEntityWithOccupiedTilesAddRequest) });
                var terrainInteractionPrefix = AccessTools.Method(
                    typeof(Patch_StaticEntitiesTerrainInteractionManager_CanAdd_Farm), "Prefix");
                if (terrainInteractionTarget == null || terrainInteractionPrefix == null)
                    s_log.Warning("[ATD FPA] StaticEntitiesTerrainInteractionManager.CanAdd patch target not found.");
                else
                    harmony.Patch(terrainInteractionTarget, prefix: new HarmonyMethod(terrainInteractionPrefix));
            }
            catch (Exception ex) { s_log.Warning("[ATD FPA] Failed to patch StaticEntitiesTerrainInteractionManager.CanAdd: " + ex.Message); }
        }
    }
}
