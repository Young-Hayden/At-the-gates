# RTS hardening verification

Date: 2026-10-27 through 2026-10-28  
Project: `D:\Unity\projects\At the Gates`  
Commits: none; no push and no worktree.

## Task 1 — save/load construction-garrison regression and defensive health bar

Preserved Task 1 evidence from `.superpowers/sdd/hardening/task-1-report.md`:

- The complete PlayMode run before Task 1 review finished **82/82 passed**, with 0 failed, skipped or inconclusive, in 44.693s. Machine log: `.superpowers/sdd/hardening/task-1-full-regression.log`.
- `SaveLoadTests.LegacyUnderConstructionGarrison_RoundTripsAndFiresOnlyAfterCompletion` covers the complete legacy under-construction garrison JSON round trip. After restoration and the under-construction/garrison/health-41 assertions, the original restored garrison directly receives `Update` with an in-range Red enemy: no projectile is allowed before `FinishConstruction`, and a projectile is required from the same enemy and same garrison on the immediately following `Update`. There is no `Ungarrison`, second restore, yield or other combat driver between those checks.
- The defensive health-bar assertion is independent in `TowerGarrisonTests.UnderConstructionTower_RejectsNormalGarrison_AndRestoreCannotFireEarly`: its already-started archer has a `HealthBar`, which is force-shown and must be hidden by the under-construction `Update` before enemy creation. That test does not ungarrison or rebuild its garrison relation.
- Earlier review had strengthened enemy/update firing coverage and then added `Ungarrison`/yield/`RestoreGarrison` health-bar preparation. Final overall review removed that preparation because it could dilute evidence about the original `SaveGameService` restoration order. Only `SaveLoadTests.cs` and `TowerGarrisonTests.cs` changed in this final correction; no production code changed.
- Final compilation `codex-22492-66` completed with 0 errors and 0 warnings. On current production code, exact runs passed: direct restored-garrison roundtrip 1/1 in 0.399s, independent health-bar control 1/1 in 0.564s, and full-snapshot control 1/1 in 0.536s; all had 0 failed/skipped/inconclusive.
- Final machine logs: `.superpowers/sdd/hardening/final-a04-direct-restored-garrison.log`, `final-a04-healthbar-control.log`, and `final-a04-full-snapshot-control.log`. Pre-edit snapshots: `final-a04-fix-before/`. Full details remain in `task-1-report.md` and `final-a04-fix-report.md`.
- The original Task 1 RED log remains absent and is not fabricated. The final evidence corrections passed on current production code and are not claimed as a new RED/GREEN production fix.

## Task 2 — D01 preflight, font fallback and empty-icon visibility

Status: implementation and selected regressions verified; probe incident restored and documented.

- RED: the new empty-icon PlayMode test failed on the intended first assertion because `Image.enabled` remained true for a null sprite. Log: `.superpowers/sdd/hardening/task-2-empty-icon-red.log`.
- Fix: runtime and editor icon paths now synchronize `enabled` with resolved sprite nullness; repair validates all arrays/buttons before mutation; LegacyRuntime falls back to Arial; null font no longer clears the Text font.
- GREEN: the new test passed initially and again after all probes, including sprite recovery and return-to-null hiding. Logs: `task-2-empty-icon-green.log` and `task-2-empty-icon-final-green.log`.
- Editor no-save probe passed length-mismatch/no-mutation, null-array, null-button, icon-transition and null-font checks. SampleScene remained clean and MD5-identical. Log: `task-2-editor-preflight.log`.
- Required regressions all passed: real factory production panel, all three `ProductionDependencyTests`, and the related quick-build control. Six selected tests total, each 1/1 with no failures/skips/inconclusive results.
- A sceneSaving guard experiment unexpectedly saved the isolated regression fixture. It was restored byte-for-byte to its original MD5 `A82510044D3474121211C9B5D5C03DB6`; final git status is clean for that fixture. SampleScene and prefabs were not changed by the probes. Full incident details are in `.superpowers/sdd/hardening/task-2-report.md` and `task-2-editor-preflight-guard-failure.log`.

Task 2 full report: `.superpowers/sdd/hardening/task-2-report.md`. Snapshot diff: `.superpowers/sdd/hardening/task-2-diff.patch`. No full-suite result is claimed for Task 2; final full verification remains Task 4.

## Task 3 — D04/C01 regression hardening

Status: **DONE**. Only the three authorized PlayMode test files were changed; no production code or scene/prefab was modified or saved.

- Quick-build scene coverage now retains expected names/order and directly checks `placer.quickBuild` versus `HUD.buildPrefabs` lengths and per-slot reference identity.
- Valid quick-build coverage uses distinct House/Castle prefabs and direct `TryQuickBuild(1)`, requiring the Castle ghost and no House ghost. Invalid-index coverage yields a frame and requires the original ghost to remain the sole instance, defeating delayed-destroy false positives.
- C01 records `amountAtRestore` after restore/delivery/resume assertions and requires harvesting below that dynamic baseline, so earlier harvesting cannot satisfy a fixed `<100` assertion.
- Compilation: 0 errors, 0 warnings (`.superpowers/sdd/hardening/task-3-compile.log`).
- Four exact controls each passed 1/1. Related fixtures passed: `PlacementTests` 8/8, `MovementAndCommandTests` 16/16, `FirstBatchSceneTests` 3/3. Total 31/31 selected executions (27 distinct tests), with no failures/skips/inconclusive results.
- Machine logs: `.superpowers/sdd/hardening/task-3-control-*.log` and `task-3-*-fixture.log`. Full details and discrimination rationale: `.superpowers/sdd/hardening/task-3-report.md`; snapshot diff: `task-3-diff.patch`.
- Existing fixture-filter run headers say `Test cases: 0`, but per-test lines and final summaries confirm all fixture children executed. Final editor state was stopped, project dirty=false, SampleScene dirty=false. No full-suite result is claimed; Task 4 owns final full verification.

