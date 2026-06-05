// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
using System;
using System.IO;
using HarmonyLib;
using Mafi;
using Mafi.Collections;
using Mafi.Core.Entities;
using Mafi.Core.Game;
using Mafi.Core.GameLoop;
using Mafi.Core.Mods;
using Mafi.Core.Notifications;
using Mafi.Core.Input;
using Mafi.Core.PathFinding;
using Mafi.Core.Prototypes;
using Mafi.Core.SaveGame;
using Mafi.Core.Simulation;
using Mafi.Core.Console;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Terrain.Props;
using Mafi.Core.Vehicles.Jobs;
using Mafi.Core.World;
using Mafi.Unity.InputControl;
using Mafi.Unity.Terrain.Designation;
using Mafi.Localization;
using Mafi.Unity.UiStatic;
using Mafi.Unity.UiStatic.Cursors;
using UnityEngine;
using CoI.AutoHelpers.Localization;
using CoI.AutoHelpers.Logging;
using CoI.AutoHelpers.Persistence;
using CoI.AutoHelpers.Settings;
using Mafi.Unity;
using Mafi.Unity.Ui.Hud;
using Mafi.Unity.UiToolkit;

namespace AutoTerrainDesignations;

public sealed class AutoTerrainDesignationsMod : IMod, IDisposable
{
    private Harmony? m_harmony;
    private IGameLoopEvents? m_gameLoopEvents;
    private ISimLoopEvents? m_simLoopEvents;
    private ISaveManager? m_saveManager;
    private SimStep m_lastSimTick;
    private IModStateJsonStore? m_towerSettingsStateStore;

    public string Name => "Auto Terrain Designations";

    public int Version => 1;

    public bool IsUiOnly => false;

    public Option<IConfig> ModConfig { get; set; }

    public ModManifest Manifest { get; }

    public static string ModVersion { get; private set; } = "?";

    public static string ModMarker => $"Kayser's AutoTerrainDesignations v{ModVersion}";

    /// <summary>Returns <paramref name="text"/> with the mod sign-off appended, for use in tooltips.</summary>
    public static string Tt(string text) => $"{text}\n[{ModMarker}]";

    public ModJsonConfig JsonConfig { get; }

    public AutoTerrainDesignationsMod(ModManifest manifest)
    {
        Manifest = manifest;
        ModVersion = manifest.Version.ToString();
        JsonConfig = new ModJsonConfig(this);
    }

    public void RegisterPrototypes(ProtoRegistrator registrator)
    {
        m_harmony = new Harmony("com.auto-terrain-designations.mod");
        AutoDepthDesignation.ApplyInspectorPatches(m_harmony);
        AutoDepthDesignation.ApplyCornerPatches(m_harmony);
        AutoDepthDesignation.ApplyVehicleDepotPatches(m_harmony);
        AutoDepthDesignation.ApplyFarmPlacementAssistPatches(m_harmony);

        AtdNotifications.RegisterPrototypes(registrator);
    }

    public void RegisterDependencies(DependencyResolverBuilder depBuilder, ProtosDb protosDb, bool gameWasLoaded)
    {
    }

    public void EarlyInit(DependencyResolver resolver)
    {
    }

    public static int MaxHeightDiff { get; private set; } = 1;

    public static void ResetGlobalDefaults()
    {
        SetMaxHeightDiff(1);
        SetRampWidth(2);
        SetMaxLayersToExcavate(30);
        SetMaxDepthToDigTo(null);
        SetOrePurityLevel(0);
        SetBottomFlatteningEnabled(true);
        SetBottomFlatteningStrength(5);
        SetMinCorridorClearance(2);
        SetTerrainDesignationsPanelCollapsed(false);
        SetOreCompositionPanelCollapsed(false);
        SetFarmingPanelCollapsed(true);
        SetExcavatorCompletionNotificationsEnabled(true);
        SetRampNotificationsEnabled(true);
        SetAutoReleaseVehiclesWhenIdle(false);
        SetCornerDesignationKey(KeyCode.K);
    }

    public static void SetMaxHeightDiff(int value)
    {
        MaxHeightDiff = Math.Max(1, Math.Min(3, value));
    }

