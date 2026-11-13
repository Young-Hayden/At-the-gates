# RTS first batch verification — Task 1

Date: 2026-10-26  
Project: `D:\Unity\projects\At the Gates`  
Scope: untouched PlayMode baseline and exact-full-name filtered execution.

## Result

- **Untouched baseline: PASS** — 59 test cases; Passed=59, Failed=0, Skipped=0, Inconclusive=0.
- **Final filtered execution: PASS** — `RunStarted` reports the actual match count `Test cases: 1`; only `SaveLoadTests.MalformedSave_ReturnsFalseWithoutThrowing` executed; Passed=1, Failed=0.
- **Non-hardcoded count control: PASS** — an invalid exact full name reports `Test cases: 0`, emits no per-test result, and finishes Passed=0, Failed=0.
- **Review items: RESOLVED** — actual leaf matching is preserved across domain reload via SessionState and cleaned on completion/exception/stale-run paths; the original stale-run comment is restored.

## 1. Untouched full baseline

This run was completed before `RegressionTestMenu.cs` was edited or snapshotted. The current workspace, including its pre-existing uncommitted changes, was used as-is.

### Commands

1. MCP bridge status: connected to project root `D:\Unity\projects\At the Gates`, port 55523.
2. `unity_editor(request_compile)` — request `codex-26928-33`: success; scripts already up to date; 0 errors, 0 warnings.
3. `unity_editor(wait_for_compile)` — request `codex-26928-34`: the installed bridge reports this action as deprecated and states that compilation commands are synchronous.
4. `unity_editor(wait_for_idle)` — request `codex-26928-35`: success; no jobs, compilation, or imports in flight.
5. `execute_csharp_script` — request `codex-26928-36`:

```csharp
RegressionTestMenu.RunPlayModeTests();
return "Baseline PlayMode run started";
```

The call returned `Baseline PlayMode run started`. A local waiter watched the actual `Logs/RegressionTests.txt` until the run-finished line appeared.

### Machine log

The following is the complete baseline log emitted by the run (captured from `Logs/RegressionTests.txt` before the filtered run overwrote it):

