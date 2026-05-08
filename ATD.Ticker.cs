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
    private float _syncTimer;

    private void Update()
    {
        // Corner designation input — runs every frame, before the 1-second throttle.
        try
        {
            AutoDepthDesignation.HandleCornerModeInput();
            AutoDepthDesignation.UpdateCornerPreview();
        }
        catch { }

        _syncTimer += Time.deltaTime;
        if (_syncTimer < 1f)
            return;
        _syncTimer = 0f;
        try
        {
            AutoDepthDesignation.ApplyPriorityToNewExcavators();
        }
        catch { }
    }

    private void OnGUI()
    {
        try
        {
            AutoDepthDesignation.DrawCornerModeHud();
        }
        catch { }
    }
}
