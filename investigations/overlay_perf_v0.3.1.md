# Auto Terrain Designations v0.3.1 - Overlay Performance Analysis

## Overview
In v0.3.1 of the Auto Terrain Designations mod, there's a persistent issue where toggling the overlay with the **L** key produces significant stutter. Based on analysis, this performance impact is due to inefficiencies in how Harmony patches interact with the game's designation rendering systems and designation manager.

---

## Findings

### Primary Suspect
Two Harmony prefixes on the `TerrainDesignationsRenderer` methods **`AddOrUpdatePreviewDesignation` and `RemovePreviewDesignation`** are the most likely culprits.

#### Code Snippets:
```csharp
// ATD.CornerDesignation.cs
public static bool AddOrUpdatePreviewPrefix() =>
    !s_cornerModeActive || s_atdPreviewActive;

public static bool RemovePreviewPrefix() =>
    !s_cornerModeActive || s_atdPreviewActive;
```

### Why These Cause Issues
- Whenever the overlay is toggled, CoI performs a **full map-wide repaint** of all designation previews.
- `AddOrUpdatePreviewDesignation` and `RemovePreviewDesignation` are called **once per designation tile**. On large saves, this equates to thousands of game-side calls in a single frame.
- These prefixes are harmless no-ops when corner mode is inactive, but **Harmony still fires** the trampoline for every single call.
- This creates unnecessary overhead from Harmony's method interception, producing a visible stutter during the L overlay toggle even though the mod itself isn't actively doing anything.

---

### Secondary Suspect
The global Harmony prefix on `TerrainDesignationsManager.AddOrReplaceDesignation`:

#### Code Snippet:
```csharp
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
```
##### Why It’s a Problem
- This method is globally patched — **it fires for every designation placement** in the game, whether from ATD or vanilla logic.
- It likely gets triggered redundantly during overlay-related re-registering of designation tiles, massively amplifying frame-time costs.

---

## Recommended Fixes

### Short-Term Fix
1. **Detach the Renderer Patches:**
   - Unpatch the `AddOrUpdatePreview` and `RemovePreview` prefixes completely when corner mode is inactive (via `Harmony.Unpatch`).
   - Re-patch them only when corner mode activates.

#### Example:
```csharp
private static void ManagePreviewPatches(bool enable)
{
    if (enable)
    {
        harmony.Patch(
            typeof(TerrainDesignationsRenderer).GetMethod("AddOrUpdatePreviewDesignation"),
            prefix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(AddOrUpdatePreviewPrefix)));

        harmony.Patch(
            typeof(TerrainDesignationsRenderer).GetMethod("RemovePreviewDesignation"),
            prefix: new HarmonyMethod(typeof(AutoDepthDesignation), nameof(RemovePreviewPrefix)));
    }
    else
    {
        harmony.Unpatch(
            typeof(TerrainDesignationsRenderer).GetMethod("AddOrUpdatePreviewDesignation"),
            HarmonyPatchType.Prefix,
            harmony.Id);

        harmony.Unpatch(
            typeof(TerrainDesignationsRenderer).GetMethod("RemovePreviewDesignation"),
            HarmonyPatchType.Prefix,
            harmony.Id);
    }
}
```

2. **Scope `AddOrReplaceDesignation` to ATD-Specific Calls**:
   - Wrap the prefix logic in a flag like `s_atdActive`, toggled only during designation placement by ATD.

---

### Long-Term Optimization
- Switch from Harmony prefixes to vanilla-compatible systems where feasible.
- Replace the `AddOrUpdatePreviewPrefix` and `RemovePreviewPrefix` functionality with **game-event listeners** that only run ATD logic when ATD is actively modifying designations.

---

## Measurable Goals

### Expected Outcome Timeline:
- **Short-Term Fix:** Reduces overlay stutter immediately in `v0.3.2`.
- **Long-Term Refactor:** Near-zero performance impact for ATD's patches across all integration points in the game.

---

End of Report.