```text
[Regression] PlayMode run started. Test cases: 59
[Regression] Passed: MovementAndCommandTests.AiAssignGatherers_SkipsUnreachableNodeInIsolatedPocket (0.310s)
[Regression] Passed: MovementAndCommandTests.AssignClear_WithCargo_DeliversOnceBeforeClearing (0.502s)
[Regression] Passed: MovementAndCommandTests.AutoAssign_SkipsUnreachableNodeAndChoosesReachableWork (0.198s)
[Regression] Passed: MovementAndCommandTests.AutoAssign_ThreeWorkers_ChooseDistinctNearestNodes (0.206s)
[Regression] Passed: MovementAndCommandTests.DeadSelectedUnit_IsRemovedBeforeDelayedCorpseDestruction (0.209s)
[Regression] Passed: MovementAndCommandTests.DeadSelection_DirectIssueMove_SkipsDeadEntryButMovesLivingUnit (0.207s)
[Regression] Passed: MovementAndCommandTests.Hud_DeadSelectedUnit_IsExcludedBeforeDelayedCorpseDestruction (0.216s)
[Regression] Passed: MovementAndCommandTests.OrdinaryMove_MonkFinishesDistantMoveBeforeHealingInRangeAllies (1.045s)
[Regression] Passed: MovementAndCommandTests.OrdinaryMove_MonkHealsOnlyAlliesAlreadyInsideHealRange (0.451s)
[Regression] Passed: MovementAndCommandTests.OrdinaryMove_PreservesCargo_ExplicitGatherDeliversBeforeNewWork (0.323s)
[Regression] Passed: MovementAndCommandTests.OrdinaryMove_WorkerRemainsManuallyIdle_AutomaticWorkDoesNotOverride (0.698s)
[Regression] Passed: MovementAndCommandTests.StalePath_NewBlocker_StopsWithoutEnteringOrReportingArrival (1.319s)
[Regression] Passed: MovementAndCommandTests.UnreachableGather_RetriesWithBackoff_AndHarvestsAfterReconnect (1.670s)
[Regression] Passed: PauseMenuTests.Escape_AfterCancelInSameFrame_DoesNotReopenPauseMenu (0.224s)
[Regression] Passed: PauseMenuTests.Escape_AfterMatchEnded_DoesNotOpenPauseMenu (0.197s)
[Regression] Passed: PauseMenuTests.Escape_WhilePlacingBuilding_DoesNotOpenPauseMenu (0.199s)
[Regression] Passed: PauseMenuTests.MainMenu_ContinueButton_OnlyExistsWhenSaveIsValid (0.491s)
[Regression] Passed: PauseMenuTests.SetOpen_PausesAndClosesWithoutLeavingTimeStopped (0.199s)
[Regression] Passed: PlacementTests.AIPlacementValidation_RejectsUnitInsideFootprint (0.194s)
[Regression] Passed: PlacementTests.Cancel_RightClickInSameFrame_DoesNotIssueMoveOrder (0.206s)
[Regression] Passed: PlacementTests.DynamicBlockers_RemovingOneRegistration_KeepsCellBlockedUntilLastRemove (0.197s)
[Regression] Passed: PlacementTests.Ghost_OverExistingRealFootprint_PreservesBlockersThroughBeginAndCancel (0.203s)
[Regression] Passed: PlacementTests.MouseCommitPath_WhenCellIsAlreadyOccupied_DoesNotPlaceOrSpendAgain (0.203s)
[Regression] Passed: PlacementTests.TryPlaceAt_PreviewValidButSourceCostUnaffordable_ReturnsFalseWithoutSideEffects (0.194s)
[Regression] Passed: ProductionDependencyTests.Archery_RemainsTheArcherFactory (0.019s)
[Regression] Passed: ProductionDependencyTests.Barracks_DoesNotProduceArchers (0.021s)
[Regression] Passed: ProductionDependencyTests.Barracks_KeepWarriorAndLancerProducts (0.000s)
[Regression] Passed: RubbleAndGarrisonCommandTests.DestroyedRubble_KeepsFootprintAndColliderBlocking (0.208s)
[Regression] Passed: RubbleAndGarrisonCommandTests.RightClickTower_DistantArcherWalksToBaseThenGarrisonsAndDamagesEnemy (1.089s)
[Regression] Passed: SaveLoadTests.FullSnapshot_RoundTripsCoreMatchState (0.586s)
[Regression] Passed: SaveLoadTests.MalformedSave_ReturnsFalseWithoutThrowing (0.225s)
[Regression] Passed: SaveLoadTests.UnsupportedVersion_ReturnsFalseWithoutThrowing (0.201s)
[Regression] Passed: SceneLifecycleTests.Restart_ReloadingScene_ClearsOldStaticsBlockersAndGhost (0.389s)
[Regression] Passed: TowerGarrisonTests.EnemyInRange_ArcherPlaysAttackAndFacesTarget (0.214s)
[Regression] Passed: TowerGarrisonTests.EnemyInRange_ProjectileSpawnsAboveTowerAtArcher (0.209s)
[Regression] Passed: TowerGarrisonTests.Garrison_ShowsRealArcherOnTower_AndUntargetsIt (0.214s)
[Regression] Passed: TowerGarrisonTests.Garrison_TowerRemainsDamageable (0.209s)
[Regression] Passed: TowerGarrisonTests.GarrisonedArcher_IgnoresIncomingArrow_AndIsDamageableAfterRelease (0.457s)
[Regression] Passed: TowerGarrisonTests.GarrisonedArcher_IsNotSelectable_AndCannotManASecondTower (0.241s)
[Regression] Passed: TowerGarrisonTests.SceneReload_DoesNotResurrectGarrisonedArcher (0.404s)
[Regression] Passed: TowerGarrisonTests.TowerDemolished_ArcherSurvivesOnWalkableGround (0.917s)
[Regression] Passed: TowerGarrisonTests.TowerDestroyed_ArcherIsReleasedBeforeTheObjectDisappears (0.919s)
[Regression] Passed: TowerGarrisonTests.TowerWreck_LeavesArcherOnWalkableGround_CombatReady (0.923s)
[Regression] Passed: TowerGarrisonTests.Ungarrison_RestoresArcherState_AndReturnsItToTheField (0.214s)
[Regression] Passed: WipeoutDefeatTests.Wipeout_AiBrokeAndHelpless_IsPlayerWin (0.724s)
[Regression] Passed: WipeoutDefeatTests.Wipeout_BrokeAndHelpless_IsDefeat (0.723s)
[Regression] Passed: WipeoutDefeatTests.Wipeout_WithRecoveryMoney_StaysPlaying (2.175s)
[Regression] Passed: WorkerBehaviorTests.Hud_Noncombatant_HidesAttackStats (0.200s)
[Regression] Passed: WorkerBehaviorTests.ThreatQuery_IgnoresNoncombatantsAndUnarmedBuildings (0.205s)
[Regression] Passed: WorkerBehaviorTests.Worker_Damage_InterruptsWork_AndPreservesCargo (0.363s)
[Regression] Passed: WorkerBehaviorTests.Worker_ExplicitGatherOrClear_CancelsRetreat (0.202s)
[Regression] Passed: WorkerBehaviorTests.Worker_HasNoCombatDamage_AndCannotAcceptAttackOrder (0.209s)
[Regression] Passed: WorkerBehaviorTests.Worker_ManualIdle_StaysManualAfterSafe (1.267s)
[Regression] Passed: WorkerBehaviorTests.Worker_NoBase_WaitsInRetreat_ThenFleesWhenBaseBuilt (1.525s)
[Regression] Passed: WorkerBehaviorTests.Worker_OrdinaryMove_ClearsRetreatAndSetsManualIdle (0.203s)
[Regression] Passed: WorkerBehaviorTests.Worker_ReachesBase_DeliversCargoExactlyOnce (0.912s)
[Regression] Passed: WorkerBehaviorTests.Worker_RetreatBaseDestroyedMidRoute_DoesNotDeliverAtRubble_ThenFindsReplacement (7.022s)
[Regression] Passed: WorkerBehaviorTests.Worker_SafeAtBase_ResumesWorkAfterThreatClears (1.762s)
[Regression] Passed: WorkerBehaviorTests.WorkerPrefabs_HaveNoCombatStats (0.194s)
[Regression] PlayMode run finished: Passed. Passed=59, Failed=0, Skipped=0, Inconclusive=0, Duration=35.340s
```

Failures: none. Failure messages/stack traces: none.

## 2. Implementation and compilation

After the baseline passed, the original file was snapshotted byte-for-byte to `.superpowers/sdd/task-1-before/Assets/Editor/RegressionTestMenu.cs`.

`RegressionTestMenu.cs` now keeps the existing menu attribute on `RunPlayModeTests()`, routes the unfiltered menu call through `RunTests(null)`, exposes `RunPlayModeTest(string fullName)`, rejects null/empty names with `ArgumentException`, and passes `testNames` into the existing PlayMode/assembly filter.

Post-edit `unity_editor(request_compile)` — request `codex-26928-37`: **Compilation completed successfully**, compile observed, 0 errors, 0 warnings. `unity_editor(wait_for_idle)` — request `codex-26928-38`: success.

## 3. Initial exact-name filtered control (pre-review)

`execute_csharp_script` — request `codex-26928-39`:

```csharp
RegressionTestMenu.RunPlayModeTest("SaveLoadTests.MalformedSave_ReturnsFalseWithoutThrowing");
return "Filtered control started";
```

The call returned `Filtered control started`. Final machine-generated `Logs/RegressionTests.txt`:

```text
[Regression] PlayMode run started. Test cases: 59
[Regression] Passed: SaveLoadTests.MalformedSave_ReturnsFalseWithoutThrowing (0.214s)
[Regression] PlayMode run finished: Passed. Passed=1, Failed=0, Skipped=0, Inconclusive=0, Duration=0.240s
```

### Control assessment

