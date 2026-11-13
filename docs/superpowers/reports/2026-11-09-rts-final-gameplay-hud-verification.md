# 《兵临城下》最终玩法验收与 HUD 清晰化验证报告

日期：2026-11-09
工程：`D:\Unity\projects\At the Gates`
分支：`main`；交付提交 `c252edd`（驻塔）与 `eef6c63`（HUD/验收）
当前状态：**已交付并本地合并到 `main`，未推送。** 四张 warning/normal 双分辨率截图逐张实看通过；最终完整套件在原始失败的 720p 下达到 274/274 PASS。合并到 main 后重新编译 0 错误/0 警告，并再次取得 274/274 PASS、0 Failed、0 Skipped、0 Inconclusive，242.296 秒。最终 Console 290 Log、0 Warning、0 Error；真实存档未变。

## 1. 新鲜全量基线与缺陷闸门

- 调用：`RegressionTestMenu.RunPlayModeTests()`，只运行一个完整 PlayMode 套件。
- 结果：**270/270 PASS，0 Failed，0 Skipped，0 Inconclusive**。
- 引擎记录耗时：**230.278 秒**。
- 归档：`Logs/final-polish-baseline-270.txt`，26,174 字节，MD5 **`5d5b79d4d1634842b7684c870cbaf50b`**。
- 归档与 `Logs/RegressionTests.txt` 逐字节一致；归档中 `[Regression] Passed:` 共 270 行。
- 最终行：`[Regression] PlayMode run finished: Passed. Passed=270, Failed=0, Skipped=0, Inconclusive=0, Duration=230.278s`。
- 完成时编辑器为 stopped、未编译、未暂停，`MainMenu.scene` 为 `dirty=false`，状态读取 `lastErrors=[]`。
- 对照归档 `Logs/tower-fixes-2026-11-08-full-270.txt` 也是 270 项，MD5 `b2e5f8129095042306e567c0c7f1dba1`；本轮结论来自上述新鲜日志，不依赖旧归档冒充新结果。

缺陷闸门现已建立：后续真实地图探针或 HUD 聚焦测试若出现产品失败，应停止后续推进，保留复现证据并另走根因分析与最小红绿修复；不能把本基线解释为所有实际玩法均无缺陷。

### Task 2 初次 precondition 阻断与上下文诊断

- 使用 brief 中原文探针 `.superpowers/sdd/final-polish/GameplayAcceptanceProbe.cs`；打开真实 `SampleScene.scene`，进入 PlayMode，禁用 AI 后执行前置检查。
- 唯一实际结果：**FAIL** `precondition grid/selection/archer/empty-handed worker missing`。原始证据单独保留在 `Logs/final-gameplay-live-2026-11-09-precondition-fail.txt`（94 字节，MD5 `ca0c204206c58c150ff91b47b99af742`）。
- 初次失败数 **1**。前置分支直接 `yield break`，故初次日志无 `SUMMARY`；当时普通移动命令/到达、显式采集/实际收获、远程战斗、唯一临时路径存档 round-trip 均未执行。后续完整重跑结果见下一小节。
- 初次失败即停后，按协调者追加要求只做上下文诊断。原始 FAIL 已另存 `Logs/final-gameplay-live-2026-11-09-precondition-fail.txt`；复制时与初次 live 日志逐字节一致，当前 live 日志已由后续经批准的完整重跑更新。
- 新鲜 SampleScene PlayMode 诊断（`Time.frameCount=7041`）：`gridPresent=True`、`selectionPresent=True`、`UnitActor.All.Count=14`、`eligibleBlueArchers=2`、`gatherers=6`、`emptyHandedGatherers=3`、`eligibleBlueGatherers=3`、**`eligibleBlueEmptyHandedGatherers=0`**；场景直接查找也得 `sceneUnitActors=14`、`sceneGatherers=6`、`sceneEmptyHandedGatherers=3`。证据：`Logs/final-gameplay-context-2026-11-09.txt`，253 字节，MD5 `80bea54919bbb2dcb7e9df005244ad90`。
- 在该诊断阶段探针保持 brief 原文，未增加等待、未重跑；未开始产品修复或 HUD 工作。停止诊断 PlayMode 后不保存并恢复 MainMenu。
- 前置失败发生在临时路径生成之前；系统临时目录 `atg-final-*.json` 及其 `.tmp`/`.bak` 匹配数均为 0。真实槽未写入，MD5 保持 `0d34d758ec3bd3eb7ee954eebf411076`。

### Task 2 受控 PlayMode fixture 与前次完整探针（SaveIdentity 失败归档）

