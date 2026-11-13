# RTS Phase A verification

Date: 2026-10-31 (phase began 2026-10-29)
Project: `D:\Unity\projects\At the Gates`
Commits: none; no push, no worktree, no branch operations. All work in place on the
existing working tree.

Phase A scope: Task 1 unit caps, Task 2 stockpile caps + full-resource gathering
diversion, Task 3 three central meat sheep, Task 4 player repair (+ Repair button),
Task 5 AI key-building repair. Tasks 1-5 were implemented and independently reviewed
during the phase; Task 6 is this integration verification and produced no code or
asset change.

This report supersedes the earlier Tasks 1-2 progress draft that occupied the same
path; the per-task detail remains in `.superpowers/sdd/progress.md` and
`.superpowers/sdd/phase-a/task-?-report.md`.

---

## Step 1 — fresh full PlayMode suite

Run once after the last code change (`Assets/Scripts/AIController.cs`, mtime
2026-10-31 11:34:13; no source file has been edited since). Started through the
menu item `Tools/Regression/Run PlayMode Tests` via
`EditorApplication.ExecuteMenuItem` — no test filter.

Machine result, copied verbatim to
`.superpowers/sdd/phase-a/final-full-regression.log` (14 KB, MD5
`54dc360469c296b91e05ad4b25b98a01`):

```
[Regression] PlayMode run started. Test cases: 141
...
[Regression] PlayMode run finished: Passed. Passed=141, Failed=0, Skipped=0, Inconclusive=0, Duration=89.211s
```

| Metric | Measured |
|---|---|
| Total test cases started | 141 |
| Passed | 141 |
| Failed | 0 |
| Skipped | 0 |
| Inconclusive | 0 |
| Duration | 89.211 s |
| "Passed:" lines in log | 141 |
| "Failed:" lines in log | 0 |

