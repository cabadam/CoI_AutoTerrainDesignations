# Accessway Pathfinding (least-work corridor search)

Status: planned design note. Candidate replacement for the straight-corridor generator described under *Accessway Routing* in [../in-progress/access-framework.md](../in-progress/access-framework.md).

This document describes an alternative accessway **generation** strategy: instead of enumerating straight corridors and ranking them, treat "reach this origin cluster from ground" as a **least-work corridor search over the terrain heightfield**. The work of digging or dumping terrain becomes graph cost, and the slope rule becomes graph structure. The rest of the access framework - clustering, the grounded-reachability flood, completion, phase gating, diagnostics - is unchanged; only the *routing* step is swapped.

It is deliberately scoped as an A/B alternative behind the existing `AccessCandidate` interface, not a rewrite. The current generator stays until this one demonstrably wins on real saves.

## Why

The straight-corridor generator has three known limits (see *Accessway Routing -> Current limitations*): no turning/switchback, no single-pass multi-bend chain, and no innate preference for cheaper geometry. A path search dissolves all three at once - turns, dog-legs, and switchbacks are simply cheaper paths through the lattice, and "cheapest" is the cost function itself. It also unifies *routing* and *selection*: the path cost is the ranking.

## It is 2.5D, not volumetric 3D

CoI terrain is a **heightfield** - every `(x, y)` column has a single surface height, and terrain designations can only raise or lower that surface. There are no tunnels or overhangs from terrain ops alone. So the search space is **`(origin tile x quantized height level)`**, not full voxels. This is the crucial simplification: the state space is bounded (area origins x a few tens of height levels between the cluster floor and surrounding ground), so A* / Dijkstra is tractable per pass.

True volumetric 3D (tunnels, overpasses, bridge **entities**) is explicitly **out of scope**; it would require placeable structures rather than terrain edits and is a different, much larger project.

## Core model

**Node.** `(origin, h)` - an origin tile together with a chosen target height level `h`. Augmenting the state with height is what makes the slope constraint local and keeps this a clean shortest-path problem; without it, adjacent target heights would be coupled constraints rather than edges.

**Edge.** A move from `(origin, h)` to a neighbouring `(origin', h')` exists **iff** the step satisfies the relevant admissibility predicate (below). The slope rule is therefore the graph's structure, not a post-filter. **Terrain-work steps are cardinal only** - digging, dumping, and leveling all proceed in the four cardinal directions, so a corridor edge that changes terrain advances N/E/S/W, not diagonally. (Diagonal *adjacency* still matters for the fight invariant below, but a terrain-changing edge is never a diagonal move.)

**Two admissibility predicates - traversal vs construction.** These are different bounds and the search must keep them separate:

  * **Traversal admissibility (`<= 0.5` per step)** - the *access-check* rule from the framework's *Edge-compatible*. It governs whether a vehicle can drive an edge over terrain/designations that already exist or will exist. Use it for grounding and for reusing existing accessways.
  * **Construction admissibility (`<= 0.25` per step today)** - the rule for any edge that ATD must **build** by digging or dumping. Constructed terrain is bound by the in-game **allowed-slope parameter**, currently `1` (max within-designation delta `1`), which effectively caps the *buildable* slope at `0.25`. An edge that changes terrain is admissible only at the construction bound, not the looser traversal bound. A full 1-level change therefore needs at least two horizontal tiles, exactly as today.

  The two differ because a `0.5` slope is drivable but **not constructible/workable**: pushing the allowed-slope all the way to within-designation delta `2` (slope `0.5`) has **proven not to work** - excavation cannot take place on that slope. The **saddle designation** is the practical middle ground (slope stays `0.25`, but the *diagonal* corner delta may be `2`) and is the relaxation knob to experiment with later, not the full `0.5` slope.

**Edge / node cost.** Two cost terms, both real:

  * **Terrain work** - the dig + dump volume to bring the node's covered footprint to `h`, with excavated **useful products discounted** (reuse the existing *Useless material moved* accounting). The discount is bounded by the excavated volume itself - a volume can hold no more useful product than its total - so the terrain-work term can fall to **zero but never below**, keeping every edge cost non-negative as Dijkstra/A* require.
  * **Traversal length** - the per-step driving cost, which is **not** zero even over already-mined or dumped ground. A longer corridor lengthens every future haul: the mining trucks that will work the whole dig site drive this accessway repeatedly, so a long flat detour across prepared ground imposes a real downstream cost on the excavation/mining teams. Reusing an existing trunk is *cheaper* than digging a fresh ramp, but it is not free; the length term keeps the search from preferring a long flat path purely because it moves little terrain.

