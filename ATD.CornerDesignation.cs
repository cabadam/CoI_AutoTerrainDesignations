// Auto Terrain Designations
// Copyright (c) 2026 Kayser
// Licensed under the MIT License.
//
// Unofficial mod for Captain of Industry. Captain of Industry, MaFi Games, and
// related trademarks, code, and assets belong to MaFi Games. This repository is
// intended to contain only original mod code/configuration; if MaFi Games material
// is included by mistake, I intend to correct it promptly upon discovery or notice.
// Auto Terrain Designations - Corner Designation Placement
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mafi;
using Mafi.Unity.Ui.Controllers.Designations;
using Mafi.Unity.Ui.Hud;
using Mafi.Unity.Ui.Library;
using Mafi.Unity.UiStatic.Controllers;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;
using Mafi.Unity.InputControl;
using Mafi.Unity.Terrain.Designation;
using Mafi.Unity.UiStatic;
using Mafi.Unity.UiStatic.Cursors;
using Mafi.Localization;
using UnityEngine;

namespace AutoTerrainDesignations
{
    public static partial class AutoDepthDesignation
    {
        // One corner of the 4x4 tile raised (+1) or lowered (-1) relative to the other three.
        // Rotation index (0-3) encodes which corner: 0=Origin(NW), 1=PlusX(NE), 2=PlusXY(SE), 3=PlusY(SW).
        internal enum CornerVariant
        {
            // Outer: one corner at base+1
            OriginHigh  = 0,
            PlusXHigh   = 1,
            PlusXyHigh  = 2,
            PlusYHigh   = 3,
            // Inner: one corner at base-1
            OriginLow   = 4,
            PlusXLow    = 5,
            PlusXyLow   = 6,
            PlusYLow    = 7,
        }

        private static bool s_cornerModeActive;
        private static CornerVariant s_activeCornerVariant;
        private static TerrainCursor? s_terrainCursor;

        // AOE drag state.
        private static bool s_dragging;
        private static Tile2i s_dragOrigin;                  // snap-aligned tile where LMB went down
        private static int s_dragBaseHeight;                 // height at drag start (snapped or terrain+bias)
        private static CornerVariant s_dragEffectiveVariant; // variant at drag start (snapped or user-selected)

        // True while the player has any terrain designation tool active (M/Z/N-mode).
        private static bool s_designationToolActive;
        // The live TerrainDesignationController instance (needed to read m_heightBias).
        private static object? s_miningController;
        private static System.Reflection.FieldInfo? s_heightBiasField;
        // The apply sound of the active controller, reflected once on activate.
        private static object? s_applySound;
        private static System.Reflection.FieldInfo? s_applySoundField;
        private static System.Reflection.MethodInfo? s_audioPlayMethod;
        // The tab-switch sound (F-mode flip) — played when entering K-mode or toggling inner/outer.
        private static object? s_switchSound;
        private static System.Reflection.FieldInfo? s_switchSoundField;

        // Tiles where ATD just placed a corner designation — protected from one subsequent
        // overwrite by the game's async placement command.
        private static readonly HashSet<Tile2i> s_protectedCornerTiles = new HashSet<Tile2i>();

        // Preview rendering.
        private static TerrainDesignationsRenderer? s_desigRenderer;
        private static readonly HashSet<Tile2i> s_previewedTiles = new HashSet<Tile2i>();

        // Custom cursor shown while a valid corner preview is displayed.
        private static Cursoor? s_cornerCursor;

        // Toolbar buttons injected into the game's designation toolboxes (one per ToolType).
        private static ToolboxItem? s_cornerModeButtonOuter; // ExpandScreen as-is
        private static ToolboxItem? s_cornerModeButtonInner; // ExpandScreen rotated 180°
        private static AreaToolbox? s_activeAreaToolbox;
        private static int s_currentAreaMode;
        private static FieldInfo? s_areaToolboxButtonsField;
        // The designation proto to use when placing corners (switches per active tool).
        private static TerrainDesignationProto? s_activeCornerProto;
        // Per-toolbox state keyed by ToolType int (0=Ramp/Mining, 1=Flat/Dumping, 2=Leveling).
        private static readonly Dictionary<int, (AreaToolbox toolbox, ToolboxItem outerBtn, ToolboxItem innerBtn, FieldInfo? buttonsField)>
            s_toolboxes = new Dictionary<int, (AreaToolbox, ToolboxItem, ToolboxItem, FieldInfo?)>();
        private static readonly Dictionary<string, int> s_controllerToToolType = new Dictionary<string, int>
        {
            { "MiningDesignationController",       (int)AreaToolbox.ToolType.Mining },
            { "DumpingDesignationController",      (int)AreaToolbox.ToolType.Dumping },
            { "LevelTerrainDesignationController", (int)AreaToolbox.ToolType.Leveling },
        };
        // True while ATD itself is calling AddOrUpdatePreviewDesignation, to distinguish
        // our calls from the game's own preview calls (which we suppress in corner mode).
        private static bool s_atdPreviewActive;

        internal static void InitializeCornerMode(TerrainCursor? terrainCursor, TerrainDesignationsRenderer? renderer, CursorManager? cursorManager)
        {
            s_terrainCursor = terrainCursor;
            s_desigRenderer = renderer;
            s_cornerModeActive = false;
            s_activeCornerVariant = CornerVariant.OriginHigh;
            if (cursorManager != null)
            {
                try { s_cornerCursor = cursorManager.RegisterCursor(CursorsStyles.Add); }
                catch (Exception ex) { Log.Warning("[ATD] Failed to register corner cursor: " + ex.Message); }
            }
        }

