// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/modification; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
// Auto Terrain Designations - Debug/diagnostic helpers (not part of the published feature set)
using Mafi;
using UnityEngine;

namespace AutoTerrainDesignations;

partial class AutoDepthDesignation
{
    // Whether the cursor tile-position debug overlay is visible.
    internal static bool ShowCursorOverlay;

    // Returns the terrain tile currently under the mouse cursor.
    internal static bool TryGetCursorTile(out Tile3f tile)
    {
        tile = default;
        if (s_terrainCursor == null) return false;
        return s_terrainCursor.TryComputeTerrainPosition(Input.mousePosition, out tile);
    }

    private static GUIStyle? s_tileOverlayStyle;

    internal static void DrawCursorOverlay(bool tickerActive, int worldGeneration)
    {
        if (!ShowCursorOverlay) return;
        if (!tickerActive || !IsWorldGenerationActive(worldGeneration)) return;
        if (!TryGetCursorTile(out Tile3f pos)) return;

        s_tileOverlayStyle ??= new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 13,
            normal = { textColor = Color.white },
        };

        Tile2i xy = pos.Xy.Tile2i;
        int z = pos.Z.ToIntRounded();
        GUI.Box(new Rect(10f, Screen.height - 36f, 190f, 26f),
            $"  ({xy.X}, {xy.Y}, {z})", s_tileOverlayStyle);
    }
}