A **fast cost approximation** is acceptable for the terrain-work term: instead of integrating the full footprint volume, approximate an origin's work by the height delta at its **four corners** (fast), or by the delta at its **center point** only (fastest, still an acceptable approximation). The exact footprint integral is the precise form; the corner/center forms are the cheap ones to try first. With the corner variant, **each corner must be counted only once** in the total: adjacent worked origins share corners (an interior corner is shared by up to four origins), so summing per-origin corner deltas naively would double-count the shared corners. Accumulate corner deltas over the *set* of distinct corners the corridor touches, not per origin.

**Goal / source.** Ground-out is reaching any **tower-reachable ground** node (or an already-reachable provider). The natural formulation is a **multi-source** search seeded from *all* ground tiles, producing a cost-to-reach-ground field over the lattice; each cluster then attaches by cheapest descent (see *Trunk-and-branch* below).

Because every admissible edge already encodes slope, and cost already encodes work, **the path cost is the candidate's score** - routing and selection collapse into one search.

## From path to designations (the corner problem)

A node carries a single reference height `h`, but a CoI designation is defined by **four corner heights**, and the fight invariant (next section) is stated over shared corners. The search therefore needs an explicit mapping from a path of `(origin, h)` nodes to concrete designations. This has two parts: the allowed **jumps** between adjacent nodes, and the **transformation** of a jump sequence into designation pieces.

### Lattice coordinates by clearance

The node lattice is offset by clearance parity, which makes *Clearance as a lattice-parity rule* concrete:

* **Clearance 1 (single lane)** - nodes are origin **centers**: tiles with `x, y` in `2 + 4n`.
* **Clearance 2 (double lane)** - nodes sit on origin **edges/vertices**: tiles with `x, y` in `4n`.

Adjacent nodes are one origin apart (4 tiles) in a cardinal direction.

### Allowed jumps depend on the designation set

Which transitions are legal between adjacent origins depends on which designation shapes ATD is allowed to emit. Three sets, increasing in power:

* **V - vanilla shapes only (flat + slope).** Each non-flat origin is a slope descending along one cardinal axis (NS, SN, EW, WE). Transition rules:
  * From a **flat or ground** origin `o`: any cardinal direction is allowed. If `h(o) == h(o')` the successor may be flat (a level join); if `h(o) != h(o')` the successor **must be a slope** - only a slope can change height.
  * From a **slope** origin `o` on axis A:
    * moving **along** A (down or up the slope): `o'` must be **flat or a slope on the same axis** (NS/SN -> NS/SN, EW/WE -> EW/WE).
    * moving **perpendicular** to A (strafing): `o'` must be a slope with the **identical configuration** as `o` (NS -> NS, SN -> SN, EW -> EW, WE -> WE).
  This is the conservative set: a turn between two differently-axised slopes requires a flat landing in between.

* **V' - corners allowed (future).** Adding corner designations (one corner raised or lowered relative to the other three) opens many more transitions. Note that the **current straight-corridor ramp generator does *not* use V'** - it emits only flat/slope shapes (V); the V' corner shapes appear in the *mining designation area*, which is a different algorithm altogether and not the routing path this note replaces. A reasonable first model for adopting V' here - to be verified against the actual corner proto rules - is that a corner acts as a quarter-turn between two slope axes: a slope may transition into a corner that begins reorienting its descent axis, and a corner may be followed by a slope on the *new* axis, giving an L-bend **without** an intervening flat landing. A single-corner height change can also satisfy the fight invariant against a diagonal neighbour that a flat/slope pair could not. The exact admissible corner-to-slope and corner-to-corner transitions should be enumerated from the game's corner designation definitions before the search relies on them.

* **V'' - saddles allowed (future).** Out of scope here; revisit once V/V' are proven.

**The first implementation restricts itself to V** (flat + slope). That set already beats the current generator - which is also limited to flat/slope but additionally to a *single straight segment* - because the search can turn and switchback within V. V' and V'' are later relaxations.

### Transformation

Given a validated jump sequence and the chosen per-node heights, emitting designations is the comparatively easy step: each node becomes the flat/slope/corner piece its incoming and outgoing jumps imply, with corner heights set so that (a) the along-path slope respects the construction bound and (b) every shared corner matches its neighbour (the fight invariant). Because the jump rules already constrain which piece each node can be, the transformation is largely a lookup from `(incoming axis, outgoing axis, height delta)` to a designation shape.

## The fight invariant

