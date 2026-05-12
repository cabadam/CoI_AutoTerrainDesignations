// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
using System;
using HarmonyLib;
using Mafi;
using Mafi.Core.Buildings.VehicleDepots;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Vehicles.Excavators;

namespace AutoTerrainDesignations;

public static partial class AutoDepthDesignation
{
    internal static void ApplyVehicleDepotPatches(Harmony harmony)
    {
        try
        {
            var tryBuildVehicle = AccessTools.Method(
                typeof(VehicleDepotBase),
                "TryBuildVehicle");
            var postfix = AccessTools.Method(
                typeof(AutoDepthDesignation),
                nameof(OnVehicleDepotTryBuildVehiclePostfix));

            if (tryBuildVehicle == null || postfix == null)
            {
                Log.Warning("[ATD] Vehicle depot excavator notification patch target not found.");
                return;
            }

            harmony.Patch(tryBuildVehicle, postfix: new HarmonyMethod(postfix));
        }
        catch (Exception ex)
        {
            Log.Warning("[ATD] Failed to patch vehicle depot excavator notifications: " + ex.Message);
        }
    }

    private static void OnVehicleDepotTryBuildVehiclePostfix(VehicleDepotBase __instance, bool __result, ref Vehicle vehicle)
    {
        try
        {
            if (!__result)
                return;

            if (!AutoTerrainDesignationsMod.ExcavatorCompletionNotificationsEnabled)
                return;

            if (!(vehicle is Excavator))
                return;

            if (!(__instance is IObjectWithTitle objectWithTitle))
                return;

            AddExcavatorCompletedNotification(objectWithTitle);
        }
        catch (Exception ex)
        {
            Log.Warning("[ATD] Vehicle depot excavator notification failed: " + ex.Message);
        }
    }
}
