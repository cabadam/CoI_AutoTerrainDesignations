// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
using System;
using System.Collections.Generic;
using System.Globalization;
using CoI.AutoHelpers.Settings;
using Mafi;
using Mafi.Localization;
using Mafi.Unity.UiToolkit;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using UnityEngine;
using Display = Mafi.Unity.Ui.Library.Display;
using Row = Mafi.Unity.UiToolkit.Library.Row;

namespace AutoTerrainDesignations
{
    internal static class AtdModSettingsTab
    {
        private const string MOD_ID = "auto-terrain-designations";
        private const string MOD_ICON = "Assets/Unity/UserInterface/Toolbar/Flatten.svg";
        private const string DEFAULTS_ICON = "Assets/Unity/UserInterface/Toolbar/Copy.svg";
        private const string GAME_SETTINGS_ICON = "Assets/Unity/UserInterface/EntityIcons/Gears.png";
        private const string ORE_QUALITY_ICON = "Assets/Unity/UserInterface/General/SwapVertical.svg";

        internal static ModSettingsTab BuildDefaultsTab()
        {
            return new ModSettingsTab(
                MOD_ID,
                AtdLocalization.SettingsModName.AsFormatted,
                AtdLocalization.SettingsTabDefaults.AsFormatted,
                100,
                BuildDefaultsContent,
                DEFAULTS_ICON,
                MOD_ICON);
        }

        internal static ModSettingsTab BuildOreQualityTab()
        {
            return new ModSettingsTab(
                MOD_ID,
                AtdLocalization.SettingsModName.AsFormatted,
                AtdLocalization.SettingsTabOreQuality.AsFormatted,
                120,
                BuildOreQualityContent,
                ORE_QUALITY_ICON);
        }

        internal static ModSettingsTab BuildGameSettingsTab()
        {
            return new ModSettingsTab(
                MOD_ID,
                AtdLocalization.SettingsModName.AsFormatted,
                AtdLocalization.SettingsTabGameSettings.AsFormatted,
                110,
                BuildGameSettingsContent,
                GAME_SETTINGS_ICON);
        }

        private static UiComponent BuildDefaultsContent()
        {
            var refreshers = new List<Action>();
            var content = BuildSettingsColumn();

            AddMiningDefaultsSection(content, refreshers);
            AddPanelDefaultsSection(content, refreshers);

            content.Add(BuildFooter(refreshers));

            return content;
        }

        private static UiComponent BuildGameSettingsContent()
        {
            var refreshers = new List<Action>();
            var content = BuildSettingsColumn();

            AddScanBehaviorSection(content, refreshers);
            AddPerformanceSection(content, refreshers);
            AddKeyboardShortcutsSection(content, refreshers);
            AddNotificationsSection(content, refreshers);

            content.Add(BuildFooter(refreshers));

            return content;
        }

        private static UiComponent BuildOreQualityContent()
        {
            var refreshers = new List<Action>();
            var content = BuildSettingsColumn();

            for (int level = 0; level < AutoDepthDesignation.PurityLevelCount; level++)
            {
                int capturedLevel = level;
                content.Add(BuildSectionHeading(L(LevelName(capturedLevel))));
                content.Add(BuildFloatStepRow(
                    AtdLocalization.SettingsMinOreHeightLabel.AsFormatted,
                    AtdLocalization.SettingsMinOreHeightTooltip.AsFormatted,
                    () => AutoDepthDesignation.GetMinOreHeightForLevel(capturedLevel),
                    value => AutoDepthDesignation.TrySetMinOreHeightForLevel(capturedLevel, Math.Max(0f, value)),
                    FormatFloat,
                    refreshers));
                content.Add(BuildFloatStepRow(
                    AtdLocalization.SettingsMinBottomDensityLabel.AsFormatted,
                    AtdLocalization.SettingsMinBottomDensityTooltip.AsFormatted,
                    () => AutoDepthDesignation.GetMinBottomOreDensityForLevel(capturedLevel),
                    value => AutoDepthDesignation.TrySetMinBottomOreDensityForLevel(capturedLevel, value),
                    FormatRatio,
                    refreshers));
                content.Add(BuildFloatStepRow(
                    AtdLocalization.SettingsMinOrePurityLabel.AsFormatted,
                    AtdLocalization.SettingsMinOrePurityTooltip.AsFormatted,
                    () => AutoDepthDesignation.GetMinOrePurityForLevel(capturedLevel),
                    value => AutoDepthDesignation.TrySetMinOrePurityForLevel(capturedLevel, value),
                    FormatRatio,
                    refreshers));
                content.Add(BuildIntStepRow(
                    AtdLocalization.SettingsMinComponentSizeLabel.AsFormatted,
                    AtdLocalization.SettingsMinComponentSizeTooltip.AsFormatted,
                    () => AutoDepthDesignation.GetMinComponentSizeForLevel(capturedLevel),
                    value => AutoDepthDesignation.TrySetMinComponentSizeForLevel(capturedLevel, value),
                    value => value.ToString(CultureInfo.InvariantCulture),
                    refreshers));
            }

            content.Add(BuildFooter(refreshers));

            return content;
        }

