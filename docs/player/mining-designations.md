# Mining Designations

## What it does

Auto Terrain Designations scans a mine tower's work area, finds the ore or material you want to target, and places mining designations automatically. Instead of drawing a large set of manual dig orders tile by tile, you let the mod build a connected designation region that follows the deposit.

The core workflow has five parts:

1. **Scan** — Sample the terrain under the tower and decide which 4×4 designation cells are worth mining.
2. **Filter** — Exclude poor or isolated tiles according to the selected ore filter and purity settings.
3. **Connect** — Fill holes and add corridors when needed so excavators and vehicles can reach the work.
4. **Ramp** — Try to add an access ramp from the tower area to the dig.
5. **Place** — Write the final mining designations into the world.

## How to use it

1. Select a mine tower.
2. Open the **Terrain Designations** panel in the inspector.
3. Adjust the per-tower settings if needed.
4. Choose a **Scanning filter** if you want to force one product, or leave it on Auto.
5. Click **Create Designations**.
6. If needed, click **Clear** to remove the ATD mining designations and try again with different settings.

The panel also includes a **Remove Debris** action. This designates debris in the tower area for cleanup without running a full ore scan. This is useful to clean large areas of debris without spending Unity.

## Inspector settings

### `Max height diff`

Controls how much vertical difference ATD allows between neighboring designation cells.

- Lower values produce smoother, safer dig shapes.
- Higher values allow steeper excavation and can follow rougher deposits more aggressively.

### `Ramp width`

Controls the width of generated access ramps.

- `0` disables ramp generation.
- Higher values reserve more width for vehicles but require more free space.

If ramp generation fails or produces a questionable result, ATD shows a yellow warning icon next to **Create Designations**.

### `Max layers to excavate`

Limits how many terrain layers ATD will dig down from the current surface.

- `∞` means no limit.
- Lower values are useful when you want a shallow surface pass instead of a full deep excavation.

### `Elevation limit`

Sets an absolute minimum elevation that ATD is allowed to dig to.

- `-∞` means no limit.
- Use this when you want to avoid digging below a known floor level.

### `Ore purity`

Controls how aggressively the scan rejects poor or contaminated ore.

- `Off` includes all matching tiles and digs to full depth.
- `Low` rejects only very weak ore.
- `Med` is a balanced setting for mixed deposits.
- `High` focuses on rich ore columns.
- `Max` is very selective and targets near-pure ore only.

### `Corridor clearance`

Controls whether ATD connects separated ore regions with passable corridors.

- `Off` leaves components separate.
- `1` allows narrow corridors for small and medium vehicles.
- `2` uses wider corridors suitable for mega vehicles.

### `Scanning filter`

Forces the scan to target one specific product.

When left on Auto, ATD prefers useful ore products first, then falls back to debris cleanup, then dirt-like cleanup when appropriate.

## Ore Composition panel

The **Ore Composition** panel analyzes the tower's current managed designations and estimates how much material is inside them.

Use it to:

- check what products are inside the current designation area
- compare mixed deposits before committing to a scan
- set excavator priority for a chosen product on vanilla mine towers

The panel reads the current designations directly, so it also works with designations that were not created by ATD.

## Excavator priority

When you set a mining priority from the ore composition cards, ATD stores it per tower and reapplies it to newly assigned excavators.

This means:

- you do not need to re-set the same priority every time a new excavator is assigned
- resetting the tower priority to none lets excavators use their normal behavior again

## Corner designations

ATD also adds manual **Corner Designations** for terrain tools, but they are useful well beyond the automatic mining workflow.

See [Corner Designations](corner-designations.md) for the standalone guide.

## Global settings and console commands

The per-tower settings in the inspector are separate from the global defaults stored in `ATDsettings.json`.

Useful console commands:

| Command | What it does |
|---|---|
| `atd_get_settings` | Prints the current global defaults and purity arrays. |
| `atd_set_max_height_diff n` | Sets the global default maximum height difference between adjacent designation cells. |
| `atd_set_ramp_width n` | Sets the global default ramp width. |
| `atd_set_max_layers_to_excavate n` | Sets the global default surface-depth limit. `0` means no limit. |
| `atd_set_max_depth_to_dig_to n` | Sets the global default minimum elevation. Use `-` for no limit. |
| `atd_set_ore_purity_level n` | Sets the global default ore purity preset. |
| `atd_set_bottom_flattening on\|off` | Toggles the extra bottom-flattening pass. |
| `atd_set_min_corridor_clearance n` | Sets the global default corridor clearance. |
| `atd_set_ramp_notifications on\|off` | Enables or disables ramp access warning notifications on mine towers (Failed, Truncated, NotAccessible icons). |
| `atd_save_settings` | Writes the current in-memory defaults to `ATDsettings.json`. |
| `atd_reset_to_defaults` | Resets the in-memory defaults to the built-in values. |

## Things to know

- ATD only clears mining designations that it recognizes as mining work. It does not bulk-remove unrelated designation types when using the clear action.
- Placing new mining designations can still overwrite other designation types if they occupy the same origin tile.
- Ramp generation tries to avoid buildings, but steep terrain or poor access can still require manual adjustment.
- Ore Composition is an estimate of material inside the current designations and does not account for landslides. Landslides usually cause ore quality to degrade, as more rock and dirt are mixed in.