        // Called every frame from AutoTerrainDesignationsTicker.Update().
        internal static void HandleCornerModeInput()
        {
            // K: only activates when the player is in M-mode (mining designation tool active).
            // If already in corner mode, K toggles inner/outer regardless.
            if (Input.GetKeyDown(KeyCode.K))
            {
                if (s_cornerModeActive)
                {
                    s_activeCornerVariant = ToggleCornerType(s_activeCornerVariant);
                    UpdateCornerButtonSelection();
                    PlaySwitchSound();
                }
                else if (s_designationToolActive)
                    EnterCornerMode();
                return;
            }

            if (!s_cornerModeActive)
                return;

            // F: exit corner mode.
            if (Input.GetKeyDown(KeyCode.F))
            {
                ExitCornerMode();
                return;
            }

            // R: rotate among the 4 orientations within the current type.
            if (Input.GetKeyDown(KeyCode.R))
            {
                s_activeCornerVariant = RotateCornerVariant(s_activeCornerVariant);
                return;
            }

            // LMB down: start drag.
            if (Input.GetMouseButtonDown(0))
            {
                TryBeginDrag();
                return;
            }

            // LMB up: commit fill.
            if (s_dragging && Input.GetMouseButtonUp(0))
            {
                TryCommitDrag();
                return;
            }
        }

        // Called every frame from AutoTerrainDesignationsTicker.OnGUI() — draws the mode status bar.
        // -- Harmony patch handlers --

        // Postfix on TerrainDesignationController.Activate — sets s_miningToolActive when the
        // player activates the Mining designation tool specifically.
        public static void DesignationToolActivatePostfix(object __instance)
        {
            var typeName = __instance.GetType().Name;
            if (!s_controllerToToolType.TryGetValue(typeName, out _)) return;

            s_designationToolActive = true;
            s_miningController = __instance;
            if (s_heightBiasField == null)
            {
                Type? t = __instance.GetType();
                while (t != null && s_heightBiasField == null)
                {
                    s_heightBiasField = t.GetField("m_heightBias",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic);
                    t = t.BaseType;
                }
            }

            // Select proto and toolbox entry matching the active controller.
            s_activeCornerProto = typeName switch
            {
                "DumpingDesignationController"      => s_dumpingProto,
                "LevelTerrainDesignationController" => s_levelingProto,
                _                                   => s_miningProto,
            };

            if (s_controllerToToolType.TryGetValue(typeName, out int toolTypeInt) &&
                s_toolboxes.TryGetValue(toolTypeInt, out var entry))
            {
                s_activeAreaToolbox       = entry.toolbox;
                s_cornerModeButtonOuter   = entry.outerBtn;
                s_cornerModeButtonInner   = entry.innerBtn;
                s_areaToolboxButtonsField = entry.buttonsField;
                s_currentAreaMode         = 0;
            }

            // Reflect m_miningApplySound once per controller type.
            if (s_applySoundField == null)
            {
                Type? t = __instance.GetType();
                while (t != null && s_applySoundField == null)
                {
                    s_applySoundField = t.GetField("m_miningApplySound",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic);
                    t = t.BaseType;
                }
            }
            s_applySound = s_applySoundField?.GetValue(__instance);
            // Cache the Play() method once from the concrete AudioSource type.
            if (s_applySound != null && s_audioPlayMethod == null)
                s_audioPlayMethod = s_applySound.GetType().GetMethod("Play",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public,
                    null, System.Type.EmptyTypes, null);

            // Reflect m_switchSound (TabSwitch — played on F-mode flip) once per controller type.
            if (s_switchSoundField == null)
            {
                Type? t2 = __instance.GetType();
                while (t2 != null && s_switchSoundField == null)
                {
                    s_switchSoundField = t2.GetField("m_switchSound",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic);
                    t2 = t2.BaseType;
                }
            }
            s_switchSound = s_switchSoundField?.GetValue(__instance);

            LogDebug($"[ATD] Designation tool activated: {typeName}");
        }

        // Postfix on TerrainDesignationController.Deactivate.
        public static void DesignationToolDeactivatePostfix(object __instance)
        {
            var typeName = __instance.GetType().Name;
            if (!s_controllerToToolType.TryGetValue(typeName, out _)) return;

            s_designationToolActive = false;
            s_miningController = null;
            s_applySound = null;
            s_switchSound = null;
            // ExitCornerMode must come BEFORE nulling the button refs, so
            // UpdateCornerButtonSelection inside it can still clear their Selected state.
            if (s_cornerModeActive)
                ExitCornerMode();
            s_activeAreaToolbox       = null;
            s_cornerModeButtonOuter   = null;
            s_cornerModeButtonInner   = null;
            s_activeCornerProto       = null;
            LogDebug($"[ATD] Designation tool deactivated: {typeName}");
        }

        // Prefix on TerrainDesignationsRenderer.AddOrUpdatePreviewDesignation — suppresses the
        // game's own preview calls while ATD's corner mode is active, preventing flicker.
        // ATD's own calls are allowed through via the s_atdPreviewActive flag.
        public static bool AddOrUpdatePreviewPrefix()
        {
            return !s_cornerModeActive || s_atdPreviewActive;
        }

