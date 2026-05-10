// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
// Auto Terrain Designations - Farming Preparation Analysis (read-only)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mafi;
using Mafi.Collections;
using Mafi.Core;
using Mafi.Core.Buildings.Mine;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.Products;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        private const float FARMING_HEIGHT_EPSILON = 0.05f;
        private const float MIN_FARMABLE_THICKNESS = 0.9003906f;

        private enum FarmingAnalysisState
        {
            Done,
            NeedsLeveling,
            ReadyForFilling,
            NeedsPreparation,
            SkippedNonFlat
        }

        private sealed class FarmingAnalysisRow
        {
            public Tile2i Origin { get; }
            public FarmingAnalysisState State { get; }
            public int? TargetHeight { get; }
            public float MinSurfaceHeight { get; }
            public float MaxSurfaceHeight { get; }
            public int NonFarmableCells { get; }
            public float MinFarmableThicknessInBand { get; }
            public string Detail { get; }

            public FarmingAnalysisRow(
                Tile2i origin,
                FarmingAnalysisState state,
                int? targetHeight,
                float minSurfaceHeight,
                float maxSurfaceHeight,
                int nonFarmableCells,
                float minFarmableThicknessInBand,
                string detail)
            {
                Origin = origin;
                State = state;
                TargetHeight = targetHeight;
                MinSurfaceHeight = minSurfaceHeight;
                MaxSurfaceHeight = maxSurfaceHeight;
                NonFarmableCells = nonFarmableCells;
                MinFarmableThicknessInBand = minFarmableThicknessInBand;
                Detail = detail;
            }
        }

        internal static string FormatFarmingAnalysisForTower(IAreaManagingTower? tower)
        {
            return FormatFarmingAnalysisForTower(tower, 10);
        }

        private static string FormatFarmingAnalysisForTower(IAreaManagingTower? tower, int? maxRows)
        {
            if (tower == null)
                return "[ATD Farming] No tower selected.";

            if (s_desigManager == null)
                return "[ATD Farming] Terrain designation manager is unavailable.";

            TerrainManager terrMgr = s_desigManager.TerrainManager;
            List<LooseProductProto> farmableDumpProducts = GetFarmableDumpProducts();
            List<FarmingAnalysisRow> rows = AnalyzeFarmingDesignations(tower, terrMgr);

            int flatCount = rows.Count(row => row.State != FarmingAnalysisState.SkippedNonFlat);
            int skippedNonFlat = rows.Count(row => row.State == FarmingAnalysisState.SkippedNonFlat);
            int done = rows.Count(row => row.State == FarmingAnalysisState.Done);
            int needsLeveling = rows.Count(row => row.State == FarmingAnalysisState.NeedsLeveling);
            int readyForFilling = rows.Count(row => row.State == FarmingAnalysisState.ReadyForFilling);
            int needsPreparation = rows.Count(row => row.State == FarmingAnalysisState.NeedsPreparation);

            var sb = new StringBuilder();
            sb.AppendLine("[ATD Farming] Stage 1 read-only analysis");
            sb.AppendLine($"  Level designations: {rows.Count} ({flatCount} flat, {skippedNonFlat} non-flat skipped)");
            sb.AppendLine($"  Done={done}, NeedsLeveling={needsLeveling}, ReadyForFilling={readyForFilling}, NeedsPreparation={needsPreparation}");
            sb.AppendLine("  Farmable dump products: " + FormatFarmableDumpProductList(farmableDumpProducts));

            if (rows.Count == 0)
            {
                sb.AppendLine("  No level designations are currently managed by this tower.");
                return sb.ToString().TrimEnd();
            }

            IEnumerable<FarmingAnalysisRow> orderedRows = rows
                .OrderBy(row => row.Origin.Y)
                .ThenBy(row => row.Origin.X);
            if (maxRows.HasValue)
                orderedRows = orderedRows.Take(maxRows.Value);

            foreach (FarmingAnalysisRow row in orderedRows)
            {
                string target = row.TargetHeight.HasValue ? row.TargetHeight.Value.ToString() : "-";
                string heights = row.TargetHeight.HasValue
                    ? $"{row.MinSurfaceHeight:F2}..{row.MaxSurfaceHeight:F2}"
                    : "-";
                string farmable = row.TargetHeight.HasValue
                    ? $"farmableMin={row.MinFarmableThicknessInBand:F2}, nonFarmCells={row.NonFarmableCells}"
                    : string.Empty;
                sb.AppendLine($"  {row.State}: ({row.Origin.X},{row.Origin.Y}) target={target}, surface={heights} {farmable} {row.Detail}".TrimEnd());
            }

            if (maxRows.HasValue && rows.Count > maxRows.Value)
                sb.AppendLine($"  ... {rows.Count - maxRows.Value} more designation(s) omitted.");

            return sb.ToString().TrimEnd();
        }

        internal static string FormatAllTowersFarmingDesignationDump()
        {
            if (s_entitiesManager == null)
                return "[ATD Farming] Entities manager is unavailable.";

            var sb = new StringBuilder();
            sb.AppendLine("[ATD Farming] Complete tower farming designation dump");

            int towerCount = 0;
            try
            {
                foreach (MineTower tower in s_entitiesManager.GetAllEntitiesOfType<MineTower>()
                    .OrderBy(tower => tower.Position2f.Tile2i.Y)
                    .ThenBy(tower => tower.Position2f.Tile2i.X))
                {
                    towerCount++;
                    Tile2i position = tower.Position2f.Tile2i;
                    bool hasEntityId = TryGetTowerEntityId(tower, out EntityId entityId) && entityId.IsValid;
                    string towerId = hasEntityId
                        ? entityId.ToString()
                        : "unknown";

                    sb.AppendLine();
                    sb.AppendLine($"Tower {towerCount}: id={towerId}, position=({position.X},{position.Y})");
                    sb.AppendLine(IsFarmingAutomationEnabledForTower(tower)
                        ? "  Farming automation: on"
                        : "  Farming automation: off");

                    FarmingPreparationSession? session;
                    if (hasEntityId && s_farmingPreparationSessions.TryGetValue(entityId, out session))
                    {
                        sb.AppendLine(FormatFarmingPreparationReport(session, null));
                    }
                    else
                    {
                        sb.AppendLine("  No active farming preparation session.");
                    }

                    sb.AppendLine(FormatFarmingAnalysisForTower(tower, null));
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("  Dump failed: " + ex.Message);
            }

            if (towerCount == 0)
                sb.AppendLine("  No mine towers found.");

            return sb.ToString().TrimEnd();
        }

        private static List<FarmingAnalysisRow> AnalyzeFarmingDesignations(IAreaManagingTower tower, TerrainManager terrMgr)
        {
            var rows = new List<FarmingAnalysisRow>();

            foreach (TerrainDesignation designation in tower.ManagedDesignations)
            {
                if (!IsLevelingDesignation(designation))
                    continue;

                DesignationData data = designation.Data;
                Tile2i origin = designation.OriginTileCoord;
                if (!TryGetFlatTargetHeight(data, out int targetHeight))
                {
                    rows.Add(new FarmingAnalysisRow(
                        origin,
                        FarmingAnalysisState.SkippedNonFlat,
                        null,
                        0f,
                        0f,
                        0,
                        0f,
                        "non-flat level designation"));
                    continue;
                }

                rows.Add(AnalyzeFarmingDesignation(origin, targetHeight, terrMgr));
            }

            return rows;
        }

        private static FarmingAnalysisRow AnalyzeFarmingDesignation(Tile2i origin, int targetHeight, TerrainManager terrMgr)
        {
            float minSurface = float.MaxValue;
            float maxSurface = float.MinValue;
            int nonFarmableCells = 0;
            float minFarmableThickness = float.MaxValue;

            foreach (Tile2i cell in EnumerateDesignatableTileCells(origin))
            {
                float surfaceHeight = terrMgr.GetHeight(cell).Value.ToFloat();
                minSurface = Math.Min(minSurface, surfaceHeight);
                maxSurface = Math.Max(maxSurface, surfaceHeight);

                GetFarmableBandStats(
                    terrMgr,
                    cell,
                    targetHeight - 1f,
                    targetHeight,
                    out float farmableThickness,
                    out float nonFarmableThickness);

                minFarmableThickness = Math.Min(minFarmableThickness, farmableThickness);
                if (nonFarmableThickness > FARMING_HEIGHT_EPSILON)
                    nonFarmableCells++;
            }

            if (minFarmableThickness == float.MaxValue)
                minFarmableThickness = 0f;

            float preparationHeight = targetHeight - 1f;
            bool belowPreparationBase = minSurface < preparationHeight - FARMING_HEIGHT_EPSILON;
            if (belowPreparationBase)
            {
                return new FarmingAnalysisRow(
                    origin,
                    FarmingAnalysisState.NeedsPreparation,
                    targetHeight,
                    minSurface,
                    maxSurface,
                    nonFarmableCells,
                    minFarmableThickness,
                    "below target-1; needs rough fill/leveling with tower dump materials before topsoil fill");
            }

            if (nonFarmableCells > 0)
            {
                return new FarmingAnalysisRow(
                    origin,
                    FarmingAnalysisState.NeedsPreparation,
                    targetHeight,
                    minSurface,
                    maxSurface,
                    nonFarmableCells,
                    minFarmableThickness,
                    "future topsoil band contains non-farmable material");
            }

            bool allAtTarget = Math.Abs(minSurface - targetHeight) <= FARMING_HEIGHT_EPSILON
                && Math.Abs(maxSurface - targetHeight) <= FARMING_HEIGHT_EPSILON;
            bool allAtOrAboveTarget = minSurface >= targetHeight - FARMING_HEIGHT_EPSILON;
            bool fullyFarmableAtTarget = allAtTarget && minFarmableThickness >= MIN_FARMABLE_THICKNESS;

            if (fullyFarmableAtTarget)
            {
                return new FarmingAnalysisRow(
                    origin,
                    FarmingAnalysisState.Done,
                    targetHeight,
                    minSurface,
                    maxSurface,
                    nonFarmableCells,
                    minFarmableThickness,
                    "already at target with farmable top band");
            }

            if (allAtOrAboveTarget)
            {
                return new FarmingAnalysisRow(
                    origin,
                    FarmingAnalysisState.NeedsLeveling,
                    targetHeight,
                    minSurface,
                    maxSurface,
                    nonFarmableCells,
                    minFarmableThickness,
                    "farmable band clear; keep level designation active");
            }

            return new FarmingAnalysisRow(
                origin,
                FarmingAnalysisState.ReadyForFilling,
                targetHeight,
                minSurface,
                maxSurface,
                nonFarmableCells,
                minFarmableThickness,
                "farmable band clear; below target needs topsoil fill");
        }

        private static FarmingAnalysisRow AnalyzeFarmingFillingDesignation(
            TerrainDesignation liveDesignation,
            int targetHeight,
            TerrainManager terrMgr)
        {
            FarmingAnalysisRow row = AnalyzeFarmingDesignation(liveDesignation.OriginTileCoord, targetHeight, terrMgr);
            bool designationFulfilled = IsDumpingDesignation(liveDesignation)
                ? liveDesignation.IsDumpingFulfilled
                : liveDesignation.IsFulfilled;
            if (row.State != FarmingAnalysisState.Done || designationFulfilled)
                return row;

            string detail = IsDumpingDesignation(liveDesignation)
                ? "farmable top band appears complete, but vanilla dump fulfillment still needs work"
                : "farmable top band appears complete, but vanilla level designation is not fulfilled yet";
            return new FarmingAnalysisRow(
                row.Origin,
                FarmingAnalysisState.ReadyForFilling,
                row.TargetHeight,
                row.MinSurfaceHeight,
                row.MaxSurfaceHeight,
                row.NonFarmableCells,
                row.MinFarmableThicknessInBand,
                detail);
        }

        private static bool IsFarmingPreparationDesignation(
            TerrainDesignation designation,
            FarmingOriginSession originState)
        {
            if (IsLevelingDesignation(designation))
                return true;

            return originState.Phase == FarmingOriginPhase.Preparing
                && IsDumpingDesignation(designation);
        }

        private static bool TryGetFlatTargetHeight(DesignationData data, out int targetHeight)
        {
            int originHeight = data.OriginTargetHeight.Value;
            targetHeight = originHeight;
            return data.PlusXTargetHeight.Value == originHeight
                && data.PlusXyTargetHeight.Value == originHeight
                && data.PlusYTargetHeight.Value == originHeight;
        }

        private static void GetFarmableBandStats(
            TerrainManager terrMgr,
            Tile2i coord,
            float bandBottom,
            float bandTop,
            out float farmableThickness,
            out float nonFarmableThickness)
        {
            farmableThickness = 0f;
            nonFarmableThickness = 0f;

            float layerTop = terrMgr.GetHeight(coord).Value.ToFloat();
            TerrainLayerEnumerator enumerator = terrMgr.EnumerateLayers(terrMgr.GetTileIndex(coord));
            while (enumerator.MoveNext())
            {
                TerrainMaterialThicknessSlim layer = enumerator.Current;
                float thickness = layer.Thickness.Value.ToFloat();
                float layerBottom = layerTop - thickness;

                if (layerTop <= bandBottom + FARMING_HEIGHT_EPSILON)
                    break;

                float overlap = Math.Min(layerTop, bandTop) - Math.Max(layerBottom, bandBottom);
                if (overlap > FARMING_HEIGHT_EPSILON)
                {
                    TerrainMaterialProto material = layer.SlimId.ToFull(terrMgr);
                    if (material.IsFarmable)
                        farmableThickness += overlap;
                    else
                        nonFarmableThickness += overlap;
                }

                layerTop = layerBottom;
            }
        }

        private static List<LooseProductProto> GetFarmableDumpProducts()
        {
            if (s_protosDb == null)
                return new List<LooseProductProto>();

            var products = new List<LooseProductProto>();
            foreach (LooseProductProto product in s_protosDb.All<LooseProductProto>())
            {
                if (product == LooseProductProto.Phantom)
                    continue;
                if (!product.TerrainMaterial.HasValue)
                    continue;
                if (!product.TerrainMaterial.Value.IsFarmable)
                    continue;
                products.Add(product);
            }

            return products
                .Distinct()
                .OrderBy(product => product.Id.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string FormatFarmableDumpProductList(List<LooseProductProto> products)
        {
            if (products.Count == 0)
                return "(none)";
            return string.Join(", ", products.Select(GetProductDisplayName).ToArray());
        }

        private static string GetProductDisplayName(LooseProductProto product)
        {
            try
            {
                return product.Strings.Name.TranslatedString;
            }
            catch
            {
                return product.Id.ToString();
            }
        }
    }
}