## Task 4 — final full verification and handoff

Status: verification and reporting evidence complete; the main controller's independent whole-batch review and final approval remain pending.

### Fresh complete PlayMode result

- Earlier final compilation/full run `codex-22492-58` through `codex-22492-60` produced the historical 45.090s result below. After the final A04 evidence correction, compilation `codex-22492-72` completed with **0 errors and 0 warnings**, idle wait `codex-22492-73` succeeded, and full-suite start `codex-22492-74` ran after the last test change.
- The fresh final machine run reported **83 total: 83 passed, 0 failed, 0 skipped, 0 inconclusive, Duration=45.249s**. This is the actual log total, not an inference from newly added tests.
- Original machine log: `Logs/RegressionTests.txt`. Byte-identical preserved copy: `.superpowers/sdd/hardening/final-full-regression.log`; both SHA-256 values were `d88c7d28c9dd6d61ab41d604574574c145fe18e3b71f44385cbb6ce126302ba9` at capture.
- No leaf failed, so no failure reproduction or classification was required. The final run includes the original-restored-object A04 firing checks, independent health-bar control, D01 empty-icon transition, D04 quick-build controls and C01 post-restore harvesting control.

| Checkpoint | Actual result | Evidence |
|---|---|---|
| Starting reference before this hardening batch | 81/81 passed; 0 failed/skipped/inconclusive; 44.605s | `.superpowers/sdd/final-review-fix-full-regression.log` |
| Complete run before Task 1 review | 82/82 passed; 0 failed/skipped/inconclusive; 44.693s | `.superpowers/sdd/hardening/task-1-full-regression.log` |
| Full run before final A04 evidence correction | 83/83 passed; 0 failed/skipped/inconclusive; 45.090s | Historical Task 4 result, superseded as the current final log |
| Final fresh run after the A04 correction | **83/83 passed; 0 failed/skipped/inconclusive; 45.249s** | `.superpowers/sdd/hardening/final-full-regression.log` |

### Final editor, save and scope state

- Final editor query `codex-22492-76`: **stopped**, active scene `Assets/Scenes/SampleScene.scene`, project dirty=false, SampleScene dirty=false, not compiling or updating.
- SampleScene MD5 remains `15D1E986069B7DD1014D28A28D6CB4FD`, matching the Task 2 post-probe value. `RegressionTests.scene` MD5 remains `A82510044D3474121211C9B5D5C03DB6`. No scene save was performed by Task 4 and no `sceneSaving` experiment was run.
- Formal save `%LOCALAPPDATA%Low/DefaultCompany/兵临城下/AtTheGates/save_slot_1.json` did not exist before or after the run. Save/load tests use `Application.temporaryCachePath`; the formal slot was not created or overwritten.
- `.superpowers/sdd/hardening/final-review.patch` contains exactly eight no-index file diffs: `SaveLoadTests.cs`, `TowerGarrison.cs`, `TowerGarrisonTests.cs`, `BuildHUD.cs`, `HUD.cs`, `FirstBatchSceneTests.cs`, `PlacementTests.cs`, and `MovementAndCommandTests.cs`. `FirstBatchSceneTests.cs` is diffed once from its earliest Task 2 snapshot through the current Task 3 state, avoiding duplicate snapshot segments. `TowerGarrisonTests.cs` uses the preserved `final-a04-fix-before` snapshot because it had no earlier hardening diff.
- The Task 1 pre-task side is **reconstructed from the exact approved plan changes because the original agent snapshot/diff was not preserved at pause**; it is not represented as a surviving original snapshot. Task 2 and Task 3 use their preserved before snapshots. Manual inspection of the complete patch found only the approved A04, D01, D04 and C01 changes. The final A04 correction itself changed two test files only.
- The extensive pre-existing uncommitted scene, prefab, art and other script changes visible in git status are user work outside this batch. Documents and `.superpowers/sdd/hardening/` evidence are intentionally outside the eight-file source patch.

### Remaining checks and next-stage recommendation

- Final overall review identified one Important A04 evidence-strength issue: health-bar setup rebuilt the restored garrison before firing assertions. It was corrected as described above and followed by three exact passing controls plus the fresh 83/83 run. No production defect was observed. Final approval remains with the main controller.
- Remaining Minor: the pre-existing class/fixture-filter run header can display `Test cases: 0` while child tests execute; per-leaf lines and final summaries remain authoritative. The runner was not changed.
- Physical GUI acceptance for **drag, wheel, middle-button and Alpha1-6 remains unperformed because Windows is locked**. No physical input was injected, and these checks are not claimed by the PlayMode result.
- Task 4 did not repeat the already approved editor-only D01 preflight probes; their no-mutation/font evidence remains in `task-2-editor-preflight.log`. The earlier sceneSaving incident remains documented above and was not repeated.
- On the current automated evidence, it is reasonable for the main controller to recommend entering the **separately designed A01/A02 project after its independent whole-batch review confirms no Critical/Important issue remains**. This is only a handoff recommendation: Task 4 and the batch are not self-approved here, and no A01/A02 implementation was started.

Task 4 machine/report details: `.superpowers/sdd/hardening/task-4-report.md`. Final review patch: `.superpowers/sdd/hardening/final-review.patch`.