    /// <summary>Ramp width in tiles. Allowed range: 0..5. 0 disables ramp generation.</summary>
    public static int RampWidth { get; private set; } = 2;

    public static void SetRampWidth(int value)
    {
        RampWidth = Math.Max(0, Math.Min(5, value));
    }

    /// <summary>Maximum number of layers to excavate from the surface. 0 = no limit.</summary>
    public static int MaxLayersToExcavate { get; private set; } = 30;

    public static void SetMaxLayersToExcavate(int value)
    {
        MaxLayersToExcavate = Math.Max(0, value);
    }

    /// <summary>Absolute minimum terrain elevation to excavate to. null = no limit.</summary>
    public static int? MaxDepthToDigTo { get; private set; } = null;

    public static void SetMaxDepthToDigTo(int? value)
    {
        MaxDepthToDigTo = value;
    }

    /// <summary>
    /// Ore purity threshold level (0=Off, 1=Low, 2=Medium, 3=High, 4=Max).
    /// Controls how aggressively poor-quality tiles and deep sparse ore are excluded.
    /// </summary>
    public static int OrePurityLevel { get; private set; } = 0;

    public static void SetOrePurityLevel(int value)
    {
        OrePurityLevel = Math.Max(0, Math.Min(4, value));
    }

    /// <summary>Whether ATD applies the extra bottom-flattening pass before placing designations.</summary>
    public static bool BottomFlatteningEnabled { get; private set; } = true;

    public static void SetBottomFlatteningEnabled(bool value)
    {
        BottomFlatteningEnabled = value;
    }

    /// <summary>
    /// Bottom-flattening aggressiveness (1–10). Controls which depth-percentile of a connected ore
    /// component is chosen as the flattening target.
    /// <list type="bullet">
    ///   <item>1 = mildest — targets the 90th-percentile depth (shallow target; only extreme outliers affected).</item>
    ///   <item>5 = moderate — targets the 50th-percentile depth (median; default).</item>
    ///   <item>10 = strongest — targets the deepest tile (all other tiles pulled down to match).</item>
    /// </list>
    /// In lower-only mode (purity = Off) tiles are only ever pulled deeper; in leveling mode they are
    /// set to the target regardless of direction.
    /// </summary>
    public static int BottomFlatteningStrength { get; private set; } = 5;

    public static void SetBottomFlatteningStrength(int value)
    {
        BottomFlatteningStrength = Math.Max(1, Math.Min(10, value));
    }

    /// <summary>
    /// Minimum corridor clearance for designation connectivity.
    /// 0 = disabled — no corridors drawn, components left separate (for vehicle-less excavation);
    /// 1 = 1-tile corridors (small/medium vehicles);
    /// 2 = 2-tile corridors (mega vehicles, current default).
    /// </summary>
    public static int MinCorridorClearance { get; private set; } = 2;

    public static void SetMinCorridorClearance(int value)
    {
        MinCorridorClearance = Math.Max(0, Math.Min(2, value));
    }

    /// <summary>Default collapsed state for the Mining designations inspector panel.</summary>
    public static bool TerrainDesignationsPanelCollapsed { get; private set; } = false;

    public static void SetTerrainDesignationsPanelCollapsed(bool value)
    {
        TerrainDesignationsPanelCollapsed = value;
    }

    /// <summary>Default collapsed state for the Ore composition inspector panel.</summary>
    public static bool OreCompositionPanelCollapsed { get; private set; } = false;

    public static void SetOreCompositionPanelCollapsed(bool value)
    {
        OreCompositionPanelCollapsed = value;
    }

    /// <summary>Whether ATD shows a green notification when a vehicle depot completes an excavator.</summary>
    public static bool ExcavatorCompletionNotificationsEnabled { get; private set; } = true;

    public static void SetExcavatorCompletionNotificationsEnabled(bool value)
    {
        ExcavatorCompletionNotificationsEnabled = value;
    }

    /// <summary>Whether ATD shows ramp access warning notifications on mine towers.</summary>
    public static bool RampNotificationsEnabled { get; private set; } = true;

