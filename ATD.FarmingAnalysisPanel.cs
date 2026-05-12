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
                    .Label(new LocStrFormatted(AtdLocalization.Tr(
                        "panel.farming.automation_toggle.label",
                        "Farmland Preparation Automation")))
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
                    .Tooltip(new LocStrFormatted(AtdLocalization.Tr(
                        "panel.farming.automation_toggle.tooltip",
                        "Prepare flat level designations for farmland by clearing unsuitable top material, then restoring the final fill orders.")));

                contentCol.Add(automationToggle);

                s_resetContentCallbacks[inspector] = (Action)delegate
                {
                    AutoDepthDesignation.EnsureFarmingAutomationDefaultEnabledForTower(
                        entityProp.GetValue(inspector) as IAreaManagingTower);
                    automationToggle.Value(AutoDepthDesignation.IsFarmingAutomationEnabledForTower(
                        entityProp.GetValue(inspector) as IAreaManagingTower));
                };

                var panel = new PanelWithHeader()
                    .Title(
                        new LocStrFormatted(AtdLocalization.Tr(
                            "panel.farming.title",
                            "Farmland Preparation")),
                        new LocStrFormatted(AtdLocalization.Tt(
                            "panel.farming.description",
                            "Automates the preparation and final filling of flat level designations so their top layer becomes farmable.")));
                panel.Collapsed(false);

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
