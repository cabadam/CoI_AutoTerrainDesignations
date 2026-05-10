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

namespace AutoTerrainDesignations;

/// <summary>
/// Selects which automatic designation workflow the terrain panel runs.
/// </summary>
public enum DesignationMode
{
    /// <summary>Scan resources and place mining designations around detected deposits.</summary>
    ResourceMining = 0,

    /// <summary>Ignore resources and fill the tower area at the configured elevation.</summary>
    Flattening = 1,
}

/// <summary>
/// Selects which terrain designation proto flattening mode places.
/// </summary>
public enum FlatteningDesignationType
{
    /// <summary>Use mining designations for the flattened area.</summary>
    Mining = 0,

    /// <summary>Use dumping designations for the flattened area.</summary>
    Dumping = 1,

    /// <summary>Use leveling designations for the flattened area.</summary>
    Leveling = 2,
}

public sealed class AutoTerrainDesignationsMod : IMod, IDisposable
{
    private Harmony? m_harmony;
    private IGameLoopEvents? m_gameLoopEvents;
    private ISimLoopEvents? m_simLoopEvents;
    private ISaveManager? m_saveManager;

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
        JsonConfig = new ModJsonConfig();
    }

    public void RegisterPrototypes(ProtoRegistrator registrator)
    {
        m_harmony = new Harmony("com.auto-terrain-designations.mod");
        AutoDepthDesignation.ApplyInspectorPatches(m_harmony);
        AutoDepthDesignation.ApplyCornerPatches(m_harmony);
        AutoDepthDesignation.ApplyVehicleDepotPatches(m_harmony);

        AtdNotifications.RegisterPrototypes(registrator);
    }

    public void RegisterDependencies(DependencyResolverBuilder depBuilder, ProtosDb protosDb, bool gameWasLoaded)
    {
    }

    public void EarlyInit(DependencyResolver resolver)
    {
    }

    public static int MaxHeightDiff { get; private set; } = 1;

    /// <summary>Automatic designation behavior.</summary>
    public static DesignationMode DesignationMode { get; private set; } = DesignationMode.ResourceMining;

    /// <summary>Designation proto used by flattening mode.</summary>
    public static FlatteningDesignationType FlatteningDesignationType { get; private set; } = FlatteningDesignationType.Mining;

    public static void ResetGlobalDefaults()
    {
        SetDesignationMode(DesignationMode.ResourceMining);
        SetFlatteningDesignationType(FlatteningDesignationType.Mining);
        SetMaxHeightDiff(1);
        SetRampWidth(2);
        SetMaxLayersToExcavate(30);
        SetMaxDepthToDigTo(null);
        SetOrePurityLevel(0);
        SetBottomFlatteningEnabled(true);
        SetMinCorridorClearance(2);
        SetTerrainDesignationsPanelCollapsed(false);
        SetOreCompositionPanelCollapsed(false);
        SetReEnableFarmingOnLoad(true);
        SetExcavatorCompletionNotificationsEnabled(true);
    }

    /// <summary>Sets the global default designation mode.</summary>
    /// <param name="value">Designation workflow to use for newly initialized tower settings.</param>
    public static void SetDesignationMode(DesignationMode value)
    {
        DesignationMode = value;
    }

    /// <summary>Sets the global default designation mode from its numeric value.</summary>
    /// <param name="value">Numeric designation workflow value, clamped to the supported enum range.</param>
    public static void SetDesignationMode(int value)
    {
        SetDesignationMode((DesignationMode)Math.Max(0, Math.Min(1, value)));
    }

    /// <summary>Sets the global default designation type used by flattening mode.</summary>
    /// <param name="value">Designation type to place when flattening mode is selected.</param>
    public static void SetFlatteningDesignationType(FlatteningDesignationType value)
    {
        FlatteningDesignationType = value;
    }

    /// <summary>Sets the global default designation type used by flattening mode from its numeric value.</summary>
    /// <param name="value">Numeric designation type value, clamped to the supported enum range.</param>
    public static void SetFlatteningDesignationType(int value)
    {
        SetFlatteningDesignationType((FlatteningDesignationType)Math.Max(0, Math.Min(2, value)));
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

    /// <summary>Default collapsed state for the Terrain Designations inspector panel.</summary>
    public static bool TerrainDesignationsPanelCollapsed { get; private set; } = false;

    public static void SetTerrainDesignationsPanelCollapsed(bool value)
    {
        TerrainDesignationsPanelCollapsed = value;
    }

    /// <summary>Default collapsed state for the Ore Composition inspector panel.</summary>
    public static bool OreCompositionPanelCollapsed { get; private set; } = false;

    public static void SetOreCompositionPanelCollapsed(bool value)
    {
        OreCompositionPanelCollapsed = value;
    }

    /// <summary>Whether ATD re-enables farming automation on loaded towers that look like farmland work.</summary>
    public static bool ReEnableFarmingOnLoad { get; private set; } = true;

    public static void SetReEnableFarmingOnLoad(bool value)
    {
        ReEnableFarmingOnLoad = value;
    }

    /// <summary>Whether ATD shows a green notification when a vehicle depot completes an excavator.</summary>
    public static bool ExcavatorCompletionNotificationsEnabled { get; private set; } = true;

    public static void SetExcavatorCompletionNotificationsEnabled(bool value)
    {
        ExcavatorCompletionNotificationsEnabled = value;
    }

    public void Initialize(DependencyResolver resolver, bool gameWasLoaded)
    {
        try
        {
            // Enable console logging for easier debugging
            ConsoleLogger.Enable();
            AutoTerrainDesignationsTicker.DestroyActive();

            RegisterAutoHelpersLocalizationLateApply(resolver);

#if DEBUG
            // Auto-enable Mafi console mirroring in Debug builds so logs show up in-game
            // without requiring a manual `also_log_to_console` command each launch.
            var gameLoopEvents = resolver.Resolve<IGameLoopEvents>();
            var consoleCommands = resolver.Resolve<GameConsoleCommandsExecutor>();
            gameLoopEvents.RegisterRendererInitState(this, () =>
            {
                bool enabled = consoleCommands.ExecuteOrSchedule("also_log_to_console true");
                if (enabled)
                    Log.Info("[ATD] Debug build: auto-executed also_log_to_console.");
                else
                    Log.Warning("[ATD] Debug build: failed to auto-execute also_log_to_console.");
            });
#endif

            m_gameLoopEvents = resolver.Resolve<IGameLoopEvents>();
            m_simLoopEvents = resolver.Resolve<ISimLoopEvents>();
            m_saveManager = resolver.Resolve<ISaveManager>();
            m_gameLoopEvents.Terminate.AddNonSaveable(this, onGameTerminated);
            m_simLoopEvents.BeforeSave.AddNonSaveable(this, beforeSave);
            m_saveManager.OnSaveDone += onSaveDone;

            ITerrainDesignationsManager desigManager = resolver.Resolve<ITerrainDesignationsManager>();
            ProtosDb protosDb = resolver.Resolve<ProtosDb>();
            IWorldMapManager worldMapManager = resolver.Resolve<IWorldMapManager>();
            IEntitiesManager entitiesManager = resolver.Resolve<IEntitiesManager>();
            TerrainPropsManager terrainPropsManager = resolver.Resolve<TerrainPropsManager>();
            IVehiclePathFindingManager vehiclePathFindingManager = resolver.Resolve<IVehiclePathFindingManager>();
            ParkAndWaitJobFactory parkAndWaitJobFactory = resolver.Resolve<ParkAndWaitJobFactory>();
            INotificationsManager notificationsManager = resolver.Resolve<INotificationsManager>();
            AutoTerrainDesignationsTicker ticker = AutoTerrainDesignationsTicker.CreateForWorld(AutoDepthDesignation.CurrentWorldGeneration + 1);
            AutoDepthDesignation.SetModRootDirectoryPath(Manifest.RootDirectoryPath);
            AutoDepthDesignation.Initialize(desigManager, protosDb, worldMapManager, ticker, entitiesManager, terrainPropsManager, vehiclePathFindingManager, parkAndWaitJobFactory, notificationsManager);
            AutoDepthDesignation.RequestFarmingReEnableOnLoad(gameWasLoaded);

            // Corner designation mode — TerrainCursor, TerrainDesignationsRenderer and
            // CursorManager may only be available on the Unity side; fail gracefully if not resolvable.
            TerrainCursor? terrainCursor = null;
            TerrainDesignationsRenderer? desigRenderer = null;
            CursorManager? cursorManager = null;
            try { terrainCursor = resolver.Resolve<TerrainCursor>(); }
            catch (Exception ex2) { Log.Warning("[ATD] TerrainCursor not available: " + ex2.Message); }
            try { desigRenderer = resolver.Resolve<TerrainDesignationsRenderer>(); }
            catch (Exception ex3) { Log.Warning("[ATD] TerrainDesignationsRenderer not available: " + ex3.Message); }
            try { cursorManager = resolver.Resolve<CursorManager>(); }
            catch (Exception ex4) { Log.Warning("[ATD] CursorManager not available: " + ex4.Message); }
            AutoDepthDesignation.InitializeCornerMode(terrainCursor, desigRenderer, cursorManager);
        }
        catch (Exception ex)
        {
            unsubscribeWorldEvents();
            AutoTerrainDesignationsTicker.DestroyActive();
            AutoDepthDesignation.ResetWorldRuntimeState();
            Log.Exception(ex, "[ATD] AutoTerrainDesignations init");
        }
    }

    private void beforeSave()
    {
        AutoDepthDesignation.PurgeTransientNotificationsForSave();
        AutoDepthDesignation.RestoreFarmingRuntimeForSave();
    }

    private void onSaveDone(SaveResult result)
    {
        AutoDepthDesignation.ResumeFarmingRuntimeAfterSave();
        AutoDepthDesignation.RestoreTransientNotificationsAfterSave();
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
            Log.Info("[ATD] Localization: late apply at renderer init state.");
            ApplyAutoHelpersLocalization();
        });
    }

    private void ApplyAutoHelpersLocalization()
    {
        string translationsDirectory = Path.Combine(Manifest.RootDirectoryPath, "translations");
        Log.Info($"[ATD] Localization: probing directory '{translationsDirectory}'.");

        if (!Directory.Exists(translationsDirectory))
        {
            Log.Warning("[ATD] Localization: translations directory does not exist; skipping.");
            return;
        }

        string[] jsonFiles = Directory.GetFiles(translationsDirectory, "*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(jsonFiles, StringComparer.OrdinalIgnoreCase);
        if (jsonFiles.Length == 0)
            Log.Warning("[ATD] Localization: no translation JSON files found.");
        else
            Log.Info($"[ATD] Localization: discovered {jsonFiles.Length} file(s): {string.Join(", ", jsonFiles)}");

        string currentCulture;
        try { currentCulture = LocalizationManager.CurrentLangInfo.CultureInfoId; }
        catch { currentCulture = "<unavailable>"; }
        Log.Info($"[ATD] Localization: current game culture before apply = '{currentCulture}'.");

        ModTranslationsApplyResult result = new ModTranslations().Apply(new ModTranslationsApplyOptions(
            translationsDirectory,
            typeof(AutoTerrainDesignationsMod).Assembly,
            Array.Empty<string>()));

        Log.Info(
            $"[ATD] Localization: applied locale='{result.AppliedLocaleCode}', upserted={result.UpsertedEntryCount}, scannedFields={result.ScannedFieldCount}, reboundFields={result.ReboundFieldCount}, readonlySkipped={result.SkippedReadonlyFieldCount}, missingTranslationSkipped={result.SkippedMissingTranslationFieldCount}, failedWrites={result.FailedFieldCount}, diagnostics={result.Diagnostics.Count}.");

        foreach (TranslationDiagnostic diagnostic in result.Diagnostics)
        {
            string itemInfo = diagnostic.ItemIndex.HasValue ? $", itemIndex={diagnostic.ItemIndex.Value}" : string.Empty;
            string message = $"[ATD] Localization diagnostic [{diagnostic.Severity}] source='{diagnostic.SourcePath}'{itemInfo}: {diagnostic.Message}";
            if (diagnostic.Severity == TranslationDiagnosticSeverity.Info)
                Log.Info(message);
            else
                Log.Warning(message);
        }
    }

    public void MigrateJsonConfig(VersionSlim savedVersion, Dict<string, object> savedValues)
    {
        savedValues.Clear();
    }

    public void Dispose()
    {
        unsubscribeWorldEvents();
        AutoTerrainDesignationsTicker.DestroyActive();
        AutoDepthDesignation.ResetWorldRuntimeState();
        m_harmony?.UnpatchAll("com.auto-terrain-designations.mod");
    }
}