        // Prefix on TerrainDesignationsRenderer.RemovePreviewDesignation — suppresses the
        // game's own remove calls while ATD's corner mode is active, preventing flicker.
        public static bool RemovePreviewPrefix()
        {
            return !s_cornerModeActive || s_atdPreviewActive;
        }

        // Prefix on TerrainDesignationController.previewInitialDesignationAt — suppresses the
        // "Invalid position." tooltip while ATD's corner mode is active. The vanilla method
        // validates heights using its own snap logic, which does not know about ATD's snapping;
        // skipping it entirely keeps the cursor clean when our snap is valid.
        public static bool PreviewInitialDesignationAtPrefix(ref LocStrFormatted? error)
        {
            if (!s_cornerModeActive) return true;
            error = null;
            return false;
        }

        // Prefix on TerrainDesignationController.handleInitialState — in corner mode, only
        // suppresses the vanilla LMB-down branch (which would play m_errorSound because
        // m_initialDesignation is always null while our preview patch is active).
        // RMB (ClearDesignation) is allowed through so vanilla handles AOE undesignation
        // normally, just like in M-mode.
        public static bool HandleInitialStatePrefixForCorner(ref object? __result)
        {
            if (!s_cornerModeActive) return true;
            // Only block when LMB just went down — that's the only path that plays the error sound.
            // Let all other inputs (RMB/ClearDesignation, no input) reach vanilla normally.
            if (Input.GetMouseButtonDown(0))
            {
                __result = null;
                return false;
            }
            return true;
        }
        // async placement command when it would overwrite a corner ATD just placed.
        // Protection is one-shot: consumed on the first suppressed call per tile.
        public static bool AddOrReplaceDesignationPrefix(DesignationData data, ref bool __result)
        {
            if (s_protectedCornerTiles.Contains(data.OriginTile))
            {
                s_protectedCornerTiles.Remove(data.OriginTile);
                __result = false;
                return false; // Skip original
            }
            return true; // Run original
        }

        // -- Shape helpers --

        /// <summary>
        /// Builds DesignationData for a corner shape. All 4 corners are at <paramref name="baseHeight"/>
        /// except the one corner indicated by <paramref name="variant"/>, which is ±1.
        /// </summary>
        internal static DesignationData BuildCornerDesignationData(
            Tile2i origin, int baseHeight, CornerVariant variant)
        {
            int nw = baseHeight, ne = baseHeight, se = baseHeight, sw = baseHeight;
            int delta = IsOuterVariant(variant) ? +1 : -1;

            // Rotation index: 0=Origin(NW), 1=PlusX(NE), 2=PlusXY(SE), 3=PlusY(SW)
            switch ((int)variant % 4)
            {
                case 0: nw += delta; break;
                case 1: ne += delta; break;
                case 2: se += delta; break;
                case 3: sw += delta; break;
            }

            // Constructor order: (origin, NW/origin, NE/plusX, SE/plusXy, SW/plusY)
            return new DesignationData(origin,
                new HeightTilesI(nw), new HeightTilesI(ne),
                new HeightTilesI(se), new HeightTilesI(sw));
        }

        // -- Private helpers --

        private static void EnterCornerMode(bool preferOuter = true)
        {
            s_cornerModeActive = true;
            s_activeCornerVariant = preferOuter ? CornerVariant.OriginHigh : ToggleCornerType(CornerVariant.OriginHigh);
            UpdateCornerButtonSelection();
            PlaySwitchSound();
            // Deselect game mode buttons so K and F don't appear simultaneously active.
            if (s_activeAreaToolbox != null &&
                s_areaToolboxButtonsField?.GetValue(s_activeAreaToolbox) is ToolboxItem[] modeButtons)
                foreach (var b in modeButtons) b.Selected(false);
            LogDebug("[ATD] Corner designation mode entered.");
        }

        private static void ExitCornerMode()
        {
            // Clear ATD's preview tiles first, while s_cornerModeActive is still true
            // (so the prefix still blocks the game's own calls during cleanup).
            ClearAllPreviews();
            s_cornerCursor?.Hide();
            s_cornerModeActive = false;
            s_dragging = false;
            s_protectedCornerTiles.Clear();
            UpdateCornerButtonSelection();
            // Restore the previously active game mode button.
            if (s_activeAreaToolbox != null)
                s_activeAreaToolbox.SetMode((AreaMode)s_currentAreaMode);
            LogDebug("[ATD] Corner designation mode exited.");
        }

        /// <summary>
        /// Syncs the selected state of the two corner mode buttons to the current active variant.
        /// </summary>
        private static void UpdateCornerButtonSelection()
        {
            bool isOuter = IsOuterVariant(s_activeCornerVariant);
            s_cornerModeButtonOuter?.Selected(s_cornerModeActive && isOuter);
            s_cornerModeButtonInner?.Selected(s_cornerModeActive && !isOuter);
        }

        /// <summary>Plays TabSwitch sound via reflection (same as F-mode flip).</summary>
        private static void PlaySwitchSound()
        {
            if (s_switchSound != null)
                s_audioPlayMethod?.Invoke(s_switchSound, null);
        }