- 协调者确认 empty-handed 过滤只是 fixture 便利条件，不是产品需求。新鲜场景记录三名 eligible blue worker：`Pawn_Blue_0 Carrying=20 Gold`、`Pawn_Blue_2 Carrying=20 Wood`、`Pawn_Blue_1 Carrying=20 Wood`，三者均 `autoStart=True`（frame=4393）。这解释准备工具调用时自动采满货物，**不作为产品 bug**。证据：`Logs/final-gameplay-workers-before-fixture-2026-11-09.txt`，MD5 `5c668e06907bffabe1f40b69c2d214d3`。
- 仅修改探针 fixture：选任一 eligible blue Gatherer，并在显式采集命令前调用既有公共 `RestoreSaveState(new Gatherer.SaveState { carrying=0, carryingKind=当前类型, manualIdle=false })`。此次选中 `Pawn_Blue_1`，记录 `originalCargo=20 originalKind=Gold autoStart=True carryingAfter=0`；未改 autoStart 或磁盘状态。
- 前次证据已归档为 `Logs/final-gameplay-live-2026-11-09-saveidentity-fail.txt`，909 字节，MD5 `b4fafca66132c0b366f7739581712247`。
- **PASS** `ordinary-move-command destination=(5,4,0)`；**PASS** `ordinary-move-arrival position=(5.50,4.50,0)`。
- **PASS** `gather-fixture-empty originalCargo=20 before=0`；**PASS** `gather-command node=Mine_Gold_1`；**PASS** `gather-harvest carrying=20 before=0`。显式 `SelectionManager.IssueMove` 指定节点与正常玩法从 0 增载都实际断言，未接受既有货物。
- **PASS** `ranged-combat health=18`（临时红靶初始 30）。
- **FAIL** `temporary-save-roundtrip`：`CaptureSnapshot` 报错 `物体 "Final gameplay red target (PlayMode only)" 没有 SaveIdentity，无法保存。请运行 Tools/Save/重建预制体目录。` 最终 **`SUMMARY failures=1`**。
- 按 actual-FAIL 即停要求，未修产品、未给靶子补组件、未弱化/跳过存档断言、未再重跑。停止 PlayMode 恢复 MainMenu，最终 stopped、未编译、`dirty=false`、`lastErrors=[]`；临时文件匹配为 0，真实槽 MD5 不变。

### Task 2 红靶 cleanup 修正与最终全 PASS run

- 协调者确认前次失败是 fixture 污染，不是存档系统缺陷。只修改探针清理：远程战斗断言后，若临时红靶仍存在则 `Destroy`，有界等待至对象销毁且 `UnitActor.All` 中同一引用为 0，再进行快照。未添加假 `SaveIdentity`，未改产品验证。
- 最终 live 证据：`Logs/final-gameplay-live-2026-11-09.txt`，934 字节，MD5 `11011dba27cdf1a1c091aaab8cc4b5f8`；同一 run 共 **8 PASS、0 FAIL、`SUMMARY failures=0`**。
- **PASS** `ordinary-move-command destination=(4,4,0)`；**PASS** `ordinary-move-arrival position=(4.50,4.50,0)`。
- **PASS** `gather-fixture-empty originalCargo=20 before=0`；**PASS** `gather-command node=Mine_Gold_1`；**PASS** `gather-harvest carrying=20 before=0`。
- **PASS** `ranged-combat health=18`；随后 `FIXTURE cleanup red-target destroyed=True registeredReferences=0`，**PASS** `fixture-target-cleanup`。
- **PASS** `temporary-save-roundtrip capture=`：cleanup 后真实场景快照在唯一系统临时路径完成 Capture/Save/Read，读回非空。路径 `atg-final-3eb7abf36fd14012872c49cfd78684ba.json` 已在 finally 删除，临时文件匹配总数 0。
- 完成后停止 PlayMode、恢复 MainMenu 不保存；最终 stopped、未编译、`dirty=false`、`lastErrors=[]`。SampleScene、MainMenu、真实槽 MD5 均不变。Task 2 已满足门槛，Task 3–6 未由本代理执行。

## 2. 证据等级说明

- **既有套件**：2026-11-09 新鲜全量日志中的自动化断言通过；多数使用隔离回归场景/临时对象，不等同于真实地图或真人操作。
- **真实地图探针**：Task 2 最终同一 run 已实测普通移动、显式采集/实际收获、远程战斗、红靶 cleanup 和唯一临时路径存档 round-trip，8 条断言全部 PASS，`SUMMARY failures=0`。最初 precondition 与前次 SaveIdentity 失败单独归档，不用旧 FAIL 覆盖最终证据。历史驻塔证据只覆盖其所述链路。
- **截图**：Task 6 已在真实 SampleScene 的 PlayMode 中采集 warning/normal × 1920×1080/1280×720 四图并逐张 Read；资源/兵种栏、选中面板、生产面板均未出屏，未见文字裁切、重叠或 Backing 遮字，warning 与 normal 配色均可读。截图只证明这两个分辨率下的视觉状态，不替代真人操作或完整玩法验收。

## 3. 功能证据矩阵

同一测试可能同时支撑多个系统，表中数量不可跨行求和。“通过”仅指列出套件的断言通过。