    public static void SetRampNotificationsEnabled(bool value)
    {
        RampNotificationsEnabled = value;
    }

    /// <summary>Default collapsed state for the Farming panel when a mine tower inspector is created.</summary>
    public static bool FarmingPanelCollapsed { get; private set; } = true;

    public static void SetFarmingPanelCollapsed(bool value)
    {
        FarmingPanelCollapsed = value;
    }

    /// <summary>Whether ATD automatically releases all vehicles from a tower when there are no pending excavation jobs.</summary>
    public static bool AutoReleaseVehiclesWhenIdle { get; private set; } = false;

    public static void SetAutoReleaseVehiclesWhenIdle(bool value)
    {
        AutoReleaseVehiclesWhenIdle = value;
    }

    /// <summary>Key used to enter and toggle corner designation mode. Default: K.</summary>
    public static KeyCode CornerDesignationKey { get; private set; } = KeyCode.K;

    public static void SetCornerDesignationKey(KeyCode value)
    {
        CornerDesignationKey = value;
    }

    public void Initialize(DependencyResolver resolver, bool gameWasLoaded)
    {
        try
        {
            AutoDepthDesignation.s_log.EnableConsoleLogging();
            AutoDepthDesignation.s_log.RegisterAutoConsoleMirroring(this, resolver.Resolve<IGameLoopEvents>(), resolver.Resolve<GameConsoleCommandsExecutor>());
            AutoTerrainDesignationsTicker.DestroyActive();

            RegisterAutoHelpersLocalizationLateApply(resolver);

            m_gameLoopEvents = resolver.Resolve<IGameLoopEvents>();
            m_simLoopEvents = resolver.Resolve<ISimLoopEvents>();
            m_saveManager = resolver.Resolve<ISaveManager>();
            m_gameLoopEvents.Terminate.AddNonSaveable(this, onGameTerminated);
            m_simLoopEvents.BeforeSave.AddNonSaveable(this, beforeSave);
            m_simLoopEvents.Update.AddNonSaveable(this, onSimUpdate);
            m_saveManager.OnSaveDone += onSaveDone;

            ITerrainDesignationsManager desigManager = resolver.Resolve<ITerrainDesignationsManager>();
            ProtosDb protosDb = resolver.Resolve<ProtosDb>();
            IWorldMapManager worldMapManager = resolver.Resolve<IWorldMapManager>();
            IEntitiesManager entitiesManager = resolver.Resolve<IEntitiesManager>();
            TerrainPropsManager terrainPropsManager = resolver.Resolve<TerrainPropsManager>();
            IVehiclePathFindingManager vehiclePathFindingManager = resolver.Resolve<IVehiclePathFindingManager>();
            ParkAndWaitJobFactory parkAndWaitJobFactory = resolver.Resolve<ParkAndWaitJobFactory>();
            INotificationsManager notificationsManager = resolver.Resolve<INotificationsManager>();
            IInputScheduler inputScheduler = resolver.Resolve<IInputScheduler>();
            AutoTerrainDesignationsTicker ticker = AutoTerrainDesignationsTicker.CreateForWorld(AutoDepthDesignation.CurrentWorldGeneration + 1);
            AutoDepthDesignation.SetModRootDirectoryPath(Manifest.RootDirectoryPath);
            AutoDepthDesignation.Initialize(desigManager, protosDb, worldMapManager, ticker, entitiesManager, terrainPropsManager, vehiclePathFindingManager, parkAndWaitJobFactory, notificationsManager, inputScheduler);
            m_towerSettingsStateStore = ModStateJsonStores.CreateDefault(JsonConfig, AutoDepthDesignation.TowerSettingsConfigKey);
            AutoDepthDesignation.LoadTowerSettingsFromJsonStore(m_towerSettingsStateStore);

            // Corner designation mode — TerrainCursor, TerrainDesignationsRenderer and
            // CursorManager may only be available on the Unity side; fail gracefully if not resolvable.
            TerrainCursor? terrainCursor = null;
            TerrainDesignationsRenderer? desigRenderer = null;
            CursorManager? cursorManager = null;
            ShortcutsManager? shortcutsManager = null;
            try { terrainCursor = resolver.Resolve<TerrainCursor>(); }
            catch (Exception ex2) { AutoDepthDesignation.s_log.Warning("TerrainCursor not available: " + ex2.Message); }
            try { desigRenderer = resolver.Resolve<TerrainDesignationsRenderer>(); }
            catch (Exception ex3) { AutoDepthDesignation.s_log.Warning("TerrainDesignationsRenderer not available: " + ex3.Message); }
            try { cursorManager = resolver.Resolve<CursorManager>(); }
            catch (Exception ex4) { AutoDepthDesignation.s_log.Warning("CursorManager not available: " + ex4.Message); }
            try { shortcutsManager = resolver.Resolve<ShortcutsManager>(); }
            catch (Exception ex5) { AutoDepthDesignation.s_log.Warning("ShortcutsManager not available: " + ex5.Message); }
            AutoDepthDesignation.InitializeCornerMode(terrainCursor, desigRenderer, cursorManager, shortcutsManager);
        }
        catch (Exception ex)
        {
            unsubscribeWorldEvents();
            AutoTerrainDesignationsTicker.DestroyActive();
            AutoDepthDesignation.ResetWorldRuntimeState();
            AutoDepthDesignation.s_log.Exception(ex, "AutoTerrainDesignations init");
        }
    }