        private static void TryBeginDrag()
        {
            if (s_terrainCursor == null) return;
            if (!s_terrainCursor.TryComputeTerrainPosition(Input.mousePosition, out Tile3f pos3f)) return;

            s_dragOrigin = SnapToDesignationGrid(pos3f.Xy.Tile2i);
            var snap = TrySnapToNeighbors(s_dragOrigin);
            if (snap.HasValue)
            {
                s_dragBaseHeight = snap.Value.baseHeight;
                s_dragEffectiveVariant = snap.Value.variant;
            }
            else
            {
                s_dragBaseHeight = GetSurfaceHeight(s_desigManager!.TerrainManager, s_dragOrigin) + GetHeightBias();
                s_dragEffectiveVariant = s_activeCornerVariant;
            }
            s_dragging = true;
        }

        private static void TryCommitDrag()
        {
            s_dragging = false;
            if (s_terrainCursor == null || s_desigManager == null) return;
            if (!s_terrainCursor.TryComputeTerrainPosition(Input.mousePosition, out Tile3f pos3f)) return;

            Tile2i end = SnapToDesignationGrid(pos3f.Xy.Tile2i);
            ClearAllPreviews();
            int placed = 0;
            foreach (var (tile, v, absHeight) in ComputeCornerFill(s_dragOrigin, end, s_dragBaseHeight, s_dragEffectiveVariant))
            {
                PlaceCornerAt(tile, absHeight, v);
                placed++;
            }
            if (placed > 0 && s_applySound != null)
                s_audioPlayMethod?.Invoke(s_applySound, null);
        }

        private static void PlaceCornerAt(Tile2i origin, int baseHeight, CornerVariant variant)
        {
            if (s_desigManager == null || s_activeCornerProto == null) return;

            DesignationData data = BuildCornerDesignationData(origin, baseHeight, variant);
            s_protectedCornerTiles.Remove(origin);
            bool placed = s_desigManager.AddOrReplaceDesignation(s_activeCornerProto, data);
            if (placed)
            {
                s_protectedCornerTiles.Add(origin);
                LogDebug($"[ATD] Placed {variant} corner at ({origin.X},{origin.Y}).");
            }
        }

        // -- Preview --

        private static void ClearAllPreviews()
        {
            if (s_desigRenderer != null)
            {
                s_atdPreviewActive = true;
                try
                {
                    foreach (Tile2i tile in s_previewedTiles)
                        s_desigRenderer.RemovePreviewDesignation(tile);
                }
                finally
                {
                    s_atdPreviewActive = false;
                }
            }
            s_previewedTiles.Clear();
        }

        // Called every frame from AutoTerrainDesignationsTicker.Update().
        internal static void UpdateCornerPreview()
        {
            if (!s_cornerModeActive || s_desigRenderer == null || s_activeCornerProto == null || s_desigManager == null)
            {
                if (s_previewedTiles.Count > 0)
                    ClearAllPreviews();
                return;
            }

            // RMB is held — vanilla is handling AOE delete; hide our preview mesh.
            if (Input.GetMouseButton(1))
            {
                if (s_previewedTiles.Count > 0)
                    ClearAllPreviews();
                s_cornerCursor?.Hide();
                return;
            }

            if (s_terrainCursor == null) return;
            if (!s_terrainCursor.TryComputeTerrainPosition(Input.mousePosition, out Tile3f pos3f)) return;

            Tile2i cursorTile = SnapToDesignationGrid(pos3f.Xy.Tile2i);
            List<(Tile2i tile, CornerVariant v, int absHeight)> fill;

            if (s_dragging)
            {
                fill = ComputeCornerFill(s_dragOrigin, cursorTile, s_dragBaseHeight, s_dragEffectiveVariant);
            }
            else
            {
                var snap = TrySnapToNeighbors(cursorTile);
                CornerVariant hoverVariant;
                int hoverHeight;
                if (snap.HasValue)
                {
                    hoverVariant = snap.Value.variant;
                    hoverHeight = snap.Value.baseHeight;
                }
                else
                {
                    hoverVariant = s_activeCornerVariant;
                    hoverHeight = GetSurfaceHeight(s_desigManager.TerrainManager, cursorTile) + GetHeightBias();
                }
                fill = ComputeCornerFill(cursorTile, cursorTile, hoverHeight, hoverVariant);
            }

            // Build new tile set.
            var newTiles = new HashSet<Tile2i>(fill.Count);
            foreach (var (tile, _, _) in fill)
                newTiles.Add(tile);

            // Add/update new tiles first, then remove stale ones — this order ensures there is
            // never a frame with no preview visible during cell transitions.
            s_atdPreviewActive = true;
            try
            {
                foreach (var (tile, v, absHeight) in fill)
                    s_desigRenderer.AddOrUpdatePreviewDesignation(
                        s_activeCornerProto, BuildCornerDesignationData(tile, absHeight, v));

                // Remove previews for tiles no longer in the new set.
                foreach (Tile2i old in s_previewedTiles)
                    if (!newTiles.Contains(old))
                        s_desigRenderer.RemovePreviewDesignation(old);
            }
            finally
            {
                s_atdPreviewActive = false;
            }

            s_previewedTiles.Clear();
            foreach (Tile2i t in newTiles)
                s_previewedTiles.Add(t);

            // Show the Add cursor whenever a valid preview is visible.
            if (newTiles.Count > 0)
                s_cornerCursor?.Show();
            else
                s_cornerCursor?.Hide();
        }