- Exact full name used: yes; no wildcard or broadened filter.
- Exactly one per-test result: yes.
- `Passed=1, Failed=0`: yes.
- `RunStarted` reports `Test cases: 1`: **no; actual value is 59**.

This initial discrepancy is retained explicitly rather than rewritten or hidden. It was resolved by the review rework verified below; the execution filter already selected exactly one test.

## 4. Review rework verification

### Implementation

`RunStarted` now reads the exact requested name from SessionState and recursively counts non-suite `ITestAdaptor` leaves with an ordinal `FullName` match. Unfiltered menu runs continue to use `testsToRun.TestCaseCount`. The exact-name state is set before `Execute` and cleared on normal completion, `Execute` exception, and stale-run cleanup. The original stale-run explanation comment was restored.

Post-rework compilation (`codex-26928-40`) completed successfully with 0 errors/0 warnings; `wait_for_idle` (`codex-26928-41`) succeeded.

### Valid exact-name control

Executed (`codex-26928-42`):

```csharp
RegressionTestMenu.RunPlayModeTest("SaveLoadTests.MalformedSave_ReturnsFalseWithoutThrowing");
return "Filtered control started";
```

Machine log:

```text
[Regression] PlayMode run started. Test cases: 1
[Regression] Passed: SaveLoadTests.MalformedSave_ReturnsFalseWithoutThrowing (0.327s)
[Regression] PlayMode run finished: Passed. Passed=1, Failed=0, Skipped=0, Inconclusive=0, Duration=0.387s
```

Result: PASS — actual matched start count 1, exactly one test executed, Passed=1/Failed=0.

### Invalid-full-name control

Executed (`codex-26928-43`):

```csharp
RegressionTestMenu.RunPlayModeTest("SaveLoadTests.DoesNotExist_Task1CountControl");
return "Invalid-name count control started";
```

Machine log:

```text
[Regression] PlayMode run started. Test cases: 0
[Regression] PlayMode run finished: Passed. Passed=0, Failed=0, Skipped=0, Inconclusive=0, Duration=0.005s
```

Result: PASS — actual matched start count 0 and no per-test result, demonstrating that the count is not hardcoded or based on request count.

A final SessionState inspection (`codex-26928-44`) returned `OwnedRun=False; FilteredFullName=''`, confirming normal-completion cleanup.

---

# RTS first batch verification — Task 2 (D01)

Date: 2026-10-26  
Scope: production-panel bindings/content, real-factory queueing, and narrow SampleScene repair. Task 1 baseline above is unchanged.

## Test-first result

The actual-scene test was added and run before `BuildHUD.cs` was changed. Exact command:

```csharp
RegressionTestMenu.RunPlayModeTest("FirstBatchSceneTests.ProductionPanel_HasBoundContent_AndRealFactoryCanQueue");
```

RED machine output (`codex-26928-49`; preserved in `.superpowers/sdd/task-2-red.log`):

```text
[Regression] PlayMode run started. Test cases: 1
[Regression] Failed: FirstBatchSceneTests.ProductionPanel_HasBoundContent_AndRealFactoryCanQueue (4.226s)
  Produce0 icon binding
  Expected: not null
  But was:  null
[Regression] PlayMode run finished: Failed(Child). Passed=0, Failed=1, Skipped=0, Inconclusive=0, Duration=4.293s
```

After compilation (0 errors/0 warnings), only `Tools/首批修复/补齐工厂生产面板` was executed with SampleScene active and clean. The same exact test passed (`codex-26928-56`), passed again against the final diff-cleaned scene (`codex-26928-62`), and passed the final completion gate (`codex-26928-64`):

```text
[Regression] PlayMode run started. Test cases: 1
[Regression] Passed: FirstBatchSceneTests.ProductionPanel_HasBoundContent_AndRealFactoryCanQueue (5.358s)
[Regression] PlayMode run finished: Passed. Passed=1, Failed=0, Skipped=0, Inconclusive=0, Duration=5.422s
```

## Production dependency regressions

All exact filtered regressions passed after the scene test turned GREEN:

```text
[Regression] Passed: ProductionDependencyTests.Barracks_DoesNotProduceArchers (0.025s)
[Regression] PlayMode run finished: Passed. Passed=1, Failed=0, Skipped=0, Inconclusive=0, Duration=0.087s

[Regression] Passed: ProductionDependencyTests.Barracks_KeepWarriorAndLancerProducts (0.028s)
[Regression] PlayMode run finished: Passed. Passed=1, Failed=0, Skipped=0, Inconclusive=0, Duration=0.090s

[Regression] Passed: ProductionDependencyTests.Archery_RemainsTheArcherFactory (0.030s)
[Regression] PlayMode run finished: Passed. Passed=1, Failed=0, Skipped=0, Inconclusive=0, Duration=0.094s
```

Combined Task 2 final result: **4 targeted tests passed, 0 failed**; no full-suite result is claimed.

## Scene diff audit

Unrelated randomized animation-speed modifications produced by the scene save were restored only within prefab instance `768207232`. Final snapshot comparison contains six new Icon/Cost GameObjects and their components, three Produce button child-list updates, and six HUD icon/cost reference updates. There are no removed objects, no Canvas replacement, and no unrelated serialized changes. Full evidence: `.superpowers/sdd/task-2-report.md`; patch: `.superpowers/sdd/task-2-diff.patch`.

---

# RTS first batch verification — Task 3 (D02)

Date: 2026-10-26  
Scope: HUD-origin left presses must not become world drag selections; world-origin drag selection must remain functional.

## Test-first and targeted result

Both behavior tests were added before production implementation. The exact UI-origin run (`codex-26928-72`) produced the brief's expected missing-interface RED: Passed=0, Failed=1. Post-implementation compilation (`codex-26928-75`) succeeded with 0 errors/0 warnings.

Exact targeted reruns then passed independently:

