# RTS Phase B verification

Date: 2026-10-31
Project: `D:\Unity\projects\At the Gates` (Tuanjie 2022.3.62t5, URP 14.1.0, UTF 1.1.33, NUnit)
Mode: evidence only. No `.cs`, prefab, scene or save-slot write was performed by this task. No git
operation beyond read-only queries.

Phase B (population, worker repair, technology + save v2, meat upkeep, UI/balance, AI technology) is
complete; every task passed an independent review, Task 3 and Task 5 after a fix plus re-review.
Baseline before this task: 213/213. Phase A was delivered at 142/142.

---

## Step 1 — fresh full PlayMode suite

`RegressionTestMenu.RunPlayModeTests()` was run once after the last code change and the log was copied
immediately (before any focused run could overwrite it).

- Command: `EditorApplication`-dispatched `RegressionTestMenu.RunPlayModeTests()` (the documented
  `EditorApplication.delayCall` wrapper did not start a run, so the menu entry point was called
  directly from `execute_csharp_script`).
- Last source change under `Assets/`: `Assets/Scripts/AIController.cs` at **17:33**; the run started at
  **17:49** — the suite covers the final code.
- Machine result: `PlayMode run finished: Passed. Passed=213, Failed=0, Skipped=0, Inconclusive=0,
  Duration=125.728s`
- Archive: `.superpowers/sdd/phase-b/final-full-regression.log` (21 KB, archived 17:49).

### Per-class counts (26 classes, all passed)

| Class | Tests | Batch |
|---|---:|---|
| **PopulationTests** | **11** | **Phase B Task 1** |
| **WorkerRepairTests** | **11** | **Phase B Task 2** |
| **TechnologyTests** | **18** | **Phase B Task 3** |
| **UpkeepTests** | **15** | **Phase B Task 4** |
| **BalanceValueTests** | **5** | **Phase B Task 5** |
| **AITechnologyTests** | **9** | **Phase B Task 6** |
| RepairButtonSceneTests | 3 | Phase A Task 7 file; +2 layout tests in Phase B Task 5 |
| UnitLimitTests | 13 | Phase A |
| ResourceLimitTests | 9 | Phase A |
| CentralMeatTests | 3 | Phase A |
| RepairTests | 8 | Phase A |
| AIRepairTests | 11 | Phase A |
| AIProductionBudgetTests | 7 | pre-Phase-A batch; fixture edited in Phase B Task 1 |
| MovementAndCommandTests | 16 | pre-Phase-A |
| CommandIntentTests | 13 | pre-Phase-A |
| TowerGarrisonTests | 13 | pre-Phase-A |
| WorkerBehaviorTests | 12 | pre-Phase-A |
| PlacementTests | 8 | pre-Phase-A |
| InputGateTests | 7 | pre-Phase-A |
| PauseMenuTests | 5 | pre-Phase-A |
| SaveLoadTests | 4 | pre-Phase-A |
| WipeoutDefeatTests | 3 | pre-Phase-A |
| ProductionDependencyTests | 3 | pre-Phase-A |
| FirstBatchSceneTests | 3 | pre-Phase-A |
| RubbleAndGarrisonCommandTests | 2 | pre-Phase-A |
| SceneLifecycleTests | 1 | pre-Phase-A |

Sum check: 11 + 11 + 18 + 15 + 5 + 9 = **69** new Phase B suites, plus 2 new tests added to the
pre-existing `RepairButtonSceneTests` (`SelectionPanel_HasHiddenWiredTechStrip`,
`SelectionPanel_DoesNotOverlapMinimap_AndBothFitCanvas`) = **71**.

**Phase A 142 → Phase B 213 (+71).**

---

## Step 2 — runtime smoke on `SampleScene`

Method and disclosures:

- PlayMode was entered on `SampleScene`, and stopped through the editor's stop command at the end (no
  save, no `SaveOpenScenes()`); the editor state was read back afterwards.