| 系统 | 既有套件（新鲜 270 项基线） | 真实地图探针 | 截图 | 当前结论与边界 |
|---|---|---|---|---|
| 建造/经济 | `PlacementTests` 9、`ResourceLimitTests` 9、`BalanceValueTests` 5，以及生产/退款相关用例通过 | **本任务未做**；Task 2 也不替代完整建造实玩 | Task 6 四图仅证明 HUD 视觉；既往 HUD 图不包含完整建造流程 | 合法/占用放置、取消、资源上下限与部分扣款路径有套件证据；施工、拆除、全经济长局不能据此宣称全部无误 |
| 选择/编队/巡逻 | `InputGateTests` 7、`ControlGroupTests` 6、`PatrolTests` 5、`MovementAndCommandTests` 17、`CommandIntentTests` 15 通过 | **PASS**：真实 SampleScene `ordinary-move-command` 目的地 `(4,4,0)`，`ordinary-move-arrival` 到达 `(4.50,4.50,0)` | Task 6 四图仅证明 HUD 视觉 | 普通移动命令与到达有本轮真实地图证据；编队、巡逻仍来自套件，不是操作系统级真人键鼠验收 |
| 采集/维修 | `WorkerBehaviorTests` 13、`WorkerRepairTests` 11、`RepairTests` 8、`AIRepairTests` 11，以及采集/卸货相关命令与资源测试通过 | **PASS**：PlayMode-only 空手 fixture 后，`gather-command` 显式指定 `Mine_Gold_1`，`gather-harvest` 从 0 正常采到 20 | Task 6 四图仅证明 HUD 视觉 | 真实地图显式采集与实际收获有本轮证据；货物重置只是运行时 fixture，未替代命令/收获断言。维修仍只有套件证据 |
| 生产/人口 | `ProductionDependencyTests` 3、`PopulationTests` 11、`UnitLimitTests` 14、`FirstBatchSceneTests` 3，以及相关拒绝提示用例通过 | **本任务未做** | Task 6 normal 两图含生产面板，未见裁切；不代表真人生产操作 | 配方归属、队列计数、人口/职业/总战斗上限和部分 UI 拒绝原因有套件证据；未新做真人生产操作 |
| 战斗/驻塔/治疗 | `TowerGarrisonTests` 13、`RubbleAndGarrisonCommandTests` 4、`CommandIntentTests` 15，以及僧侣治疗/断粮停疗相关用例通过 | **本轮 PASS**：真实 SampleScene `ranged-combat health=18`，临时红靶初始生命 30。**历史 PASS**：受控正式预制体入塔/出塔，见驻塔报告及两份 `Logs/tower-live-controlled-*.txt` | **历史驻塔图**：`Logs/tower-controlled-garrisoned-2026-11-09.png`；Task 6 四图仅证明 HUD 视觉 | 本轮只证明一名蓝方远程兵对一个 PlayMode-only 红靶造成伤害；原场景天然蓝塔、所有兵种战斗、治疗与真人右键手感不在该结论内 |
| 科技/维护费 | `TechnologyTests` 18、`UpkeepTests` 15、`BacklogFixTests` 5 通过 | **本任务未做** | 既往菜单/HUD 图不能替代科技全过程实测 | 研究扣费/进度/增益/存档与维护费、断粮拒绝有套件证据；未在本任务新做长局科技实战 |
| AI | `AITechnologyTests` 9、`AIProductionBudgetTests` 8、`AIRepairTests` 11、`AiCompositionTests` 5，以及 AI 选址、攻击波和人口相关用例通过 | **本任务未做**；历史长局报告不是本轮新证据 | Task 6 四图仅证明 HUD 视觉 | AI 研究、预算、维修、配比与波次续走/绕行有套件证据；不能据此宣称所有长局平衡或实战无误 |
| 存档/生命周期 | `SaveLoadTests` 7、`SceneLifecycleTests` 1、`PauseMenuTests` 7、`WipeoutDefeatTests` 3，以及科技/羊/驻塔等存档用例通过 | **PASS**：红靶 cleanup 后，`temporary-save-roundtrip capture=` 在唯一系统临时路径完成真实场景快照写入/读回；最终 `SUMMARY failures=0`。真实槽只读，MD5 前后不变 | Task 6 新四图仅覆盖游戏内 HUD；菜单仍以历史图为证据 | 本轮证明真实地图临时路径 round-trip；未写真实槽，也未新做菜单往返、真人加载或独立播放器验收 |
| HUD/菜单 | 既有套件及本轮四个 `HudReadability20261109Tests` 聚焦测试均通过；最终完整套件 **274/274 PASS**，见 `Logs/final-polish-full-274-fixed.txt`；最终 Console 为 `Logs/final-polish-console-fixed.json`（290 Log、0 Warning、0 Error） | 不适用为本任务的新玩法探针 | **Task 6 新图**：`Logs/final-hud-warning-1920x1080.png`、`final-hud-normal-1920x1080.png`、`final-hud-warning-1280x720.png`、`final-hud-normal-1280x720.png`，四图均逐张实看通过 | Backing、字号、UnitCost fit、运行时配色与中文生产标题有聚焦 GREEN、双状态/双分辨率视觉证据及最终全量通过；真人审美、其他 DPI 仍未验收 |

## 4. 文件完整性与工作区

Task 1 基线前后、Task 2 探针前后三个受保护文件的尺寸和 MD5 均完全一致：

| 文件 | 字节 | MD5 |
|---|---:|---|
| `Assets/Scenes/SampleScene.scene` | 4,283,255 | `61ccb5b7c8958b3cbca60062ab8cb81c` |
| `Assets/Scenes/MainMenu.scene` | 147,538 | `90bb497775fd5e4c60d0f06d950145ec` |
| 真实槽 `save_slot_1.json` | 424,419 | `0d34d758ec3bd3eb7ee954eebf411076` |

开始时 `git diff --check` 无输出。已有两处受跟踪修改为 `SelectionManager.cs` 与 `RubbleAndGarrisonCommandTests.cs`（合计 80 行新增、2 行删除），以及既有驻塔报告和新设计/计划文档；它们是本轮前已交付/已批准工作，不算 Task 1 产品改动。

