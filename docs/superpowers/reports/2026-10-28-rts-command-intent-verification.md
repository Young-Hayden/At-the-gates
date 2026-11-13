# RTS command-intent verification

Date: 2026-10-28  
Project: `D:\Unity\projects\At the Gates`  
Commits: none; no push and no worktree.

## Task 1 — CombatAI Move / AttackMove intent core

Status: **DONE**.

- Pre-edit complete baseline: **83/83 passed**, 0 failed/skipped/inconclusive, 47.751s.
- Test-first RED: six expected CS1061 errors for missing `OrderMove`, `OrderAttackMove` and `CompleteOrder`.
- Implemented temporary `Auto / Move / AttackMove / Garrison` intent, completion/aborted-path restoration and attack-move destination resume. Explicit Attack/Hold restore Auto; `ClearOrder` delegates to `CompleteOrder`; chase paths remain callback-free.
- New production-path tests: **3/3 passed**, including plain-move non-interruption, attack-move resume after a kill, and automatic combat recovery after a building blocks the path.
- Controls: `MovementAndCommandTests` **16/16 passed** (including both Healer controls and AI assignment); `AIProductionBudgetTests` **6/6 passed**.
- Complete PlayMode before the review-only assertion strengthening: **86/86 passed**, 0 failed/skipped/inconclusive, 48.864s.
- Final editor state: stopped; project dirty=false; SampleScene dirty=false; not compiling or updating.

Evidence and self-review: `.superpowers/sdd/command-intent/task-1-report.md`, `task-1-diff.patch`, and the adjacent machine logs. Task 2 SelectionManager/garrison wiring and Task 3 unreachable-target reselection remain outside this result. Physical GUI input was not tested.

### Task 1 review-only test hardening

Added the requested immediate assertion that the route enemy is dead before the attack-move destination wait. Production code was unchanged. Compilation `codex-22492-99` completed with 0 errors/warnings; the exact strengthened test passed **1/1**, with 0 failed/skipped/inconclusive, in 1.216s. Machine log: `.superpowers/sdd/command-intent/task-1-attackmove-kill-assertion.log`; `task-1-diff.patch` was regenerated.

## Task 2 — SelectionManager attack-move and garrison wiring

Status: **DONE**.

- Test-first RED: **3 passed, 2 failed**. The attack button killed the route enemy but left the fighter 5.787555 units short of its ground destination; the garrison traveller was interrupted by the route enemy. Evidence: `.superpowers/sdd/command-intent/task-2-red.log`.
- Split armed ground attack-move from ordinary `IssueMove` while preserving ordinary target priority and sharing the original formation/A* loop. Combatants receive `OrderAttackMove`/`OrderMove` plus completion callbacks; workers and non-combatant healers are excluded from attack-move. Garrison approach now uses `OrderGarrison`, with failure completion and existing successful `TowerGarrison` cleanup.
- GREEN: `CommandIntentTests` **5/5 passed**, 0 failed/skipped/inconclusive, 4.702s. Complete PlayMode regression **88/88 passed**, 0 failed/skipped/inconclusive, 50.114s. Compilation completed with 0 errors/warnings.
- The full run includes the required distant real-arrival garrison, under-construction rejection, worker manual-idle and both Monk move/heal controls; all passed.
- Evidence and self-review: `.superpowers/sdd/command-intent/task-2-report.md`, `task-2-diff.patch`, `task-2-command-intent-green.log` and `task-2-full-regression.log`. Physical GUI input was not tested.

## Task 3 — unreachable automatic target reselection and retry

Status: **DONE**.

- Test-first RED: **6 passed, 3 failed**. Automatic combat locked onto the nearest isolated enemy, the reconnect control could not get past that initial failure, and AttackMove with no reachable alternate did not resume its destination. The forced-target compatibility control passed. Evidence: `.superpowers/sdd/command-intent/task-3-red.log`.
- Added an optional candidate filter to the central nearest-enemy overload for both units and buildings; omitted filters preserve existing behaviour. Non-forced Chase failures now clear the target and exclude only the same target/goal for `retargetInterval`, allowing moved goals and later retries. Forced attacks retain their target and use the existing repath backoff.
- GREEN: `CommandIntentTests` **9/9 passed**, 0 failed/skipped/inconclusive, 7.379s. This covers reachable-alternate selection, reconnect/retry, forced-target preservation, and AttackMove destination resume with no alternate. Compilation completed with 0 errors/warnings.
- Complete PlayMode regression: **92/92 passed**, 0 failed/skipped/inconclusive, 53.373s. Worker Threat/Hold, AI production/wipeout, Assignment, Move, AttackMove and Garrison controls all passed.
- Evidence and self-review: `.superpowers/sdd/command-intent/task-3-report.md`, `task-3-diff.patch`, `task-3-command-intent-green.log` and `task-3-full-regression.log`. The retry cache is intentionally one target/goal entry, as specified. Physical GUI input was not injected.
- Final editor state: stopped; project dirty=false; SampleScene dirty=false; not compiling or updating.

