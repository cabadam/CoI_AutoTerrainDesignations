// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
// Auto Terrain Designations - Farming Fill Activation
using System.Collections.Generic;
using System.Linq;
using Mafi;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        private static bool HasQueuedFarmingFillingOrigins(FarmingPreparationSession session)
        {
            return session.Origins.Values.Any(IsQueuedForFarmingFilling);
        }

        private static bool IsQueuedForFarmingFilling(FarmingOriginSession origin)
        {
            return origin.Phase == FarmingOriginPhase.ReadyForFilling
                || (origin.Phase == FarmingOriginPhase.Done && !origin.IsFillingActivated);
        }

        private static void ClearFarmingFillingActivation(FarmingPreparationSession session)
        {
            session.LastFillingActivationDetail = string.Empty;
            foreach (FarmingOriginSession origin in session.Origins.Values)
                origin.IsFillingActivated = false;
        }

        private static int ActivateFarmingFillingOrigins(
            FarmingPreparationSession session,
            out int failed)
        {
            failed = 0;
            if (s_desigManager == null || s_levelingProto == null)
                return 0;

            List<FarmingOriginSession> queued = session.Origins.Values
                .Where(IsQueuedForFarmingFilling)
                .OrderBy(origin => origin.Origin.Y)
                .ThenBy(origin => origin.Origin.X)
                .ToList();
            if (queued.Count == 0)
            {
                session.LastFillingActivationDetail = "Filling activation: no queued origins remain.";
                return 0;
            }

            int activated = 0;
            RemoveOwnedFarmingPreparationShoulders(session);
            foreach (FarmingOriginSession originState in queued)
            {
                if (s_desigManager.AddOrReplaceDesignation(s_levelingProto, originState.OriginalData))
                {
                    activated++;
                    originState.IsHiddenUntilFilling = false;
                    originState.IsFillingActivated = true;
                    originState.Phase = FarmingOriginPhase.Filling;
                    originState.Detail = "activated final fill designation";
                    s_farmingDebugStoredDesignations.Remove(originState.Origin);
                }
                else
                {
                    failed++;
                    originState.Phase = FarmingOriginPhase.Blocked;
                    originState.Detail = "failed to restore original level designation for filling";
                }
            }

            session.LastFillingActivationDetail =
                $"Filling activation: activated queued fill origins={activated}, failed={failed}.";
            return activated;
        }
    }
}
