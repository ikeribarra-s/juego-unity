# MultiSectionDungeonGenerator — Refine Loop Journal

Screenshot-driven refinement. Scorer = visual judgment on the grid-derived
schematic (`RefineCaptures/seed_N_plan.png`) + connectivity metrics. Goal:
**layout quality** — room/corridor structure, no dead boxes, non-linear paths,
section transitions that read well. One change per iteration, keep if better.

Backup of the starting generator: `MultiSectionDungeonGenerator.cs.bak.20260618-182108`.

## Baseline (seeds 1–5)
- Connectivity already perfect: 100% reachable, 1 island, every seed.
- Problems (layout quality):
  1. **Graveyard + Hell are dead rectangles** — perfectly square, no internal
     floor structure. Only Gothic (BSP) has rooms/corridors.
  2. **Flow is dead-straight linear** — one horizontal spine through all three
     section centers ("three boxes on a stick").
  3. **Top loop skips the Graveyard** — connects only Gothic↔Hell, not a true
     3-section circuit.
  4. **Transitions are abrupt** — corridor dissolves into the open box; weak
     threshold read between corridor and section.

## Iterations
- **iter 1 — KEEP.** Organic open-area footprints. Replaced `CarveOpenArea`
  rectangle fill with deterministic boundary erosion (heavier toward edges) + 3
  cellular-automata smoothing passes. Graveyard/Hell now read as natural cave /
  cemetery blobs with jagged edges (and occasional internal wall-islands)
  instead of flat squares. Floor ~5170→~4113 (seed1), still 100% reachable / 1
  island all seeds. Fixes problem #1.
  (Also brightened the harness 3D render: directional key light + stronger ambient.)
- **iter 2 — KEEP.** True 3-section circuit. Added a reserved spur from the top
  loop down into the Graveyard center, so the middle is reachable two ways
  (spine + top). Floor +~21 tiles, 100% / 1 island. Fixes problem #3.
  NOTE: `open_application` does NOT refresh Unity when it is already frontmost —
  must send **Ctrl+R** in the editor to force reimport+recompile each iteration.
- **iter 3 — KEEP.** Consistent Gothic fill. Under-sized BSP leaves were being
  skipped (large dead voids); now the room is clamped down to fit the leaf
  (floor 4 tiles). Gothic reads as densely as the open sections; seed 4 floor
  3954→4505. All seeds 100% reachable. Side effect: seed 5 produced a 2-tile
  isolated erosion pocket (islands=2) → addressed in iter 4.
- **iter 4 — KEEP.** Prune disconnected floor pockets. New `PruneFloorIslands`
  (run after `ConnectSections`) keeps only the largest connected component;
  unreachable erosion nooks are turned back to wall. Guarantees islands=1 / 100%
  reachable on every seed. Fixes the iter-3 side effect; robustness.

## Result (seeds 1–8, all 100% reachable / islands=1)
Baseline "three dead rectangles on a straight stick" → three density-balanced
organic regions (structured Gothic ruins / eroded cemetery / eroded pillar hall)
wired into a real circuit (central spine + top loop + graveyard spur), with a
hard single-component guarantee. Problems #1 and #3 fully fixed; #2 substantially
improved (circuit, not a chain). Untouched / candidates for a future pass:
- #4 transitions still enter near section centers — could offset doorways / add
  gateway structure (note: jogging the spine off-center has a connectivity risk
  because the Gothic anchor must stay on the guaranteed-carved center).
- Graveyard reads sparse in 3D — that's decoration density (`GraveDensity`), not
  layout, so out of scope here.

Harness: `Assets/Editor/RefineHarness.cs` (editor-only). Trigger a capture by
writing seed CSV to `RefineCaptures/trigger.txt`; reads back `*_plan.png`
(grid schematic), `*_top.png` (render), `metrics.txt`. Unity must be refreshed
(Ctrl+R) after editing the generator before re-triggering.