        private static Column BuildSettingsColumn()
        {
            return new Column(2.pt())
                .AlignItemsStretch()
                .PaddingLeft(1.pt())
                .PaddingRight(1.pt());
        }

        private static Title BuildSectionHeading(LocStrFormatted title)
        {
            return new Title(title)
                .Color(Theme.PrimaryColor)
                .MarginTop(2.pt())
                .MarginLeft(-1.pt());
        }

        private static void AddMiningDefaultsSection(Column content, List<Action> refreshers)
        {
            content.Add(BuildSectionHeading(AtdLocalization.SettingsHeadingMiningDefaults.AsFormatted));

            content.Add(BuildIntStepRow(
                AtdLocalization.DesigRampWidthLabel.AsFormatted,
                AtdLocalization.DesigRampWidthTip.AsFormatted,
                () => AutoTerrainDesignationsMod.RampWidth,
                value => AutoTerrainDesignationsMod.SetRampWidth(value),
                value => value.ToString(CultureInfo.InvariantCulture),
                refreshers));
            content.Add(BuildIntStepRow(
                AtdLocalization.DesigMaxLayersLabel.AsFormatted,
                AtdLocalization.DesigMaxLayersTip.AsFormatted,
                () => AutoTerrainDesignationsMod.MaxLayersToExcavate,
                value => AutoTerrainDesignationsMod.SetMaxLayersToExcavate(value),
                FormatNoLimitZero,
                refreshers));
            content.Add(BuildNullableDepthRow(refreshers));
            content.Add(BuildIntStepRow(
                AtdLocalization.DesigOrePurityLabel.AsFormatted,
                AtdLocalization.DesigOrePurityTip.AsFormatted,
                () => AutoTerrainDesignationsMod.OrePurityLevel,
                value => AutoTerrainDesignationsMod.SetOrePurityLevel(value),
                FormatOrePurityLevel,
                refreshers));
            content.Add(BuildIntStepRow(
                AtdLocalization.DesigCorridorClearanceLabel.AsFormatted,
                AtdLocalization.DesigCorridorClearanceTip.AsFormatted,
                () => AutoTerrainDesignationsMod.MinCorridorClearance,
                value => AutoTerrainDesignationsMod.SetMinCorridorClearance(value),
                FormatClearance,
                refreshers));
        }

        private static void AddScanBehaviorSection(Column content, List<Action> refreshers)
        {
            content.Add(BuildSectionHeading(AtdLocalization.SettingsHeadingDesignations.AsFormatted));

            content.Add(BuildIntStepRow(
                AtdLocalization.SettingsMaxSlopeLabel.AsFormatted,
                AtdLocalization.SettingsMaxSlopeTooltip.AsFormatted,
                () => AutoTerrainDesignationsMod.MaxHeightDiff,
                value => AutoTerrainDesignationsMod.SetMaxHeightDiff(value),
                value => value.ToString(CultureInfo.InvariantCulture),
                refreshers));
            content.Add(BuildIntStepRow(
                AtdLocalization.SettingsBottomFlatteningLabel.AsFormatted,
                AtdLocalization.SettingsBottomFlatteningTooltip.AsFormatted,
                GetBottomFlatteningValue,
                SetBottomFlatteningValue,
                value => value.ToString(CultureInfo.InvariantCulture),
                refreshers));
        }

        private static void AddPerformanceSection(Column content, List<Action> refreshers)
        {
            content.Add(BuildSectionHeading(AtdLocalization.SettingsHeadingScanPerformance.AsFormatted));

            content.Add(BuildIntStepRow(
                AtdLocalization.SettingsBatchSizeLabel.AsFormatted,
                AtdLocalization.SettingsBatchSizeTooltip.AsFormatted,
                () => AutoDepthDesignation.BatchSize,
                value => AutoDepthDesignation.SetBatchSize(value),
                value => value.ToString(CultureInfo.InvariantCulture),
                refreshers));
        }

