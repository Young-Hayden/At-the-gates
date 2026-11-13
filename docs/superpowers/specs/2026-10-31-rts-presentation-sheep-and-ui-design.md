# 2026-10-31 RTS 表现层阶段: Sheep wandering and UI refinement

## Context

Phase A (142 tests) and Phase B (213 tests) are complete and delivered. Phase C (AI recovery,
composition, garrison) remains undesigned. The user asked for two presentation-layer changes on
2026-10-31:

1. **Sheep should move within a range** — chosen: **small-range wandering with walking/grazing
   animation**.
2. **UI refinement across all screens** — chosen: **keep the existing carved/parchment pixel style and
   refine it** (not a restyle).

A live PlayMode screenshot (`Screenshots/b_ui_now.png`, captured 2026-10-31) is the visual baseline.
Observed problems: the resource bar has icons but no 金/木/肉 labels and tight padding; the build bar's
cost text is crammed onto one line (e.g. `100金50木`) and the 1–6 hotkeys are not advertised; the unit
panel's title is vertical and the portraits are dark with no frame; the population readout is plain white
text floating over terrain; the minimap has no frame and its map render is washed out.

## Goals

- Sheep visibly graze and wander a short distance, without ever breaking map connectivity or blocking a
  worker that is harvesting them.
- The HUD reads cleanly at a glance: consistent panels, aligned readouts, legible costs, visible hotkeys,
  clear disabled states, population in the resource row, a framed minimap.
- The menus share the same visual language.

## Non-goals

- No restyle: the carved/parchment look stays; only hierarchy, spacing, alignment, legibility and state
  feedback change.
- No new art assets. Reuse `Carved_9Slides`, `Carved_3Slides`, `Button_Blue_9Slides`, the existing icons
  and the sheep's three existing sprite sheets.
- No gameplay changes beyond the sheep's movement; no balance changes.
- No Phase C AI work.

## Part 1 — Sheep wandering

### Art (already present, no new assets)

| Sheet | Frames | Use |
|---|---:|---|
| `HappySheep_Idle` | 8 | standing / grazing |
| `HappySheep_Bouncing` | 6 | walking (bouncing gait) |
| `HappySheep_All` | 4 | spare poses (unused unless a graze variant is wanted) |

