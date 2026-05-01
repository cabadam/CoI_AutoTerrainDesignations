// Copyright (c) 2026 Kayser
// SPDX-License-Identifier: MIT
using UnityEngine;

namespace AutoTerrainDesignations;

public sealed class AutoTerrainDesignationsTicker : MonoBehaviour
{
    private float _syncTimer;

    private void Update()
    {
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
}