    // Runs on the simulation thread — safe to call game simulation APIs.
    private void onSimUpdate()
    {
        if (m_simLoopEvents == null)
            return;
        // Run once per game-second (10 sim steps = 1 game-second).
        SimStep current = m_simLoopEvents.CurrentStep;
        if (current - m_lastSimTick < Duration.OneSecond)
            return;
        m_lastSimTick = current;
        try { AutoDepthDesignation.TickFarmingPreparationSessions(); }
        catch (Exception ex) { AutoDepthDesignation.s_log.Exception(ex, "TickFarmingPreparationSessions"); }
        try { AutoDepthDesignation.TickIdleVehicleRelease(); }
        catch (Exception ex) { AutoDepthDesignation.s_log.Exception(ex, "TickIdleVehicleRelease"); }
    }

    private void beforeSave()
    {
        IModStateJsonStore store = m_towerSettingsStateStore
            ?? ModStateJsonStores.CreateDefault(JsonConfig, AutoDepthDesignation.TowerSettingsConfigKey);
        m_towerSettingsStateStore = store;
        AutoDepthDesignation.SaveTowerSettingsToJsonStore(store);
        AutoDepthDesignation.PurgeTransientNotificationsForSave();
        AutoDepthDesignation.RestoreFarmingRuntimeForSave();
        AutoDepthDesignation.RestoreIdleReleasedVehiclesForSave();
    }

    private void onSaveDone(SaveResult result)
    {
        AutoDepthDesignation.ResumeFarmingRuntimeAfterSave();
        AutoDepthDesignation.RestoreTransientNotificationsAfterSave();
        AutoDepthDesignation.ReReleaseIdleVehiclesAfterSave();
    }

    private void onGameTerminated()
    {
        unsubscribeWorldEvents();
        AutoTerrainDesignationsTicker.DestroyActive();
        AutoDepthDesignation.ResetWorldRuntimeState();
    }

    private void unsubscribeWorldEvents()
    {
        if (m_gameLoopEvents != null)
        {
            try { m_gameLoopEvents.Terminate.RemoveNonSaveable(this, onGameTerminated); }
            catch { }
            m_gameLoopEvents = null;
        }

        if (m_simLoopEvents != null)
        {
            try { m_simLoopEvents.BeforeSave.RemoveNonSaveable(this, beforeSave); }
            catch { }
            try { m_simLoopEvents.Update.RemoveNonSaveable(this, onSimUpdate); }
            catch { }
            m_simLoopEvents = null;
        }

        if (m_saveManager != null)
        {
            try { m_saveManager.OnSaveDone -= onSaveDone; }
            catch { }
            m_saveManager = null;
        }
    }