```text
[Regression] Passed: InputGateTests.LeftPress_StartedOnUI_NeverStartsBoxSelection (0.454s)
[Regression] PlayMode run finished: Passed. Passed=1, Failed=0, Skipped=0, Inconclusive=0, Duration=0.525s

[Regression] Passed: InputGateTests.LeftPress_StartedInWorld_BoxSelectsUnitAfterDrag (0.451s)
[Regression] PlayMode run finished: Passed. Passed=1, Failed=0, Skipped=0, Inconclusive=0, Duration=0.517s
```

The positive control selects an actual Blue `UnitMover` inside the drag rectangle and asserts both membership and a selection count of one. RED/GREEN logs are preserved under `.superpowers/sdd/task-3-*.log`.

## Regression result

A full PlayMode run (`codex-26928-81`) passed all 62 tests. Required classes remained green: MovementAndCommandTests 13/13, PlacementTests 6/6, PauseMenuTests 5/5, RubbleAndGarrisonCommandTests 2/2; InputGateTests 2/2.

```text
[Regression] PlayMode run started. Test cases: 62
[Regression] PlayMode run finished: Passed. Passed=62, Failed=0, Skipped=0, Inconclusive=0, Duration=48.094s
```

Full machine log: `.superpowers/sdd/task-3-full-regression.log`. Implementation, snapshot hash, isolated diff audit, and file list: `.superpowers/sdd/task-3-report.md`; patch: `.superpowers/sdd/task-3-diff.patch`. No commits or pushes were created.

---

# RTS first batch verification — Task 4 (D03)

Date: 2026-10-26  
Scope: HUD/modal gating for RTS camera middle-drag and wheel zoom, with world-started drag continuity and ordinary world controls preserved. Earlier sections are unchanged.

## Test-first RED and targeted GREEN

Five camera tests were added to `InputGateTests` before `RTSCamera` was edited; the two existing D02 tests were retained. Each new exact-name run failed before implementation with the expected missing seven-parameter `HandlePointerInput` assertion (5 separate runs, each Passed=0/Failed=1). RED logs are `.superpowers/sdd/task-4-red-*.log`.

Post-implementation compilation (`codex-26928-88`) succeeded with 0 errors/0 warnings. Each new exact-name rerun then passed independently (`codex-26928-89` through `codex-26928-93`), one test per run, Passed=1/Failed=0. GREEN logs are `.superpowers/sdd/task-4-green-*.log`.

Coverage includes: HUD and modal position/zoom immunity; ordinary world wheel zoom; no HUD-origin middle drag; world-origin drag continuing over HUD and ending on release; and modal cancellation of an existing drag.

## Full PlayMode regression

The complete suite was run once after implementation (`codex-26928-94`). Machine-generated `Logs/RegressionTests.txt` reports:

```text
[Regression] PlayMode run started. Test cases: 67
[Regression] PlayMode run finished: Passed. Passed=67, Failed=0, Skipped=0, Inconclusive=0, Duration=49.559s
```

All 67 leaf results are Passed, including InputGateTests 7/7, PauseMenuTests 5/5, and SceneLifecycleTests 1/1. The normalized log text matches `.superpowers/sdd/task-4-full-regression.log`; the saved copy differs only in text encoding/newline serialization.

## Minimap smoke

A separate runtime smoke (`codex-26928-98`, not counted in the 67 automated tests) verified both that HUD wheel input left camera position/zoom unchanged and that the unchanged `Minimap.OnPointerClick` moved the main camera to the requested `(50,50,-10)` map position. Temporary objects were destroyed and play mode was exited. Details: `.superpowers/sdd/task-4-minimap-smoke.log`.

## Scope and artifacts

Only `Assets/Scripts/RTSCamera.cs` and `Assets/Tests/PlayMode/InputGateTests.cs` were edited for implementation/testing. SelectionManager and Minimap were not changed. Before snapshots are under `.superpowers/sdd/task-4-before/`; the isolated patch is `.superpowers/sdd/task-4-diff.patch`; implementation details and audit results are in `.superpowers/sdd/task-4-report.md`. No commits, pushes, or worktrees were created.


---

# RTS first batch verification — Task 5 (D04)

Date: 2026-10-26
Scope: scene-configured 1–6 quick-build shortcuts and indexed `TryQuickBuild(int)`. Earlier sections are unchanged.

## RED and targeted GREEN

Live inspection found `placer.quickBuild.Length=0` while `HUD.buildPrefabs` had the required six entries in order. Three tests were added before implementation. Exact-name RED runs each failed 1/1: scene order expected 6 but found 0; valid-index and invalid-index tests both reported the missing `BuildingPlacer.TryQuickBuild(int)` entry point. Logs: `.superpowers/sdd/task-5-red-*.log`.

Implementation added `TryQuickBuild(int)`, retained `HandleQuickKeys` as the only numeric-key loop, and configured the scene by a narrow editor-C# clone of `HUD.buildPrefabs`. Compilation request `codex-26928-111` reported 0 errors/0 warnings. All three exact GREEN runs passed 1/1. D01 production-panel, scene-lifecycle, and all six pre-existing placement tests were also rerun by exact name and each passed 1/1. Logs: `.superpowers/sdd/task-5-green-*.log` and `task-5-reg-*.log`.

## Scene audit

The editor save introduced unrelated randomized Tilemap animation-speed values; as in D01, those were removed while retaining only the six-entry `quickBuild` block. Final snapshot diff for `SampleScene.scene` is 7 insertions and 1 deletion, all inside `BuildingPlacer.quickBuild`. HUD was not modified by this task, and BuildHUD was not run.

## Full-suite result

The complete suite grew from 67 to 70 tests. One full run passed all 70. Two other full runs passed 69/70 because the pre-existing `RubbleAndGarrisonCommandTests.RightClickTower_DistantArcherWalksToBaseThenGarrisonsAndDamagesEnemy` approach-distance assertion (`Vector2.Distance(...) < 2.1f`, in world units, not seconds) exceeded its threshold by 0.0023 and 0.066; it passed 1/1 in isolation. Every D04, placement, FirstBatchScene, and SceneLifecycle result passed in every full run.