        private static void AddKeyboardShortcutsSection(Column content, List<Action> refreshers)
        {
            content.Add(BuildSectionHeading(AtdLocalization.SettingsHeadingKeyboardShortcuts.AsFormatted));
            content.Add(BuildCornerKeyRow(refreshers));
        }

        private static void AddPanelDefaultsSection(Column content, List<Action> refreshers)
        {
            content.Add(BuildSectionHeading(AtdLocalization.SettingsHeadingPanelDefaults.AsFormatted));

            content.Add(BuildToggleRow(
                AtdLocalization.SettingsMiningPanelCollapsedLabel.AsFormatted,
                AtdLocalization.SettingsMiningPanelCollapsedTooltip.AsFormatted,
                () => AutoTerrainDesignationsMod.TerrainDesignationsPanelCollapsed,
                value => AutoTerrainDesignationsMod.SetTerrainDesignationsPanelCollapsed(value),
                refreshers));
            content.Add(BuildToggleRow(
                AtdLocalization.SettingsOrePanelCollapsedLabel.AsFormatted,
                AtdLocalization.SettingsOrePanelCollapsedTooltip.AsFormatted,
                () => AutoTerrainDesignationsMod.OreCompositionPanelCollapsed,
                value => AutoTerrainDesignationsMod.SetOreCompositionPanelCollapsed(value),
                refreshers));
            content.Add(BuildToggleRow(
                AtdLocalization.SettingsFarmingPanelCollapsedLabel.AsFormatted,
                AtdLocalization.SettingsFarmingPanelCollapsedTooltip.AsFormatted,
                () => AutoTerrainDesignationsMod.FarmingPanelCollapsed,
                value => AutoTerrainDesignationsMod.SetFarmingPanelCollapsed(value),
                refreshers));
            content.Add(BuildToggleRow(
                AtdLocalization.FarmingIdleReleaseLabel.AsFormatted,
                AtdLocalization.FarmingIdleReleaseTip.AsFormatted,
                () => AutoTerrainDesignationsMod.AutoReleaseVehiclesWhenIdle,
                value => AutoTerrainDesignationsMod.SetAutoReleaseVehiclesWhenIdle(value),
                refreshers));
        }

        private static void AddNotificationsSection(Column content, List<Action> refreshers)
        {
            content.Add(BuildSectionHeading(AtdLocalization.SettingsHeadingNotifications.AsFormatted));

            content.Add(BuildToggleRow(
                AtdLocalization.SettingsExcavatorNotificationsLabel.AsFormatted,
                AtdLocalization.SettingsExcavatorNotificationsTooltip.AsFormatted,
                () => AutoTerrainDesignationsMod.ExcavatorCompletionNotificationsEnabled,
                value => AutoTerrainDesignationsMod.SetExcavatorCompletionNotificationsEnabled(value),
                refreshers));
            content.Add(BuildToggleRow(
                AtdLocalization.SettingsRampNotificationsLabel.AsFormatted,
                AtdLocalization.SettingsRampNotificationsTooltip.AsFormatted,
                () => AutoTerrainDesignationsMod.RampNotificationsEnabled,
                value => AutoTerrainDesignationsMod.SetRampNotificationsEnabled(value),
                refreshers));
        }

        private static Row BuildIntStepRow(
            LocStrFormatted label,
            LocStrFormatted tooltip,
            Func<int> getValue,
            Action<int> setValue,
            Func<int, string> format,
            List<Action> refreshers)
        {
            var display = new Display(L(format(getValue()))).MinDigits(4).AlignSelfStretch().MarginTopBottom(2.px());
            void Refresh() => display.SetValue(L(format(getValue())));
            refreshers.Add(Refresh);

            return BuildStepRow(
                label,
                tooltip,
                display,
                () =>
                {
                    setValue(getValue() + ModifierStepSize());
                    Refresh();
                },
                () =>
                {
                    setValue(getValue() - ModifierStepSize());
                    Refresh();
                });
        }

        private static int GetBottomFlatteningValue()
        {
            return AutoTerrainDesignationsMod.BottomFlatteningEnabled
                ? AutoTerrainDesignationsMod.BottomFlatteningStrength
                : 0;
        }

        private static void SetBottomFlatteningValue(int value)
        {
            int clamped = Math.Max(0, Math.Min(10, value));
            AutoTerrainDesignationsMod.SetBottomFlatteningEnabled(clamped > 0);
            if (clamped > 0)
                AutoTerrainDesignationsMod.SetBottomFlatteningStrength(clamped);
        }