- The scene's own AI (`AIController`, Red) was disabled at the first probe so it could not spend or
  build during the measurements; its `firstAttack`/`maxGatherers` values were read before disabling.
- The scene's six `Gatherer` components and all `Healer` components were disabled so incidental
  deliveries could not move the bank while a check was being measured; individual workers were
  re-enabled where a check needed them.
- **The scene has no player building at match start** (249 buildings: 0 Blue, 1 Red under
  construction, 248 Neutral), so the house, barracks and command centre used by checks 1 and 3 were
  runtime instances of the shipped prefabs (`House_Blue`, `Barracks_Blue`, `Castle_Blue`), in memory
  only. They were discarded on exiting PlayMode; the scene file was not touched.
- Raw evidence: `.superpowers/sdd/phase-b/task-7-smoke.log`.
- Repair was ordered through `Gatherer.AssignRepair`, the public entry point the selection manager
  uses after its eligibility check; the right-click routing itself was not exercised.

### Smoke results

| # | Check | Measured value | Verdict |
|---|---|---|---|
| 1a | Base capacity, both sides | Blue **12**, Red **12** | pass |
| 1b | Live `Used` vs starting forces | Blue **10** (3 worker, 2 archer, 1 monk, 1 warrior, 1 lancer), Red **8** (3 worker, 1 archer, 1 warrior, 1 lancer) — matches the design's 10/8 population points over 8 Blue / 6 Red units | pass |
| 1c | Completed house raises capacity | `House_Blue(Clone)` at (-34,-4), `IsUnderConstruction=False`; Blue capacity **12 → 18**, Red unchanged at 12 | pass |
| 1d | Production at capacity | Filled to 18/18; `TryQueue(Warrior)` = **false**, reason **`人口已满`**, gold 1000→1000, wood 300→300 (nothing spent) | pass |
| 2a | Worker repair rate while adjacent | Health **173 → 213** over **2.005 s**, both samples in `维修中` = **19.96 HP/s** (nominal 20) | pass |
| 2b | Repair stops at full | **260 → 300/300**; `RepairTarget` cleared; worker returned to `去采集` | pass |
| 3a | Tech strip with a command centre selected | Strip `activeSelf=True`, **4** children, **3** buttons, selection `Castle_Blue(Clone)` | pass |
| 3b | Button costs | `250金100木`, `250金100木`, `200金`; identical to `Technology.CostOf` (250/100, 250/100, 200/0) | pass |
| 3c | Research starts and pays | `BeginResearch(Forge)` = true, paid **250 gold + 100 wood** (1000→750, 300→200), line set to **45 s**; completed via `Technology.Complete(Team.Blue)` (waiting 45 s in a smoke is impractical) | pass |
| 3d | Effective stats reflect tech, prefab unchanged | Warrior `EffectiveAttackDamage` **16 → 18** (Forge) and `EffectiveMaxHealth` **110 → 127** (Armour, health raised to 127); instance `attackDamage` 16 / `maxHealth` 110 unchanged; **loaded prefab asset still 16 / 110 before and after** | pass |
| 4a | Upkeep tick | `Upkeep.UnitCount(Blue)` = **5**, `Upkeep.Tick(Blue, 10f)` took meat **90 → 85** (exactly the living-combatant count) | pass |
| 4b | Starvation blocks production | Bank emptied, `Tick` → `IsStarving=True`; `TryQueue(Warrior)` = false, reason **`食物不足`**, gold/wood/meat unchanged | pass |
| 4c | Starvation stops monk healing | `Healer.Patient` = **null**, status `待命`, wounded unit 55 → 55 over 2.5 s | pass |
| 4d | Feeding recovers | `NoteMeatReceived` → `IsStarving=False`; monk acquired `Warrior_Blue` (`治疗中`, 55 → 85 HP); `TryQueue` succeeded (gold 1000 → 920) | pass |
| 5a | House cost from loaded assets | `House_Blue` **75** gold, `House_Red` **75** gold (wood/meat 0) | pass |
| 5b | Scene `EnemyAI` balance | `firstAttack=120`, `maxGatherers=5` (team Red) | pass |
| 5c | Player start gold | **600** read live at match start; scene's `Economy` component serializes 600/300/200 | pass |
| 5d | Pawn flee values from loaded assets | `Pawn_Blue` and `Pawn_Red`: `fleeSafeRadius=6`, `fleeResumeSeconds=3`; the live scene pawns read 6/3 as well | pass |
| 6 | No formal save-slot write | `%LOCALAPPDATA%Low\DefaultCompany\兵临城下\AtTheGates` exists, **0 files** after the smoke | pass |

