# Add Tower Gravity and Hourglass Penalty to Access Pathfinder

The pathfinder will be updated to include spatial heuristics that guide the generated accessways toward the tower, while avoiding the "hourglass" zone where a straight 25% ramp would be impossible.

## Proposed Changes

### Configuration and Snapshot
We will introduce two new settings to control the heuristics, and pass them down into the pathfinder.

#### [MODIFY] `ATD.Mod.cs`
- Add `AccessTowerGravityWeight` (default `0.1f`).
- Add `AccessHourglassPenaltyWeight` (default `2.0f`).

#### [MODIFY] `ATD.Settings.cs` & `ATD.ModSettingsTab.cs`
- Expose the two new settings in the UI so they can be tweaked dynamically during testing.

#### [MODIFY] `AccessSearchModels.cs` (AccessSearchSnapshot)
- Add `int TowerHeight2` to represent `z(tower)` in half-levels.
- Add `float TowerGravityWeight` and `float HourglassPenaltyWeight`.
- Update the constructor to accept these parameters.

#### [MODIFY] `ATD.ExperimentalAccessPathfinding.cs`
- In `TryBuildExperimentalAccessSnapshot`, calculate the tower's terrain center height:
  ```csharp
  int towerHeight2 = ToHeight2(terrMgr.GetHeight(towerCenter).Value.ToFloat());
  ```
- Pass the new settings and `towerHeight2` into the `AccessSearchSnapshot` constructor.

---

### Pathfinder Adjustments
We will apply the heuristic penalties directly to the edge costs during the search.

#### [MODIFY] `AccessPathSearch.cs`
- In `Relax`, we will calculate the penalty based on the `next` node's position and height.
- We will multiply the penalty by the `stepDistance` (e.g., 4 for sloped expansion, 1 for ground expansion) to ensure the penalty is applied fairly across different step sizes.

```csharp
int manhattanToTower = Math.Abs(next.CostPosition.X - snapshot.TowerCenter.X) + Math.Abs(next.CostPosition.Y - snapshot.TowerCenter.Y);
int dz2 = Math.Abs(snapshot.TowerHeight2 - next.Height2);

// z is in half-levels. A 25% slope = 1 full level (2 half-levels) per 4 horizontal tiles.
// So 4 * dz (in full levels) = 2 * dz2 (in half-levels).
bool inHourglass = manhattanToTower - 2 * dz2 < 0;

float gravityCost = snapshot.TowerGravityWeight * manhattanToTower;
float hourglassCost = inHourglass ? snapshot.HourglassPenaltyWeight : 0f;

// Calculate the step distance to scale the penalty
int stepDistance = hasCurrent ? Math.Max(1, Math.Abs(next.CostPosition.X - current.CostPosition.X) + Math.Abs(next.CostPosition.Y - current.CostPosition.Y)) : 1;

float penalty = stepDistance * (gravityCost + hourglassCost);
nextCost += penalty;
```