Task 1/2 未修改产品代码、场景或预制体，未调用场景保存或真实游戏写档 API，未提交、合并或推送。Task 2 新增/修改文件仅为探针 fixture、原始/归档失败证据、上下文与工人状态诊断日志、最新 live 日志和任务报告，并更新本报告与 RESUME。

## 5. 当前未验证边界

- Task 2 最终同一 run 的普通移动、显式采集/实际收获、远程战斗、红靶 cleanup 与唯一临时路径存档 round-trip 均 PASS，`SUMMARY failures=0`；这不等于真人鼠标、长期平衡或真实槽写入验收。
- Task 5 四项聚焦 GREEN、对象/动画/AI 审计、diff check 与无保存幂等均已通过；Task 6 四张最终截图也通过逐张视觉检查。最终完整套件在 1280×720 下 **274/274 PASS**，0 Failed、0 Skipped、0 Inconclusive，证据为 `Logs/final-polish-full-274-fixed.txt`。
- 最终 Console 的完整 JSON `Logs/final-polish-console-fixed.json` 已归档并经 Python 全量解析：**290 Log、0 Warning、0 Error**。
- 未做真人操作系统级键鼠、独立播放器、其他 DPI/分辨率、长局压力或平衡验收。截图只覆盖 16:9 的 1920×1080 与 1280×720，不证明缩放、超宽屏或真人输入手感。

## 6. Task 5 初次场景 Apply 审计阻断与回滚

- 执行前为 stopped、MainMenu clean、编译 `0 errors / 0 warnings`；调用 `ApplyHudReadability20261109.Apply()` 成功，原始工具日志记录 `Layout validated.`、`Scene saved.`，保存后 MD5 `29d2cc30ccd0464c4672cf2198a01d81`。
- backup 与首次保存结果审计为 `objects 1763 1771`、`added 8 removed 0`、`animationSpeed_equal False`、**`aiWeights_equal False`**。新增 8 个对象是两个目标面板各自的 Backing GameObject、RectTransform、Image 和配套 CanvasRenderer。
- 除已知动画噪声外，保存结果新增 `workerWeight: 1`、`warriorWeight: 3`、`lancerWeight: 2`、`archerWeight: 3`、`monkWeight: 1`；备份中这五个字段均不存在。虽然数值等于当前 `AIController` 默认值，仍按任务硬门槛视为未授权 AI 字段差异，未予隐藏或豁免。
- 已在运行任何聚焦测试之前，从 `.superpowers/sdd/final-polish/sample-before.scene` 完整字节恢复 SampleScene；核验 `byte_equal=True`，最终 MD5 回到 `61ccb5b7c8958b3cbca60062ab8cb81c`。回滚后审计为 `objects 1763 1763`、`added 0 removed 0`、`animationSpeed_equal=True`、`aiWeights_equal=True`。
- **没有四项 GREEN 日志，也没有幂等重跑结论。** 因首次保存审计已阻断，未运行四个聚焦测试、未执行第二次 Apply，未开始 Task 6 截图或完整套件。Task 3 测试源码与 0.35 秒等待未改。
- 原始备份和工具日志均保留；工具日志 MD5 `edfa3d1894b916f40ef965c1d947d7f0`。MainMenu `90bb497775fd5e4c60d0f06d950145ec` 与真实存档 `0d34d758ec3bd3eb7ee954eebf411076` 不变。
- 完整操作、request ID、对象 ID 与回滚证据见 `.superpowers/sdd/final-polish/task-5-report.md`；该初次阶段结论为 **BLOCKED**，不是 HUD 场景验收通过。后续经授权清理与最新阻断见下节。

## 7. Task 5 授权序列化清理、对象审计通过与聚焦 inset RED

- 协调者提供既往 `2026-11-08-rts-ui-polish-verification.md` §3.2 的同类证据，授权按既有 `ui-polish/clean_scene_noise.py` 只清理动画速度和五个新序列化 AI 默认字段。原始 BLOCKED 报告/日志已分别归档为 `task-5-initial-blocked-report.md`、`hud-readability-tool-initial-blocked.log`，MD5 分别为 `3992b82ec3fec5802e38166a85de6ae3`、`edfa3d1894b916f40ef965c1d947d7f0`。
- 从已回滚 baseline 第二次 Apply 后，按既有脚本清理。最终 SampleScene MD5 **`4d3780a2ca967cbe7f9ab82a1db5a72b`**；第二次工具日志记录 `Layout validated.`、`Scene saved.`，清理前保存 MD5 `df30934d9fa10b9394683b3722e54f42`。
- 清理后磁盘审计：**`objects 1763 1771`、`added 8 removed 0`、`animationSpeed_equal=True`、`aiWeights_equal=True`**。新增仅 ProductionPanel/SelectionPanel 各一套 Backing GameObject、RectTransform、Image、CanvasRenderer。
- 29 个既有变化对象逐字段审计全部为目标 HUD：6 UnitName 字号、6 UnitCost 字号+颜色、6 Build Cost 颜色、4 Caption 颜色、4 Backing alpha、1 Selection Body 颜色、2 个面板新增子引用；没有其他 YAML 对象变化。
- 已从磁盘 Single 重开 SampleScene，未保存，状态 clean。随后首个精确测试 `TargetPanels_UseDarkReadableBackings` **RED**：`ResourceBar inset min Expected: (6.00, 6.00) But was: (8.00, 8.00)`，最终 `Passed=0, Failed=1, Skipped=0, Inconclusive=0, Duration=2.575s`。
- 失败日志：`Logs/final-hud-red-TargetPanels_UseDarkReadableBackings-after-cleanup.txt`，927 字节，MD5 `79e8833fc7ba619a2708237afa41bbc1`。失败原因是 `PolishHUD.BarInset=8` 与未改测试的 6px 要求不一致；alpha 已通过，未修改断言或 0.35 秒等待。
- 按聚焦失败门规已停止：其余三个测试未跑、GREEN=0、未执行幂等第二次 Apply、未开始 Task 6。保留已通过对象审计的清理场景；MainMenu `90bb497775fd5e4c60d0f06d950145ec`、真实存档 `0d34d758ec3bd3eb7ee954eebf411076` 不变，最终编辑器 stopped、SampleScene clean、`lastErrors=[]`。
- 当前结论仍 **BLOCKED**；需要协调者决定如何在不放宽断言的前提下修正实现/工具，不能宣称 DONE。