        // Shared fill computation used by both UpdateCornerPreview and TryCommitDrag.
        private static List<(Tile2i tile, CornerVariant v, int absHeight)> ComputeCornerFill(
            Tile2i start, Tile2i end, int baseHeight, CornerVariant effectiveVariant)
        {
            int minX = Math.Min(start.X, end.X);
            int maxX = Math.Max(start.X, end.X);
            int minY = Math.Min(start.Y, end.Y);
            int maxY = Math.Max(start.Y, end.Y);

            int originGridX = start.X / 4;
            int originGridY = start.Y / 4;
            int originParity = ((originGridX + originGridY) % 2 + 2) % 2;

            CornerVariant complement = CheckerboardComplement(effectiveVariant);
            int rot = (int)effectiveVariant % 4;
            int outerRot = IsOuterVariant(effectiveVariant) ? rot : (rot + 2) % 4;

            var result = new List<(Tile2i, CornerVariant, int)>();

            for (int gx = minX; gx <= maxX; gx += 4)
            {
                for (int gy = minY; gy <= maxY; gy += 4)
                {
                    int gridX = gx / 4;
                    int gridY = gy / 4;
                    int parity = ((gridX + gridY) % 2 + 2) % 2;
                    bool isSameParity = (parity == originParity);

                    CornerVariant v = isSameParity ? effectiveVariant : complement;

                    int deltaGridX = gridX - originGridX;
                    int deltaGridY = gridY - originGridY;

                    int slopeSum;
                    switch (outerRot)
                    {
                        case 0:  slopeSum =  deltaGridX + deltaGridY; break; // uphill NW
                        case 1:  slopeSum =  deltaGridY - deltaGridX; break; // uphill NE
                        case 2:  slopeSum = -(deltaGridX + deltaGridY); break; // uphill SE
                        default: slopeSum =  deltaGridX - deltaGridY; break; // uphill SW
                    }

                    int zOffset;
                    if (isSameParity)
                        zOffset = -(slopeSum / 2);
                    else if (IsOuterVariant(effectiveVariant))
                        zOffset = -(slopeSum - 1) / 2;
                    else
                        zOffset = -(slopeSum + 1) / 2;

                    result.Add((new Tile2i(gx, gy), v, baseHeight + zOffset));
                }
            }

            return result;
        }

