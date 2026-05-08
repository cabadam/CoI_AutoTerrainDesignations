// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
// Auto Terrain Designations - Mine Tower Inspector Patching
using System;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Core.Buildings.Towers;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using Mafi.Unity.Ui.Library;
using UnityEngine;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        public static void ApplyInspectorPatches(Harmony harmony)
        {
            try
            {
                LogDebug("[AutoDepth] ApplyInspectorPatches() called");

                var assembly = typeof(Mafi.Unity.Entities.EntityMb).Assembly;
                var inspectorType = assembly.GetType("Mafi.Unity.Ui.Inspectors.MineTowerInspector");
                if (inspectorType == null)
                {
                    Log.Warning("[AutoDepth] MineTowerInspector type not found");
                    return;
                }

                LogDebug("[AutoDepth] Found MineTowerInspector type");

                var ctors = inspectorType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                LogDebug($"[AutoDepth] Found {ctors.Length} constructors");

                if (ctors.Length > 0)
                {
                    harmony.Patch(ctors[0],
                        postfix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(InspectorCtorPostfix)));
                    LogDebug("[AutoDepth] Patched first constructor");
                }

                // Patch OnActivated() on MineTowerInspector (DeclaredOnly — safe, does not affect
                // other inspector types). Only resets the Ore Composition panel to its prompt;
                // no scan is triggered so there are no timing issues.
                try
                {
                    var onActivatedMethod = inspectorType.GetMethod("OnActivated",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (onActivatedMethod != null)
                        harmony.Patch(onActivatedMethod,
                            postfix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(InspectorActivatePostfix)));
                }
                catch (Exception ex2)
                {
                    Log.Warning($"[AutoDepth] EXCEPTION patching OnActivated: {ex2}");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"AutoDepth.ApplyInspectorPatches EXCEPTION: {ex}");
            }
        }

        public static void InspectorActivatePostfix(object __instance)
        {
            DesignationPanel.RefreshDisplays(__instance);
            OreCompositionPanel.ResetContent(__instance);
        }

        public static void InspectorCtorPostfix(object __instance)
        {
            try
            {
                if (!s_settingsLoadAttempted)
                    LoadSettingsFromJson();

                LogDebug("[AutoDepth] InspectorCtorPostfix called");

                var inspectorType = __instance.GetType();
                var baseType = inspectorType;
                PropertyInfo? entityProp = null;

                while (baseType != null)
                {
                    if (entityProp == null)
                        entityProp = baseType.GetProperty("Entity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    baseType = baseType.BaseType;
                }

                if (entityProp == null)
                {
                    Log.Warning("[AutoDepth] Entity property not found on inspector");
                    return;
                }

                var inspector = __instance;
                Func<IAreaManagingTower?> getTower = () => entityProp.GetValue(inspector) as IAreaManagingTower;

                var atdPanel = DesignationPanel.Build(getTower, inspector);

                FieldInfo? mainBodyField = null;
                var searchType = inspectorType;
                while (searchType != null && mainBodyField == null)
                {
                    mainBodyField = searchType.GetField("MainBody", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    searchType = searchType.BaseType;
                }

                if (mainBodyField != null)
                {
                    var mainBody = mainBodyField.GetValue(__instance) as Column;
                    if (mainBody != null)
                    {
                        mainBody.InsertAt(0, atdPanel);
                        OreCompositionPanel.Inject(mainBody, entityProp, inspector);
                        mainBody.Show();
                        LogDebug("[AutoDepth] ATD panel and Ore Composition panel inserted");
                    }
                    else
                    {
                        Log.Warning("[AutoDepth] MainBody field is not a Column");
                    }
                }
                else
                {
                    Log.Warning("[AutoDepth] MainBody field not found");
                }
            }
            catch (Exception ex) { Debug.Log($"AutoDepth InspectorCtorPostfix EXCEPTION: {ex}"); }
        }
    }
}
