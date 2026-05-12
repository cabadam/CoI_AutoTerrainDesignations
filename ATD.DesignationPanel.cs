// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
// Auto Terrain Designations - Terrain Designation Panel
using System;
using System.Collections.Generic;
using System.Linq;
using Mafi;
using Mafi.Core.Buildings.Towers;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.Terrain;
using Mafi.Localization;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using Mafi.Unity.Ui.Library;
using Row = Mafi.Unity.UiToolkit.Library.Row;
using UnityEngine;

namespace AutoTerrainDesignations
{
    /// <summary>
    /// Builds the "Terrain Designations" inspector panel independently of any specific
    /// inspector type. Call <see cref="Build"/> and insert the returned panel wherever
    /// needed. Can be used by external mods via <see cref="AutoTerrainDesignationsApi"/>.
    /// </summary>
    internal static class DesignationPanel
    {
        private static ProtosDb? s_protosDb;
        private static readonly string s_debrisIconPath = Mafi.Unity.Assets.Unity.UserInterface.Toolbar.Sweep_svg;

        private sealed class Bindings
        {
            public Func<IAreaManagingTower?> GetTower { get; }
            public Mafi.Unity.Ui.Library.Display RampWidthDisplay { get; }
            public Mafi.Unity.Ui.Library.Display MaxLayersDisplay { get; }
            public Mafi.Unity.Ui.Library.Display MinElevDisplay { get; }
            public Mafi.Unity.Ui.Library.Display OrePurityDisplay { get; }
            public Mafi.Unity.Ui.Library.Display ClearanceDisplay { get; }
            public Icon RampWarningIcon { get; }

            public Bindings(
                Func<IAreaManagingTower?> getTower,
                Mafi.Unity.Ui.Library.Display rampWidthDisplay,
                Mafi.Unity.Ui.Library.Display maxLayersDisplay,
                Mafi.Unity.Ui.Library.Display minElevDisplay,
                Mafi.Unity.Ui.Library.Display orePurityDisplay,
                Mafi.Unity.Ui.Library.Display clearanceDisplay,
                Icon rampWarningIcon)
            {
                GetTower = getTower;
                RampWidthDisplay = rampWidthDisplay;
                MaxLayersDisplay = maxLayersDisplay;
                MinElevDisplay = minElevDisplay;
                OrePurityDisplay = orePurityDisplay;
                ClearanceDisplay = clearanceDisplay;
                RampWarningIcon = rampWarningIcon;
            }
        }

        private static readonly Dictionary<object, Bindings> s_bindings =
            new Dictionary<object, Bindings>();

        internal static bool HasBindings(object key) => s_bindings.ContainsKey(key);

        internal static void ClearRampWarning(object key)
        {
            if (s_bindings.TryGetValue(key, out var b))
                b.RampWarningIcon.SetVisible(false);
        }

        internal static void Initialize(ProtosDb? protosDb)
        {
            s_protosDb = protosDb;
        }

        /// <summary>
        /// Refreshes the display values of a previously built panel.
        /// Call this when the inspector switches to a different tower.
        /// </summary>
        internal static void RefreshDisplays(object key)
        {
            if (!s_bindings.TryGetValue(key, out var b)) return;
            var tower = b.GetTower();
            if (tower == null) return;
            b.RampWidthDisplay.SetValue(new LocStrFormatted(RampWidthText(AutoDepthDesignation.GetTowerRampWidth(tower))));
            b.MaxLayersDisplay.SetValue(new LocStrFormatted(MaxLayersText(AutoDepthDesignation.GetTowerMaxLayersToExcavate(tower))));
            b.MinElevDisplay.SetValue(new LocStrFormatted(MinElevText(AutoDepthDesignation.GetTowerMaxDepthToDigTo(tower))));
            b.OrePurityDisplay.SetValue(new LocStrFormatted(OrePurityLevelText(AutoDepthDesignation.GetTowerOrePurityLevel(tower))));
            b.ClearanceDisplay.SetValue(new LocStrFormatted(ClearanceLevelText(AutoDepthDesignation.GetTowerCorridorClearance(tower))));
            RefreshRampWarning(b, tower);
        }