        private static Row BuildFloatStepRow(
            LocStrFormatted label,
            LocStrFormatted tooltip,
            Func<float> getValue,
            Func<float, bool> setValue,
            Func<float, string> format,
            List<Action> refreshers)
        {
            var display = new Display(L(format(getValue()))).MinDigits(5).AlignSelfStretch().MarginTopBottom(2.px());
            void Refresh() => display.SetValue(L(format(getValue())));
            refreshers.Add(Refresh);

            return BuildStepRow(
                label,
                tooltip,
                display,
                () =>
                {
                    setValue(getValue() + FloatStepSize());
                    Refresh();
                },
                () =>
                {
                    setValue(getValue() - FloatStepSize());
                    Refresh();
                });
        }

        private static Row BuildNullableDepthRow(List<Action> refreshers)
        {
            var display = new Display(L(FormatDepth(AutoTerrainDesignationsMod.MaxDepthToDigTo)))
                .MinDigits(4)
                .AlignSelfStretch()
                .MarginTopBottom(2.px());
            void Refresh() => display.SetValue(L(FormatDepth(AutoTerrainDesignationsMod.MaxDepthToDigTo)));
            refreshers.Add(Refresh);

            var row = BuildStepRow(
                AtdLocalization.DesigElevLimitLabel.AsFormatted,
                AtdLocalization.DesigElevLimitTip.AsFormatted,
                display,
                () =>
                {
                    int? current = AutoTerrainDesignationsMod.MaxDepthToDigTo;
                    AutoTerrainDesignationsMod.SetMaxDepthToDigTo(current == null ? -50 : current.Value + ModifierStepSize());
                    Refresh();
                },
                () =>
                {
                    int? current = AutoTerrainDesignationsMod.MaxDepthToDigTo;
                    if (current != null)
                    {
                        int next = current.Value - ModifierStepSize();
                        AutoTerrainDesignationsMod.SetMaxDepthToDigTo(next < -50 ? (int?)null : next);
                    }
                    Refresh();
                });

            return row;
        }

        private static Row BuildCornerKeyRow(List<Action> refreshers)
        {
            var field = new TextField()
                .Text(FormatKeyCodeForPlayer(AutoTerrainDesignationsMod.CornerDesignationKey))
                .CharLimit(24)
                .ClearFocusOnEscape();
            var status = new Label(L(string.Empty)).MarginTopBottom(2.px());

            void Refresh()
            {
                field.Text(FormatKeyCodeForPlayer(AutoTerrainDesignationsMod.CornerDesignationKey));
                status.Value(L(string.Empty));
            }

            refreshers.Add(Refresh);

            field.OnEditEnd(text =>
            {
                if (TryParsePlayerKeyCode(text, out KeyCode parsed))
                {
                    AutoTerrainDesignationsMod.SetCornerDesignationKey(parsed);
                    field.Text(FormatKeyCodeForPlayer(parsed));
                    field.MarkAsError(false);
                    status.Value(AtdLocalization.SettingsApplied.AsFormatted);
                }
                else
                {
                    field.MarkAsError(true, AtdLocalization.SettingsCornerModeInvalidTooltip.AsFormatted);
                    status.Value(AtdLocalization.SettingsInvalidKey.AsFormatted);
                }
            });

            var row = new Row().MarginTop(1.pt()).AlignItemsCenter();
            row.Add(new Label(AtdLocalization.SettingsCornerModeLabel.AsFormatted)
                .Tooltip(AtdLocalization.SettingsCornerModeTooltip.AsFormatted));
            row.Add(new UiComponent().FlexGrow(1f));
            row.Add(field.Width(120.px()));
            row.Add(status);
            return row;
        }

        private static Row BuildToggleRow(
            LocStrFormatted label,
            LocStrFormatted tooltip,
            Func<bool> getValue,
            Action<bool> setValue,
            List<Action> refreshers)
        {
            var toggle = new Toggle(standalone: true)
                .Label(label)
                .Value(getValue())
                .OnValueChanged(value => setValue(value))
                .Tooltip(tooltip);
            refreshers.Add(() => toggle.Value(getValue()));
            var row = new Row().MarginTop(1.pt()).AlignItemsCenter();
            row.Add(toggle);
            return row;
        }

