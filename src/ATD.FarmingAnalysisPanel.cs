// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
// Auto Terrain Designations - Farming Preparation Analysis Panel
using System;
using System.Reflection;
using Mafi;
using Mafi.Core.Buildings.Towers;
using Mafi.Localization;
using Mafi.Unity.Ui.Library;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;

namespace AutoTerrainDesignations
{
    internal static class FarmingAnalysisPanel
    {
        private static readonly System.Collections.Generic.Dictionary<object, Action> s_resetContentCallbacks =
            new System.Collections.Generic.Dictionary<object, Action>();

        internal static void ResetContent(object inspectorInstance)
        {
            if (s_resetContentCallbacks.TryGetValue(inspectorInstance, out Action cb))
            {
                try { cb(); } catch { }
            }
        }

        internal static void Inject(Column mainBody, PropertyInfo entityProp, object inspector)
        {
            try
            {
                AutoDepthDesignation.EnsureFarmingAutomationDefaultEnabledForTower(
                    entityProp.GetValue(inspector) as IAreaManagingTower);
                var contentCol = new Column(2.pt());
                var automationToggle = new Toggle(standalone: true)
                    .Label(AtdLocalization.FarmingToggleLabel)
                    .ObserveValue(() =>
                    {
                        var tower = entityProp.GetValue(inspector) as IAreaManagingTower;
                        return AutoDepthDesignation.IsFarmingAutomationEnabledForTower(tower);
                    })
                    .OnValueChanged((Action<bool>)delegate(bool isOn)
                    {
                        var tower = entityProp.GetValue(inspector) as IAreaManagingTower;
                        AutoDepthDesignation.SetFarmingAutomationEnabledForTower(tower, isOn);
                    })
                    .Tooltip(AtdLocalization.FarmingToggleTip);

                contentCol.Add(automationToggle);

                var idleReleaseToggle = new Toggle(standalone: true)
                    .Label(AtdLocalization.FarmingIdleReleaseLabel)
                    .ObserveValue(() =>
                    {
                        var tower = entityProp.GetValue(inspector) as IAreaManagingTower;
                        if (tower == null) return AutoTerrainDesignationsMod.AutoReleaseVehiclesWhenIdle;
                        return AutoDepthDesignation.GetTowerAutoReleaseWhenIdle(tower);
                    })
                    .OnValueChanged((Action<bool>)delegate(bool isOn)
                    {
                        var tower = entityProp.GetValue(inspector) as IAreaManagingTower;
                        if (tower == null) return;
                        AutoDepthDesignation.SetTowerAutoReleaseWhenIdle(tower, isOn);
                    })
                    .Tooltip(AtdLocalization.FarmingIdleReleaseTip);

                contentCol.Add(idleReleaseToggle);

                var farmingInitTower = entityProp.GetValue(inspector) as IAreaManagingTower;
                var panel = new PanelWithHeader()
                    .Title(
                        AtdLocalization.FarmingTitle,
                        AtdLocalization.Tip(AtdLocalization.FarmingDescription));
                panel.Collapsed(farmingInitTower != null
                    ? AutoDepthDesignation.GetTowerFarmingPanelCollapsed(farmingInitTower)
                    : AutoTerrainDesignationsMod.FarmingPanelCollapsed);
                panel.Header.OnClick((Action)delegate
                {
                    panel.Collapsed(!panel.IsCollapsed);
                    var t = entityProp.GetValue(inspector) as IAreaManagingTower;
                    if (t != null) AutoDepthDesignation.SetTowerFarmingPanelCollapsed(t, panel.IsCollapsed);
                });

                s_resetContentCallbacks[inspector] = (Action)delegate
                {
                    var tower = entityProp.GetValue(inspector) as IAreaManagingTower;
                    AutoDepthDesignation.EnsureFarmingAutomationDefaultEnabledForTower(tower);
                    automationToggle.Value(AutoDepthDesignation.IsFarmingAutomationEnabledForTower(tower));
                    idleReleaseToggle.Value(tower == null
                        ? AutoTerrainDesignationsMod.AutoReleaseVehiclesWhenIdle
                        : AutoDepthDesignation.GetTowerAutoReleaseWhenIdle(tower));
                    if (tower != null)
                        panel.Collapsed(AutoDepthDesignation.GetTowerFarmingPanelCollapsed(tower));
                };

                panel.BodyAdd(contentCol);
                mainBody.InsertAt(2, panel);
            }
            catch (Exception ex)
            {
                Log.Warning($"[ATD] FarmingAnalysisPanel.Inject EXCEPTION: {ex}");
            }
        }

    }
}