The prefab currently has no `Animator` (`ResourceNode.swayAnimator` is empty). Add one with two states,
`Idle` and `Walk`, driven by a bool or float parameter, and bind the tracks to the **same GameObject that
carries the `SpriteRenderer`** (this project's animation-path trap: the binding path is relative to the
Animator's own object, so it is `""` when they are on the same object).

### Behaviour

- `SheepWander` component on the sheep prefab (so it survives save/load, which re-instantiates from the
  prefab).
- States: `Idle` (5–9 s random, graze animation) → `Walk` (move 1–2 cells toward a random cell within
  **2 cells of the home anchor**) → `Idle`. Home anchor = the position recorded when the sheep's
  `Start` runs (after a load, that is the saved position).
- One cell per step, ~0.55 s per step, moving smoothly between cell centres.
- **Gates, in order, before every single step:**
  1. Do not move if `GameState` is over, or if the sheep is depleted/regrowing.
  2. Do not move while **any `Gatherer` targets this node** (direct order, en-route or harvesting), and
     do not start a move while a worker stands adjacent to it.
  3. The destination's two cells must be free of other buildings/units and terrain-walkable.
  4. **Connectivity guard:** simulate the move — remove the old footprint from `MapGrid`'s dynamic
     blockers, add the new one, then verify that the blue and red team regions are still the same region
     and that the sheep is still harvest-reachable from that region; restore the old footprint before
     returning. If any check fails, cancel the move and stay idle.
  5. Only then move, call `Building.Recompute()` (which re-registers the footprint), and save nothing —
     position is already part of the building's save state.
- The guard must cost little: three sheep stepping at most every few seconds is fine even though
  `RelabelRegions` is O(map).

### Risk

- The blue↔red corridor at `(21,7)` is one cell wide. A wandering sheep that steps into it would
  stalemate the match; the connectivity gate above exists specifically for that, and the sheep's own
  `CentralMeatTests` connectivity invariant must be extended to cover movement.
- Save/load: a sheep restored at a saved position must reset its home anchor there, not wander back to
  the original spawn cell.

## Part 2 — UI refinement

### HUD (incremental, no rebuild)

**User-mandated acceptance rules for every button (2026-10-31):** the label must be **centred both axes**
(`TextAnchor.MiddleCenter`) and **must never overflow its button** — no clipped or spilling text ("穿模").
Each is checked at runtime and asserted in a test: `Text.preferredWidth <= rect.width` and
`Text.preferredHeight <= rect.height` after a `Canvas.ForceUpdateCanvases()` pass; a label that would not
fit is either shortened (two-line cost, shorter caption) or given a smaller font, never left to overflow.

**Smoothness is also a user-mandated rule:** no per-frame scans or allocations in any new HUD code; the HUD
already refreshes on a cadence and must keep doing so.

Done through a new idempotent editor tool `PolishHUD.cs` that adjusts the existing objects — **never**
`Tools/重建 HUD`, which would wipe the incrementally added attack button, repair button, tech strip and
game-over exit.

- **Resource bar**: three equal cells, icon left / value right-aligned, a `金` `木` `肉` label under each
  value, a dark translucent backing behind the numbers so they read over any terrain, consistent 8 px
  padding inside the carved panel.
- **Population**: a fourth cell in the same row, same styling (`人口 10/12`), replacing the floating white
  text. Amber when at capacity, red while starving.
- **Build bar**: cost on two lines (`金 50` / `木 0`), each button a 64×64 icon with a small hotkey badge
  (`1`–`6`) in the top-right corner, consistent 6 px gaps; unaffordable buttons dim their cost text.
- **Unit panel**: horizontal `兵种` title at the top-left instead of vertical; each slot gets a 9-sliced
  frame around the portrait, the name under it and the cost under that; unavailable slots dim the whole
  portrait (population full, starving, unaffordable, no factory).
- **Selection panel**: the existing title/body/buttons keep their style; the tech strip's buttons gain
  the same disabled-state treatment (dim) rather than only recolouring their cost label.
- **Minimap**: a `Carved_9Slides` frame, a dark backing behind the map, and a thicker view rectangle.
- **Status line**: a subtle dark backing strip so it reads on bright terrain.

### Menus

- **Main menu** (`MainMenu.scene`, built by `BuildMainMenu.cs` / `EnhanceMainMenu.cs`): consistent title
  bar, button sizes and spacing; the rules block uses the same body text style as the HUD.
- **Pause menu** and **game-over/exit confirm**: same panel and button spec.
- **Loading screen**: same frame and bar styling.
- Menu work is done by **updating the builder scripts and re-running them**, because those scenes contain
  nothing incrementally added — but each builder must be verified to reproduce its current content plus
  the polish, and the pre-change scene must be byte-backed up.

## Architecture

- `Assets/Scripts/SheepWander.cs` (new) — wandering state machine and the five gates.
- `Assets/Prefabs/Resources/HappySheep_Idle.prefab` — Animator + controller + the new component.
- `Assets/Editor/BuildSheepAnimation.cs` (new) — builds the two clips and the controller from the
  existing sheets.
- `Assets/Editor/PolishHUD.cs` (new) — idempotent HUD refinement.
- `Assets/Editor/BuildMainMenu.cs`, `BuildPauseMenu.cs`, `BuildLoadingScreen.cs` (modified) — menu polish.
- `Assets/Scenes/SampleScene.scene`, `MainMenu.scene`, and the loading prefab — written under the risk
  gate.
- Tests: `SheepWanderTests.cs` (movement gates, connectivity, harvest protection, save/load anchor),
  `HudPolishTests.cs` (the new readouts exist and are wired), plus keeping the existing scene tests green.

## Testing

1. Sheep: a step is refused when a worker targets the node; refused when the destination is occupied;
   refused when the move would cut the blue↔red corridor (construct it with the sheep next to `(21,7)`);
   the sheep returns home after the range; save/load preserves position and resets the anchor; the
   animation switches between Idle and Walk.
2. HUD: the resource cells report live values; the population cell matches `Population.Used/Capacity` and
   turns amber/red; the build-bar hotkey badges exist for all six; the minimap frame exists; the existing
   layout test (no overlap, inside canvas) still passes.
3. Menus: each rebuilt scene keeps its existing tests green.
4. The full suite must stay green; every asset write gets a byte backup and an MD5 pair.

## Acceptance

- Sheep visibly move within two cells and never break connectivity, never block a harvesting worker.
- The HUD is legible in a PlayMode screenshot: costs on two lines, labelled resources, population in the
  resource row, framed minimap, dimmed disabled states.
- Menus match the HUD's visual language.
- Physical GUI acceptance remains unperformed; the visual result is judged from `ScreenCapture` images,
  which this project supports even though the MCP capture tools are blocked.
