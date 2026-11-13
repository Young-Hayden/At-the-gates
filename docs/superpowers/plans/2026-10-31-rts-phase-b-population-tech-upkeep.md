# RTS Phase B: Population, Worker Repair, Technology, Upkeep and Balance — Implementation Plan

> **For agentic workers:** use superpowers:subagent-driven-development. One implementer per task, an
> independent task review after each, fixes only for Critical/Important.

**Goal:** Add house-driven population, a free-timed worker repair path, three one-level technologies
saved across sessions, a soft meat upkeep that makes the sheep matter, and the six approved balance
corrections — without reworking Phase A's limits, gathering or repair architecture.

**Architecture:** Two new derived-state services (`Population`, `Technology`) follow the Phase A
`UnitLimits`/`ResourceLimits` pattern: a cached count with explicit invalidation, consulted by every
production path. Technology is the only new persistent state, carried in `GameState.SaveState` with a
backward-compatible version bump. Worker repair reuses the `Gatherer` task/retreat machinery and the
existing right-click routing. Upkeep is a stateless timer in `Economy`/`AIController`.

**Tech stack:** Tuanjie 2022.3.62t5, UTF 1.1.33, NUnit, UGUI, existing `RegressionFixture` and
`RegressionTestMenu`, incremental editor tools.

**Approved design:** `docs/superpowers/specs/2026-10-31-rts-phase-b-population-tech-upkeep-design.md`

## Global Constraints

- Phase B only. Do not implement AI minimum-building recovery, composition ratios or AI garrison
  (Phase C).
- Do not add new units, buildings or art.
- No full HUD rebuild. All UI additions go through incremental editor tools.
- The **only** save-format change is the per-side `techMask`; `SaveGameService.Version` becomes 2 and a
  version-1 save must load with `techMask = 0` and no data loss.
- Population, upkeep and worker repair are **derived or transient** — no save fields for them.
- Preserve all pre-existing uncommitted work. No commits, pushes or worktrees.
- Player and AI share every rule; only their inputs (bank, timers) differ.
- Use real production-path tests. Physical GUI verification is not possible in this project.
- Baseline is the delivered Phase A state: **142/142**. Every final claim comes from a fresh run after
  the last code change.
- **Risk gate (user rule):** any scene or prefab write requires a six-part assessment presented to the
  user before saving: exact change, benefit, risk grading, mitigations, rollback path, unverifiable
  items. No scene save without it.

## File Map

- `Assets/Scripts/Population.cs` (new): capacity, per-unit pop cost, `CanFit`, invalidation.
- `Assets/Scripts/Technology.cs` (new): definitions, per-team mask, research queue, bonus application.
- `Assets/Scripts/ProductionBuilding.cs`: consult population after the Phase A caps.
- `Assets/Scripts/UnitLimits.cs`: expose the role of a prefab for the population cost table (reuse
  `RoleOf`).
- `Assets/Scripts/UnitActor.cs` / `CombatAI.cs` / `Healer.cs`: apply technology bonuses.
- `Assets/Scripts/Gatherer.cs`: worker-repair task; `fleeSafeRadius` 10 → 6, resume delay 3 s.
- `Assets/Scripts/Building.cs`: worker-facing repair entry point (health only, no bank).
- `Assets/Scripts/SelectionManager.cs`: right-click a damaged friendly building → repair order.
- `Assets/Scripts/Economy.cs`: upkeep tick, starvation flag.
- `Assets/Scripts/AIController.cs`: tech research order, meat preference, upkeep awareness.
- `Assets/Scripts/HUD.cs`: population readout, tech strip, starvation reasons.
- `Assets/Scripts/GameState.cs` / `SaveGameService.cs` / `SaveGameData.cs`: `techMask`, version 2.
- `Assets/Editor/BuildTechPanel.cs` (new): incremental tech buttons/readout.
- `Assets/Scenes/SampleScene.scene`: tech UI objects + AI timing/start values + house/goldmine costs.
- Tests: `PopulationTests.cs`, `WorkerRepairTests.cs`, `TechnologyTests.cs`, `UpkeepTests.cs`,
  `BalanceValueTests.cs`, `AITechnologyTests.cs`.

---

### Task 1: Population service and production enforcement

**Files:** create `Assets/Scripts/Population.cs`; modify `ProductionBuilding.cs`, `HUD.cs`;
create `Assets/Tests/PlayMode/PopulationTests.cs`.

**Interfaces:**
- `Population.Capacity(Team)` — base 12 + 6 per **completed** friendly `House` (skip under construction,
  wrecked, ghosts), clamped to 48.
- `Population.Used(Team)` — same counting rule as `UnitLimits` (living + garrisoned + queued).
- `Population.Cost(GameObject prefab)` — Worker 1, Archer 1, Monk 1, Warrior 2, Lancer 2, Other 1.
- `Population.CanFit(Team, GameObject prefab, out string reason)`.
- `Population.Invalidate()` after queue/spawn/death/house completion/destruction and on load.

Steps: write the failing tests (capacity from base and houses; under-construction house adds nothing;
destroyed house lowers capacity; used counts queued and garrisoned; over-cap existing units remain;
production refused before spending with `人口已满`; player and AI identical). Implement. Hook into
`ProductionBuilding.CanQueue` after the Phase A cap check. Surface the reason in `HUD` production and
unit-shop cost labels. Verify with population, unit-limit, production and save tests.

### Task 2: Worker repair

**Files:** modify `Gatherer.cs`, `Building.cs`, `SelectionManager.cs`; create
`Assets/Tests/PlayMode/WorkerRepairTests.cs`.