```text
[Regression] PlayMode run finished: Passed. Passed=70, Failed=0, Skipped=0, Inconclusive=0, Duration=53.391s
[Regression] PlayMode run finished: Failed(Child). Passed=69, Failed=1, Skipped=0, Inconclusive=0, Duration=57.592s
```

Machine logs: `.superpowers/sdd/task-5-full-regression-rerun.log` and `task-5-full-regression-final.log`. Final compilation request `codex-26928-137` reported 0 errors and 0 warnings. Full implementation and audit details: `.superpowers/sdd/task-5-report.md`; isolated patch: `.superpowers/sdd/task-5-diff.patch`. No commits, pushes, or worktrees were created.

---

# RTS first batch verification — Task 6 (A04)

Date: 2026-10-26  
Scope: half-built towers cannot be garrisoned through the normal entry; legacy save restoration keeps the garrison relation but cannot fire until construction finishes. Earlier sections are unchanged.

## RED and targeted GREEN

Two tests were added to `TowerGarrisonTests` before production changes. The test assembly could not compile against the pre-implementation code because `RestoreGarrison` was missing; Tuanjie MCP `request_compile` request `codex-26928-141` reported one error (CS1061 at `TowerGarrisonTests.cs:540`) and zero warnings. The exact machine capture is `.superpowers/sdd/task-6-red-compile.log`. Because compilation stopped at the missing API, the pre-fix behavioral garrison/firing conditions were not separately executed; this is recorded rather than reconstructed.

Post-implementation compilation request `codex-26928-142` reported 0 errors and 0 warnings. Both exact-name tests then passed independently:

```text
[Regression] PlayMode run started. Test cases: 1
[Regression] Passed: TowerGarrisonTests.UnderConstructionTower_RejectsNormalGarrison_AndRestoreCannotFireEarly (0.810s)
[Regression] PlayMode run finished: Passed. Passed=1, Failed=0, Skipped=0, Inconclusive=0, Duration=0.899s

[Regression] PlayMode run started. Test cases: 1
[Regression] Passed: TowerGarrisonTests.UnderConstructionTower_PlayerGarrisonOrder_IsConsumedWithoutPathOrGarrison (0.482s)
[Regression] PlayMode run finished: Passed. Passed=1, Failed=0, Skipped=0, Inconclusive=0, Duration=0.586s
```

The first run's shared `Logs/RegressionTests.txt` was concurrently overwritten by another editor run; its console capture is preserved as `.superpowers/sdd/task-6-green-construction.log`, and the second run as `task-6-green-player-order.log`. No machine output was reconstructed.

## Regression and full suite

- `TowerGarrisonTests` (11 existing + 2 new): 13/13 passed; `.superpowers/sdd/task-6-reg-towergarrison.log`.
- `RubbleAndGarrisonCommandTests`: 2/2 passed; `.superpowers/sdd/task-6-reg-rubble-garrison.log`.
- `SaveLoadTests`: 3/3 passed; `.superpowers/sdd/task-6-reg-saveload.log`.
- Full PlayMode suite: 72/72 passed, 0 failed/skipped/inconclusive, 57.461 s; `.superpowers/sdd/task-6-full-regression.log`.

```text
[Regression] PlayMode run started. Test cases: 72
[Regression] PlayMode run finished: Passed. Passed=72, Failed=0, Skipped=0, Inconclusive=0, Duration=57.461s
```

The known `RightClickTower_DistantArcherWalksToBaseThenGarrisonsAndDamagesEnemy` distance-threshold flake did not reproduce in this task's runs and was not modified.

## Scope and artifacts

Only `Assets/Scripts/TowerGarrison.cs`, `Assets/Scripts/SelectionManager.cs`, `Assets/Scripts/SaveGameService.cs`, and `Assets/Tests/PlayMode/TowerGarrisonTests.cs` were edited. Normal `Garrison` rejects under-construction towers; `RestoreGarrison` is used only by `SaveGameService`; both share one validation method. `TowerGarrison.Update` neither decrements the firing timer nor searches targets while the tower is null, dead, ghost, or under construction. The player tower order is consumed with the existing blocked marker before any path is created. A05 exit positioning was not touched. Before snapshots are under `.superpowers/sdd/task-6-before/`; full details: `.superpowers/sdd/task-6-report.md`; isolated patch: `.superpowers/sdd/task-6-diff.patch`. No commits, pushes, or worktrees were created.



---

# RTS first batch verification — Task 7 (C01)

Date: 2026-10-26  
Scope: loaded workers restored as non-manual immediately resume delivery and then gathering; a restored manual-idle worker keeps its cargo and stays put; a missing drop-off keeps the load with no credit. Earlier sections are unchanged.

## RED and targeted GREEN

Three tests were added to `MovementAndCommandTests` before production changes, using the existing worker/node/drop-off fixtures and real movement plus economy assertions. Pre-implementation compile request `codex-26928-164` reported 0 errors and 0 warnings. The loaded-cargo test failed exactly at the pre-fix status: expected `"运送中"`, was `"待命(持5)"`; the no-drop-off variant failed the same way. The manual-idle control passed before the fix as designed, proving the control is not weakened. Evidence: `.superpowers/sdd/task-7-red-loaded.log`, `task-7-red-manual-idle.log`, and `task-7-red-no-dropoff.log`.

Post-implementation compile request `codex-26928-172` reported 0 errors and 0 warnings. All three exact-name tests then passed:

```text
[Regression] Passed: MovementAndCommandTests.RestoreSaveState_NonManualCargo_DeliversAndResumesWork (0.673s)
[Regression] PlayMode run finished: Passed. Passed=1, Failed=0, Skipped=0, Inconclusive=0, Duration=0.796s

[Regression] Passed: MovementAndCommandTests.RestoreSaveState_ManualIdleWithCargo_StaysIdleAndKeepsCargo (0.721s)
[Regression] PlayMode run finished: Passed. Passed=1, Failed=0, Skipped=0, Inconclusive=0, Duration=0.856s

[Regression] Passed: MovementAndCommandTests.RestoreSaveState_NonManualCargoWithoutDropoff_WaitsWithoutCredit (0.746s)
[Regression] PlayMode run finished: Passed. Passed=1, Failed=0, Skipped=0, Inconclusive=0, Duration=0.831s
```

