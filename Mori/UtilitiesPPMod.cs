using System;
using System.IO;
using HarmonyLib;
using Mafi;
using Mafi.Collections;
using Mafi.Core.Game;
using Mafi.Core.Mods;
using Mafi.Core.Prototypes;
using Mafi.Core.SaveGame;
using UnityEngine;

namespace UtilitiesPP
{
    public sealed class UtilitiesPPMod : IMod
    {
        public string Name => "Utilities++";
        public int Version => 1;
        public bool IsUiOnly => false;
        public Option<IConfig> ModConfig { get; set; }
        public ModManifest Manifest { get; }
        public ModJsonConfig JsonConfig { get; }

        public static string ModDir { get; private set; }

        private Harmony m_harmony;

        public UtilitiesPPMod(ModManifest manifest)
        {
            Manifest = manifest;
            ModDir = manifest.RootDirectoryPath;
            EnsureConfigExists(manifest.RootDirectoryPath);
            JsonConfig = new ModJsonConfig(this);
        }

        private static void EnsureConfigExists(string modDir)
        {
            var path = Path.Combine(modDir, "config.json");
            if (File.Exists(path)) return;
            File.WriteAllText(path, @"{
    ""message"": { ""default"": 0, ""is_integer"": true, ""description"": ""Utility belt not included. Batman was already using it."", ""min"": 0, ""max"": 0 },
    ""placebo"": { ""default"": 0, ""is_integer"": true, ""description"": ""Robin tried to change this setting. Batman said it does nothing. Robin left it at 0 anyway."", ""min"": 0, ""max"": 0 },
    ""surplusBlueprintMode"": { ""default"": 0, ""is_integer"": true, ""description"": ""Surplus Power blueprint override (0=Default, 1=Force ON, 2=Force OFF). Set in-game via the Utilities++ window or Ctrl+S."", ""min"": 0, ""max"": 2 }
}");
        }

        public void RegisterPrototypes(ProtoRegistrator registrator)
        {
            ModTranslation.Initialize(Manifest.RootDirectoryPath);
            HeightFilterState.Init(Manifest.RootDirectoryPath);

            m_harmony = new Harmony("com.utilities-plus-plus.mod");
            HeightFilterPatch.ApplyPatches(m_harmony);
            SurplusBlueprintPatch.ApplyPatches(m_harmony);
        }

        public void RegisterDependencies(DependencyResolverBuilder depBuilder,
            ProtosDb protosDb, bool gameWasLoaded)
        {
            depBuilder.RegisterDependency<UtilitiesPPManager>().AsSelf();
        }

        public void EarlyInit(DependencyResolver resolver) { }
        public void MigrateJsonConfig(VersionSlim savedVersion, Dict<string, object> savedValues) { }

        public void Initialize(DependencyResolver resolver, bool gameWasLoaded)
        {
            var saveManager = resolver.Resolve<ISaveManager>();
            HeightFilterState.SetSaveName(saveManager.GameName);
            var manager = resolver.Resolve<UtilitiesPPManager>();
            HeightFilterPatch.LateInit(resolver);

            var savedMode = JsonConfig.GetInt("surplusBlueprintMode");
            if (savedMode >= 0 && savedMode <= 2)
                UtilitiesPPManager.SurplusBlueprintMode = savedMode;

            try
            {
                var unityAsm = typeof(Mafi.Unity.UiToolkit.Component.UiComponent).Assembly;
                var tcType = unityAsm.GetType("Mafi.Unity.InputControl.TerrainCursor");
                if (tcType != null)
                {
                    var opt = resolver.TryResolve(tcType);
                    if (opt.HasValue)
                        manager.DragTool?.SetTerrainCursor(opt.Value);
                }

                var hlType = unityAsm.GetType("Mafi.Unity.Entities.EntityHighlighter");
                var syncerType = unityAsm.GetType("Mafi.Unity.Entities.EntityHighlighter+EntityHighlightersSyncer");
                var ermType = unityAsm.GetType("Mafi.Unity.Entities.EntitiesRenderingManager");
                if (hlType != null && syncerType != null && ermType != null)
                {
                    var syncerOpt = resolver.TryResolve(syncerType);
                    var ermOpt = resolver.TryResolve(ermType);
                    if (syncerOpt.HasValue && ermOpt.HasValue)
                    {
                        var highlighter = Activator.CreateInstance(hlType, ermOpt.Value, syncerOpt.Value);
                        manager.DragTool?.SetHighlighter(highlighter);
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log($"[U++] TerrainCursor resolve EXCEPTION: {ex}");
            }
        }

        public void Dispose()
        {
            m_harmony?.UnpatchAll("com.utilities-plus-plus.mod");
        }
    }
}
