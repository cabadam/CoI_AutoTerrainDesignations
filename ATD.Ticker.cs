// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/modification; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
using UnityEngine;

namespace AutoTerrainDesignations;

public sealed class AutoTerrainDesignationsTicker : MonoBehaviour
{
    private static AutoTerrainDesignationsTicker? s_activeTicker;

    private float _prioritySyncTimer;
    private float _farmingSyncTimer;
    private int _worldGeneration;
    private bool _active;
    private const float ACTIVE_SYNC_INTERVAL_SECONDS = 1f;
    private const float PAUSED_SYNC_INTERVAL_SECONDS = 0.1f;
    private const float FARMING_SYNC_INTERVAL_GAME_SECONDS = 1f;

    internal static AutoTerrainDesignationsTicker CreateForWorld(int worldGeneration)
    {
        DestroyActive();

        AutoTerrainDesignationsTicker ticker = new GameObject("AutoTerrainDesignationsTicker").AddComponent<AutoTerrainDesignationsTicker>();
        ticker._worldGeneration = worldGeneration;
        ticker._active = true;
        s_activeTicker = ticker;
        Object.DontDestroyOnLoad(ticker.gameObject);
        return ticker;
    }

    internal static void DestroyActive()
    {
        if (s_activeTicker == null)
            return;

        AutoTerrainDesignationsTicker ticker = s_activeTicker;
        s_activeTicker = null;
        ticker._active = false;
        if (ticker != null)
            Object.Destroy(ticker.gameObject);
    }

    private void Update()
    {
        if (!_active || !AutoDepthDesignation.IsWorldGenerationActive(_worldGeneration))
            return;

        // Corner designation input — runs every frame, before the 1-second throttle.
        try
        {
            AutoDepthDesignation.HandleCornerModeInput();
            AutoDepthDesignation.UpdateCornerPreview();
        }
        catch { }

        bool gamePaused = Time.timeScale <= 0.001f;
        float syncInterval = gamePaused ? PAUSED_SYNC_INTERVAL_SECONDS : ACTIVE_SYNC_INTERVAL_SECONDS;
        _prioritySyncTimer += Time.unscaledDeltaTime;
        if (_prioritySyncTimer >= syncInterval)
        {
            _prioritySyncTimer = 0f;
            try
            {
                AutoDepthDesignation.ApplyPriorityToNewExcavators();
            }
            catch { }
        }

        if (gamePaused)
            return;

        _farmingSyncTimer += Time.deltaTime;
        if (_farmingSyncTimer < FARMING_SYNC_INTERVAL_GAME_SECONDS)
            return;
        _farmingSyncTimer = 0f;
        try
        {
            AutoDepthDesignation.TickFarmingPreparationSessions();
        }
        catch { }
    }

    private void OnGUI()
    {
    }

    private void OnDestroy()
    {
        if (s_activeTicker == this)
            s_activeTicker = null;
        _active = false;
    }
}