Machine captures: `.superpowers/sdd/task-7-green-loaded.log`, `task-7-green-manual-idle.log`, and `task-7-green-no-dropoff.log`.

## Regression and full suite

- `MovementAndCommandTests` (13 existing + 3 new): 16/16 passed; `.superpowers/sdd/task-7-reg-movement.log`.
- `WorkerBehaviorTests`: 12/12 passed; `.superpowers/sdd/task-7-reg-worker.log`.
- `SaveLoadTests`: 3/3 passed; `.superpowers/sdd/task-7-reg-saveload.log`.
- Full PlayMode suite: 75/75 passed, 0 failed/skipped/inconclusive, 58.964 s; `.superpowers/sdd/task-7-full-regression.log`.

```text
[Regression] PlayMode run started. Test cases: 75
[Regression] PlayMode run finished: Passed. Passed=75, Failed=0, Skipped=0, Inconclusive=0, Duration=58.964s
```

The known `RightClickTower_DistantArcherWalksToBaseThenGarrisonsAndDamagesEnemy` distance-threshold flake did not reproduce in this task's runs and was not modified.

## Scope and artifacts

Only `Assets/Scripts/Gatherer.cs` and `Assets/Tests/PlayMode/MovementAndCommandTests.cs` were edited. `RestoreSaveState` stops work, restores clamped cargo/kind/manual-idle, returns immediately for manual idle, enters `Phase.ToDropoff` with a fresh drop-off lookup for non-manual cargo, and otherwise keeps the existing `TryAutoAssign` path. Restore never credits directly and never serializes a drop-off or path. Save fields and version were not changed. Before snapshots are under `.superpowers/sdd/task-7-before/`; full details: `.superpowers/sdd/task-7-report.md`; isolated patch: `.superpowers/sdd/task-7-diff.patch`. No commits, pushes, or worktrees were created.

## Post-review test hardening

After the first GREEN pass, the two cargo tests were strengthened without changing production code. The manual-idle control now creates a reachable drop-off at `(3,0,0)` and asserts `Status == "待命(持5)"` and `TargetNode == null` immediately after restore plus the same status after 0.25 s; a wrong branch order that evaluated `carrying > 0` before `manualIdle` would move and credit within 0.25 s and fail those assertions. The loaded-cargo test now starts with `autoStart = true`, yields one frame, proves the node was already selected, then asserts restore clears `TargetNode` and enters `"运送中"`, covering stale-target cleanup. The hardened control remains green against the pre-fix code too, so it is a branch-order regression guard, not a replacement for the task RED; the RED remains the two loaded-cargo status failures. After hardening, compilation request `codex-26928-183` reported 0 errors/0 warnings and all targeted, class, and full runs were repeated with the results above; machine logs are the `task-7-*.log` files.


---

# RTS first batch verification — Task 8 (B09)

Date: 2026-10-26  
Scope: AI production now shares one living-plus-queued worker budget across all factories per tick. Military products remain outside the budget, existing over-cap workers are kept, and no AI refund path was introduced. Earlier sections are unchanged.

## RED and targeted GREEN

`Assets/Tests/PlayMode/AIProductionBudgetTests.cs` was added and compiled before the production edit (request `codex-26928-195`, 0 errors / 0 warnings). The required multi-factory test failed against the old `Produce`: two completed red houses each queued a worker in one tick (`Expected: 1, But was: 2`), with `maxGatherers=2` and one living red worker. Evidence: `.superpowers/sdd/task-8-red.log`.

Compile requests `codex-26928-199` and `codex-26928-204` then reported 0 errors / 0 warnings. Exact-name results:

```text
[Regression] Passed: AIProductionBudgetTests.Produce_TwoHouses_ShareOneWorkerBudgetPerTick (0.399s)
[Regression] Passed: AIProductionBudgetTests.Produce_OverCapLivingWorkers_StopNewHiresButKeepThem (0.365s)
[Regression] Passed: AIProductionBudgetTests.Produce_ZeroWorkerBudget_StillQueuesMilitaryProduct (0.364s)
[Regression] Passed: AIProductionBudgetTests.Produce_QueuedMilitary_DoesNotConsumeWorkerBudget (0.393s)
```

Machine captures: `.superpowers/sdd/task-8-green-budget.log`, `task-8-green-overcap.log`, `task-8-green-military.log`, and `task-8-green-military-queue.log`.

## Regression and full suite

- Full PlayMode suite: **79/79 passed**, 0 failed/skipped/inconclusive, 49.038 s; `.superpowers/sdd/task-8-full-regression.log`.
- In that run: `AIProductionBudgetTests` 4/4, `ProductionDependencyTests` 3/3, `WorkerBehaviorTests` 12/12, `MovementAndCommandTests` 16/16, `SaveLoadTests` 3/3, `TowerGarrisonTests` 13/13, `FirstBatchSceneTests` 2/2.

```text
[Regression] PlayMode run started. Test cases: 79
[Regression] PlayMode run finished: Passed. Passed=79, Failed=0, Skipped=0, Inconclusive=0, Duration=49.038s
```

The known `RubbleAndGarrisonCommandTests.RightClickTower_DistantArcherWalksToBaseThenGarrisonsAndDamagesEnemy` distance-threshold flake did not reproduce and was not modified. `SampleScene.scene` remained clean (dirty=false) and was not saved.

## Scope and artifacts