## 8. 每面板 inset 测试修正后的 BuildBar 冲突

- 按用户裁定只参数化 `AssertBacking`：ResourceBar=8、BuildBar=8、UnitPanel/SelectionPanel/ProductionPanel=6；其他断言与 0.35 秒等待不变。编译 request `codex-29872-100` 为 0 errors / 0 warnings。
- 首个测试重跑 request `codex-29872-101`：ResourceBar 8px 已通过，但 **BuildBar inset min Expected `(8,8)` / But was `(6,6)`**，最终 `Passed=0, Failed=1`，Duration `2.590s`。日志 `Logs/final-hud-red-TargetPanels_UseDarkReadableBackings-buildbar-inset.txt`，MD5 `8430beeda02d820993ba034caf45bec1`。
- 原始 backup 和当前 SampleScene 的 BuildBar/Backing 均为 full-stretch + `m_SizeDelta=(-12,-12)`，即两者原本/现在都是 6px；ResourceBar 两者均为 8px。故 BuildBar 的 6px 不是本轮 Apply 引入，裁定中的“原 8px”与 backup 证据冲突。
- 未改场景 inset，SampleScene MD5 仍为 `4d3780a2ca967cbe7f9ab82a1db5a72b`。按 stop gate，其余三个测试、YAML 尾空格清理、幂等和 Task 6 均未执行；GREEN=0。最终 stopped、SampleScene clean，MainMenu/真实槽不变。
- 需要明确 BuildBar 应保留原始 6px 还是授权改为 8px；当前保持 **BLOCKED**，不宣称 DONE。

## 9. 更正后的 Backing GREEN 与 UnitCost fit RED

- 协调者正式更正此前“BuildBar 原 8px”报告；backup ID `98705560` 证明原始为 6px。最终测试映射 ResourceBar=8，BuildBar/UnitPanel/SelectionPanel/ProductionPanel=6，仅改 BuildBar 测试参数。编译 request `codex-29872-103` 为 0 errors / 0 warnings。
- **Backing GREEN**：request `codex-29872-104`，`Passed=1, Failed=0, Skipped=0, Inconclusive=0, Duration=2.382s`；日志 `Logs/final-hud-green-TargetPanels_UseDarkReadableBackings.txt`，MD5 `865be6b72985b8b022eec835f3cb270d`，在下一 run 前独立归档。
- **UnitLabels RED**：request `codex-29872-106`，UnitCost 高度 fit `Expected <=14.0100002f / But was 14.25f`（测试 line 38）；最终 `Passed=0, Failed=1`，Duration `2.117s`。日志 `Logs/final-hud-red-UnitLabels_UseReadableSizes_AndStillFit-after-cleanup.txt`，MD5 `5848cbc9167a96fc23155c0a1c2aa430`。
- 未放宽 fit、未改布局。后两个测试、四处 YAML 尾空格清理、幂等和 Task 6 均未执行。SampleScene MD5 仍为 `4d3780a2ca967cbe7f9ab82a1db5a72b`，MainMenu/真实槽不变；最终 stopped、SampleScene clean。当前 **BLOCKED**。

## 10. UnitCost lineSpacing=0.95 假设的实测结果

- 仅新增授权的 UnitCost `lineSpacing=0.95f` 实现，保留 13pt、14px rect、位置和所有断言；编译 0/0，经增量 Apply 与限定动画/AI 清理后，场景 MD5 `4c7b4353b6c3a81f2ef6caf5e1113b8d`。审计仍为 added=8、removed=0、animationSpeed_equal=True、aiWeights_equal=True。
- 从磁盘重开后，五个 UnitCost 均实测 `font=13 lineSpacing=0.95 preferredHeight=14.250 rectHeight=14.000`；假设的约 13.54px 未出现。
- UnitLabels 重跑 request `codex-29872-113` 仍 RED：`Expected <=14.0100002f / But was 14.25f`，`Passed=0, Failed=1`，Duration `3.020s`。日志 `Logs/final-hud-red-UnitLabels_UseReadableSizes_AndStillFit-linespacing-095.txt`，MD5 `85a5c2d2a3db385df6dfb8c18795a107`。
- Backing GREEN 与历史 RED 均保留；后两项、尾空格清理、幂等和 Task 6 未执行。最终 stopped、SampleScene clean，MainMenu/真实槽不变。当前 **BLOCKED**。