Edge *geometry* admissibility is **phase-independent**: digging, leveling, and dumping are all legal in every phase, so phase never decides *which* edges exist. What a phase **does** control is the *fill material*, through the tower dumping rules: the **Prepare** phase wants access to all filling materials (rock, slag, etc.), while the **Filling** phase bans them absolutely and admits only soil. That distinction is a property of the dump designation's material, not of the corridor geometry, so it leaves the routing graph unchanged. (This corrects an earlier assumption that dumping edges were phase-restricted; phase gating remains, but it gates dump *material* and dump-rule ownership, not edge admissibility. In principle Prepare and Filling could share one phase if filling were restricted to soil/ocean or known to be negligible, but ATD phases them tightly for robustness - see the [Farmland Preparation Sub-Process](../in-progress/farmland-preparation-subprocess.md).)

The cross-designation constraint the search must respect is instead the **fight precondition invariant**:

> **Every pair of designations that share one or more corners must be height-aligned on all shared corners.**

There is **no same-type exemption**: a pair with one or more misaligned shared corners causes a landslide and risks irreparable disruption *even when both designations are of the same type*. Alignment on every shared corner is therefore required unconditionally.

A corridor the search lays down must satisfy this against (a) the existing designations it abuts and (b) itself. This is a local, per-node feasibility check during expansion: a node whose required corner heights would leave any shared corner misaligned with a neighbouring designation is **inadmissible**. Because the check is over *shared corners*, it includes **diagonal** neighbours (which share a single corner), not just the cardinal ones a terrain-changing edge can move along. This replaces "phase coupling" as the cross-designation constraint.

## Durability: don't route where mining will undermine it

The fight invariant prevents an *immediate* landslide between adjacent designations. A second, **temporal** hazard is just as damaging: an accessway built too close to a zone that will later be mined **deeper** can collapse once that mining proceeds, because the excavation removes the support beside the ramp. In game terms this is effectively irreparable - rebuilding the collapsed ramp would require filling the mining pit back up, but the pit is full of active mining designations, so the fill cannot be placed. A whole new accessway has to be routed instead, often while mining crews are stranded in the pit behind the collapse.

The search must therefore treat **future excavation depth** as a routing hazard, not just current terrain: keep a safety margin between a built-up or ramped accessway and any adjacent origin whose target height is (or will become) substantially lower. This generalizes the existing *Ramp safety margin* roadmap item into the cost/admissibility model - either as an inadmissible-edge rule near deeper pending excavation, or as a steep cost penalty that pushes corridors away from soon-to-be-undermined ground. Whether the margin is a hard ban or a cost gradient, and how wide it must be, is an open question.

## Clearance as a lattice-parity rule

`corridorWidth` (from the `accessWayClearance` setting; see the framework) maps onto the search as a **parity of the lattice**, because an N-wide band centers differently for odd and even N:

| Clearance | Band centering | Lattice node position | Node cost footprint |
|---|---|---|---|
| **1** (odd) | centered on one origin | origin **centers** | the single covered origin |
| **2** (even) | centered on the seam between two origins, one lane each side | origin **vertices** | the two covered origins (one per side) |

So the horizontal node set shifts by half an origin with width parity: origin-centered for odd clearance, vertex-centered for even clearance. Two refinements:

* **Cost spans the whole footprint.** The precise node cost is the work to bring the node's *entire covered footprint* to `h`, not just one column. The fast corner/center approximations in *Core model* are deliberate cheaper estimates of this same quantity; the center-point form in particular under-counts wide bands, so prefer the corner (or full-footprint) form as clearance grows.
* **Perpendicular planarity must still hold.** The along-path slope is handled by the edge rule, but the band must also be drivable *across*. Each node's lateral profile has to be makeable planar within the fight invariant. This is the one place a pure single-lane centerline search can lie, and it is the first thing to prototype carefully.
* **A double lane needs a 2x2 origin block to turn.** A width-2 corridor cannot pivot on a single origin the way a single lane can - a turn requires an **8x8-tile (2x2 origin)** area to swing the outer lane around the inner one. So a double-lane search should **turn on a 2x2 origin grid**: coarsen the turning lattice to 2x2 origin blocks and apply the same V jump rules *within that coarser grid* (a "node" for turning purposes is a 2x2 block, and straight runs between turns can stay on the finer lattice). Straight double-lane segments still advance one origin at a time; only the **turns** snap to the 2x2 block grid. This is still a large upgrade over the straight-only model, just with a coarser turning granularity for wide corridors.

Auto clearance derives the width from assigned/global vehicles exactly as the framework's *Corridor width* describes; the search just consumes the resulting integer width and its parity.

## Width handling strategy

Searching the full perpendicular band profile as state is exponential and is **not** the plan. In increasing order of cost/correctness:

1. **Centerline + thicken + revalidate** (build first). Search a single-lane `(origin, h)` path, expand it to the `corridorWidth` band, re-check perpendicular slope/clearance and the fight invariant, and lateral-retry on failure - the same spirit as today's lateral retry. Cheap; can occasionally miss a valid wiggly band.
2. **Footprint-cost search** (escalate if #1 yields bad corridors). Keep the single-lane lattice but cost and feasibility-test each node over its full width-N footprint, forbidding nodes whose band cannot be made locally planar. Correct-ish, more expensive.
3. **Full band-state search.** State = entire perpendicular profile. Exponential; avoid.

## Trunk-and-branch via a cost-to-ground field

Rather than an independent per-cluster search, run **one multi-source Dijkstra** from all ground (and already-reachable provider) nodes to build a cost-to-reach-ground field across the lattice. Each not-yet-accessible cluster then attaches at its cheapest descent into that field. Because existing accessways traverse at low cost (cheaper than fresh digging, but **not** free - the traversal-length term still applies), clusters that share a corridor *emerge* reusing a common trunk - the framework's closest-first trunk-and-branch behaviour, but produced by the cost field rather than a greedy processing order, and often a better topology. The framework's order-independence for *evaluation* is preserved; this only changes *generation*.

## Performance and bounding

* Height augmentation multiplies node count by the number of quantized levels, so cap the search: bound the height range to `[cluster floor, surrounding ground + small margin]`, bound the horizontal radius, and use an **admissible A* heuristic** (horizontal distance-to-nearest-ground plus a lower bound on the unavoidable height-gap work).
* The search re-runs each pass like the rest of the framework; keeping it bounded is what makes per-pass re-planning affordable.
* Quantize height to the designation grid's own vertical resolution so the lattice matches what designations can actually express.

## Diagnosability

The framework deliberately avoids an opaque numeric score (`decidedBy=<criterion>` instead). A single path cost regresses that. Mitigation: log the **cost breakdown** (dig vs dump vs length) and the chosen path, so "why did it build this ramp" stays explainable. The `decidedBy` concept becomes "which cost term dominated", and the path geometry is reported alongside it. Preserving this explainability is a hard requirement, not a nicety - it is the reason the framework exists.

## A/B rollout

Build behind the same `AccessCandidate` contract the framework already defines, and compare against the current generator on real saves before promoting:

1. **Single-lane height-augmented A*** on the heightfield, restricted to the **V** (flat + slope) designation set and the construction-slope bound. Validate it reproduces today's straight ramps **and** discovers a switchback the current code cannot.
2. **Add width** (centerline + thicken + revalidate, strategy #1), still within V.
3. **Multi-source cost-to-ground field** for cross-cluster trunk reuse.
4. **Fight-invariant feasibility, undermining safety-margin, and cost-breakdown diagnostics.** Fight-invariant feasibility must be enforced from step 1 (it prevents irreparable landslides); only its *diagnostics* arrive here.
5. **Compare** against the straight-corridor generator on representative saves; promote when it wins on cost, robustness, and explainability.

## Open questions

* Exact vertical quantization to use for height levels, and whether useful-product discounting changes the admissible-heuristic lower bound.
* The full **V' corner-to-slope / corner-to-corner** transition table, enumerated from the game's corner designation definitions.
* The undermining safety margin: hard inadmissible-edge ban vs steep cost gradient, and how wide it must be relative to adjacent excavation depth.
* Whether centerline + thicken (#1) is sufficient in practice or whether footprint-cost search (#2) is needed for common terrain.
* How aggressively to bound the search radius before declaring `Blocked`, and how that maps onto the existing `NoCandidate` / `MouthUnreachable` reasons.
* Whether the cost-to-ground field can be cached across passes and incrementally invalidated, or must be rebuilt each pass.

## Relationship to the access framework

This note changes only the **generation** step. It plugs into the framework at:

* **Provision Pipeline step 8** - the search *is* the missing-provider generator; the cost field replaces closest-first ordering.
* **Accessway Routing** - this is the alternative routing engine; *What a routed candidate is* and *Two routed families* still describe the output (corridors; ramp/bridge), but they are now produced by search rather than straight enumeration.
* **Candidate Selection** - largely subsumed: the Valid filter becomes edge/fight admissibility, and *Useless material moved* / *Mouth distance* become the cost terms. Selection remains as the tie-break vocabulary and the diagnostic surface.
* **Accessway Routing -> Current limitations** - this is the planned removal of the no-turn / no-multi-bend / no-cheaper-geometry limits.

Everything else in the framework (clustering, the grounded-reachability fixpoint flood, completion, phase gating, removability) is untouched.