        /// <summary>
        /// Builds the full "Terrain Designations" panel and returns it. Insert the result
        /// at any position in any inspector's <c>Column</c>.
        /// </summary>
        /// <param name="getTower">
        /// Delegate that returns the currently active tower, called lazily inside button
        /// handlers and display refresh. May return null between inspector activations.
        /// </param>
        /// <param name="key">
        /// Opaque key (typically the inspector instance) used to route
        /// <see cref="RefreshDisplays"/> calls back to this panel. Pass the same key to
        /// <see cref="AutoDepthDesignation.CreateDesignationsForTower(IAreaManagingTower, object?)"/>
        /// so the Ore Composition panel auto-refreshes after a scan.
        /// </param>
        internal static PanelWithHeader Build(Func<IAreaManagingTower?> getTower, object key)
        {
            var initialTower = getTower();

            // --- Ore picker ---
            SingleProductPickerUi? orePicker = null;
            if (s_protosDb != null)
            {
                try
                {
                    var minableProducts = s_protosDb.All<TerrainMaterialProto>()
                        .Select(m => (ProductProto)m.MinedProduct)
                        .Distinct()
                        .OrderBy(product => AutoDepthDesignation.GetProductPickerSortRank(product))
                        .ThenBy(product => product.Id.ToString(), StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    orePicker = new SingleProductPickerUi(
                        () => minableProducts,
                        delegate(ProductProto selected)
                        {
                            var tower = getTower();
                            if (tower == null) return;
                            if (selected is LooseProductProto loose)
                            {
                                AutoDepthDesignation.SetSelectedOre(tower, loose);
                            }
                        },
                        () =>
                        {
                            var tower = getTower();
                            if (tower == null) return Option.None;
                            var sel = AutoDepthDesignation.GetSelectedOre(tower);
                            return sel != null ? Option.Some((ProductProto)sel) : Option.None;
                        },
                        delegate
                        {
                            var tower = getTower();
                            if (tower == null) return;
                            AutoDepthDesignation.SetSelectedOre(tower, null);
                        },
                        new LocStrFormatted(AtdLocalization.Tr(
                            "panel.designations.ore_filter.auto",
                            "Auto (useful -> debris -> dirt)")),
                        compact: true,
                        primaryButtonIfNoProtoSet: false
                    );
                }
                catch (Exception ex)
                {
                    Log.Warning($"[ATD] EXCEPTION creating ore picker in DesignationPanel: {ex}");
                }
            }

            // --- Ramp warning icon (created before buttons so closures can capture it) ---
            var rampWarningIcon = new Icon()
                .Value("Assets/Unity/UserInterface/General/Warning128.png", new ColorRgba(255, 200, 0))
                .Size(20.px(), 20.px())
                .AlignSelfCenter();
            rampWarningIcon.SetVisible(false);

            // --- Dig button ---
            var digBtn = new ButtonIconText(
                Button.Primary,
                "Assets/Unity/UserInterface/EntityIcons/Designation.png",
                new LocStrFormatted(AtdLocalization.Tr(
                    "panel.designations.create_button",
                    "Create Designations")))
                .OnClick((Action)delegate
                {
                    try
                    {
                        var tower = getTower();
                        if (tower == null) return;
                        rampWarningIcon.SetVisible(false);
                        AutoDepthDesignation.CreateDesignationsForTower(tower, key);
                    }
                    catch (Exception ex) { Debug.Log($"[ATD] Dig button click EXCEPTION: {ex}"); }
                });
            digBtn.Tooltip(new LocStrFormatted(AtdLocalization.Tt(
                "panel.designations.create_tooltip",
                "Scan and place mining designations in this tower's area.")));
            digBtn.Icon.Size(Px.Auto, 24.px());

            var debrisBtn = new ButtonIcon(
                Button.General,
                s_debrisIconPath,
                (Action)delegate
                {
                    try
                    {
                        var tower = getTower();
                        if (tower == null) return;
                        rampWarningIcon.SetVisible(false);
                        AutoDepthDesignation.ClearTowerLastRampOutcome(tower);
                        AutoDepthDesignation.MarkDebrisForRemovalForTower(tower);
                    }
                    catch (Exception ex) { Debug.Log($"[ATD] Debris button click EXCEPTION: {ex}"); }
                })
                .Tooltip(new LocStrFormatted(AtdLocalization.Tt(
                    "panel.designations.debris_tooltip",
                    "Designate all debris in the area for mining/removal. Overrides any forestry designations.")));

            // --- Clear button ---
            var clearBtn = new ButtonIcon(
                Button.General,
                "Assets/Unity/UserInterface/General/Trash128.png",
                (Action)delegate
                {
                    try
                    {
                        var tower = getTower();
                        if (tower == null) return;
                        rampWarningIcon.SetVisible(false);
                        AutoDepthDesignation.ClearTowerLastRampOutcome(tower);
                        AutoDepthDesignation.ClearDesignationsForTower(tower);
                    }
                    catch (Exception ex) { Debug.Log($"[ATD] Clear button click EXCEPTION: {ex}"); }
                })
                .Tooltip(new LocStrFormatted(AtdLocalization.Tt(
                    "panel.designations.clear_tooltip",
                    "Clear all mining designations in this tower's area.")));

            digBtn.MarginTopBottom(1.pt());
            debrisBtn.MarginTopBottom(1.pt()).AlignSelfEnd();
            clearBtn.MarginTopBottom(1.pt()).AlignSelfEnd();

            var contentRow = new Row().Gap(3.pt()).AlignItemsEnd();
            contentRow.Add(new UiComponent().FlexGrow(1f));
            contentRow.Add(digBtn);
            contentRow.Add(rampWarningIcon);
            contentRow.Add(new UiComponent().FlexGrow(1f));
            contentRow.Add(clearBtn);
            contentRow.Add(debrisBtn);

            var panel = new PanelWithHeader()
                .Title(new LocStrFormatted(AtdLocalization.Tr(
                           "panel.designations.title",
                           "Terrain Designations")),
                       new LocStrFormatted(AtdLocalization.Tt(
                           "panel.designations.description",
                           "Create automatic terrain designations for this tower.")));
            panel.Collapsed(AutoTerrainDesignationsMod.TerrainDesignationsPanelCollapsed);
            panel.BodyAdd(contentRow);

            // --- Ramp width ---
            int initRamp = initialTower != null
                ? AutoDepthDesignation.GetTowerRampWidth(initialTower)
                : AutoTerrainDesignationsMod.RampWidth;
            var rampWidthDisplay = new Mafi.Unity.Ui.Library.Display(new LocStrFormatted(RampWidthText(initRamp)))
                .MinDigits(3).AlignSelfStretch().MarginTopBottom(2.px());
            panel.BodyAdd(BuildStepRow(
                new LocStrFormatted(AtdLocalization.Tr("panel.designations.ramp_width.label", "Ramp width")),
                new LocStrFormatted(AtdLocalization.Tr(
                    "panel.designations.ramp_width.tooltip",
                    "Width of generated access ramps (0 = disable ramp).")),
                rampWidthDisplay,
                (Action)delegate
                {
                    var tower = getTower(); if (tower == null) return;
                    AutoDepthDesignation.SetTowerRampWidth(tower, AutoDepthDesignation.GetTowerRampWidth(tower) + ModifierStepSize());
                    rampWidthDisplay.SetValue(new LocStrFormatted(RampWidthText(AutoDepthDesignation.GetTowerRampWidth(tower))));
                },
                (Action)delegate
                {
                    var tower = getTower(); if (tower == null) return;
                    AutoDepthDesignation.SetTowerRampWidth(tower, AutoDepthDesignation.GetTowerRampWidth(tower) - ModifierStepSize());
                    rampWidthDisplay.SetValue(new LocStrFormatted(RampWidthText(AutoDepthDesignation.GetTowerRampWidth(tower))));
                }));

            // --- Max layers ---
            int initLayers = initialTower != null
                ? AutoDepthDesignation.GetTowerMaxLayersToExcavate(initialTower)
                : AutoTerrainDesignationsMod.MaxLayersToExcavate;
            var maxLayersDisplay = new Mafi.Unity.Ui.Library.Display(new LocStrFormatted(MaxLayersText(initLayers)))
                .MinDigits(3).AlignSelfStretch().MarginTopBottom(2.px());
            panel.BodyAdd(BuildStepRow(
                new LocStrFormatted(AtdLocalization.Tr("panel.designations.max_layers.label", "Max layers to excavate")),
                new LocStrFormatted(AtdLocalization.Tr(
                    "panel.designations.max_layers.tooltip",
                    "Maximum layers to excavate from the surface. (\u221e = no limit.)")),
                maxLayersDisplay,
                (Action)delegate
                {
                    var tower = getTower(); if (tower == null) return;
                    int cur = AutoDepthDesignation.GetTowerMaxLayersToExcavate(tower);
                    int step = ModifierStepSize();
                    if (cur != 0)
                        AutoDepthDesignation.SetTowerMaxLayersToExcavate(tower, cur + step > 50 ? 0 : cur + step);
                    maxLayersDisplay.SetValue(new LocStrFormatted(MaxLayersText(AutoDepthDesignation.GetTowerMaxLayersToExcavate(tower))));
                },
                (Action)delegate
                {
                    var tower = getTower(); if (tower == null) return;
                    int cur = AutoDepthDesignation.GetTowerMaxLayersToExcavate(tower);
                    AutoDepthDesignation.SetTowerMaxLayersToExcavate(tower, cur == 0 ? 50 : Math.Max(1, cur - ModifierStepSize()));
                    maxLayersDisplay.SetValue(new LocStrFormatted(MaxLayersText(AutoDepthDesignation.GetTowerMaxLayersToExcavate(tower))));
                }));

            // --- Elevation limit ---
            int? initElev = initialTower != null
                ? AutoDepthDesignation.GetTowerMaxDepthToDigTo(initialTower)
                : AutoTerrainDesignationsMod.MaxDepthToDigTo;
            var minElevDisplay = new Mafi.Unity.Ui.Library.Display(new LocStrFormatted(MinElevText(initElev)))
                .MinDigits(3).AlignSelfStretch().MarginTopBottom(2.px());
            panel.BodyAdd(BuildStepRow(
                new LocStrFormatted(AtdLocalization.Tr("panel.designations.elevation_limit.label", "Elevation limit")),
                new LocStrFormatted(AtdLocalization.Tr(
                    "panel.designations.elevation_limit.tooltip",
                    "Maximum (absolute) excavation depth (-\u221e = no limit.)")),
                minElevDisplay,
                (Action)delegate
                {
                    var tower = getTower(); if (tower == null) return;
                    int? cur = AutoDepthDesignation.GetTowerMaxDepthToDigTo(tower);
                    AutoDepthDesignation.SetTowerMaxDepthToDigTo(tower, cur == null ? -50 : cur.Value + ModifierStepSize());
                    minElevDisplay.SetValue(new LocStrFormatted(MinElevText(AutoDepthDesignation.GetTowerMaxDepthToDigTo(tower))));
                },
                (Action)delegate
                {
                    var tower = getTower(); if (tower == null) return;
                    int? cur = AutoDepthDesignation.GetTowerMaxDepthToDigTo(tower);
                    if (cur != null)
                    {
                        int next = cur.Value - ModifierStepSize();
                        AutoDepthDesignation.SetTowerMaxDepthToDigTo(tower, next < -50 ? (int?)null : next);
                    }
                    minElevDisplay.SetValue(new LocStrFormatted(MinElevText(AutoDepthDesignation.GetTowerMaxDepthToDigTo(tower))));
                }));

            // --- Ore purity ---
            int initPurity = initialTower != null
                ? AutoDepthDesignation.GetTowerOrePurityLevel(initialTower)
                : AutoTerrainDesignationsMod.OrePurityLevel;
            var orePurityDisplay = new Mafi.Unity.Ui.Library.Display(new LocStrFormatted(OrePurityLevelText(initPurity)))
                .MinDigits(3).AlignSelfStretch().MarginTopBottom(2.px());
            panel.BodyAdd(BuildStepRow(
                new LocStrFormatted(AtdLocalization.Tr("panel.designations.ore_purity.label", "Ore purity")),
                new LocStrFormatted(
                    AtdLocalization.Tr(
                        "panel.designations.ore_purity.tooltip",
                        "How strictly the scan filters for ore quality.\n" +
                        "Off: include all tiles, dig to full depth.\n" +
                        "Low: exclude very sparse tiles, trim thin trailing ore at the bottom.\n" +
                        "Med: moderate quality — skip tiles with heavy overburden or little ore.\n" +
                        "High: only rich tiles with a clean ore column.\n" +
                        "Max: near-pure ore only — strict on overburden, depth and ore density.")),
                orePurityDisplay,
                (Action)delegate
                {
                    var tower = getTower(); if (tower == null) return;
                    AutoDepthDesignation.SetTowerOrePurityLevel(tower, AutoDepthDesignation.GetTowerOrePurityLevel(tower) + 1);
                    orePurityDisplay.SetValue(new LocStrFormatted(OrePurityLevelText(AutoDepthDesignation.GetTowerOrePurityLevel(tower))));
                },
                (Action)delegate
                {
                    var tower = getTower(); if (tower == null) return;
                    AutoDepthDesignation.SetTowerOrePurityLevel(tower, AutoDepthDesignation.GetTowerOrePurityLevel(tower) - 1);
                    orePurityDisplay.SetValue(new LocStrFormatted(OrePurityLevelText(AutoDepthDesignation.GetTowerOrePurityLevel(tower))));
                }));

            // --- Corridor clearance ---
            int initClearance = initialTower != null
                ? AutoDepthDesignation.GetTowerCorridorClearance(initialTower)
                : AutoTerrainDesignationsMod.MinCorridorClearance;
            var clearanceDisplay = new Mafi.Unity.Ui.Library.Display(new LocStrFormatted(ClearanceLevelText(initClearance)))
                .MinDigits(3).AlignSelfStretch().MarginTopBottom(2.px());
            panel.BodyAdd(BuildStepRow(
                new LocStrFormatted(AtdLocalization.Tr("panel.designations.corridor_clearance.label", "Corridor clearance")),
                new LocStrFormatted(
                    AtdLocalization.Tr(
                        "panel.designations.corridor_clearance.tooltip",
                        "Minimum corridor width for connecting ore regions and enforcing passability.\n" +
                        "0 = disabled (regions left separate, no corridors or hole-filling).\n" +
                        "1 = 1-tile corridors (small and medium vehicles).\n" +
                        "2 = 2-tile corridors (mega vehicles).\n")),
                clearanceDisplay,
                (Action)delegate
                {
                    var tower = getTower(); if (tower == null) return;
                    AutoDepthDesignation.SetTowerCorridorClearance(tower, AutoDepthDesignation.GetTowerCorridorClearance(tower) + ModifierStepSize());
                    clearanceDisplay.SetValue(new LocStrFormatted(ClearanceLevelText(AutoDepthDesignation.GetTowerCorridorClearance(tower))));
                },
                (Action)delegate
                {
                    var tower = getTower(); if (tower == null) return;
                    AutoDepthDesignation.SetTowerCorridorClearance(tower, AutoDepthDesignation.GetTowerCorridorClearance(tower) - ModifierStepSize());
                    clearanceDisplay.SetValue(new LocStrFormatted(ClearanceLevelText(AutoDepthDesignation.GetTowerCorridorClearance(tower))));
                }));

            // --- Ore picker row ---
            if (orePicker != null)
            {
                var oreRow = new Row().MarginTop(1.pt());
                oreRow.Add(new Label(new LocStrFormatted(AtdLocalization.Tr(
                        "panel.designations.scanning_filter.label",
                        "Scanning filter:")))
                    .Tooltip(new LocStrFormatted(AtdLocalization.Tr(
                        "panel.designations.scanning_filter.tooltip",
                        "Force the scan to target a specific product. None = useful products first, then debris, then dirt."))));
                oreRow.Add(new UiComponent().FlexGrow(1f));
                oreRow.Add(orePicker);
                panel.BodyAdd(oreRow);
            }

            s_bindings[key] = new Bindings(getTower, rampWidthDisplay, maxLayersDisplay, minElevDisplay, orePurityDisplay, clearanceDisplay, rampWarningIcon);
            return panel;
        }

        private static void RefreshRampWarning(Bindings b, IAreaManagingTower tower)
        {
            AutoDepthDesignation.RampPlacementOutcome? outcome = AutoDepthDesignation.GetTowerLastRampOutcome(tower);
            bool hasWarning = outcome == AutoDepthDesignation.RampPlacementOutcome.Failed
                || outcome == AutoDepthDesignation.RampPlacementOutcome.Truncated
                || outcome == AutoDepthDesignation.RampPlacementOutcome.NotAccessible;
            b.RampWarningIcon.SetVisible(hasWarning);
            if (hasWarning)
            {
                string msg = outcome == AutoDepthDesignation.RampPlacementOutcome.Failed
                    ? AtdLocalization.Tr(
                        "panel.designations.ramp_warning.failed",
                        "Ramp generation failed \u2014 no valid path found.")
                    : outcome == AutoDepthDesignation.RampPlacementOutcome.Truncated
                        ? AtdLocalization.Tr(
                            "panel.designations.ramp_warning.truncated",
                            "Ramp placed but did not reach the surface \u2014 excavators may not be able to excavate.")
                        : AtdLocalization.Tr(
                            "panel.designations.ramp_warning.not_accessible",
                            "Couldn't find a valid path from the tower to the generated ramp. Check for access problems.");
                b.RampWarningIcon.Tooltip(new LocStrFormatted(msg));
            }
        }

        private static Row BuildStepRow(
            LocStrFormatted label,
            LocStrFormatted tooltip,
            Mafi.Unity.Ui.Library.Display display,
            Action onPlus,
            Action onMinus)
        {
            var plusBtn = new ButtonIcon(Button.General, "Assets/Unity/UserInterface/General/Plus128.png")
                .Compact().IconSize(14.px()).OnClick(onPlus, allowKeyPresses: true);
            var minusBtn = new ButtonIcon(Button.General, "Assets/Unity/UserInterface/General/Minus128.png")
                .Compact().IconSize(14.px()).OnClick(onMinus, allowKeyPresses: true);
            var row = new Row().MarginTop(1.pt());
            row.Add(new Label(label).Tooltip(tooltip));
            row.Add(new UiComponent().FlexGrow(1f));
            row.Add(minusBtn);
            row.Add(display);
            row.Add(plusBtn);
            return row;
        }

        private static int ModifierStepSize()
        {
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) return 10;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return 5;
            return 1;
        }

        private static string RampWidthText(int value) => value.ToString();

        private static string MaxLayersText(int value) => value == 0 ? "\u221e" : value.ToString();

        private static string MinElevText(int? value)
        {
            if (value == null) return "-\u221e";
            return value.Value > 0 ? "+" + value.Value : value.Value.ToString();
        }

        private static string OrePurityLevelText(int value)
        {
            switch (value)
            {
                case 0: return AtdLocalization.Tr("common.level.off", "Off");
                case 1: return AtdLocalization.Tr("common.level.low", "Low");
                case 2: return AtdLocalization.Tr("common.level.med", "Med");
                case 3: return AtdLocalization.Tr("common.level.high", "High");
                case 4: return AtdLocalization.Tr("common.level.max", "Max");
                default: return value.ToString();
            }
        }

        private static string ClearanceLevelText(int value)
        {
            switch (value)
            {
                case 0: return AtdLocalization.Tr("common.level.off", "Off");
                case 1: return "1";
                case 2: return "2";
                default: return value.ToString();
            }
        }
    }
}