## 11. Task 5 最终通过：用户批准的 UnitCost 15px 高度

- 用户明确批准 UnitCost 13pt、仅高度 14→15，y=10 不变。最终几何五项均为 bottom=10、height=15、top=25、Name bottom=26、gap=1、preferredHeight=14.25；lineSpacing 已恢复 1.0，相关临时实现代码已删除。
- 增量 Apply 与既定动画/AI 噪声清理、磁盘重载后，完整审计为 **1763→1771、added=8、removed=0、changed_existing=35、animationSpeed_equal=True、aiWeights_equal=True**。新增仅两个 Backing 的四件套；35 个既有变化全部是目标 HUD 字段，其中 6 个为 UnitCost RectTransform 高度。
- 四个聚焦测试分别串行、每项 `Passed=1, Failed=0, Skipped=0, Inconclusive=0`，且逐项归档：

| 测试 | Duration | GREEN 日志 MD5 |
|---|---:|---|
| TargetPanels_UseDarkReadableBackings | 2.382s | `865be6b72985b8b022eec835f3cb270d` |
| UnitLabels_UseReadableSizes_AndStillFit | 2.456s | `5afbbaaeabfd2871aa08a2a8d838eeef` |
| ProductionTitle_UsesChineseBuildingName | 2.144s | `6f14816ffb9b5c7a35420d04964a8c9f` |
| NormalUnitCost_KeepsMainTint_AfterRuntimeRefresh | 2.519s | `9a0a139eabaa95da82d35bc58ea7e8a8` |

- 仅在两个新 Image YAML 块删除四个尾空格；`git diff --check` 无输出。最终 SampleScene 4,164,037 字节，MD5 **`610ea426d9ce89bf4e552e960ab093dc`**。
- 幂等 Apply request `codex-29872-145`：`Layout validated.`、`No changes needed — scene not saved`，场景 MD5 保持 `610ea426d9ce89bf4e552e960ab093dc`；幂等日志 MD5 `a039c8d6b251ddfa9183fb9e4d9617be`。
- backup `61ccb5b7c8958b3cbca60062ab8cb81c`、MainMenu `90bb497775fd5e4c60d0f06d950145ec`、真实槽 `0d34d758ec3bd3eb7ee954eebf411076` 不变。最终 stopped、SampleScene/project clean、`lastErrors=[]`。
- **Task 5 DONE**；未开始 Task 6，未采新截图或运行完整套件。完整历史 RED、错误 BuildBar 8px 报告的更正、失败 lineSpacing 假设和最终用户决定见 `.superpowers/sdd/final-polish/task-5-report.md`。

## 12. Task 6 初次截图、原始全量与完整性（历史 BLOCKED，已由 §14 取代）

### 12.1 截图探针与实看结果

- 创建 `.superpowers/sdd/final-polish/HudScreenshotProbe.cs`，内容与 brief 给定代码一致，1,839 字节，MD5 `c7f50121d693005702327a160f099f5b`。每种分辨率都从磁盘重新打开 SampleScene、进入 PlayMode、执行探针、停止播放；未保存场景，临时 Castle/Barracks 随停止播放丢弃。
- 本会话 `unity_gameview set_resolution` 调用未接收到其文档要求的 width/height 参数，连续返回参数缺失。为不伪造工具成功记录，实际改用 Game View 已有固定分辨率预设（Full HD index 3、Codely 1280x720 index 9），并以 `unity_gameview get_resolution` 分别确认 **1920×1080**、**1280×720**；PNG IHDR 也逐张核为相同尺寸。该操作偏差及失败 request 已写入 Task 6 执行报告。
- 1080p 首次探针因准备工具调用期间未禁用 AI、蓝方单位已被消灭而在选择 archer 前置处 NullReference；该次未生成可接受截图、未改产品。随后重新加载全新场景并立即执行原文探针，成功请求 `codex-29872-181`；720p 成功请求 `codex-29872-187`。最终证据只来自这两次新鲜场景成功运行。

| 文件 | 实际尺寸 | 字节 | MD5 | 逐张 Read 结论 |
|---|---:|---:|---|---|
| `Logs/final-hud-warning-1920x1080.png` | 1920×1080 | 701,528 | `dd4e7b95928176ef94bed9aabd87a507` | 弓手选中、需前置兵种造价警告可读；各 HUD 在屏内，无裁切/重叠/Backing 遮字 |
| `Logs/final-hud-normal-1920x1080.png` | 1920×1080 | 713,933 | `88bc8b1f9b46ab4972d45dc24e3ef49b` | 兵营中文标题、正常生产面板与兵种栏可读；无出屏、裁切、重叠或错误遮挡 |
| `Logs/final-hud-warning-1280x720.png` | 1280×720 | 398,845 | `ab374f831a061560580dba90ee085a9a` | warning 文字和选中面板在 720p 仍可读；无出屏、裁切、重叠或 Backing 遮字 |
| `Logs/final-hud-normal-1280x720.png` | 1280×720 | 408,434 | `0b553d2c0c60d15b361fd76690948fd3` | 中文兵营标题与正常生产面板在屏内且可读；无裁切、重叠或错误遮挡 |

