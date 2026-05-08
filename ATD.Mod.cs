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
using Mafi.Collections;
using Mafi.Core.Entities;
using Mafi.Core.Game;
using Mafi.Core.GameLoop;
using Mafi.Core.Mods;
using Mafi.Core.Prototypes;
using Mafi.Core.Console;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Terrain.Props;
using Mafi.Core.World;
using Mafi.Unity.InputControl;
using Mafi.Unity.Terrain.Designation;
using UnityEngine;

namespace AutoTerrainDesignations;

public sealed class AutoTerrainDesignationsMod : IMod, IDisposable
{
    private Harmony? m_harmony;

    public string Name => "Auto Terrain Designations";

    public int Version => 1;

    public bool IsUiOnly => false;

    public Option<IConfig> ModConfig { get; set; }

    public ModManifest Manifest { get; }

    public static string ModVersion { get; private set; } = "?";

    public static string ModMarker => $"Kayser's AutoTerrainDesignations v{ModVersion}";

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
    }

    public void RegisterDependencies(DependencyResolverBuilder depBuilder, ProtosDb protosDb, bool gameWasLoaded)
    {
    }

    public void EarlyInit(DependencyResolver resolver)
    {
    }

    public static int MaxHeightDiff { get; private set; } = 1;

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

    public void Initialize(DependencyResolver resolver, bool gameWasLoaded)
    {
        try
        {
            // Enable console logging for easier debugging
            ConsoleLogger.Enable();

#if DEBUG
            // Auto-enable Mafi console mirroring in Debug builds so logs show up in-game
            // without requiring a manual `also_log_to_console` command each launch.
            var gameLoopEvents = resolver.Resolve<IGameLoopEvents>();
            var consoleCommands = resolver.Resolve<GameConsoleCommandsExecutor>();
            gameLoopEvents.RegisterRendererInitState(this, () =>
            {
                bool enabled = consoleCommands.ExecuteOrSchedule("also_log_to_console true");
                if (enabled)
                    Debug.Log("[ATD] Debug build: auto-executed also_log_to_console.");
                else
                    Debug.LogWarning("[ATD] Debug build: failed to auto-execute also_log_to_console.");
            });
#endif

            ITerrainDesignationsManager desigManager = resolver.Resolve<ITerrainDesignationsManager>();
            ProtosDb protosDb = resolver.Resolve<ProtosDb>();
            IWorldMapManager worldMapManager = resolver.Resolve<IWorldMapManager>();
            IEntitiesManager entitiesManager = resolver.Resolve<IEntitiesManager>();
            TerrainPropsManager terrainPropsManager = resolver.Resolve<TerrainPropsManager>();
            AutoTerrainDesignationsTicker ticker = new GameObject("AutoTerrainDesignationsTicker").AddComponent<AutoTerrainDesignationsTicker>();
            UnityEngine.Object.DontDestroyOnLoad(ticker.gameObject);
            AutoDepthDesignation.SetModRootDirectoryPath(Manifest.RootDirectoryPath);
            AutoDepthDesignation.Initialize(desigManager, protosDb, worldMapManager, ticker, entitiesManager, terrainPropsManager);

            // Corner designation mode — TerrainCursor and TerrainDesignationsRenderer may only
            // be available on the Unity side; fail gracefully if not resolvable.
            TerrainCursor? terrainCursor = null;
            TerrainDesignationsRenderer? desigRenderer = null;
            try { terrainCursor = resolver.Resolve<TerrainCursor>(); }
            catch (Exception ex2) { Debug.LogWarning("[ATD] TerrainCursor not available: " + ex2.Message); }
            try { desigRenderer = resolver.Resolve<TerrainDesignationsRenderer>(); }
            catch (Exception ex3) { Debug.LogWarning("[ATD] TerrainDesignationsRenderer not available: " + ex3.Message); }
            AutoDepthDesignation.InitializeCornerMode(terrainCursor, desigRenderer);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[ATD] AutoTerrainDesignations init: " + ex.Message);
        }
    }

    public void MigrateJsonConfig(VersionSlim savedVersion, Dict<string, object> savedValues)
    {
        savedValues.Clear();
    }

    public void Dispose()
    {
        m_harmony?.UnpatchAll("com.auto-terrain-designations.mod");
    }
}
