# RTS Presentation Phase: Sheep wandering and UI refinement — Implementation Plan

> **For agentic workers:** superpowers:subagent-driven-development, one implementer per task, an
> independent review after each, fixes only for Critical/Important.

**Goal:** Make the central sheep graze and wander a short distance without ever breaking map
connectivity, and refine every screen's UI within the existing carved/parchment style.

**Architecture:** A `SheepWander` component on the sheep prefab drives a two-state animator and steps
cell-by-cell through a five-gate check that simulates the footprint move in `MapGrid` before committing.
UI work is incremental for the HUD (a new `PolishHUD` tool — never a rebuild) and builder-driven for the
menus (whose scenes contain nothing incrementally added).

**Approved design:** `docs/superpowers/specs/2026-10-31-rts-presentation-sheep-and-ui-design.md`
**Visual baseline:** `Screenshots/b_ui_now.png` (captured 2026-10-31 in PlayMode)

## Global Constraints

- Presentation only. No gameplay, balance or Phase C AI changes beyond the sheep's movement.
- No new art assets; reuse the existing sheets, panels, buttons and icons.
- **Risk gate (user rule):** every scene or prefab write needs a six-part assessment presented first,
  byte backups, before/after MD5s, a classified delta, and a full suite after the last change.
- The HUD is **never** rebuilt as a whole; its changes are incremental so the attack button, repair
  button, tech strip and game-over exit survive.
- Existing scene tests (`RepairButtonSceneTests`, `CentralMeatTests`, `BalanceValueTests`) must stay green;
  the layout test must keep passing.
- Animation track binding is relative to the object carrying the `Animator` (this project's trap: `""`
  when the Animator and `SpriteRenderer` share an object).
- Baseline: **213/213**. Every final claim comes from a fresh run after the last change.
- **User-mandated (2026-10-31): game smoothness must not regress** — new logic is event-driven, no
  per-frame `FindObjects`/`Building.All` scans and no per-frame allocations; frame time is measured before
  and after and reported.
- **User-mandated (2026-10-31): every HUD button label is centred both axes and never overflows its
  button** ("no 穿模"). Verified at runtime (`preferredWidth/Height` vs the button rect) and asserted in a
  test; a label that does not fit is shortened or given a smaller font, never left to spill.

## File Map

- `Assets/Scripts/SheepWander.cs` (new)
- `Assets/Prefabs/Resources/HappySheep_Idle.prefab` (Animator + component)
- `Assets/Editor/BuildSheepAnimation.cs` (new: two clips + controller from the existing sheets)
- `Assets/Editor/PolishHUD.cs` (new: idempotent HUD refinement)
- `Assets/Editor/BuildMainMenu.cs`, `EnhanceMainMenu.cs`, `BuildPauseMenu.cs`, `BuildLoadingScreen.cs`,
  `BuildPauseConfirm.cs` (modified for the menu pass)
- `Assets/Scenes/SampleScene.scene`, `Assets/Scenes/MainMenu.scene`, loading prefab (written under the gate)
- Tests: `SheepWanderTests.cs`, `HudPolishTests.cs`

---

### Task P1: Sheep wandering

**Files:** create `Assets/Scripts/SheepWander.cs`, `Assets/Editor/BuildSheepAnimation.cs`,
`Assets/Tests/PlayMode/SheepWanderTests.cs`; modify the sheep prefab.

**Interfaces:** `SheepWander` states `Idle`/`Walk`; public `HomeAnchor`, `IsWalking`, and a testable
`TryStep(Vector3Int destination)` so tests drive movement without waiting on random timers.

Five gates, in order: game over / depleted → worker targeting or adjacent → destination cells free and
terrain-walkable → simulated footprint move keeps the blue and red sides in one region and the sheep
harvest-reachable (restore the old footprint on failure) → commit and `Building.Recompute()`.

**Steps:** failing tests first for each gate (build the cut-vertex case explicitly next to `(21,7)`),
then the component and animation, then GREEN.

### Task P2: HUD refinement

**Files:** create `Assets/Editor/PolishHUD.cs`, `Assets/Tests/PlayMode/HudPolishTests.cs`; modify
`SampleScene.scene`.

**Content:** resource cells with 金/木/肉 labels and a dark backing; population as a fourth cell (amber at
capacity, red while starving); two-line build costs plus 1–6 hotkey badges; unit panel with a horizontal
title, framed portraits and dimmed unavailable slots; tech buttons dimmed when unavailable; minimap framed
with a dark backing; a backing strip for the status line.

**Steps:** the tool must be idempotent (re-run = no new objects, no save when nothing changed), assert
every new rect inside its panel and disjoint from siblings, and the existing layout test must still pass.

### Task P3: Menu refinement

**Files:** modify the menu builders and `MainMenu.scene`, the pause prefab/scene, the loading prefab.

**Steps:** each builder is updated to produce the polished result and re-run; the pre-change scene is
byte-backed up; the scene is verified to keep its existing content (title, buttons, rules block, continue
button logic) plus the polish.

### Task P4: Verification

**Files:** create `docs/superpowers/reports/2026-10-31-rts-presentation-verification.md`.

**Steps:** fresh full suite; PlayMode screenshots of the HUD, the unit/build bars with a selection and a
castled selection, the main menu, the pause menu and the loading screen — **actually inspect the images**;
scope audit; independent whole-phase review; ledger and RESUME update.

## Testing and acceptance

- Every task: RED first, GREEN, independent review, fixes for Critical/Important only.
- The sheep must never break connectivity; the HUD must be legible in a screenshot; menus must match.
- Physical GUI acceptance remains unperformed and is disclosed; visual judgement uses `ScreenCapture`
  images, which work in this project even though the MCP capture tools are blocked.