截图结论是这两个 16:9 分辨率下的视觉通过，不是对失败回归的豁免。

### 12.2 聚焦与最终完整回归

- Task 5 四个聚焦测试保持各自 GREEN，日志与 MD5 见 §11；Task 6 在原始完整套件归档前未运行聚焦测试。后续仅对唯一失败做同测试双分辨率根因对照，见 §13。
- 截图完成后只调用一次 `RegressionTestMenu.RunPlayModeTests()`（请求 `codex-29872-191`）。运行结束立即把 `Logs/RegressionTests.txt` 归档为 **`Logs/final-polish-full-274.txt`**，源与归档逐字节一致。
- 归档 27,079 字节，MD5 **`324a08868c0be0f2e7bdf9513f23b5fe`**；耗时 **233.501 秒**。结果：**273 PASS、1 Failed、0 Skipped、0 Inconclusive**。`[Regression] Passed:` 共 273 行。
- 唯一失败：`UiPolish20261108Tests.AttackLabel_KeepsRightInset`（1.854 秒）。断言 `The attack label must retain a six-pixel right inset.`，期望 `>=5.98999977f`，实际 **`4.0f`**；位置 `Assets/Tests/PlayMode/UiPolish20261108Tests.cs:120`。
- 按失败门规保留证据并判定 **BLOCKED**；未修改该测试、未放宽断言、未保存额外场景改动。原始全量日志独立归档，后续诊断没有覆盖它。

### 12.3 Console、场景审计与受保护文件

- 完整套件前清空 Console；完成后 `unity_console get` 返回 **290 条**。完整原始 JSON 已保存为 `Logs/final-polish-console.json`，135,807 字节，MD5 `bff8a71809a9a26744409b8cb9a9a841`。Python 对全部 290 条进行 `json.loads` 和类型统计：**Log=290、Warning=0、Error=0**；errors/warnings 完整提取结果为空列表。失败断言存在于回归日志，不冒充 Console Error。
- Task 5 已批准场景审计保持：对象 **1763→1771**、added=8、removed=0、changed_existing=35；新增仅两个 Backing 的 GameObject/RectTransform/Image/CanvasRenderer，动画与 AI 字段审计均为 equal。无保存幂等 Apply 返回 `No changes needed — scene not saved`。Task 6 未调用场景保存，最终 SampleScene MD5 与批准值一致。
- 最终编辑器为 stopped、未暂停、未编译、未更新；活动场景 `MainMenu.scene`，scene/project 均 `dirty=false`。最终 `git diff --check` 无输出；`git status` 仍显示批准的既有修改/新文件，无提交、合并、推送或 worktree 操作。

| 受保护文件 | 最终字节 | 最终 MD5 | 对照 |
|---|---:|---:|---|
| `Assets/Scenes/SampleScene.scene` | 4,164,037 | `610ea426d9ce89bf4e552e960ab093dc` | 与 Task 5 批准场景一致 |
| `Assets/Scenes/MainMenu.scene` | 147,538 | `90bb497775fd5e4c60d0f06d950145ec` | 与原始指纹一致 |
| 真实槽 `save_slot_1.json` | 424,419 | `0d34d758ec3bd3eb7ee954eebf411076` | 与原始指纹一致，未写入 |

**历史/初次运行结论（已被 §14 的最终 274/274 结果取代）：当时视觉截图与完整性检查通过，但初次全量回归未通过，Task 6 暂判 BLOCKED。** 此处保留原始门控历史，不是当前最终结论；当前交付状态见页首与 §14。

## 13. 唯一失败的历史根因复核：分辨率依赖（当时停止盲修，最终处理见 §14）

- 原始 `final-polish-full-274.txt` 保持 MD5 `324a08868c0be0f2e7bdf9513f23b5fe`。续作前将当前场景字节备份为 `.superpowers/sdd/final-polish/sample-before-attack-inset.scene`，MD5 `610ea426d9ce89bf4e552e960ab093dc`。
- 当前场景与最初 `sample-before.scene` 的 Attack/Label RectTransform 完全一致；只读探针 `codex-29872-202` 实测 `offsetMin=(30,0)`、`offsetMax=(-6,0)`、`worldRightInset=6`。当前磁盘没有 4px inset。
- `BuildAttackButton` 创建时设置 right=-6；`ApplyUiPolish20261108` 也恢复 -6。`NormalizeButtonLabels` 对 full-stretch Attack Label 的 `splitFace` 判断为 True，实际不进入居中重置。三个候选路径均无导致此次 4px 的证据。
- 同一 `AttackLabel_KeepsRightInset` 零改动对照：720p `codex-29872-198` 仍 RED，actual=4，日志 `Logs/final-attack-inset-red-720.txt`（MD5 `ada91d24a12ef00a3492f916b01fda46`）；1080p `codex-29872-201` GREEN 1/1，日志 `Logs/final-attack-inset-diagnostic-1080.txt`（MD5 `9248bde76c8acf37ba530d5f5b29193d`）。
- CanvasScaler reference=1920×1080；720p 缩放 2/3，测试的 GetWorldCorners 距离把本地 6px 变为 4px，数值精确吻合。这证明原始全量失败由截图后在 720p 运行完整套件触发，不是产品布局回归。
- 按“根因无支持则停止”的要求，没有修改工具、产品、测试或场景。需明确选择：**完整回归固定在 1920×1080 参考分辨率运行**，或**经批准把测试改为分辨率无关的本地/偏移测量**。未擅自选择任一方案，保持 BLOCKED。
- 最终 MainMenu stopped/clean，SampleScene/MainMenu/真实槽 MD5 仍为 `610ea426d9ce89bf4e552e960ab093dc` / `90bb497775fd5e4c60d0f06d950145ec` / `0d34d758ec3bd3eb7ee954eebf411076`；`git diff --check` 通过，未提交。