Per-class counts (counted with a script over the log's per-test `Passed:` lines):

| Class | Tests |
|---|---|
| `UnitLimitTests` | 13 |
| `ResourceLimitTests` | 9 |
| `CentralMeatTests` | 3 |
| `RepairTests` | 8 |
| `AIRepairTests` | 11 |

All 19 test classes in the assembly: AIProductionBudgetTests 7, AIRepairTests 11,
CentralMeatTests 3, CommandIntentTests 13, FirstBatchSceneTests 3, InputGateTests 7,
MovementAndCommandTests 16, PauseMenuTests 5, PlacementTests 8,
ProductionDependencyTests 3, RepairTests 8, ResourceLimitTests 9,
RubbleAndGarrisonCommandTests 2, SaveLoadTests 4, SceneLifecycleTests 1,
TowerGarrisonTests 13, UnitLimitTests 13, WipeoutDefeatTests 3,
WorkerBehaviorTests 12 — sum 141.

No number above was inferred from an earlier run; every figure comes from this log.

---

## Step 2 — runtime smoke on `SampleScene`

PlayMode was entered on the saved `SampleScene` and all observations were read live
from the running game objects. Full transcript:
`.superpowers/sdd/phase-a/task-6-smoke.log` (410 lines). Time was frozen
(`Time.timeScale = 0`) for the deterministic probes and restored to 1 for the AI
repair pass, so the AI could not move state between a read and the next read.

### Scene facts measured before the checks

- `Building.All.Count = 249`; non-neutral buildings: **blue 0, red 1** (neutral 248).
- The only non-neutral building is `Castle_Red_AI` (Red, `isCommandCenter = True`,
  under construction at load, `constructionSeconds = 25`).
- **`SampleScene` contains no player-side building and no `ProductionBuilding` at
  load** (`FindObjectsByType<ProductionBuilding>` = 0).
- Live unit roster: `UnitActor.All.Count = 14` (blue 8, red 6).

Because the brief's player-side checks (#1 queued unit, #4 player repair) target a
player factory / friendly building that does not exist in the shipped scene, those
two checks were performed on **runtime instances of the shipped prefabs**
(`Assets/Prefabs/Buildings/Knights/Barracks_Blue.prefab` and `House_Blue.prefab`),
placed in memory at runtime and removed by exiting PlayMode. No prefab, scene or
source file was written. This is a deviation from a literal reading of "against the
live game objects" and is disclosed here rather than substituted silently.

### Smoke results

| # | Check | Measured value | Verdict |
|---|---|---|---|
| 1 | `UnitLimits.CombatCap` | 14 | PASS |
| 1 | Five role caps | Worker 6, Warrior 8, Lancer 5, Archer 5, Monk 4 | PASS |
| 1 | `UnitLimits.Count` vs live units | blue: Worker 3, Warrior 1, Lancer 1, Archer 2, Monk 1 = 8 (live blue 8); red: Worker 3, Warrior 1, Lancer 1, Archer 1, Monk 0 = 6 (live red 6) | PASS |
| 1 | Queued unit counts once | `House_Blue` (runtime) queued 1 Worker (index 0, 40 gold): `Count(Blue,Worker)` 3 → 4 (+1) and live blue actors stayed 8 (+0). Next frame, without `Invalidate`, the per-frame cache also read 4. `CancelLast` refunded +40 gold and the count returned to 3 | PASS |
| 2 | Gold clamp | gold 1990 + `Add(Gold, 5000)` = **2000** | PASS |
| 2 | Wood clamp | wood 1190 + `Add(Wood, 5000)` = **1200** | PASS |
| 2 | Meat clamp | meat 990 + `Add(Meat, 5000)` = **1000** | PASS |
| 2 | `Gatherer.IsKindFull` | True for Gold, Wood and Meat at cap; False for Gold at cap−1 | PASS |
| 3 | Sheep count | exactly 3 `Sheep_Meat_*` nodes | PASS |
| 3 | Sheep fields | all three: `kind = Meat`, `amount/maxAmount = 300/300`, `perTrip = 20`, `regrowSeconds = 120`, non-null `blockingBuilding` (self), `IsRegrowing = False` | PASS |
| 3 | Sheep cells | `Sheep_Meat_1` = (18,3),(19,3); `Sheep_Meat_2` = (19,5),(20,5); `Sheep_Meat_3` = (22,7),(23,7) — two `Building.Cells` each | PASS |
| 3 | Harvest to zero | `Sheep_Meat_1` 300 → 0 in 15 × `Harvest(20)` (the real harvest path); before: `blocksMovement = True`, both cells `Walkable = False`; after: `blocksMovement = False`, both cells `Walkable = True`, `IsBlockedByBuilding = False` | PASS |
| 3 | Regrow entry | `Depleted = True`, **`IsRegrowing = True`**, `regrowSeconds = 120`, `RegrowProgress = 0` (timer frozen at `timeScale = 0`; completion is covered by `CentralMeatTests.CentralSheep_SurviveSaveLoad_...`) | PASS |
| 4 | Repair button exists | `HUD.repairButton` resolves to object `Repair`, parent panel active | PASS |
| 4 | Button active/interactable when usable | damaged friendly `Barracks_Blue` selected: `activeSelf = True`, `interactable = True` | PASS |
| 4 | Repair restores health and spends exactly `RepairCost` | 575/600 (`MissingHealth = 25`, cost 3/3/3) → `onClick.Invoke()` → health 600/600; bank 600/300/200 → 597/297/197 (spent 3/3/3 = the pre-computed `RepairCost`) | PASS |
| 4 | No second spend at full health | second `onClick.Invoke()` left health 600/600 and bank 597/297/197 unchanged | PASS |
| 4 | Button inactive when nothing selected | selection cleared → `activeSelf = False` | PASS |
| 5 | AI repair mechanism | `AIController` (Red) has a live `repairTimer` field (read 1.11, later 1.70/1.20) and `RepairCheckSeconds = 3`; `RepairBuildings()` is ticked from `Update` | PASS |
| 5 | AI repair behaviour (real time) | `Castle_Red_AI` (priority 0, finished) damaged 1200 → 600 (50 %, below the 70 % gate, cost 60/60/60); AI bank topped to 1600/1200/1000; **no manual call** — after ~6 real seconds the castle read **1200/1200, MissingHealth = 0**. A second damage to 40 % was applied after reading, then time was frozen. Bank at read time 775/1200/940 includes other AI spending, so the bank delta is not used as the proof; health restoration is | PASS |
| 6 | No formal save-slot write | `SaveGameService.SavePath = C:\Users\22492\AppData\LocalLow\DefaultCompany\兵临城下\AtTheGates\save_slot_1.json`; `HasSave = False`; the directory exists and contains **0 files** | PASS |

### Post-smoke editor hand-off

- PlayMode stopped (`isPlaying = False`, `isCompiling = False`, `isUpdating = False`).
- `SceneManager.sceneCount = 1`; the only loaded scene is
  `Assets/Scenes/SampleScene.scene`, `isDirty = False`; it is the active scene.
- Project `dirty = False`; console `unreadCount = 0`, `lastErrors = []`.
- The scene was never saved; `EditorSceneManager.SaveOpenScenes()` was never called.
- The runtime-only prefab instances and the damage were discarded by leaving PlayMode.

---

## Step 3 — scope audit

### Files under `Assets/` modified or created since Phase A began

Command:
`find Assets -newermt '2026-10-29 20:00' -type f -not -path '*/Library/*' -printf '...' | sort`
(31 files).

| mtime | File | Attribution |
|---|---|---|
| 2026-10-29 20:25:25 | `Assets/Scripts/MapGrid.cs` | **Pre-Phase-A** — predates the Phase A plan and first brief (both 22:05); belongs to the command-intent batch verified at 21:17 |
| 2026-10-29 21:11:14 | `Assets/Tests/PlayMode/CommandIntentTests.cs` | **Pre-Phase-A** — same batch (report `2026-10-28-rts-command-intent-verification.md`, 21:17) |
| 2026-10-29 21:12:34 | `Assets/Scripts/CombatAI.cs` | **Pre-Phase-A** — same batch |
| 2026-10-29 22:10:52 | `Assets/Tests/PlayMode/UnitLimitTests.cs.meta` | Task 1 (new test) |
| 2026-10-29 22:12:28 | `Assets/Scripts/UnitLimits.cs.meta` | Task 1 (new file) |
| 2026-10-29 22:39:55 | `Assets/Tests/PlayMode/UnitLimitTests.cs` | Task 1 (13 tests) |
| 2026-10-29 22:41:48 | `Assets/Scripts/UnitLimits.cs` | Task 1 (caps) |
| 2026-10-29 22:42:17 | `Assets/Scripts/ProductionBuilding.cs` | Task 1 (`CanQueue`/`LastRefusalReason`) |
| 2026-10-29 22:53:31 | `Assets/Tests/PlayMode/AIProductionBudgetTests.cs` | Task 1 follow-up ("AI wood assertion" Minor) |
| 2026-10-29 23:05:54 | `Assets/Tests/PlayMode/ResourceLimitTests.cs.meta` | Task 2 (new test) |
| 2026-10-29 23:06:12 | `Assets/Scripts/ResourceLimits.cs` | Task 2 (caps service) |
| 2026-10-29 23:06:25 | `Assets/Scripts/Economy.cs` | Task 2 (`ClampAdd`/`ClampStockpiles`) |
| 2026-10-29 23:07:08 | `Assets/Scripts/ResourceLimits.cs.meta` | Task 2 |
| 2026-10-29 23:08:16 | `Assets/Tests/PlayMode/ResourceLimitTests.cs` | Task 2 (9 tests) |
| 2026-10-29 23:09:50 | `Assets/Scripts/Gatherer.cs` | Task 2 (full-kind diversion) |
| 2026-10-30 18:52:06 | `Assets/Tests/PlayMode/CentralMeatTests.cs.meta` | Task 3 (new test) |
| 2026-10-30 18:54:57 | `Assets/Editor/AddCentralMeat.cs.meta` | Task 3 (new tool) |
| 2026-10-30 20:19:51 | `Assets/Editor/AddCentralMeat.cs` | Task 3 (incremental scene tool) |
| 2026-10-31 09:05:28 | `Assets/Tests/PlayMode/CentralMeatTests.cs` | Task 3 (3 tests) |
| 2026-10-31 09:07:55 | `Assets/Prefabs/Resources/HappySheep_Idle.prefab` | Task 3 fix (footprint 2×1, regrow 120, blockingBuilding self-ref) |
| 2026-10-31 09:59:31 | `Assets/Tests/PlayMode/RepairTests.cs` | Task 4 Phase 1 (8 tests) |
| 2026-10-31 09:59:43 | `Assets/Tests/PlayMode/RepairTests.cs.meta` | Task 4 Phase 1 |
| 2026-10-31 10:07:29 | `Assets/Scripts/Building.cs` | Task 4 Phase 1 (`MissingHealth`/`RepairCost`/`CanRepair`) |
| 2026-10-31 10:07:31 | `Assets/Scripts/SelectionManager.cs` | Task 4 Phase 1 (`RepairSelected`) |
| 2026-10-31 10:08:06 | `Assets/Scripts/HUD.cs` | Task 4 Phase 1 (repair wiring/panel line) |
| 2026-10-31 11:01:40 | `Assets/Editor/BuildRepairButton.cs` | Task 4 Phase 2 (incremental button tool) |
| 2026-10-31 11:01:52 | `Assets/Editor/BuildRepairButton.cs.meta` | Task 4 Phase 2 |
| 2026-10-31 11:02:27 | `Assets/Scenes/SampleScene.scene` | Task 4 Phase 2 (the one authorised scene write) |
| 2026-10-31 11:18:45 | `Assets/Tests/PlayMode/AIRepairTests.cs.meta` | Task 5 (new test) |
| 2026-10-31 11:30:39 | `Assets/Tests/PlayMode/AIRepairTests.cs` | Task 5 (11 tests) |
| 2026-10-31 11:34:13 | `Assets/Scripts/AIController.cs` | Task 5 (`repairTimer`/`RepairBuildings`/`RepairPriority`) |

Everything else modified since Phase A began is accounted for. The three files at
20:25-21:12 predate the Phase A plan (22:05) and the first task brief (22:05), and
belong to the command-intent batch whose verification report is stamped 21:17. No
unexplained file exists. `AIProductionBudgetTests.cs` is the only Phase A file
outside the brief's projected list and is explained by Task 1's accepted
"AI wood assertion" follow-up.

No file was deleted during Phase A (the list above contains only created/modified
paths).

### Absence checks (grep over the 11 Phase A source files)

Set: `UnitLimits.cs`, `ResourceLimits.cs`, `ProductionBuilding.cs`, `HUD.cs`,
`Economy.cs`, `AIController.cs`, `Gatherer.cs`, `Building.cs`, `SelectionManager.cs`,
`AddCentralMeat.cs`, `BuildRepairButton.cs`.

| Concept | Command | Result |
|---|---|---|
| House population | `grep -rniE "population\|人口\|popcap\|housing cap" $FILES` | No matches (exit 1) — absent |
| Worker repair | `grep -rniE "worker.{0,20}repair\|repair.{0,20}worker\|工人.{0,6}维修\|维修.{0,6}工人" $FILES` | No matches (exit 1) — absent; repair is building-only, as specified |
| Technology / tech tree | `grep -rniE "tech ?tree\|technology\|科技\|研发" $FILES` | One hit, `Building.cs:418` doc comment "the rest of the tech tree"; `git diff -U0 -- Building.cs \| grep "tech tree"` is empty, so the line is unchanged from HEAD (pre-existing) — no tech system added |
| Meat upkeep | `grep -rniE "upkeep\|maintenance\|口粮\|肉.{0,4}消耗\|消耗.{0,4}肉" $FILES` | No matches (exit 1) — absent |
| Unit stat/speed/range changes | `grep -rniE "\b(speed\|attackRange\|attackDamage\|maxHealth\|moveSpeed)\s*[=+*]" $FILES` | Only `HUD.cs:579` (read-only display), `AIController.cs:343` (the 70 % repair gate), `Gatherer.cs:173` `attackDamage = 0` (pre-existing worker rule; not in `git diff`), and pre-existing `Building.maxHealth` defaults. `UnitActor.cs`/`Unit.cs` are not in the modified list. No unit stat changed |
| Wave timing changes | `grep -rniE "wave\|attackEvery\|firstAttack\|waveEvery" $FILES`; then `git diff -U0 -- AIController.cs \| grep -E "^[-+].*(firstAttack\|attackEvery\|wave\|attackTimer)"` | Only `repairTimer` and SaveState timer plumbing matched the diff; `firstAttack = 45f`, `attackEvery = 38f` are unchanged from HEAD. No wave timing change |
| Starting-resource changes | `grep -nE "public int (gold\|wood\|meat) =" Economy.cs`; `git diff -U0 -- Economy.cs \| grep -E "^[-+].*public int (gold\|wood\|meat)"` | `500/300/200` (player) and `500/300/200` (AI `startGold/startWood/startMeat`); the diff has **no** added/removed line for them — unchanged |
| Full HUD rebuild | `grep -niE "RebuildHUD\|BuildHUD\|重建\|CreateHUD" BuildRepairButton.cs HUD.cs` | Only comments; `BuildRepairButton.cs` explicitly documents that it must not run `BuildHUD`. Phase A's HUD delta is insert-only (45 insertions / 0 deletions measured against the pre-task byte baseline in Task 4 Phase 1); Phase 2 added three scene GameObjects and did not rebuild the canvas |
| Save-version bump | `grep -n "Version" SaveGameService.cs`; `git status --porcelain SaveGameService.cs SaveGameData.cs` | `public const int Version = 1;` still 1; both files are untracked from the earlier save-system batch and do **not** appear in the `find -newermt` list, i.e. untouched during Phase A. `AIController.RepairCheckSeconds` is deliberately not part of the save schema |

Supporting fact for the last row: `Task 5`'s review specifically verified that
`SaveState` was not extended for the repair timer.

---

## Accepted Minors carried from the five task reviews

Count: **36** (Task 2: 5; Task 3: 8 from the review + 2 non-blocking notes from the
re-review; Task 4 Phase 1: 7; Task 4b: 7; Task 5: 7). All were accepted as
non-blocking; none changes a measured result above. Summary by theme:

**Task 2 (5) — gathering/cap edges**
The floating delivery text shows the pre-clamp amount (a full bank can display
"+20 gold" while 0 is banked); the manual delivery branch lacks the
`!node.Depleted` guard its auto twin has (safe via the per-frame re-guard); a local
team variable duplicates the `UnitTeam` property; a pointless `RefCap` helper and
redundant asserts in `ResourceLimitTests`; `WaitUntil` swallows timeouts so a
failure surfaces as the next assertion's message.

**Task 3 (8 + 2) — scene tooling and sheep**
`AddCentralMeat`'s rollback destroys created sheep but does not undo field
mutations on pre-existing ones before validation; duplicate `Sheep_Meat_*` objects
are silently ignored (First-by-name, no count check); the relocation path does not
re-apply `amount`/`maxAmount`/`perTrip`; the tool's runtime guard samples only the
first blue and first red unit while the test checks all 14; 21 redundant identity
overrides per sheep; non-`Building` SpriteRenderers are not scanned for occupancy;
a failure message compares units that may be enemies; the registration loop sits
outside its try/finally. Re-review notes: the scene redundantly duplicates the
three prefab-pinned values as instance overrides (harmless; removing them would
cost another 400-line-churn scene save), and one verbatim RED block in the report
carries a run-summary line that is in Editor.log but not in the dedicated RED log.

**Task 4 Phase 1 (7) — repair command/tests**
The full-health second-call assertion is not discriminating (0-cost repair would
pass even without the `MissingHealth > 0` guard) and no test asserts
`CanRepair == false` at full health; `HUD` recomputes `RepairCost` three times per
frame; the eligibility predicate is duplicated between body text and button; the
listener is instance-idempotent rather than following the file's
`RemoveAllListeners()` convention; the player-team ternary appears four times; the
report said 42 added HUD lines where the measured value is 45; filtered runs print
"Test cases: 0" because of a pre-existing class-filter display bug.

**Task 4b (7) — scene tool**
`backupCopied` is dead; the save gate uses the scene-wide `isDirty`; `md5After` is
logged but never asserted different from `md5Before`; a failure between the
in-memory mutation and the save leaves a dirty, partly built button; the tool log
is overwritten each run; the sprite/child construction duplicates
`BuildAttackButton`; no test asserts the button exists or is wired, and a future
HUD rebuild would silently null `hud.repairButton`.

**Task 5 (7) — AI repair**
No isolated gold reserve-floor test (only wood/meat are isolated); the priority
test does not isolate priority from most-damaged; no equal-priority/equal-missing
tie-break test; the 3 s `Update` wiring is verified by inspection only (the tests
call `RepairBuildings()` directly) — note that this Task 6 smoke did exercise the
real tick end-to-end; a test comment says "exactly one repair is payable" when both
are payable individually (the ledger proves one per tick); the neutral-team check
after the team check is unreachable; selecting the top candidate before the reserve
check lets an unaffordable command centre block an affordable house indefinitely.

---

## Explicit limitations and disclosures

1. **Physical GUI acceptance was NOT performed.** No real mouse drag, mouse wheel,
   middle button or Alpha1-6 keyboard input was injected. The Repair button was
   exercised through its real `Button.onClick.Invoke()` and through the real
   `HUD`/`SelectionManager` update path, not through a physical click.
2. **The Repair button's visual appearance was NOT verified.** Static PlayMode
   screenshots are blocked in this project. The only evidence is measured state and
   geometry from Task 4b: a `Repair` object with an 84×34 `RectTransform` at
   anchored position (14,10), `Button_Blue_9Slides` sliced image, 维修 label at
   16 pt, `activeSelf`/`interactable` as recorded above. Its rendered look is
   unverified.
3. **Player-side smoke checks used runtime prefab instances.** `SampleScene` has no
   player building and no `ProductionBuilding` at load, so the queue-count and
   player-repair checks were measured on `Barracks_Blue`/`House_Blue` instantiated
   at runtime. They exercise the real components, production path and HUD, but the
   objects are not part of the saved scene. No asset was written.
4. **AI repair bank delta is not isolated.** The castle was restored to full health
   by the automatic 3 s pass, but the AI bank moved for other reasons too during the
   same interval, so only the health restoration is used as proof.
5. **Sheep regrow completion was not observed** (time was frozen at 0 to keep the
   probe deterministic); the measured fact is that the node enters `IsRegrowing`
   with a 120 s timer and releases its cells. Completion/re-blocking is covered by
   `CentralMeatTests`.
6. The suite run is a fresh run that postdates the last source edit; no earlier
   number was reused.

---

## Hand-off state

| Item | Value |
|---|---|
| PlayMode | stopped (not playing, not paused, not compiling, not updating) |
| Scenes loaded | 1 — `Assets/Scenes/SampleScene.scene` (also the active scene) |
| Scene dirty | false |
| Project dirty | false |
| Console | unread 0, lastErrors [] |
| `Assets/Scenes/SampleScene.scene` MD5 | **`90adb20841342388b0430fd697459c8e`** (unchanged before/after the smoke) |
| Formal save slot | `C:\Users\22492\AppData\LocalLow\DefaultCompany\兵临城下\AtTheGates\` exists, **0 files**; `SaveGameService.HasSave = false` |
| Git operations | none |
| Files written by Task 6 | this report and `.superpowers/sdd/phase-a/task-6-smoke.log`, `.superpowers/sdd/phase-a/final-full-regression.log` only |

## Evidence index

- `.superpowers/sdd/phase-a/final-full-regression.log` — fresh 141/141 machine log
- `.superpowers/sdd/phase-a/task-6-smoke.log` — full runtime smoke transcript
- `.superpowers/sdd/progress.md` — task ledger incl. every accepted Minor
- `.superpowers/sdd/phase-a/task-1..5-report.md` and `task-?-review.md` — per-task detail
- `Logs/RegressionTests.txt` — the live log the suite wrote (copied to the evidence file)