### Task 3 review fix — connectivity retry

Status: **DONE and independently approved**.

The first Task 3 review found that a map reconnection could not retry the unchanged target during the failure window, and that the reconnect fixture disabled the alternate and cleared the retry cache. The fix adds a read-only `MapGrid.RegionVersion`, incremented when terrain is scanned or dynamic building footprints relabel regions. `CombatAI` records the failure version and permits the same target/goal to retry when the version changes. A separate `unreachableRetrySeconds` keeps the failure window configurable independently of the short target-retarget cadence.

The reconnect test now keeps the alternate alive, moves it out of aggro to prove the failure record is retained (`Target == null`), records the region version, reconnects the map, asserts the version increased, and verifies the original target is selected and physically approached. A controlled temporary removal of the version check failed the test as expected (`.superpowers/sdd/command-intent/task-3-version-red.log`); the restored implementation passed.

Final machine suite after the fix: **93/93 Passed, 0 Failed, 0 Skipped, 0 Inconclusive, 56.297s**. Evidence: `.superpowers/sdd/command-intent/task-3-version-green-full.log`, `task-3-review-fix.diff`. Independent review: Approved, no Critical/Important. Remaining Minor items: field-name overlap with Gatherer, two assertion-strength improvements, no direct RelabelRegions version test, and a conservative version bump on every blocker change. Task 4 final integration was not started per the user's stop instruction. Physical GUI input remains unperformed.

## Task 4 — final integration and Minor cleanup

Status: **verification complete, whole-work review requested**.

- Renamed the CombatAI tuning field to `autoTargetRetrySeconds`; Gatherer's unrelated `unreachableRetrySeconds` is unchanged.
- Reconnect test now proves in-window rejection before reconnecting and bounds prompt reacquisition to 1s.
- Pure-timeout test now proves rejection before expiry.
- Added a direct `MapGrid.RegionVersion` test for adding/removing a building footprint.
- Removed the duplicate `MapGrid.Instance` read in `FailChase`.

Final complete PlayMode after all changes:

```text
[Regression] PlayMode run started. Test cases: 94
[Regression] PlayMode run finished: Passed. Passed=94, Failed=0, Skipped=0, Inconclusive=0, Duration=58.440s
```

Evidence: `.superpowers/sdd/command-intent/final-full-regression.log`; whole-work diff: `.superpowers/sdd/command-intent/final-review.patch`; report: `task-4-report.md`.

Physical GUI acceptance (UI/world drag, wheel/middle camera input, Alpha1–6) was not performed. No commits, pushes or worktrees were created.

## Final review Important fix — forced attack during a temporary intent

Status: **fixed and re-verified; controller re-review required.**

The final whole-work review found that `CombatAI.OrderAttack` switched to Auto/forced without stopping a path left by `Move`, `AttackMove` or `Garrison`. The stale arrival callback still ran: `CompleteOrder()` cleared the forced target for Move/AttackMove, and `TowerGarrison.Garrison()` fired for a garrison approach.

Test-first RED (two real PlayMode tests in `CommandIntentTests`):

- `.superpowers/sdd/command-intent/final-forced-order-fix-red-move.log` — after the old path's arrival time, `Expected: same as <Isolated forced target (UnitActor)> But was: null`.
- `.superpowers/sdd/command-intent/final-forced-order-fix-red-garrison.log` — `Expected: same as <Isolated garrison target (UnitActor)> But was: null`.

The first fixture attempt placed the island within the archer's real 4.5 attack range, so Chase replaced the path itself; the saved RED runs widen the gap beyond 4.5 so the forced target is genuinely unreachable.

Fix: when `orderKind` is `Move`, `AttackMove` or `Garrison`, `OrderAttack` calls `mover.Stop()` before entering Auto/forced. Normal Auto chase paths are not stopped; no generic command queue and no `UnitMover` change.

GREEN:

- `CommandIntentTests` **13/13**, 0 failed/skipped/inconclusive, 17.589s.
- `TowerGarrisonTests` **13/13**, `RubbleAndGarrisonCommandTests` **2/2**, `MovementAndCommandTests` **16/16**.
- Complete PlayMode: **96/96 Passed, 0 Failed, 0 Skipped, 0 Inconclusive, 65.328s** — `.superpowers/sdd/command-intent/final-full-regression.log` (byte-identical copy of `Logs/RegressionTests.txt`, SHA-256 `389fcece208907f7044abe42ad8bf273c3d0934fe0b4b2924c5232169d973468`).
- Compilation: 0 errors / 0 warnings. Editor stopped; project and `MainMenu.scene` dirty=false.

Reports/diffs: `.superpowers/sdd/command-intent/final-forced-order-fix-report.md`, `final-forced-order-fix.diff` (focused), `task-3-review-fix.diff` (refreshed from `pause-current` to current), and `final-review.patch` (earliest snapshot to current). Remaining Minor: fixed-wait timing in the two new tests, the conservative region-version bump, and unperformed physical GUI acceptance. Not self-approved.