Only `Assets/Scripts/AIController.cs` and the new `Assets/Tests/PlayMode/AIProductionBudgetTests.cs` (plus Unity's `.meta`) were edited. `Produce` computes `max(0, maxGatherers - living workers - queued worker orders)` once per tick; each worker queue action decrements the shared budget. Living workers must be same-team, alive, and carry a `Gatherer`; queued workers are counted only from eligible completed same-team factories and only when the product prefab carries a `Gatherer`. Ghost/construction/queue-limit/affordability filters are preserved, and no refund or `CancelLast` path was added. Before snapshot is under `.superpowers/sdd/task-8-before/`; details: `.superpowers/sdd/task-8-report.md`; isolated patch: `.superpowers/sdd/task-8-diff.patch` (SHA-256 `c9d0e674795abb310f6c4dc3485f0845d2e5316c1810d7c185d8acfe9ac0f42f`). No commits, pushes, or worktrees were created.

---

# RTS first batch verification — Task 9 final verification

Date: 2026-10-27  
Status: **DONE_WITH_CONCERNS** — all seven behaviours have automated and actual-scene runtime evidence; only the production UI click and minimap click were driven through Game View UI input. OS-level world drag, wheel/middle drag, and numeric-key gestures were not manually reproduced.

## Baseline versus final

- Compilation before runtime verification (`codex-29048-3`): **0 errors / 0 warnings**; compile was observed and editor was idle.
- Untouched baseline: **59/59 passed**, 0 failed/skipped/inconclusive, 35.340 s.
- Final complete run: **79/79 passed**, 0 failed/skipped/inconclusive, **45.418 s**.
- `.superpowers/sdd/task-9-full-regression.log` and current `Logs/RegressionTests.txt` are byte-identical (`cmp` exit 0). Their start/final lines and all 79 leaf results were checked, so the already-complete suite was not redundantly rerun.
- The 20 added leaves are: FirstBatchSceneTests 2, InputGateTests 7, quick-build PlacementTests 2, construction TowerGarrisonTests 2, cargo-restore MovementAndCommandTests 3, and AIProductionBudgetTests 4.

## Per-ID final evidence

| ID | Implementation and automated evidence | Actual SampleScene / Game View evidence |
|---|---|---|
| D01 | Narrow production-content repair; `FirstBatchSceneTests.ProductionPanel_HasBoundContent_AndRealFactoryCanQueue` plus all 3 ProductionDependencyTests passed in 79/79. | Real `Barracks_Blue` selected in play mode; icons/costs were Warrior 80金 and Lancer 110金/30木. EventSystem clicked `Produce0`, charged 80 and produced a Warrior. With only the timer frozen for observation, EventSystem clicked `Produce1`: queue=1, product=Lancer_Blue, gold 520→410 and wood 300→270. Screenshots: `task-9-d01-production-panel.png`, `task-9-d01-production-queued.png`. |
| D02 | Both UI-origin and world-origin `InputGateTests` passed. | The same public `SelectionManager.HandleLeftMouseInput` production path was driven in SampleScene. UI-origin left selection empty; world-origin selected the real `Pawn_Blue_1` (exactly one in the refined 20 px rectangle). **Not an OS/GUI drag.** |
| D03 | All five camera gate tests plus the two selection tests passed; ordinary world zoom positive control passed. | Actual camera production path: HUD position/zoom unchanged, pause menu gave timeScale=0 and blocked input, world scroll changed zoom 15→8. A real EventSystem minimap click moved the camera `(0,5,-10)→(-30.78,5,-10)`; screenshot `task-9-d03-minimap-click.png`. **Not OS wheel/middle input.** |
| D04 | Scene-order, valid-index and invalid-index tests passed; existing placement tests remained green. | `TryQuickBuild(0..5)` entered ghosts in the exact House/Barracks/Archery/Monastery/Tower/Castle order. With no completed Blue command centre, slots 1–5 all refused placement with `需要先建成主基地`, no building/resource side effects, and their HUD buttons were disabled. Screenshot `task-9-d04-locked-tower-ghost.png`. **Numeric keys were not injected.** |
| A04 | Both new construction tests passed; TowerGarrisonTests 13/13 and the completed-tower combat controls passed. | In SampleScene at timeScale=0, normal garrison on an under-construction tower returned false and left it empty; legacy restore retained the archer; ten direct production `Update` calls spawned 0 projectiles. A completed tower accepted normal garrison and its next update spawned 1 projectile. |
| C01 | All three cargo-restore tests passed; MovementAndCommandTests 16/16, WorkerBehaviorTests 12/12, SaveLoadTests 3/3. The automated non-manual test observes credit, resumed target, and actual harvesting; manual and missing-drop-off controls also passed. | Saved to and read from Task-9-only `task-9-cargo-roundtrip.json`, then ran `RestoreSnapshot`. After correcting only the probe drop-off location to reachable land, loaded non-manual cargo delivered (wood 300→305) and resumed `Tree6`; manual-idle worker kept 7 wood, never moved, and remained `待命(持7)`. Formal `save_slot_1.json` remained absent. |
| B09 | All four AIProductionBudgetTests passed, including shared houses, over-cap survival, zero-budget military, and queued-military controls. | Actual Red AI plus real House/Barracks prefabs, multiple direct `Produce` ticks: living 3 + queued worker 1 = cap 4 on every tick; over ticks 5–7 the military queue grew 1→2→3 while the worker total stayed 4. |

Machine/runtime evidence: `.superpowers/sdd/task-9-runtime-probes.log`, `.superpowers/sdd/task-9-cargo-roundtrip.json`, and the four PNG files above under `.superpowers/sdd/`.

## Unperformed manual GUI checks and probe limitations

- **Not manually reproduced:** UI-origin physical mouse drag, world-origin physical box drag, physical wheel/middle-drag over HUD or pause, and physical Alpha1–Alpha6 key presses. The project does not have Input System enabled; Tuanjie's world/keyboard virtual-device creation returned that `CODELY_INPUT_SYSTEM` is unavailable. These behaviours are therefore reported as automated coverage plus direct production-path runtime probes, not as hand-driven GUI verification.
- Game View UI input did cover real `Produce0`/`Produce1` clicks and a real minimap click. Tower, cargo, and AI checks used runtime C# setup and production APIs/coroutines; no production source was added or edited.
- The first cargo probe put the play-only drop-off at `(0,0)`, where no walkable destination satisfied the existing 1.6 action radius; it correctly did not credit cargo. Moving that probe drop-off to valid land `(0,3)` produced the passing result above. The first AI probe had no Red military factory because ambient AI building had been disabled; adding the real Barracks made the military control discriminating. These were probe-arrangement failures, not product test failures.
- An initial uncontrolled play session was discarded because ambient AI combat interfered with the factory fixture. Controlled sessions disabled ambient AI/combat only in play mode; no play-mode scene state was saved.

## Failed, skipped, and historical failures

- Final full run: **Failed=0, Skipped=0, Inconclusive=0**. No test was skipped by Task 9.
- Task 5's historical full-suite distance-threshold flake (`RightClickTower_DistantArcherWalksToBaseThenGarrisonsAndDamagesEnemy`, `Vector2.Distance(...) < 2.1f` world units, not seconds) is retained in the earlier record. It did not fail in the final 79/79 run and was not modified; this wording correction does not claim the flake is fixed.
- No new product failure was confirmed during Task 9. Probe setup corrections are explicitly retained in `task-9-runtime-probes.log`.

## Final scope audit

`git status --short` still contains the user's extensive pre-existing prefab/art/script changes. The Tasks 1–8 review patch contains only the narrow regression runner, production-panel repair, SampleScene panel/quick-build changes, targeted scripts, and tests; it contains no prefab/art changes. Task 9 added only reports and `.superpowers/sdd` evidence and did not run BuildHUD or any full HUD rebuild. Current HEAD remains `848e6a6e06c88d4d05a2a3cad89181b1dd7c1e03`; no commit, push, branch, or worktree was created.

After the final stop: editor stopped, active scene `Assets/Scenes/SampleScene.scene`, scene dirty=false, project dirty=false, no console errors. Scene SHA-256 stayed `71f10ee02f1a00c3af2fffbc4be7f4a3e848e459d70aec39d2800162145eccb0`; the formal save file still does not exist.

## Next-stage recommendations (review only, not implemented)

1. **Closed by final review repair — B09 mixed-product selection:** the mixed-factory and living-non-worker controls were added, and selection now skips only budget-blocked worker candidates rather than the whole factory. The strengthened queued-military control also has a nonzero worker budget. Final suite: **81/81 passed**. Evidence: `.superpowers/sdd/final-review-fix-report.md`. No further B09 selection work is recommended in this batch.
2. **Medium — lifecycle robustness:** add an under-construction garrison full save/load roundtrip, direct dead/ghost right-click consumption coverage, and a defensive health-bar hide test for the construction early-return. Keep the existing legacy-restore semantics.
3. **Medium — test reliability and D04/C01 hardening:** if the historical garrison approach-distance flake recurs, make the test timing-tolerant without weakening or mislabelling its `Vector2.Distance(...) < 2.1f` world-unit assertion; compare scene quick-build order directly with HUD order; cover an index greater than zero and wait a frame for illegal-ghost cleanup; capture the post-restore resource amount baseline in C01.
4. **Lower — D01 editor repair robustness:** validate HUD array lengths, choose the intended Chinese font fallback explicitly instead of relying on platform fallback, and define null-sprite Image behaviour so a missing icon cannot become a misleading white block.
5. **Low/cleanup:** consolidate duplicated AI factory eligibility only after the behaviour tests above exist. The earlier TestRunnerApi destruction concern remains covered by the existing `DestroyRunnerObjects` completion/exception paths; no new runner work is indicated by the final run.

---

# Final overall-review repair — B09 and documentation corrections

Date: 2026-10-27  
Status: **PASS for this repair wave**. Task 9 remains owned by the main controller; this section does not change its ledger state.

## Important finding, RED and narrow fix

The mixed-product factory risk was reproduced before changing production code. A runtime factory offered a 40-gold Red worker and an 80-gold Red warrior with `maxGatherers=0`. The old selection chose the cheaper worker and then skipped the whole factory: `Produce_ZeroWorkerBudget_MixedFactory_SkipsWorkerAndQueuesMilitary` failed with queue expected 1, actual 0 (`.superpowers/sdd/final-review-fix-red.log`).

`AIController.Produce` now excludes worker candidates while the shared worker budget is zero, then chooses the cheapest affordable remaining product. The per-tick shared budget, independent AI ledger, existing eligibility filters, and no-refund behaviour are unchanged. After the fix, the mixed factory queues only product index 1, charges 80, and creates no worker order.

The queued-military control now uses `maxGatherers=2`: tick two must queue both a second worker and a second warrior (house/barracks queues 2/2, gold 760), so counting military against the worker budget would fail the test. A new living Red archer plus Red house control proves a living non-worker does not consume the worker slot.

## Targeted and final results

- Compilation after test changes and after the fix: 0 errors / 0 warnings.
- `AIProductionBudgetTests`: **6/6 passed** (`.superpowers/sdd/final-review-fix-green-budget.log`).
- Related regressions: ProductionDependencyTests **3/3**, WorkerBehaviorTests **12/12**, MovementAndCommandTests **16/16**, all passed.
- Final complete PlayMode run: **81/81 passed**, 0 failed/skipped/inconclusive, **44.605 s** (`.superpowers/sdd/final-review-fix-full-regression.log`). The two additional leaves are the mixed-factory and living-non-worker tests.
- Final editor state: stopped and unpaused, `SampleScene` active with scene dirty=false and project dirty=false, no last console errors. No GUI or save operation was performed in this wave.

## Documentation correction and deferred Minor items

The historical distant-garrison `2.1` is a `Vector2.Distance` world-unit threshold, not seconds. Earlier prose and the Task 9 recommendation were corrected without changing historical machine logs or the test. The Task 5 flake is not claimed fixed.

Still deferred deliberately: D01 `RepairProductionPanel` array-length validation; A04 full under-construction garrison save/load roundtrip; D01 deterministic font/null-sprite behaviour; defensive construction health-bar behaviour; and D04/C01 test hardening. These are separate robustness/lifecycle items outside this narrow B09 repair, not newly confirmed final-suite failures.

Artifacts: `.superpowers/sdd/final-review-fix-report.md`, `final-review-fix.diff`, refreshed `task-8-diff.patch`, and refreshed `final-first-batch-review.patch`. No commit, push, or worktree was created.