        /// <summary>
        /// Reads the height constraints imposed by existing neighbor designations at all 4 corner
        /// points of <paramref name="origin"/> and finds the (variant, baseHeight) pair that satisfies
        /// the greatest number of them. Ties are broken by variant index order (arbitrary but stable).
        /// Returns null when no neighbors are present at any corner (no snap needed).
        /// </summary>
        /// <summary>
        /// Reads the heights that the 4 direct neighbor designations impose on the new tile's corners,
        /// then picks the (variant, baseHeight) that best satisfies those constraints.
        ///
        /// Corner layout (indices used below):
        ///   0 = NW = OriginTargetHeight
        ///   1 = NE = PlusXTargetHeight
        ///   2 = SE = PlusXyTargetHeight
        ///   3 = SW = PlusYTargetHeight
        ///
        /// Each neighbor contributes at most two adjacent corner constraints:
        ///   West  tile (origin-4X): its NE(1) → our NW(0);  its SE(2) → our SW(3)
        ///   East  tile (origin+4X): its NW(0) → our NE(1);  its SW(3) → our SE(2)
        ///   North tile (origin-4Y): its SW(3) → our NW(0);  its SE(2) → our NE(1)
        ///   South tile (origin+4Y): its NW(0) → our SW(3);  its NE(1) → our SE(2)
        ///
        /// Tie-breaking priority: most edges matched → most corners at terrain height → user-selected variant.
        /// Returns null when no neighbors are present (no snap needed).
        /// </summary>
        private static (CornerVariant variant, int baseHeight)? TrySnapToNeighbors(Tile2i origin)
        {
            // c[i] = cardinal (edge-sharing) height constraint on corner i, null if unconstrained.
            int?[] c = new int?[4];
            // d[i] = diagonal (corner-only) height constraint on corner i, null if unconstrained.
            int?[] d = new int?[4];

            void constrain(int?[] arr, int cornerIdx, int height)
            {
                // If already constrained to a different value, keep first (first neighbor wins).
                if (!arr[cornerIdx].HasValue)
                    arr[cornerIdx] = height;
            }

            // Corner index layout (origin = NW):
            //   0=NW (origin), 1=NE (PlusX), 2=SE (PlusXy), 3=SW (PlusY)

            // West neighbor: its NE → our NW(0), its SE → our SW(3)
            var west = s_desigManager!.GetDesignationAt(origin.AddX(-4));
            if (west.HasValue)
            {
                constrain(c, 0, west.Value.Data.PlusXTargetHeight.Value);
                constrain(c, 3, west.Value.Data.PlusXyTargetHeight.Value);
            }

            // East neighbor: its NW → our NE(1), its SW → our SE(2)
            var east = s_desigManager.GetDesignationAt(origin.AddX(4));
            if (east.HasValue)
            {
                constrain(c, 1, east.Value.Data.OriginTargetHeight.Value);
                constrain(c, 2, east.Value.Data.PlusYTargetHeight.Value);
            }

            // North neighbor: its SW → our NW(0), its SE → our NE(1)
            var north = s_desigManager.GetDesignationAt(origin.AddY(-4));
            if (north.HasValue)
            {
                constrain(c, 0, north.Value.Data.PlusYTargetHeight.Value);
                constrain(c, 1, north.Value.Data.PlusXyTargetHeight.Value);
            }

            // South neighbor: its NW → our SW(3), its NE → our SE(2)
            var south = s_desigManager.GetDesignationAt(origin.AddY(4));
            if (south.HasValue)
            {
                constrain(c, 3, south.Value.Data.OriginTargetHeight.Value);
                constrain(c, 2, south.Value.Data.PlusXTargetHeight.Value);
            }

            // Diagonal neighbors — each shares exactly one corner with our tile.
            // NW diagonal: its SE (PlusXy) → our NW(0)
            var nwDiag = s_desigManager.GetDesignationAt(origin.AddX(-4).AddY(-4));
            if (nwDiag.HasValue)
                constrain(d, 0, nwDiag.Value.Data.PlusXyTargetHeight.Value);

            // NE diagonal: its SW (PlusY) → our NE(1)
            var neDiag = s_desigManager.GetDesignationAt(origin.AddX(4).AddY(-4));
            if (neDiag.HasValue)
                constrain(d, 1, neDiag.Value.Data.PlusYTargetHeight.Value);

            // SE diagonal: its NW (origin) → our SE(2)
            var seDiag = s_desigManager.GetDesignationAt(origin.AddX(4).AddY(4));
            if (seDiag.HasValue)
                constrain(d, 2, seDiag.Value.Data.OriginTargetHeight.Value);

            // SW diagonal: its NE (PlusX) → our SW(3)
            var swDiag = s_desigManager.GetDesignationAt(origin.AddX(-4).AddY(4));
            if (swDiag.HasValue)
                constrain(d, 3, swDiag.Value.Data.PlusXTargetHeight.Value);

            bool anyEdge = c[0].HasValue || c[1].HasValue || c[2].HasValue || c[3].HasValue;
            bool anyDiag = d[0].HasValue || d[1].HasValue || d[2].HasValue || d[3].HasValue;
            if (!anyEdge && !anyDiag)
                return null;

            TerrainManager terrMgr = s_desigManager.TerrainManager;
            int[] terrain =
            {
                GetSurfaceHeight(terrMgr, origin),
                GetSurfaceHeight(terrMgr, origin.AddX(4)),
                GetSurfaceHeight(terrMgr, origin.AddXy(4)),
                GetSurfaceHeight(terrMgr, origin.AddY(4)),
            };

            CornerVariant bestVariant = s_activeCornerVariant;
            int bestBase = 0;
            int bestEdgeScore = -1;
            int bestDiagScore = -1;
            int bestGroundScore = -1;
            bool bestIsUserVariant = false;

            // Enumerate all 8 variants × all base heights implied by each constrained corner.
            // Both cardinal (c) and diagonal (d) constraints are used as candidate sources.
            for (int vi = 0; vi < 8; vi++)
            {
                int specialCorner = vi % 4;    // corner index that carries the ±1 offset
                int delta = vi < 4 ? +1 : -1; // outer=+1, inner=-1

                for (int pass = 0; pass < 2; pass++) // pass 0 = cardinal, pass 1 = diagonal
                {
                    int?[] src = pass == 0 ? c : d;
                    for (int ci = 0; ci < 4; ci++)
                    {
                        if (!src[ci].HasValue) continue;

                        // Derive baseHeight from this corner's constraint.
                        int b = (ci == specialCorner) ? src[ci]!.Value - delta : src[ci]!.Value;

                        // Score against cardinal constraints (highest priority).
                        int edgeScore = 0;
                        for (int cj = 0; cj < 4; cj++)
                        {
                            if (!c[cj].HasValue) continue;
                            int expected = (cj == specialCorner) ? b + delta : b;
                            if (c[cj]!.Value == expected) edgeScore++;
                        }

                        // Score against diagonal constraints (mid priority).
                        int diagScore = 0;
                        for (int cj = 0; cj < 4; cj++)
                        {
                            if (!d[cj].HasValue) continue;
                            int expected = (cj == specialCorner) ? b + delta : b;
                            if (d[cj]!.Value == expected) diagScore++;
                        }

                        // Count how many corners of the new tile sit at terrain level (low priority).
                        int groundScore = 0;
                        for (int cj = 0; cj < 4; cj++)
                        {
                            int h = (cj == specialCorner) ? b + delta : b;
                            if (h == terrain[cj]) groundScore++;
                        }

                        bool isUserVariant = ((CornerVariant)vi == s_activeCornerVariant);

                        // Prefer by: (1) edge matches, (2) diagonal matches, (3) ground contact, (4) user variant.
                        bool better = edgeScore > bestEdgeScore
                            || (edgeScore == bestEdgeScore && diagScore > bestDiagScore)
                            || (edgeScore == bestEdgeScore && diagScore == bestDiagScore && groundScore > bestGroundScore)
                            || (edgeScore == bestEdgeScore && diagScore == bestDiagScore && groundScore == bestGroundScore && isUserVariant && !bestIsUserVariant);

                        if (better)
                        {
                            bestEdgeScore = edgeScore;
                            bestDiagScore = diagScore;
                            bestGroundScore = groundScore;
                            bestIsUserVariant = isUserVariant;
                            bestVariant = (CornerVariant)vi;
                            bestBase = b;
                        }
                    }
                }
            }

            return (bestEdgeScore > 0 || bestDiagScore > 0) ? ((CornerVariant variant, int baseHeight)?)(bestVariant, bestBase) : null;
        }

