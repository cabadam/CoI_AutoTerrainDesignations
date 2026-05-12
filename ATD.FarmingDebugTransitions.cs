// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
// Auto Terrain Designations - Farming Preparation Debug Transitions
using System.Collections.Generic;
using Mafi;
using Mafi.Core.Terrain.Designation;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        private sealed class FarmingDebugStoredDesignation
        {
            public DesignationData OriginalData { get; }
            public int TargetHeight { get; }

            public FarmingDebugStoredDesignation(DesignationData originalData, int targetHeight)
            {
                OriginalData = originalData;
                TargetHeight = targetHeight;
            }
        }

        private static readonly Dictionary<Tile2i, FarmingDebugStoredDesignation> s_farmingDebugStoredDesignations =
            new Dictionary<Tile2i, FarmingDebugStoredDesignation>();

        private enum FarmingPreparationShoulderSide
        {
            West,
            East,
            North,
            South
        }

        internal static string AnalyzeFarmingOriginForDebug(int x, int y)
        {
            if (!TryGetFarmingDebugContext(x, y, out Tile2i origin, out TerrainDesignation designation, out string error))
                return error;

            if (!TryGetFlatTargetHeight(designation.Data, out int targetHeight))
                return FormatFarmingDebugPrefix(origin) + "SkippedNonFlat: designation has mixed corner target heights.";

            FarmingAnalysisRow row = AnalyzeFarmingDesignation(origin, targetHeight, s_desigManager!.TerrainManager);
            return FormatFarmingDebugRow(row);
        }

        internal static string PrepareFarmingOriginForDebug(int x, int y)
        {
            if (!TryGetFarmingDebugContext(x, y, out Tile2i origin, out TerrainDesignation designation, out string error))
                return error;

            if (s_levelingProto == null)
                return "[ATD Farming] Leveling prototype is unavailable.";

            if (s_farmingDebugStoredDesignations.ContainsKey(origin))
                return FormatFarmingDebugPrefix(origin) + "This origin already has a stored Stage 2 preparation designation. Use atd_farming_restore_origin first.";

            if (!TryGetFlatTargetHeight(designation.Data, out int targetHeight))
                return FormatFarmingDebugPrefix(origin) + "SkippedNonFlat: designation has mixed corner target heights.";

            FarmingAnalysisRow row = AnalyzeFarmingDesignation(origin, targetHeight, s_desigManager!.TerrainManager);
            if (row.State != FarmingAnalysisState.NeedsPreparation)
                return FormatFarmingDebugPrefix(origin) + $"No preparation transition performed. Current state is {row.State}. {row.Detail}";

            if (!TryPlaceFarmingPreparationDesignation(origin, designation.Data, targetHeight))
                return FormatFarmingDebugPrefix(origin) + "Failed to place temporary preparation level designation.";

            return FormatFarmingDebugPrefix(origin) +
                $"Prepared: stored original target={targetHeight}, placed temporary LevelDesignator target={targetHeight - 1}.";
        }

        internal static string RestoreFarmingOriginForDebug(int x, int y)
        {
            if (s_desigManager == null)
                return "[ATD Farming] Terrain designation manager is unavailable.";

            if (s_levelingProto == null)
                return "[ATD Farming] Leveling prototype is unavailable.";

            Tile2i origin = TerrainDesignation.GetOrigin(new Tile2i(x, y));
            if (!s_farmingDebugStoredDesignations.TryGetValue(origin, out FarmingDebugStoredDesignation stored))
                return FormatFarmingDebugPrefix(origin) + "No stored Stage 2 original designation exists for this origin.";

            if (!s_desigManager.AddOrReplaceDesignation(s_levelingProto, stored.OriginalData))
                return FormatFarmingDebugPrefix(origin) + "Failed to restore original level designation. Stored data was kept.";

            s_farmingDebugStoredDesignations.Remove(origin);
            return FormatFarmingDebugPrefix(origin) + $"Restored original level designation target={stored.TargetHeight}.";
        }

        private static bool TryGetFarmingDebugContext(
            int x,
            int y,
            out Tile2i origin,
            out TerrainDesignation designation,
            out string error)
        {
            origin = TerrainDesignation.GetOrigin(new Tile2i(x, y));
            designation = default!;
            error = string.Empty;

            if (s_desigManager == null)
            {
                error = "[ATD Farming] Terrain designation manager is unavailable.";
                return false;
            }

            var designationAtOrigin = s_desigManager.GetDesignationAt(origin);
            if (!designationAtOrigin.HasValue)
            {
                error = FormatFarmingDebugPrefix(origin) + "No terrain designation exists at this origin.";
                return false;
            }

            designation = designationAtOrigin.Value;
            if (!IsLevelingDesignation(designation))
            {
                error = FormatFarmingDebugPrefix(origin) + $"Designation is {designation.Prototype.Id}, not a LevelDesignator.";
                return false;
            }

            return true;
        }

        private static string FormatFarmingDebugRow(FarmingAnalysisRow row)
        {
            string target = row.TargetHeight.HasValue ? row.TargetHeight.Value.ToString() : "-";
            return FormatFarmingDebugPrefix(row.Origin) +
                $"{row.State}: target={target}, surface={row.MinSurfaceHeight:F2}..{row.MaxSurfaceHeight:F2}, " +
                $"farmableMin={row.MinFarmableThicknessInBand:F2}, nonFarmCells={row.NonFarmableCells}. {row.Detail}";
        }

        private static string FormatFarmingDebugPrefix(Tile2i origin)
        {
            return $"[ATD Farming] ({origin.X},{origin.Y}) ";
        }

        private static bool TryPlaceFarmingPreparationDesignation(Tile2i origin, DesignationData originalData, int targetHeight)
        {
            return TryPlaceFarmingPreparationDesignation(
                origin,
                originalData,
                targetHeight,
                session: null,
                out _);
        }

        private static bool TryPlaceFarmingPreparationDesignation(
            Tile2i origin,
            DesignationData originalData,
            int targetHeight,
            FarmingPreparationSession? session,
            out int shoulderCount)
        {
            if (s_desigManager == null || s_levelingProto == null)
            {
                shoulderCount = 0;
                return false;
            }

            int preparationHeight = targetHeight - 1;
            DesignationData preparationData = BuildFlatLevelDesignationData(origin, preparationHeight);

            s_farmingDebugStoredDesignations[origin] = new FarmingDebugStoredDesignation(originalData, targetHeight);
            if (s_desigManager.AddOrReplaceDesignation(s_levelingProto, preparationData))
            {
                shoulderCount = PlaceFarmingPreparationShoulders(origin, preparationHeight, session);
                return true;
            }

            s_farmingDebugStoredDesignations.Remove(origin);
            shoulderCount = 0;
            return false;
        }

        private static int PlaceFarmingPreparationShoulders(
            Tile2i origin,
            int preparationHeight,
            FarmingPreparationSession? session)
        {
            if (session == null || s_desigManager == null || s_dumpingProto == null)
                return 0;

            int placed = 0;
            foreach (var shoulder in EnumerateNeededFarmingPreparationShoulders(origin, preparationHeight))
            {
                Tile2i shoulderOrigin = shoulder.Key;
                if (!IsFarmingDesignationOriginValid(s_desigManager.TerrainManager, shoulderOrigin))
                    continue;

                if (session.Origins.ContainsKey(shoulderOrigin))
                    continue;

                var existing = s_desigManager.GetDesignationAt(shoulderOrigin);
                bool isOwnedShoulder = session.PreparationShoulderOrigins.Contains(shoulderOrigin);
                if (existing.HasValue && !isOwnedShoulder)
                    continue;

                if (s_desigManager.AddOrReplaceDesignation(
                    s_dumpingProto,
                    BuildSlopedPreparationShoulderData(shoulderOrigin, preparationHeight, shoulder.Value)))
                {
                    placed++;
                    session.PreparationShoulderOrigins.Add(shoulderOrigin);
                }
            }

            return placed;
        }

        private static IEnumerable<KeyValuePair<Tile2i, FarmingPreparationShoulderSide>> EnumerateNeededFarmingPreparationShoulders(
            Tile2i origin,
            int preparationHeight)
        {
            if (s_desigManager == null)
                yield break;

            var terrMgr = s_desigManager.TerrainManager;
            if (IsAnyFarmingShoulderEdgeBelowPreparation(terrMgr, origin.X - 1, origin.Y, 0, 1, preparationHeight))
                yield return new KeyValuePair<Tile2i, FarmingPreparationShoulderSide>(
                    new Tile2i(origin.X - 4, origin.Y),
                    FarmingPreparationShoulderSide.West);
            if (IsAnyFarmingShoulderEdgeBelowPreparation(terrMgr, origin.X + 4, origin.Y, 0, 1, preparationHeight))
                yield return new KeyValuePair<Tile2i, FarmingPreparationShoulderSide>(
                    new Tile2i(origin.X + 4, origin.Y),
                    FarmingPreparationShoulderSide.East);
            if (IsAnyFarmingShoulderEdgeBelowPreparation(terrMgr, origin.X, origin.Y - 1, 1, 0, preparationHeight))
                yield return new KeyValuePair<Tile2i, FarmingPreparationShoulderSide>(
                    new Tile2i(origin.X, origin.Y - 4),
                    FarmingPreparationShoulderSide.North);
            if (IsAnyFarmingShoulderEdgeBelowPreparation(terrMgr, origin.X, origin.Y + 4, 1, 0, preparationHeight))
                yield return new KeyValuePair<Tile2i, FarmingPreparationShoulderSide>(
                    new Tile2i(origin.X, origin.Y + 4),
                    FarmingPreparationShoulderSide.South);
        }

        private static bool IsAnyFarmingShoulderEdgeBelowPreparation(
            Mafi.Core.Terrain.TerrainManager terrMgr,
            int startX,
            int startY,
            int stepX,
            int stepY,
            int preparationHeight)
        {
            for (int i = 0; i < 4; i++)
            {
                var tile = new Tile2i(startX + stepX * i, startY + stepY * i);
                if (!terrMgr.IsValidCoord(tile))
                    continue;

                if (terrMgr.GetHeight(tile).Value.ToFloat() < preparationHeight - FARMING_HEIGHT_EPSILON)
                    return true;
            }

            return false;
        }

        private static bool IsFarmingDesignationOriginValid(
            Mafi.Core.Terrain.TerrainManager terrMgr,
            Tile2i origin)
        {
            return terrMgr.IsValidCoord(origin)
                && terrMgr.IsValidCoord(new Tile2i(origin.X + 3, origin.Y + 3));
        }

        private static void RemoveOwnedFarmingPreparationShoulders(FarmingPreparationSession session)
        {
            if (s_desigManager == null || session.PreparationShoulderOrigins.Count == 0)
                return;

            foreach (Tile2i origin in new List<Tile2i>(session.PreparationShoulderOrigins))
            {
                var current = s_desigManager.GetDesignationAt(origin);
                if (current.HasValue && s_dumpingProto != null && current.Value.Prototype == s_dumpingProto)
                    s_desigManager.RemoveDesignation(origin);
            }

            session.PreparationShoulderOrigins.Clear();
        }

        private static DesignationData BuildFlatLevelDesignationData(Tile2i origin, int targetHeight)
        {
            return new DesignationData(
                origin,
                new HeightTilesI(targetHeight),
                new HeightTilesI(targetHeight),
                new HeightTilesI(targetHeight),
                new HeightTilesI(targetHeight));
        }

        private static DesignationData BuildSlopedPreparationShoulderData(
            Tile2i origin,
            int innerHeight,
            FarmingPreparationShoulderSide side)
        {
            int outerHeight = innerHeight - 1;
            int nw = innerHeight;
            int ne = innerHeight;
            int se = innerHeight;
            int sw = innerHeight;

            switch (side)
            {
                case FarmingPreparationShoulderSide.West:
                    nw = outerHeight;
                    sw = outerHeight;
                    break;
                case FarmingPreparationShoulderSide.East:
                    ne = outerHeight;
                    se = outerHeight;
                    break;
                case FarmingPreparationShoulderSide.North:
                    nw = outerHeight;
                    ne = outerHeight;
                    break;
                case FarmingPreparationShoulderSide.South:
                    sw = outerHeight;
                    se = outerHeight;
                    break;
            }

            return new DesignationData(
                origin,
                new HeightTilesI(nw),
                new HeightTilesI(ne),
                new HeightTilesI(se),
                new HeightTilesI(sw));
        }
    }
}