Additional observations recorded in the log:

- The HUD status line read `人口 21/18` while the probe had spawned units above capacity — the
  population readout is live (over-cap survivors are displayed, not deleted).
- The combatant count for upkeep was 5 (scene warrior + 2 archers + 1 lancer + one probe warrior), and
  the charged amount matched it exactly.
- Match-start meat read 196 at ~10 s into PlayMode. The scene serializes meat 200; a single upkeep tick
  at t=10 s charging Blue's 4 starting combatants explains the 4-point drop exactly.

### Probe artefacts (kept in the raw log, disclosed rather than deleted)

1. The first starving-production attempt reported `没有这个产品` because the probe had picked a
   `House_Blue(Clone)` (product list `Pawn_Blue`) instead of the `Barracks_Blue(Clone)`. The factories
   were then enumerated and the check repeated against the barracks, which produced the intended
   `食物不足`.
2. One stop-at-full attempt targeted a probe-built house whose second footprint cell fell outside the
   walkable region (region -1 vs the worker's region 0), so no path existed and the worker stayed in
   `去维修`. The measurement was repeated on the reachable probe house at (-33.5,-3.5) and passed.

Neither is a product defect. All other rows were measured once and passed.

### Post-smoke editor hand-off

- PlayMode stopped, `isCompiling=false`, `isUpdating=false`.
- `sceneCount=1`, scene 0 = `Assets/Scenes/SampleScene.scene`.
- Scene `dirty=false`; project `dirty=false`; console unread 0, `lastErrors=[]`.
- `Assets/Scenes/SampleScene.scene` MD5 **e69c0b2508e8731ec7394251b8bdf431** (unchanged before and
  after the smoke).
- Formal save slot: 0 files.

---

## Step 3 — scope audit

### Files under `Assets/` modified or created since Phase B began (2026-10-31 13:30)

Command: `find Assets -type f -newermt "2026-10-31 13:30" -printf "%TY-%Tm-%Td %TH:%TM  %p\n" | sort`

| mtime | File | Accounted for by |
|---|---|---|
| 13:39 | `Scripts/Population.cs.meta` | Task 1 (new) |
| 13:39 | `Tests/PlayMode/PopulationTests.cs` (+`.meta`) | Task 1 (new) |
| 13:43 | `Scripts/Population.cs` | Task 1 |
| 13:51 | `Tests/PlayMode/AIProductionBudgetTests.cs` | Task 1 approved fixture edit (two completed Red houses so the fixture is not on the 18-point boundary) |
| 14:05 | `Tests/PlayMode/WorkerRepairTests.cs` (+`.meta`) | Task 2 (new) |
| 14:08 | `Scripts/Building.cs` | Task 2 (`RepairByWorker`, `Population.Invalidate` on finish/wreck) |
| 14:11 | `Scripts/SelectionManager.cs` | Task 2 (click-to-repair routing) |
| 14:33 | `Tests/PlayMode/TechnologyTests.cs.meta` | Task 3 (new) |
| 14:35 | `Scripts/CombatAI.cs` | Task 3 (effective damage) |
| 14:36 | `Scripts/SaveGameService.cs` | Task 3 (Version 2, v1 readable, tech restore) |
| 14:36 | `Scripts/Technology.cs.meta` | Task 3 (new) |
| 14:58 | `Scripts/Technology.cs` | Task 3 |
| 15:00 | `Tests/PlayMode/TechnologyTests.cs` | Task 3 |
| 15:04 | `Scripts/TowerGarrison.cs` | Task 3 (effective damage at fire time) |
| 15:04 | `Scripts/Unit.cs` | Task 3 (`EffectiveAttackDamage`, `EffectiveMaxHealth`) |
| 15:22 | `Scripts/Upkeep.cs` (+`.meta`) | Task 4 (new) |
| 15:22 | `Tests/PlayMode/UpkeepTests.cs.meta` | Task 4 (new) |
| 15:23 | `Scripts/Healer.cs` | Task 4 (starvation gate) |
| 15:24 | `Scripts/GameState.cs` | Task 3/4 (save fields, per-frame upkeep tick) |
| 15:25 | `Tests/PlayMode/UpkeepTests.cs` | Task 4 |
| 15:27 | `Scripts/ProductionBuilding.cs` | Task 4 (starvation refusal, last in the chain) |
| 16:25 | `Prefabs/Buildings/Knights/House_Blue.prefab` | Task 5 (cost 50 → 75) |
| 16:25 | `Prefabs/Buildings/Knights/House_Red.prefab` | Task 5 (cost 50 → 75) |
| 16:25 | `Scripts/Economy.cs` | Task 5 (starting gold 600) |
| 16:25 | `Scripts/Gatherer.cs` | Task 5 (flee 6 s / resume 3 s defaults) |
| 16:27 | `Editor/BuildTechPanel.cs` (+`.meta`) | Task 5 (new editor tool) |
| 16:32 | `Tests/PlayMode/BalanceValueTests.cs.meta` | Task 5 (new) |
| 16:34 | `Scripts/HUD.cs` | Task 5 (tech strip wiring, effective stats, population + `食物不足` status) |
| 16:40 | `Prefabs/Units/Pawn_Blue.prefab` | Task 5 (fleeSafeRadius 6, fleeResumeSeconds 3) |
| 16:40 | `Prefabs/Units/Pawn_Red.prefab` | Task 5 (same, AI side) |
| 16:41 | `Tests/PlayMode/BalanceValueTests.cs` | Task 5 |
| 16:54 | `Scenes/SampleScene.scene` | Task 5 (tech strip, minimap move, two AI values) |
| 16:55 | `Tests/PlayMode/RepairButtonSceneTests.cs` | Task 5 (two added scene tests) |
| 17:19 | `Tests/PlayMode/AITechnologyTests.cs.meta` | Task 6 (new) |
| 17:27 | `Tests/PlayMode/AITechnologyTests.cs` | Task 6 |
| 17:33 | `Scripts/AIController.cs` | Task 6 (research, population-aware product filter, IsStarving readers) |

40 entries, every one accounted for. Against the brief's expected set:

- `SaveGameData.cs` **was not touched** (mtime 2026-10-19 13:45) — the v2 fields live on
  `GameState.SaveState`, so the brief's "`SaveGameData.cs` if touched" is satisfied by not touching it.
- Two files not named in the brief are accounted for: `AIProductionBudgetTests.cs` (approved Task 1
  fixture edit) and `RepairButtonSceneTests.cs` (Task 5's two layout/tech-strip scene tests).
- No other file under `Assets/` changed after 13:30 (maximum mtime under `Assets/` is 17:33).

### Absence checks

| Must be absent | Check | Result |
|---|---|---|
| Phase C AI work (minimum-building recovery, composition ratios, garrison) | `grep -in "garrison\|composition\|ratio\|minimum" Assets/Scripts/AIController.cs` | Only a doc-comment about `Research` (line 75) and the Phase A "Minimum empty ring around a new building" spacing comment (line 313). No composition table, no AI garrison, no recovery pass. |
| New unit / building / art | `find Assets -type f -newermt "2026-10-31 13:30" \( -iname "*.png" -o -iname "*.psd" -o -iname "*.jpg" -o -iname "*.anim" -o -iname "*.controller" -o -iname "*.asset" \)` and `find Assets -type f -name "*.prefab" -newermt "2026-10-31 00:00"` | No image, animation or asset files. Prefabs modified today: the four Phase B prefabs plus `HappySheep_Idle.prefab` (09:07, Phase A). **No new prefab file was created.** |
| A full HUD rebuild | `git diff --no-index --numstat .superpowers/sdd/phase-b/task-5-scene-before-original.scene Assets/Scenes/SampleScene.scene` and counting `m_Name` lines | 1386 inserted / 404 removed; **0 removed `m_Name` lines**, 24 added names (`TechStrip`, `Tech_Forge`/`Tech_Armour`/`Tech_Gather` each with `Label` + `Cost`, `Progress`). The scene is byte-identical to the Task 5 "final good" copy (`MD5 e69c0b25...`). The loaded scene holds exactly one `HUD` component, one `SelectionPanel` and one `TechStrip` (live probe, and one `techStrip:`/`selectionPanel:`/`buildPrefabs:` serialized field each). |
| A save-format change beyond `techMask` + research progress | Inspect `Assets/Scripts/SaveGameData.cs` and `GameState.SaveState` | `SaveGameData.cs` untouched since 2026-10-19. `GameState.SaveState` adds exactly `blueTechMask`, `redTechMask`, `blueResearchTech`, `blueResearchRemaining`, `redResearchTech`, `redResearchRemaining`. `SaveGameService.Version = 2`, `OldestReadableVersion = 1`. |

### Git entry count and deletions

- `git ls-files | wc -l` → **2647** tracked entries.
- `git ls-files --deleted | wc -l` → **0** — no tracked file was deleted.
- `git status --porcelain | wc -l` → 228 dirty entries, all pre-existing uncommitted work in this
  in-place project. No commit, push, branch, checkout, restore, stash or clean was run by this task.

---

## Save-compatibility statement

- **Version 2 writes.** `SaveGameService.Version = 2`; every new save is written with version 2.
- **Version 1 reads.** `SaveGameService.OldestReadableVersion = 1`, and `Validate` rejects anything
  outside `[1, 2]` with a message naming that range. A version-1 payload has none of the technology
  fields, so `JsonUtility` leaves `blueTechMask`/`redTechMask` and the four research fields at 0;
  `GameState.RestoreTechnology` applies them, which yields **masks 0 (no technology) and no active
  research**, and clears any stale line left in memory.
- **Real v1 payload.** `.superpowers/sdd/phase-b/task-3-v1-payload.json` is a payload captured from a
  real save and rewritten to the version-1 shape (version 1, no technology fields, a castle and a
  "Legacy warrior", bank 640/210). It loads with masks 0 even after seeding a stale mask of 7 and a
  stale 30 s Forge line. The final suite's
  `TechnologyTests.VersionOnePayload_LoadsWithAnEmptyMask_AndNoDataLoss` passed.
- **Research in progress is inside version 2** (Task 3 fix, controller-approved deviation from the
  design's "only the mask" wording so a save cannot burn 250 gold + 100 wood).
  `TechnologyTests.ResearchInProgress_SurvivesSaveAndLoad_WithoutPayingTwice` passed; the mask and the
  line are restored **before** units, so an Armour-buffed unit keeps its head-room.

---

## Accepted Minors carried from the six task reviews

**45 Minor findings**, all accepted and none blocking (0 Critical, 0 Important in the six final
verdicts; the three Task 3 Importants and the Task 5 Important were fixed and re-reviewed):

| Task | Minors | Main themes |
|---|---:|---|
| 1 Population | 9 | Non-discriminating ghost sub-case; no refusal-precedence test (cap reason vs `人口已满`); house-completion/death invalidation unwired (harmless, one frame); `Used` hand-enumerates the six roles; AI product filter ignored population (folded into Task 6); duplicated `CanFit`/`Invalidate` calls; missing assertion messages. |
| 2 Worker repair | 7 | One-frame `Repairing` with a null target after an invalidated arrival; loose 1 s rate band; 33 % margin on the two-worker stacking assertion; unreachable mirror branches; no carrying → deliver → repair test; damaged friendly tower ungarrisons before repairing (brief-mandated order); no repair animation. |
| 3 Technology | 2 | Load-no-double-charge assertions partly masked by the bank being rewritten from the snapshot; one fix-report sentence misstates the `<= 0` resume branch. |
| 4 Upkeep | 12 | `UnitCount` duplicates `UnitLimits.Counts`; a readerless `AIController.IsStarving` (closed by Task 6); unguarded `NoteMeatReceived` on negative deltas; a starving side that loses its last combatant stays refused up to one period; no population-vs-starvation precedence test; literal Blue/Red ticks in `GameState`; `Gatherer.Credit` fallback credits the player for a non-player team (pre-existing); missing-bank diagnostic; one-charge-per-tick overshoot; RED log sparser than the report claims. |
| 5 UI + balance | 10 | Stale `task-5-scene.diff`; the minimap move is not encoded in `BuildTechPanel`; an `OpenScene(..., Additive)` fallback left literal; duplicate-strip check runs after saving; no test for the population/`食物不足` status line or the effective-stat display; strip cases (already researched / unaffordable / under construction) uncovered; no font assertion; disabled tech buttons only recolour the cost label; status line after game over; `PanelSpace` assumes the panel pivot. |
| 6 AI technology | 5 | `Research()` discards the refusal reason and logs no start line; two idioms for `IsStarving`; one test omits the file's `Technology.Reset()` convention; the ninth test has no RED record (added after GREEN); a `.meta` GUID quoted in base64 form in the report. |

Minor 5 of Task 1 (population-aware AI product filter) and Minor 2 of Task 4 (readerless `IsStarving`)
were both closed by Task 6 rather than carried forward.

---

## Documented layout deviations and the gold-mine finding

Measured live from the saved scene (read-only probe; rects in canvas space, canvas 1920×1080):

| Item | Before Phase B | Now | Deviation |
|---|---|---|---|
| `SelectionPanel` | 320 × **190**, anchored (-16,-16), pivot (1,1) | 320 × **266**, anchored (-16,-16), pivot (1,1) | +76 px taller; because the pivot is the top-right corner, the panel grows **downward** by 76 px |
| Command row (`Repair`/`Demolish`/`Attack`) | anchored y = 10 from the panel's bottom edge | anchored y = 10 from the panel's bottom edge; world Y **808..842** | The row moved **down 76 px** with the panel's bottom edge (same anchored offsets, same sizes 84/104/96 × 34) |
| `MinimapPanel` | anchored (-16,**-218**), 340 × 204 | anchored (-16,**-306**), 340 × 204, world (1564..1904 × **570..774**) | Moved **down 88 px**; size unchanged |
| `TechStrip` | — | anchored (14,50), 292 × 68, world Y 848..916 | New; sits **6 px above** `Body` (922..1018) and inside the panel (1598..1890 × 848..916) |

The move cleared a 64 px overlap between `SelectionPanel` (1584..1904 × 798..1064) and the minimap
panel; the two now have a **24 px gap** (minimap bottom 774, selection top 798). The guard test
`SelectionPanel_DoesNotOverlapMinimap_AndBothFitCanvas` pins this.

**Gold-mine finding.** The design's gold-mine price change (100 → 300) was dropped because **neither
side can build a gold mine**:

- player build bar `HUD.buildPrefabs` = `House_Blue, Barracks_Blue, Archery_Blue, Monastery_Blue,
  Tower_Blue, Castle_Blue`;
- keyboard shortcuts `BuildingPlacer.quickBuild` = the same six entries;
- AI build order `AIController.buildOrder` = `Castle_Red, Barracks_Red, Archery_Red, Monastery_Red,
  House_Red, Tower_Red, House_Red`;
- no runtime script under `Assets/Scripts/` mentions `GoldMine`, and the only editor-tool references
  confirm the exclusion: `Assets/Editor/SecondStageEconomy.cs` (2026-10-16) explicitly strips
  `GoldMine_Active` out of `placer.quickBuild` and `ai.buildOrder` (lines 111 and 114), and
  `Assets/Editor/BuildHUD.cs` (2026-10-28) only keeps the label mapping `GoldMine* → 金矿`.

The scene does contain gold mines, but only as neutral map scenery: `Mine_Gold_1` at (-2.5, 6.0) and
`Mine_Gold_Red` at (35.5, 6.0) are each a `Building` with `team=Neutral`, `cost=100` (serialized on the
instance) and a `ResourceNode` of `kind=Gold, amount=1500`. They are harvestable gold sources, not
producible units of either side, and neither side's build list can place one.

`GoldMine_Active` therefore remains unreachable configuration and its price is irrelevant to play; the
prefab was deliberately left untouched.

(Note on method: the `.meta` GUIDs in this project are a 61-character base64 form rather than the
32-hex form used inside scene files, so a scene-file GUID grep is not a valid reachability test. The
reachability conclusion above rests on the live component lists read from the loaded scene plus the
runtime enumeration of the two scene objects.)

---

## Explicit disclosures

- **Physical GUI acceptance was never performed.** No real mouse, keyboard or gamepad input was
  injected at any point in Phase B verification. Every interaction in the smoke was driven through the
  runtime API (component calls and direct field writes), not through the EventSystem.
- **The visual appearance of the tech strip and of the moved minimap was never verified.** Static
  PlayMode screenshots are blocked in this project, so no image of either was ever produced or
  inspected. The only evidence for the layout is measured geometry: the rects in the table above, the
  24 px panel gap, the strip's 6 px clearance above `Body`, and the scene tests that assert the strip
  exists, starts hidden, is wired, and does not overlap the minimap.
- The tech strip's text was verified only as `Text.text` values (`250金100木`, `250金100木`, `200金`,
  `已研究`); that the labels carry a usable font was **not** asserted (a known Minor from the Task 5
  review), so legibility is unverified together with the strip's appearance.
- The smoke exercised repair, upkeep, technology, population and balance through the runtime API on
  the real scene; it did not exercise them through player input.

---

## Hand-off state

- Editor: **stopped**, not compiling, not updating.
- Scene: `Assets/Scenes/SampleScene.scene` active and the **only** loaded scene (`sceneCount=1`).
- Scene `dirty=false`; project `dirty=false`; console unread 0, `lastErrors=[]`.
- `Assets/Scenes/SampleScene.scene` MD5 **e69c0b2508e8731ec7394251b8bdf431** — unchanged from the
  pre-task value, before and after both PlayMode passes.
- Formal save slot `%LOCALAPPDATA%Low\DefaultCompany\兵临城下\AtTheGates` — **0 files**.
- Git: 2647 tracked entries, 0 deleted, 228 dirty entries (pre-existing). No git write of any kind.
- No scene, prefab, asset or source file was written by this task; the only writes were this report
  and the two log files below.

## Evidence index

- `.superpowers/sdd/phase-b/final-full-regression.log` — archived full-suite log (213/213, 125.728 s).
- `.superpowers/sdd/phase-b/task-7-smoke.log` — raw smoke log, including the two disclosed probe
  artefacts and an interpreter-notes footer.
- `.superpowers/sdd/phase-b/task-7-scope-audit.log` — build-bar/AI-order lists and measured HUD rects.
- `.superpowers/sdd/phase-b/task-3-v1-payload.json` — the real version-1 payload used for the
  compatibility statement.
- `.superpowers/sdd/progress.md` — the six task reviews whose Minors are summarised above.
- `docs/superpowers/reports/2026-10-31-rts-phase-b-verification.md` — this report.