**Interfaces:**
- `Gatherer` gains a `Repairing` task state: move adjacent to a damaged friendly building, repair at
  20 HP/s while adjacent, stop when full, re-acquire if the target changes, flee on damage and resume
  afterwards.
- `Building.RepairByWorker(float amount)` — clamps health to `maxHealth`; no bank access.
- `SelectionManager.IssueMove` inserts the friendly-damaged-building case after the enemy check and
  before the plain move.

Steps: failing tests first (rate, adjacency, full-health stop, refusal for under-construction/rubble/
neutral/enemy, multiple workers, retreat-and-resume, no interaction with the instant repair's money).
Then implement. Verify with worker, movement, repair and save suites.

### Task 3: Technology core, save version 2

**Files:** create `Assets/Scripts/Technology.cs`; modify `GameState.cs`, `SaveGameService.cs`,
`SaveGameData.cs`, `UnitActor.cs`, `CombatAI.cs`, `Healer.cs`; create
`Assets/Tests/PlayMode/TechnologyTests.cs`.

**Interfaces:**
- `Technology.Tech { Forge, Armour, Gathering }`; per-team mask; `Has(Team, Tech)`;
  `BeginResearch(Team, Tech, out reason)`, progress, `Complete` applies.
- Bonuses: Forge +2 damage per hit; Armour +15 % max HP (apply as a multiplier on the *current* max,
  recomputed once, not compounding); Gathering +2 resources per trip.
- Save: `GameState.SaveState.techMask` (per side); `SaveGameService.Version = 2`; a version-1 file
  loads with `techMask = 0`.

Steps: failing tests first (cost, 45 s, one at a time, applies to existing and future units, gate on
command centre, save round trip, version-1 migration). Implement. Verify with technology, unit-stat,
save-load and combat suites.

### Task 4: Meat upkeep

**Files:** modify `Economy.cs`, `AIController.cs`, `HUD.cs`, `ProductionBuilding.cs`;
create `Assets/Tests/PlayMode/UpkeepTests.cs`.

**Interfaces:**
- Every 10 s each side pays 1 meat per living combat unit (workers excluded).
- If the bank cannot pay in full: set a `Starving` flag for that side; production is refused with
  `食物不足`; monk healing is disabled. Never takes the bank negative; no health loss.
- The flag clears as soon as meat is positive again.

Steps: failing tests first (tick amount, rounding, never negative, starvation blocks production and
healing, recovery, workers cost nothing, player and AI identical). Implement. Verify with upkeep,
repair, AI and worker suites.

### Task 5: UI and balance asset changes (scene + prefab writes — RISK GATE)

**Files:** create `Assets/Editor/BuildTechPanel.cs`; modify `Assets/Scenes/SampleScene.scene`;
modify `Assets/Prefabs/Buildings/Knights/House_Blue.prefab`,
`Assets/Prefabs/Buildings/Knights/House1/2/3_Blue.prefab` if used as houses,
`Assets/Prefabs/Resources/GoldMine_Active.prefab`; modify `Assets/Scripts/Economy.cs` (player start gold)
and `Assets/Scripts/AIController.cs` (defaults).

**Changes (all six approved balance items + UI):**
- Player starting gold 500 → 600 (`Economy` default; code).
- AI `firstAttack` 90 → 120, `maxGatherers` 4 → 5 (scene values).
- House cost 50 → 75 (prefab).
- Gold mine cost 100 → 300 (prefab).
- Worker `fleeSafeRadius` 10 → 6 and a 3 s resume delay (code).
- Population readout and starvation warning in the existing status line (no new scene object needed).
- Technology strip: three incremental buttons + progress text in the command-centre selection panel
  (`BuildTechPanel`), idempotent, **never** rebuilding the HUD.

**Before any save:** present the six-part risk assessment (exact changes, benefit, risk grading,
mitigations, rollback, unverifiable) and get the user's approval. Then byte-backup the scene, save it,
classify the delta line by line (expected: the tech UI blocks + the AI value changes + 400
`m_AnimationSpeed` re-rolls), and re-run the full suite.

### Task 6: AI technology and meat preference

**Files:** modify `AIController.cs`; create `Assets/Tests/PlayMode/AITechnologyTests.cs`.

**Interfaces:** the AI researches Forge → Armour → Gathering when it owns a completed command centre,
can afford the cost, is not already researching, and is not starving. While starving it prioritises
meat harvesting over wood when choosing a gather target.

Steps: failing tests first. Implement. Verify with AI technology, AI budget, wipe-out and worker suites.

### Task 7: Integration verification and whole-phase review

**Files:** create `docs/superpowers/reports/2026-10-31-rts-phase-b-verification.md`; review all changes.

Steps: fresh full suite after the last change; runtime smoke on `SampleScene` (population blocking,
worker repair, a researched tech affecting a unit, upkeep starvation and recovery, the six balance
values as loaded); scope audit; independent whole-phase review on the most capable model; update the
ledger and `RESUME.md`.

## Testing and acceptance

- Every task: RED first (a real failing test, never a weakened assertion), then GREEN, then an
  independent task review; fix only Critical/Important.
- The full suite must be run once after the last code change of each task and once at the end.
- Acceptance: a player who builds no house cannot exceed 12 population; two viable repair paths; meat
  flows and the sheep are contested; technology is legible and survives save/load; the opening is
  survivable (no wave before 120 s, starting gold level with the AI); version-1 saves still load.
- Physical GUI acceptance remains unperformed and must be disclosed.
