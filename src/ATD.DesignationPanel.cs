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

            /// <summary>Display showing the selected automatic designation workflow.</summary>
            public Mafi.Unity.Ui.Library.Display ModeDisplay { get; }

            /// <summary>Display showing the designation type used by flattening mode.</summary>
            public Mafi.Unity.Ui.Library.Display FlatteningDesignationTypeDisplay { get; }
            public Mafi.Unity.Ui.Library.Display RampWidthDisplay { get; }
            public Mafi.Unity.Ui.Library.Display MaxLayersDisplay { get; }
            public Mafi.Unity.Ui.Library.Display MinElevDisplay { get; }
            public Mafi.Unity.Ui.Library.Display OrePurityDisplay { get; }
            public Mafi.Unity.Ui.Library.Display ClearanceDisplay { get; }
            public Icon RampWarningIcon { get; }

            /// <summary>Row containing the flattening-mode designation type controls.</summary>
            public Row FlatteningDesignationTypeRow { get; }

            /// <summary>Row containing ramp width controls, disabled during flattening mode.</summary>
            public Row RampWidthRow { get; }

            /// <summary>Row containing max layer controls, disabled during flattening mode.</summary>
            public Row MaxLayersRow { get; }

            /// <summary>Row containing ore purity controls, disabled during flattening mode.</summary>
            public Row OrePurityRow { get; }

            /// <summary>Row containing corridor clearance controls, disabled during flattening mode.</summary>
            public Row ClearanceRow { get; }

            /// <summary>Optional row containing the product scanning filter, disabled during flattening mode.</summary>
            public Row? OrePickerRow { get; }

            /// <summary>Creates panel binding references used by refresh and mode-dependent UI state.</summary>
            /// <param name="getTower">Delegate that returns the currently active tower.</param>
            /// <param name="modeDisplay">Display showing the selected automatic designation workflow.</param>
            /// <param name="flatteningDesignationTypeDisplay">Display showing the designation type used by flattening mode.</param>
            /// <param name="rampWidthDisplay">Display showing the configured ramp width.</param>
            /// <param name="maxLayersDisplay">Display showing the configured maximum excavation layers.</param>
            /// <param name="minElevDisplay">Display showing the elevation limit or flattening target.</param>
            /// <param name="orePurityDisplay">Display showing the configured ore purity threshold.</param>
            /// <param name="clearanceDisplay">Display showing the configured corridor clearance.</param>
            /// <param name="flatteningDesignationTypeRow">Row containing the flattening-mode designation type controls.</param>
            /// <param name="rampWidthRow">Row containing ramp width controls.</param>
            /// <param name="maxLayersRow">Row containing max layer controls.</param>
            /// <param name="orePurityRow">Row containing ore purity controls.</param>
            /// <param name="clearanceRow">Row containing corridor clearance controls.</param>
            /// <param name="orePickerRow">Optional row containing the product scanning filter.</param>
            /// <param name="rampWarningIcon">Icon used to display ramp generation warnings.</param>
            public Bindings(
                Func<IAreaManagingTower?> getTower,
                Mafi.Unity.Ui.Library.Display modeDisplay,
                Mafi.Unity.Ui.Library.Display flatteningDesignationTypeDisplay,
                Mafi.Unity.Ui.Library.Display rampWidthDisplay,
                Mafi.Unity.Ui.Library.Display maxLayersDisplay,
                Mafi.Unity.Ui.Library.Display minElevDisplay,
                Mafi.Unity.Ui.Library.Display orePurityDisplay,
                Mafi.Unity.Ui.Library.Display clearanceDisplay,
                Row flatteningDesignationTypeRow,
                Row rampWidthRow,
                Row maxLayersRow,
                Row orePurityRow,
                Row clearanceRow,
                Row? orePickerRow,
                Icon rampWarningIcon)
            {
                GetTower = getTower;
                ModeDisplay = modeDisplay;
                FlatteningDesignationTypeDisplay = flatteningDesignationTypeDisplay;
                RampWidthDisplay = rampWidthDisplay;
                MaxLayersDisplay = maxLayersDisplay;
                MinElevDisplay = minElevDisplay;
                OrePurityDisplay = orePurityDisplay;
                ClearanceDisplay = clearanceDisplay;
                FlatteningDesignationTypeRow = flatteningDesignationTypeRow;
                RampWidthRow = rampWidthRow;
                MaxLayersRow = maxLayersRow;
                OrePurityRow = orePurityRow;
                ClearanceRow = clearanceRow;
                OrePickerRow = orePickerRow;
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
            DesignationMode designationMode = AutoDepthDesignation.GetTowerDesignationMode(tower);
            b.ModeDisplay.SetValue(new LocStrFormatted(DesignationModeText(designationMode)));
            b.FlatteningDesignationTypeDisplay.SetValue(new LocStrFormatted(FlatteningDesignationTypeText(AutoDepthDesignation.GetTowerFlatteningDesignationType(tower))));
            SetModeDependentRowsEnabled(b, designationMode);
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
                        AtdLocalization.DesigOreFilterAuto,
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
                AtdLocalization.DesigCreateBtn)
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
            digBtn.Tooltip(AtdLocalization.Tip(AtdLocalization.DesigCreateTip));
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
                .Tooltip(AtdLocalization.Tip(AtdLocalization.DesigDebrisTip));

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
                .Tooltip(AtdLocalization.Tip(AtdLocalization.DesigClearTip));

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
                .Title(AtdLocalization.DesigTitle,
                       AtdLocalization.Tip(AtdLocalization.DesigDescription));
            panel.Collapsed(AutoTerrainDesignationsMod.TerrainDesignationsPanelCollapsed);
            panel.BodyAdd(contentRow);

            // --- Designation mode ---
            DesignationMode initMode = initialTower != null
                ? AutoDepthDesignation.GetTowerDesignationMode(initialTower)
                : AutoTerrainDesignationsMod.DesignationMode;
            var modeDisplay = new Mafi.Unity.Ui.Library.Display(new LocStrFormatted(DesignationModeText(initMode)))
                .MinDigits(3).AlignSelfStretch().MarginTopBottom(2.px());

            // --- Flattening designation type ---
            FlatteningDesignationType initFlatteningDesignationType = initialTower != null
                ? AutoDepthDesignation.GetTowerFlatteningDesignationType(initialTower)
                : AutoTerrainDesignationsMod.FlatteningDesignationType;
            var flatteningDesignationTypeDisplay = new Mafi.Unity.Ui.Library.Display(new LocStrFormatted(FlatteningDesignationTypeText(initFlatteningDesignationType)))
                .MinDigits(3).AlignSelfStretch().MarginTopBottom(2.px());
            Row? rampWidthRow = null;
            Row? maxLayersRow = null;
            Row? orePurityRow = null;
            Row? clearanceRow = null;
            Row? oreRow = null;
            Row? flatteningDesignationTypeRow = null;

            Row modeRow = BuildStepRow(
                new LocStrFormatted("Mode"),
                new LocStrFormatted(
                    "Resource Mining: scan resources and place mining designations.\n" +
                    "Flattening: ignore resources and set the whole tower area to the configured elevation."),
                modeDisplay,
                (Action)delegate
                {
                    var tower = getTower(); if (tower == null) return;
                    AutoDepthDesignation.SetTowerDesignationMode(tower, NextDesignationMode(AutoDepthDesignation.GetTowerDesignationMode(tower)));
                    DesignationMode mode = AutoDepthDesignation.GetTowerDesignationMode(tower);
                    modeDisplay.SetValue(new LocStrFormatted(DesignationModeText(mode)));
                    SetModeDependentRowsEnabled(
                        flatteningDesignationTypeRow,
                        rampWidthRow,
                        maxLayersRow,
                        orePurityRow,
                        clearanceRow,
                        oreRow,
                        mode);
                },
                (Action)delegate
                {
                    var tower = getTower(); if (tower == null) return;
                    AutoDepthDesignation.SetTowerDesignationMode(tower, PreviousDesignationMode(AutoDepthDesignation.GetTowerDesignationMode(tower)));
                    DesignationMode mode = AutoDepthDesignation.GetTowerDesignationMode(tower);
                    modeDisplay.SetValue(new LocStrFormatted(DesignationModeText(mode)));
                    SetModeDependentRowsEnabled(
                        flatteningDesignationTypeRow,
                        rampWidthRow,
                        maxLayersRow,
                        orePurityRow,
                        clearanceRow,
                        oreRow,
                        mode);
                });
            panel.BodyAdd(modeRow);

            flatteningDesignationTypeRow = BuildStepRow(
                new LocStrFormatted("Level action"),
                new LocStrFormatted("Designation type to place when Mode is Flattening."),
                flatteningDesignationTypeDisplay,
                (Action)delegate
                {
                    var tower = getTower(); if (tower == null) return;
                    AutoDepthDesignation.SetTowerFlatteningDesignationType(tower, NextFlatteningDesignationType(AutoDepthDesignation.GetTowerFlatteningDesignationType(tower)));
                    flatteningDesignationTypeDisplay.SetValue(new LocStrFormatted(FlatteningDesignationTypeText(AutoDepthDesignation.GetTowerFlatteningDesignationType(tower))));
                },
                (Action)delegate
                {
                    var tower = getTower(); if (tower == null) return;
                    AutoDepthDesignation.SetTowerFlatteningDesignationType(tower, PreviousFlatteningDesignationType(AutoDepthDesignation.GetTowerFlatteningDesignationType(tower)));
                    flatteningDesignationTypeDisplay.SetValue(new LocStrFormatted(FlatteningDesignationTypeText(AutoDepthDesignation.GetTowerFlatteningDesignationType(tower))));
                });
            panel.BodyAdd(flatteningDesignationTypeRow);

            // --- Ramp width ---
            int initRamp = initialTower != null
                ? AutoDepthDesignation.GetTowerRampWidth(initialTower)
                : AutoTerrainDesignationsMod.RampWidth;
            var rampWidthDisplay = new Mafi.Unity.Ui.Library.Display(new LocStrFormatted(RampWidthText(initRamp)))
                .MinDigits(3).AlignSelfStretch().MarginTopBottom(2.px());
            rampWidthRow = BuildStepRow(
                AtdLocalization.DesigRampWidthLabel,
                AtdLocalization.DesigRampWidthTip,
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
                });
            panel.BodyAdd(rampWidthRow);

            // --- Max layers ---
            int initLayers = initialTower != null
                ? AutoDepthDesignation.GetTowerMaxLayersToExcavate(initialTower)
                : AutoTerrainDesignationsMod.MaxLayersToExcavate;
            var maxLayersDisplay = new Mafi.Unity.Ui.Library.Display(new LocStrFormatted(MaxLayersText(initLayers)))
                .MinDigits(3).AlignSelfStretch().MarginTopBottom(2.px());
            maxLayersRow = BuildStepRow(
                AtdLocalization.DesigMaxLayersLabel,
                AtdLocalization.DesigMaxLayersTip,
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
                });
            panel.BodyAdd(maxLayersRow);

            // --- Elevation ---
            int? initElev = initialTower != null
                ? AutoDepthDesignation.GetTowerMaxDepthToDigTo(initialTower)
                : AutoTerrainDesignationsMod.MaxDepthToDigTo;
            var minElevDisplay = new Mafi.Unity.Ui.Library.Display(new LocStrFormatted(MinElevText(initElev)))
                .MinDigits(3).AlignSelfStretch().MarginTopBottom(2.px());
            panel.BodyAdd(BuildStepRow(
                AtdLocalization.DesigElevLimitLabel,
                AtdLocalization.DesigElevLimitTip,
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
            orePurityRow = BuildStepRow(
                AtdLocalization.DesigOrePurityLabel,
                AtdLocalization.DesigOrePurityTip,
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
                });
            panel.BodyAdd(orePurityRow);

            // --- Corridor clearance ---
            int initClearance = initialTower != null
                ? AutoDepthDesignation.GetTowerCorridorClearance(initialTower)
                : AutoTerrainDesignationsMod.MinCorridorClearance;
            var clearanceDisplay = new Mafi.Unity.Ui.Library.Display(new LocStrFormatted(ClearanceLevelText(initClearance)))
                .MinDigits(3).AlignSelfStretch().MarginTopBottom(2.px());
            clearanceRow = BuildStepRow(
                AtdLocalization.DesigCorridorClearanceLabel,
                AtdLocalization.DesigCorridorClearanceTip,
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
                });
            panel.BodyAdd(clearanceRow);

            // --- Ore picker row ---
            if (orePicker != null)
            {
                oreRow = new Row().MarginTop(1.pt());
                oreRow.Add(new Label(AtdLocalization.DesigScanningFilterLabel)
                    .Tooltip(AtdLocalization.DesigScanningFilterTip));
                oreRow.Add(new UiComponent().FlexGrow(1f));
                oreRow.Add(orePicker);
                panel.BodyAdd(oreRow);
            }

            SetModeDependentRowsEnabled(
                flatteningDesignationTypeRow,
                rampWidthRow,
                maxLayersRow,
                orePurityRow,
                clearanceRow,
                oreRow,
                initMode);

            s_bindings[key] = new Bindings(
                getTower,
                modeDisplay,
                flatteningDesignationTypeDisplay,
                rampWidthDisplay,
                maxLayersDisplay,
                minElevDisplay,
                orePurityDisplay,
                clearanceDisplay,
                flatteningDesignationTypeRow!,
                rampWidthRow!,
                maxLayersRow!,
                orePurityRow!,
                clearanceRow!,
                oreRow,
                rampWarningIcon);
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
                LocStr warnStr = outcome == AutoDepthDesignation.RampPlacementOutcome.Failed
                    ? AtdLocalization.DesigRampWarnFailed
                    : outcome == AutoDepthDesignation.RampPlacementOutcome.Truncated
                        ? AtdLocalization.DesigRampWarnTruncated
                        : AtdLocalization.DesigRampWarnNotAccessible;
                b.RampWarningIcon.Tooltip(warnStr);
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

        /// <summary>Applies mode-dependent row enabled state to an existing panel binding set.</summary>
        /// <param name="bindings">Panel binding set containing rows that should be updated.</param>
        /// <param name="designationMode">Currently selected designation mode.</param>
        private static void SetModeDependentRowsEnabled(Bindings bindings, DesignationMode designationMode)
        {
            SetModeDependentRowsEnabled(
                bindings.FlatteningDesignationTypeRow,
                bindings.RampWidthRow,
                bindings.MaxLayersRow,
                bindings.OrePurityRow,
                bindings.ClearanceRow,
                bindings.OrePickerRow,
                designationMode);
        }

        /// <summary>Enables controls that apply to the selected designation mode and disables irrelevant rows.</summary>
        /// <param name="flatteningDesignationTypeRow">Row containing the flattening-mode designation type controls.</param>
        /// <param name="rampWidthRow">Row containing ramp width controls.</param>
        /// <param name="maxLayersRow">Row containing max layer controls.</param>
        /// <param name="orePurityRow">Row containing ore purity controls.</param>
        /// <param name="clearanceRow">Row containing corridor clearance controls.</param>
        /// <param name="orePickerRow">Optional row containing the product scanning filter.</param>
        /// <param name="designationMode">Currently selected designation mode.</param>
        private static void SetModeDependentRowsEnabled(
            Row? flatteningDesignationTypeRow,
            Row? rampWidthRow,
            Row? maxLayersRow,
            Row? orePurityRow,
            Row? clearanceRow,
            Row? orePickerRow,
            DesignationMode designationMode)
        {
            bool isResourceMining = designationMode == DesignationMode.ResourceMining;
            SetRowEnabled(flatteningDesignationTypeRow, isResourceMining);
            SetRowEnabled(rampWidthRow, !isResourceMining);
            SetRowEnabled(maxLayersRow, !isResourceMining);
            SetRowEnabled(orePurityRow, !isResourceMining);
            SetRowEnabled(clearanceRow, !isResourceMining);
            SetRowEnabled(orePickerRow, !isResourceMining);
        }

        /// <summary>Enables or disables every interactive child in a row.</summary>
        /// <param name="row">Row whose children should be enabled or disabled, or null when the row was not built.</param>
        /// <param name="enabled">True to enable all child components; false to disable them.</param>
        private static void SetRowEnabled(Row? row, bool enabled)
        {
            if (row == null)
            {
                return;
            }

            foreach (UiComponent child in row.AllChildren)
            {
                child.Enabled(enabled);
            }
        }

        /// <summary>Returns the next designation mode in the inspector's two-state selector.</summary>
        /// <param name="value">Current designation mode.</param>
        /// <returns>The next designation mode in UI order.</returns>
        private static DesignationMode NextDesignationMode(DesignationMode value)
        {
            return value == DesignationMode.ResourceMining ? DesignationMode.Flattening : DesignationMode.ResourceMining;
        }

        /// <summary>Returns the previous designation mode in the inspector's two-state selector.</summary>
        /// <param name="value">Current designation mode.</param>
        /// <returns>The previous designation mode in UI order.</returns>
        private static DesignationMode PreviousDesignationMode(DesignationMode value)
        {
            return NextDesignationMode(value);
        }

        /// <summary>Returns the next flattening-mode designation type in UI order.</summary>
        /// <param name="value">Current flattening-mode designation type.</param>
        /// <returns>The next flattening-mode designation type in UI order.</returns>
        private static FlatteningDesignationType NextFlatteningDesignationType(FlatteningDesignationType value)
        {
            switch (value)
            {
                case FlatteningDesignationType.Mining: return FlatteningDesignationType.Dumping;
                case FlatteningDesignationType.Dumping: return FlatteningDesignationType.Leveling;
                default: return FlatteningDesignationType.Mining;
            }
        }

        /// <summary>Returns the previous flattening-mode designation type in UI order.</summary>
        /// <param name="value">Current flattening-mode designation type.</param>
        /// <returns>The previous flattening-mode designation type in UI order.</returns>
        private static FlatteningDesignationType PreviousFlatteningDesignationType(FlatteningDesignationType value)
        {
            switch (value)
            {
                case FlatteningDesignationType.Leveling: return FlatteningDesignationType.Dumping;
                case FlatteningDesignationType.Dumping: return FlatteningDesignationType.Mining;
                default: return FlatteningDesignationType.Leveling;
            }
        }

        /// <summary>Formats a designation mode for display in the inspector.</summary>
        /// <param name="value">Designation mode to format.</param>
        /// <returns>Display text for the designation mode.</returns>
        private static string DesignationModeText(DesignationMode value)
        {
            switch (value)
            {
                case DesignationMode.ResourceMining: return "Resource Mining";
                case DesignationMode.Flattening: return "Flattening";
                default: return value.ToString();
            }
        }

        /// <summary>Formats a flattening-mode designation type for display in the inspector.</summary>
        /// <param name="value">Flattening-mode designation type to format.</param>
        /// <returns>Display text for the flattening-mode designation type.</returns>
        private static string FlatteningDesignationTypeText(FlatteningDesignationType value)
        {
            switch (value)
            {
                case FlatteningDesignationType.Mining: return "Mining";
                case FlatteningDesignationType.Dumping: return "Dumping";
                case FlatteningDesignationType.Leveling: return "Leveling";
                default: return value.ToString();
            }
        }

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
                case 0: return AtdLocalization.LevelOff.TranslatedString;
                case 1: return AtdLocalization.LevelLow.TranslatedString;
                case 2: return AtdLocalization.LevelMed.TranslatedString;
                case 3: return AtdLocalization.LevelHigh.TranslatedString;
                case 4: return AtdLocalization.LevelMax.TranslatedString;
                default: return value.ToString();
            }
        }

        private static string ClearanceLevelText(int value)
        {
            switch (value)
            {
                case 0: return AtdLocalization.LevelOff.TranslatedString;
                case 1: return "1";
                case 2: return "2";
                default: return value.ToString();
            }
        }
    }
}