        private static Row BuildStepRow(
            LocStrFormatted label,
            LocStrFormatted tooltip,
            Display display,
            Action onPlus,
            Action onMinus)
        {
            var plusBtn = new ButtonIcon(Button.General, "Assets/Unity/UserInterface/General/Plus128.png")
                .Compact().IconSize(14.px()).OnClick(onPlus, allowKeyPresses: true);
            var minusBtn = new ButtonIcon(Button.General, "Assets/Unity/UserInterface/General/Minus128.png")
                .Compact().IconSize(14.px()).OnClick(onMinus, allowKeyPresses: true);
            var row = new Row().MarginTop(1.pt()).AlignItemsCenter();
            row.Add(new Label(label).Tooltip(tooltip));
            row.Add(new UiComponent().FlexGrow(1f));
            row.Add(minusBtn);
            row.Add(display);
            row.Add(plusBtn);
            return row;
        }

        private static PanelFooterRow BuildFooter(List<Action> refreshers)
        {
            var status = new Label(L(string.Empty)).MarginTopBottom(1.pt());
            var save = new ButtonText(Button.Primary, AtdLocalization.SettingsSaveAsGlobal.AsFormatted, () =>
            {
                if (AutoDepthDesignation.TrySaveSettings(out string _))
                    status.Value(AtdLocalization.SettingsSavedToFile.AsFormatted);
                else
                    status.Value(AtdLocalization.SettingsSaveFailed.AsFormatted);
            }).Tooltip(AtdLocalization.SettingsSaveAsGlobalTooltip.AsFormatted);

            var reset = new ButtonText(Button.General, AtdLocalization.SettingsRestoreDefaults.AsFormatted, () =>
            {
                AutoDepthDesignation.ResetSettingsToDefaults();
                foreach (Action refresh in refreshers)
                    refresh();
                status.Value(AtdLocalization.SettingsRestoredDefaults.AsFormatted);
            }).Tooltip(AtdLocalization.SettingsRestoreDefaultsTooltip.AsFormatted);

            return new PanelFooterRow().BodyAdd(
                row => row.Gap(2.pt()).AlignItemsCenter(),
                status,
                new UiComponent().FlexGrow(1f),
                reset,
                save);
        }

        private static int ModifierStepSize()
        {
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) return 10;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return 5;
            return 1;
        }

        private static float FloatStepSize()
        {
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) return 0.25f;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return 0.10f;
            return 0.05f;
        }

        private static string FormatNoLimitZero(int value)
        {
            return value == 0 ? "∞" : value.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatDepth(int? value)
        {
            if (value == null)
                return "-∞";
            return value.Value > 0
                ? "+" + value.Value.ToString(CultureInfo.InvariantCulture)
                : value.Value.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatOrePurityLevel(int value)
        {
            switch (value)
            {
                case 0: return "Off";
                case 1: return "Low";
                case 2: return "Med";
                case 3: return "High";
                case 4: return "Max";
                default: return value.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static string FormatClearance(int value)
        {
            return value == 0 ? "Off" : value.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string FormatRatio(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string FormatKeyCodeForPlayer(KeyCode key)
        {
            int keyValue = (int)key;
            int alpha0 = (int)KeyCode.Alpha0;
            int keypad0 = (int)KeyCode.Keypad0;

            if (keyValue >= alpha0 && keyValue <= alpha0 + 9)
                return (keyValue - alpha0).ToString(CultureInfo.InvariantCulture);
            if (keyValue >= keypad0 && keyValue <= keypad0 + 9)
                return "Numpad " + (keyValue - keypad0).ToString(CultureInfo.InvariantCulture);
            if (key == KeyCode.Escape)
                return "Escape";
            if (key == KeyCode.Space)
                return "Space";
            return key.ToString();
        }

        private static bool TryParsePlayerKeyCode(string text, out KeyCode key)
        {
            string normalized = (text ?? string.Empty).Trim();
            if (normalized.Length == 1 && normalized[0] >= '0' && normalized[0] <= '9')
            {
                key = KeyCode.Alpha0 + (normalized[0] - '0');
                return true;
            }

            string compact = normalized.Replace(" ", string.Empty);
            if (compact.StartsWith("Numpad", StringComparison.OrdinalIgnoreCase)
                && compact.Length == 7
                && compact[6] >= '0'
                && compact[6] <= '9')
            {
                key = KeyCode.Keypad0 + (compact[6] - '0');
                return true;
            }

            if (string.Equals(normalized, "Esc", StringComparison.OrdinalIgnoreCase))
            {
                key = KeyCode.Escape;
                return true;
            }

            return Enum.TryParse(normalized, true, out key);
        }

        private static string LevelName(int level)
        {
            return level + " - " + FormatOrePurityLevel(level);
        }

        private static LocStrFormatted L(string text)
        {
            return new LocStrFormatted(text ?? string.Empty);
        }
    }
}