        /// <summary>
        /// Checkerboard complement: rotate 180° and flip outer↔inner.
        /// E.g. NW-high → SE-low; both share all four edge heights at baseHeight.
        /// </summary>
        private static CornerVariant CheckerboardComplement(CornerVariant v)
        {
            int rot        = (int)v % 4;
            int typeOffset = (int)v >= 4 ? 0 : 4; // flip outer↔inner
            int newRot     = (rot + 2) % 4;        // rotate 180°
            return (CornerVariant)(typeOffset + newRot);
        }

        private static Tile2i SnapToDesignationGrid(Tile2i t)
        {
            // Designation origins must be on a 4-tile boundary.
            // Use floor division so negative coords also align correctly.
            int x = (int)Math.Floor(t.X / 4.0) * 4;
            int y = (int)Math.Floor(t.Y / 4.0) * 4;
            return new Tile2i(x, y);
        }

        private static int GetHeightBias()
        {
            if (s_miningController == null || s_heightBiasField == null)
                return 0;
            try
            {
                object? boxed = s_heightBiasField.GetValue(s_miningController);
                if (boxed is ThicknessTilesI bias)
                    return bias.Value;
            }
            catch { }
            return 0;
        }

        private static CornerVariant RotateCornerVariant(CornerVariant v)
        {
            int typeOffset = (int)v / 4 * 4; // 0 for outer, 4 for inner
            int rot        = (int)v % 4;
            return (CornerVariant)(typeOffset + (rot + 3) % 4);
        }

        private static CornerVariant ToggleCornerType(CornerVariant v)
        {
            int rot        = (int)v % 4;
            int typeOffset = (int)v >= 4 ? 0 : 4; // flip outer↔inner
            return (CornerVariant)(typeOffset + rot);
        }

        private static bool IsOuterVariant(CornerVariant v) => (int)v < 4;

        private static string GetCornerRotationLabel(CornerVariant v)
        {
            switch ((int)v % 4)
            {
                case 0: return "NW";
                case 1: return "NE";
                case 2: return "SE";
                case 3: return "SW";
                default: return "?";
            }
        }

        public static void AreaToolboxCtorPostfix(object __instance, AreaToolbox.ToolType toolType)
        {
            if (__instance is not AreaToolbox toolbox) return;
            try
            {
                var buttonsField = typeof(AreaToolbox).GetField(
                    "m_buttons", BindingFlags.Instance | BindingFlags.NonPublic);

                // Mutual exclusivity: switching game mode exits K-mode.
                // (Initial mode is captured via the SetMode postfix patch.)
                toolbox.OnModeChanged += mode =>
                {
                    if (s_cornerModeActive) ExitCornerMode();
                };

                // Insert the K button at position 0 (left of the F buttons) by
                // directly inserting into the toolbox's internal body Row.
                var bodyField = typeof(Toolbox).GetField(
                    "m_body", BindingFlags.Instance | BindingFlags.NonPublic);
                var smField = typeof(Toolbox).GetField(
                    "m_shortcutsManager", BindingFlags.Instance | BindingFlags.NonPublic);
                var body = bodyField?.GetValue(toolbox) as Row;
                var sm   = smField?.GetValue(toolbox) as ShortcutsManager;

                if (body != null)
                {
                    var capturedToolType = toolType;
                    const string iconPath = "Assets/Unity/UserInterface/General/ExpandScreen.svg";

                    // Outer corner button — ExpandScreen as-is, keybind K.
                    var outerItem = new ToolboxItem(
                        _ => KeyBindings.FromKey(KbCategory.Designation, ShortcutMode.Game, KeyCode.K),
                        iconPath,
                        () =>
                        {
                            if (!s_designationToolActive) return;
                            if (!s_cornerModeActive)
                                EnterCornerMode(preferOuter: true);
                            else if (IsOuterVariant(s_activeCornerVariant))
                                ExitCornerMode();
                            else
                            {
                                s_activeCornerVariant = ToggleCornerType(s_activeCornerVariant);
                                UpdateCornerButtonSelection();
                                PlaySwitchSound();
                            }
                        });

                    // Inner corner button — ExpandScreen rotated 180°, keybind K (label only).
                    var innerItem = new ToolboxItem(
                        _ => KeyBindings.FromKey(KbCategory.Designation, ShortcutMode.Game, KeyCode.K),
                        iconPath,
                        () =>
                        {
                            if (!s_designationToolActive) return;
                            if (!s_cornerModeActive)
                                EnterCornerMode(preferOuter: false);
                            else if (!IsOuterVariant(s_activeCornerVariant))
                                ExitCornerMode();
                            else
                            {
                                s_activeCornerVariant = ToggleCornerType(s_activeCornerVariant);
                                UpdateCornerButtonSelection();
                                PlaySwitchSound();
                            }
                        });

                    // Rotate the inner icon 180° so the arrow points inward.
                    if (innerItem.m_btn is ButtonIcon innerBtnIcon)
                        innerBtnIcon.Icon.Element.transform.rotation = Quaternion.Euler(0f, 0f, 180f);

                    outerItem.Tooltip(new LocStrFormatted(AtdLocalization.Tt(
                        "toolbox.corner_outer.tooltip",
                        "Corner (outer): place convex corner ramps.")));
                    innerItem.Tooltip(new LocStrFormatted(AtdLocalization.Tt(
                        "toolbox.corner_inner.tooltip",
                        "Corner (inner): place concave corner ramps.")));

                    var divider = new VerticalDivider().AlignSelfStretch().MarginTopBottom(2.pt());
                    body.InsertAt(0, outerItem);
                    body.InsertAt(1, innerItem);
                    body.InsertAt(2, divider);
                    if (sm != null) { outerItem.Update(sm); innerItem.Update(sm); }

                    s_toolboxes[(int)capturedToolType] = (toolbox, outerItem, innerItem, buttonsField);
                    LogDebug($"[ATD] K-mode buttons (outer+inner) injected into {capturedToolType} toolbox.");
                }
                else
                {
                    Log.Warning($"[AutoDepth] Could not access toolbox body for {toolType} K-mode button.");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoDepth] EXCEPTION injecting K-mode button: {ex}");
            }
        }