## 14. 分辨率无关测试修正与最终全绿

- 经协调者裁定，仅修改 `AttackLabel_KeepsRightInset`：世界角仍由 GetWorldCorners 取得，随后分别经 `button.InverseTransformPoint` 转为 button-local，再断言右上 x 差 >=5.99；6px 要求和 icon 不重叠断言保留。注释明确 1920×1080 参考分辨率与 720p 2/3 缩放，未退化为只检查 offsetMax。
- 测试 diff 7 增/1 删，MD5 `7d2ca8f997237dd1f7e133c7ab7d7747`；编译 `codex-29872-205` 为 0 errors / 0 warnings。产品、工具、场景、预制体和图片均未改。

| 验证 | 请求 | 结果 | 日志与 MD5 |
|---|---|---|---|
| 修正后 720p 聚焦 | `codex-29872-208` | 1/1 PASS，2.229s | `Logs/final-attack-inset-green-local-720.txt`；`1963eb3577aee4591bc78722d860a9bc` |
| 修正后 1080p 聚焦 | `codex-29872-211` | 1/1 PASS，2.034s | `Logs/final-attack-inset-green-local-1080.txt`；`be1a05fc1385f39ffce1d853fc966ae7` |
| 最终完整套件，仍为 720p | `codex-29872-215` | **274/274 PASS**，0 Failed/Skipped/Inconclusive，246.996s | `Logs/final-polish-full-274-fixed.txt`；`c8d7ce0f9b2193351750d23ae57bfcd2` |

- 新完整日志 26,572 字节、274 条 Passed，与运行时日志逐字节一致。原失败归档 `Logs/final-polish-full-274.txt` 保持 MD5 `324a08868c0be0f2e7bdf9513f23b5fe`，红绿历史完整。
- 最终 Console request `codex-29872-216` 经 Python 全量 JSON 解析：290 Log、0 Warning、0 Error，errors/warnings 提取为空；`Logs/final-polish-console-fixed.json` MD5 `413f4e6ba58c95c7d28b452db04e849e`。
- 因没有视觉/产品变化，§12 四张最终截图保持有效，无需重采。SampleScene/MainMenu/真实槽 MD5 仍为 `610ea426d9ce89bf4e552e960ab093dc` / `90bb497775fd5e4c60d0f06d950145ec` / `0d34d758ec3bd3eb7ee954eebf411076`。
- 最终 MainMenu stopped/clean、未编译，`lastErrors=[]`；`git diff --check` 无输出，无临时 InitTestScene 残留；未提交、合并或推送。
- **最终状态：已交付并本地合并到 `main`，未推送。** 验证边界仍不包含真人 OS 键鼠、standalone player、长局压力/平衡和其他 DPI/分辨率。

## 15. 全分支独立终审

- **Ready to deliver: Yes；Must-fix items: none；0 Critical / 0 Important / 3 Minor。** 终审实际检查了驻塔与 HUD 完整 diff、场景、测试修正、四张截图、274 条唯一 PASS、Console 和受保护文件指纹。
- 非阻断边界：真实地图远程探针证明临时红靶受到 12 点伤害，但不做攻击者/投射物来源归因；临时存档探针的未来失败诊断可再补 saveError/readError。
- 终审指出的计划旧句“矩形尺寸不变”已修正为“除用户批准的 UnitCost 高度 14→15 外，其余矩形不变”。此项是文档维护，不改变已验收产品或场景。
- 完整终审记录：`.superpowers/sdd/final-polish/final-whole-branch-review.md`。上述 Minor 均为非阻断维护项，外部验收边界不变。

## 16. 本地合并与合并后复验

- 用户选择本地合并。驻塔提交 `c252edd`、HUD/验收提交 `eef6c63` 已 fast-forward 合并到 `main`；仓库无远端，未 pull/push。
- 合并后 `request_compile` 实际观察编译完成，0 errors / 0 warnings。随后在 `main` 重跑完整套件：**274/274 PASS，0 Failed、0 Skipped、0 Inconclusive，242.296 秒**。
- 合并后日志：`Logs/merge-main-2026-11-09-final-274.txt`，MD5 `939b94b3e8225d3c1bca22b172bdda8e`；274 条唯一 Passed。
- 功能分支 `fix/tower-top-garrison` 已在合并和测试通过后删除。最终编辑器 stopped、MainMenu clean，真实存档 MD5 不变。
- Git 切换后的 SampleScene 原始工作区 MD5 为 `24af4b31e398458e49a30f2eac1f1672`；规范化 CRLF→LF 后为已验收的 `610ea426d9ce89bf4e552e960ab093dc`，证明只是行尾变化，无内容差异。