    private void RegisterAutoHelpersLocalizationLateApply(DependencyResolver resolver)
    {
        IGameLoopEvents gameLoopEvents = resolver.Resolve<IGameLoopEvents>();
        gameLoopEvents.RegisterRendererInitState(this, () =>
        {
            AutoDepthDesignation.s_log.Info($"AutoTerrainDesignations v{ModVersion} | dll: {ModLogger.GetDllBuildTimestamp(typeof(AutoTerrainDesignationsMod).Assembly)}");
            AutoDepthDesignation.s_log.Info("Localization: late apply at renderer init state.");
            ApplyAutoHelpersLocalization();
            RegisterSettingsTabs(resolver);
        });
    }

    private static void RegisterSettingsTabs(DependencyResolver resolver)
    {
        try
        {
            ModSettings.EnsureInitialized(
                resolver.Resolve<HudController>(),
                resolver.Resolve<UiRoot>(),
                resolver.Resolve<IRootEscapeManager>());

            ModSettings.RegisterTab(AtdModSettingsTab.BuildDefaultsTab());
            ModSettings.RegisterTab(AtdModSettingsTab.BuildGameSettingsTab());
            ModSettings.RegisterTab(AtdModSettingsTab.BuildOreQualityTab());
        }
        catch (Exception ex)
        {
            AutoDepthDesignation.s_log.Exception(ex, "ATD settings tab registration");
        }
    }

    private void ApplyAutoHelpersLocalization()
    {
        string translationsDirectory = Path.Combine(Manifest.RootDirectoryPath, "translations");
        AutoDepthDesignation.s_log.Info($"Localization: probing directory '{translationsDirectory}'.");

        if (!Directory.Exists(translationsDirectory))
        {
            AutoDepthDesignation.s_log.Warning("Localization: translations directory does not exist; skipping.");
            return;
        }

        string[] jsonFiles = Directory.GetFiles(translationsDirectory, "*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(jsonFiles, StringComparer.OrdinalIgnoreCase);
        if (jsonFiles.Length == 0)
            AutoDepthDesignation.s_log.Warning("Localization: no translation JSON files found.");
        else
            AutoDepthDesignation.s_log.Info($"Localization: discovered {jsonFiles.Length} file(s): {string.Join(", ", jsonFiles)}");

        string currentCulture;
        try { currentCulture = LocalizationManager.CurrentLangInfo.CultureInfoId; }
        catch { currentCulture = "<unavailable>"; }
        AutoDepthDesignation.s_log.Info($"Localization: current game culture before apply = '{currentCulture}'.");

        ModTranslationsApplyResult result = new ModTranslations().Apply(new ModTranslationsApplyOptions(
            translationsDirectory,
            typeof(AutoTerrainDesignationsMod).Assembly,
            Array.Empty<string>()));

        AutoDepthDesignation.s_log.Info(
            $"Localization: applied locale='{result.AppliedLocaleCode}', upserted={result.UpsertedEntryCount}, scannedFields={result.ScannedFieldCount}, reboundFields={result.ReboundFieldCount}, readonlySkipped={result.SkippedReadonlyFieldCount}, missingTranslationSkipped={result.SkippedMissingTranslationFieldCount}, failedWrites={result.FailedFieldCount}, diagnostics={result.Diagnostics.Count}.");

        foreach (TranslationDiagnostic diagnostic in result.Diagnostics)
        {
            string itemInfo = diagnostic.ItemIndex.HasValue ? $", itemIndex={diagnostic.ItemIndex.Value}" : string.Empty;
            string message = $"Localization diagnostic [{diagnostic.Severity}] source='{diagnostic.SourcePath}'{itemInfo}: {diagnostic.Message}";
            if (diagnostic.Severity == TranslationDiagnosticSeverity.Info)
                AutoDepthDesignation.s_log.Info(message);
            else
                AutoDepthDesignation.s_log.Warning(message);
        }
    }

    public void MigrateJsonConfig(VersionSlim savedVersion, Dict<string, object> savedValues)
    {
    }

    public void Dispose()
    {
        unsubscribeWorldEvents();
        AutoTerrainDesignationsTicker.DestroyActive();
        AutoDepthDesignation.ResetWorldRuntimeState();
        m_harmony?.UnpatchAll("com.auto-terrain-designations.mod");
    }
}