        public static void AreaToolboxSetModePostfix(object __instance, AreaMode mode)
        {
            if (s_activeAreaToolbox != null && __instance == s_activeAreaToolbox)
                s_currentAreaMode = (int)mode;
        }

        public static void ApplyCornerPatches(Harmony harmony)
        {
            var assembly = typeof(Mafi.Unity.Entities.EntityMb).Assembly;

            // Patch AreaToolbox constructor to inject the K-mode button into the
            // mining designation toolbar.
            try
            {
                var areaToolboxType = assembly.GetType("Mafi.Unity.Ui.Controllers.Designations.AreaToolbox");
                if (areaToolboxType != null)
                {
                    var ctors = areaToolboxType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (ctors.Length > 0)
                        harmony.Patch(ctors[0],
                            postfix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(AreaToolboxCtorPostfix)));

                    var setModeMethod = areaToolboxType.GetMethod("SetMode",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (setModeMethod != null)
                        harmony.Patch(setModeMethod,
                            postfix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(AreaToolboxSetModePostfix)));
                }
                else
                {
                    Log.Warning("[AutoDepth] AreaToolbox type not found");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoDepth] EXCEPTION patching AreaToolbox ctor: {ex}");
            }

            // Patch TerrainDesignationController.Activate / Deactivate to track when
            // the player is actively using the mining designation tool (M-mode).
            try
            {
                var controllerType = assembly.GetType("Mafi.Unity.Ui.Controllers.Designations.TerrainDesignationController");
                if (controllerType != null)
                {
                    var activateMethod = controllerType.GetMethod("Activate",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (activateMethod != null)
                        harmony.Patch(activateMethod,
                            postfix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(DesignationToolActivatePostfix)));

                    var deactivateMethod = controllerType.GetMethod("Deactivate",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (deactivateMethod != null)
                        harmony.Patch(deactivateMethod,
                            postfix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(DesignationToolDeactivatePostfix)));

                    var previewInitMethod = controllerType.GetMethod(
                        "previewInitialDesignationAt",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    if (previewInitMethod != null)
                        harmony.Patch(previewInitMethod,
                            prefix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(PreviewInitialDesignationAtPrefix)));
                    else
                        Log.Warning("[AutoDepth] previewInitialDesignationAt method not found");

                    var handleInitialMethod = controllerType.GetMethod(
                        "handleInitialState",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    if (handleInitialMethod != null)
                        harmony.Patch(handleInitialMethod,
                            prefix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(HandleInitialStatePrefixForCorner)));
                    else
                        Log.Warning("[AutoDepth] handleInitialState method not found");
                }
                else
                {
                    Log.Warning("[AutoDepth] TerrainDesignationController type not found");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoDepth] EXCEPTION patching TerrainDesignationController: {ex}");
            }

            // Patch TerrainDesignationsManager.AddOrReplaceDesignation to protect
            // ATD-placed corner tiles from being overwritten by the game's placement command.
            try
            {
                var desigMgrMethod = typeof(TerrainDesignationsManager).GetMethod(
                    "AddOrReplaceDesignation",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (desigMgrMethod != null)
                    harmony.Patch(desigMgrMethod,
                        prefix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(AddOrReplaceDesignationPrefix)));
                else
                    Log.Warning("[AutoDepth] AddOrReplaceDesignation method not found");
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoDepth] EXCEPTION patching AddOrReplaceDesignation: {ex}");
            }

            // Patch TerrainDesignationsRenderer.AddOrUpdatePreviewDesignation and
            // RemovePreviewDesignation to suppress the game's own preview calls while
            // ATD's corner mode is active (prevents flicker).
            try
            {
                var addPreviewMethod = typeof(TerrainDesignationsRenderer).GetMethod(
                    "AddOrUpdatePreviewDesignation",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (addPreviewMethod != null)
                    harmony.Patch(addPreviewMethod,
                        prefix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(AddOrUpdatePreviewPrefix)));
                else
                    Log.Warning("[AutoDepth] AddOrUpdatePreviewDesignation method not found");

                var removePreviewMethod = typeof(TerrainDesignationsRenderer).GetMethod(
                    "RemovePreviewDesignation",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (removePreviewMethod != null)
                    harmony.Patch(removePreviewMethod,
                        prefix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(RemovePreviewPrefix)));
                else
                    Log.Warning("[AutoDepth] RemovePreviewDesignation method not found");
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoDepth] EXCEPTION patching preview renderer methods: {ex}");
            }
        }
    }
}
