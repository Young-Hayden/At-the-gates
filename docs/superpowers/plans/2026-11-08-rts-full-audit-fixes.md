# RTS 全面体检修复 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: use test-driven-development and execute inline task-by-task. No subagents and no git commits are authorized for this run.

**Goal:** 按 2026-11-08 全面体检报告修复已复现的功能缺陷，并完成两项已有实测基线的性能优化。

**Architecture:** 保留现有系统边界：AI 攻击复用 `CombatAI.AttackMove`；采集修复只调整 `Gatherer` 卸货状态机；存档在现有服务内增加结构校验、时间恢复和备份替换；人口名簿改为变更失效缓存；AI 选址共享单次单位占格快照。随后做独立的小型输入/账务修正。

**Tech Stack:** 团结引擎 2022.3.62t5、C#、NUnit PlayMode、现有 `RegressionTestMenu`。

## Global Constraints

- 不写 `SampleScene` / `MainMenu` / `ModeSelect`，不改预制体，不写真实存档，不提交或合并 git。
- 所有存档测试只写 `Application.temporaryCachePath`。
- 每个功能缺陷先写失败测试，再写最小实现，再跑聚焦测试；最终全量测试日志立即归档。
- 改代码后必须 `request_compile`；绝不同时运行两个 PlayMode 套件。
- 性能优化用本轮已有数据做 before/after 对照，不承诺未复测的收益。
- 基线：252/252。计划新增 12 项，目标 264/264。

## File Structure

- `AIController.cs`：攻击波命令与选址单位快照。
- `Gatherer.cs`：普通卸货点失效处理。
- `SaveGameService.cs`：结构校验、时间恢复、备份替换。
- `Unit.cs`、`UnitLimits.cs`：名簿生命周期失效与跨帧缓存。
- `SelectionManager.cs`、`PauseMenu.cs`、`ProductionBuilding.cs`、`MainMenu.cs`：独立 P2/潜伏修正。
- 测试只在既有对应测试文件中增加用例。

---

### Task 1: AI 攻击波使用 AttackMove

**Files:** `AIController.cs:764-778`；`CommandIntentTests.cs`

- [x] 新增 `AiWave_EnemyKilled_ResumesWaveDestination`：蓝弓手从 `(0,0)` 接收 `SendTo(10,0)`，杀死 `(4,0)` 的 5 HP 敌人后必须继续到目标。
- [x] 新增 `AiWave_PathBlocked_RepathsAround`：行进中阻挡 `(4,0)`，必须绕行到 `(10,0)`。
- [x] 聚焦运行两项，旧代码必须因最终距目标过远而失败。
- [x] 最小实现：

```csharp
if (path != null)
{
    var ai = u.GetComponent<CombatAI>();
    if (ai != null)
    {
        ai.OrderAttackMove(dest.Value);
        mover.SetPath(path, ai.CompleteOrder);
    }
    else mover.SetPath(path);
}
```

- [x] 编译并复跑两项，必须 2/2 通过。

### Task 2: 普通送货失效时保留货物

**Files:** `Gatherer.cs:632-687`；`WorkerBehaviorTests.cs`

- [x] 新增 `Worker_NormalDropoffDestroyedMidRoute_KeepsCargoUntilReplacement`：工人背 5 木前往唯一卸货点，途中销毁；等待旧路径全程后仍须背 5、银行 0；建立替代点后只入账 5 一次。
- [x] 旧实现必须在“唯一基地已毁但银行增加”处失败。
- [x] 目标失效时停止旧移动；`OnReachedDropoff` 在清货物前重新验证存活、完工且 `isDropoff`：

```csharp
if (Combatants.IsGone(dropoff))
{
    if (!ReferenceEquals(dropoff, null)) mover.Stop();
    dropoff = null;
}
```

```csharp
if (ReferenceEquals(dropoff, null) || Combatants.IsGone(dropoff) ||
    dropoff.IsUnderConstruction || !dropoff.isDropoff)
{
    dropoff = null;
    dropoffRetry = 0f;
    phase = Phase.ToDropoff;
    return;
}
```

- [x] 聚焦测试与 `WorkerBehaviorTests` 全类复跑。

### Task 3: 存档结构、时间恢复与备份替换

**Files:** `SaveGameService.cs:55-75,294-506`；`SaveLoadTests.cs`

- [x] 新增 `NullContainers_AreRejectedBeforeWriting`：分别把 buildings/units/ai/bushes 设为 null，写临时文件后 `TryReadFromPath` 必须返回 false 和明确错误。
- [x] 新增 `RestoreNullContainer_ReturnsFalseAndPreservesTimeScale`：直接恢复 null buildings，回调 false、无异常、timeScale 不变。
- [x] 新增 `RewritingExistingSave_KeepsPreviousFileAsBackup`：临时目标连续保存两个不同快照，正式文件是新值，`.bak` 是旧值。
- [x] 旧代码前两项失败；第三项证明新增备份行为。
- [x] `Validate` 对四个必需列表显式拒绝 null。
- [x] `RestoreSnapshot` 在设置 `Time.timeScale=0` 后用 `try/finally` 包住恢复主体，finally 恢复原值。
- [x] 已存在正式档时使用：

```csharp
string backup = path + ".bak";
if (File.Exists(path)) File.Replace(temp, path, backup);
else File.Move(temp, path);
```

- [x] 聚焦运行 SaveLoadTests；确认真实存档 MD5 未变。

### Task 4: UnitLimits 名簿改为变更失效缓存

**Files:** `UnitLimits.cs:24-33,142-192`；`Unit.cs:122-123,168-190`；`UnitLimitTests.cs`

- [x] 新增 `RosterCache_SurvivesFrames_AndDeathInvalidatesImmediately`：首次查询后反射取得该队字典引用；下一帧未变化时必须仍为同一引用；单位死亡后同帧数量必须减少且字典重建。旧实现会在跨帧引用相同处失败。
- [x] 用 dirty 标志替代“每帧必然重建”；`Invalidate` 清缓存并置 dirty。某队重建后，另一队因缓存已清空仍会按需重建。
- [x] `UnitActor.OnEnable/OnDisable` 调用 `UnitLimits.Invalidate()`；`Die` 立即调用，保证尸体延迟期间名簿也即时正确；`RestoreSaveState` 改队伍后失效。
- [x] 复跑 UnitLimitTests、PopulationTests、TowerGarrisonTests、SaveLoadTests。
- [x] 真实 SampleScene 复测稳态 GC，与修复前约 153 KB/帧对照。

### Task 5: AI 选址共享单位占格快照

**Files:** `AIController.cs:312-387`；`PlacementTests.cs`

- [x] 新增 `AiFindSite_UnitOnFirstCandidate_SelectsAnotherSite`：第一次找到位置后，在其占地放 UnitMover；第二次结果必须不同。这是行为回归，不要求旧实现失败。
- [x] `FindSite` 开始时只用一次 `FindObjectsByType<UnitMover>` 构造 `HashSet<Vector3Int>`；`Fits` 使用 `ValidateCells(checkUnits:false)` 后检查该集合。
- [x] 保持地形、坡度、草丛、高度、建筑间距规则不变。
- [x] 聚焦 PlacementTests 和 AIProductionBudgetTests；用同一 House_Red 微基准复测 FindSite，与 17.8–20.0 ms 对照。

### Task 6: 混合选择与结算时间缩放

**Files:** `SelectionManager.cs:407-434`；`PauseMenu.cs:116-128`；`CommandIntentTests.cs` 或 `MovementAndCommandTests.cs`；`PauseMenuTests.cs`

- [x] 新增混合选择测试：工人和士兵选择同一资源点上的 aggro 外敌人，工人必须采集、士兵必须收到该敌人目标。旧实现士兵无目标。
- [x] 资源采集已被接受时，额外对同点敌人调用 `OrderAttack`；纯工人和无敌人路径保持原样。
- [x] 新增 `Close_AfterMatchEnded_KeepsTimeFrozen`；旧实现得到 timeScale=1。
- [x] 关闭面板时仅在未结算时恢复 1：

```csharp
Time.timeScale = open ? 0f :
    (GameState.Instance != null && GameState.Instance.IsOver ? 0f : 1f);
```

### Task 7: AI 取消退款与新局 pendingLoad

**Files:** `AIController.cs`、`ProductionBuilding.cs:176-184`、`MainMenu.cs:121-126`；对应测试文件

- [x] 新增红方工厂已付款订单取消测试：AI 银行恢复，玩家银行不变；旧实现错误退给玩家。
- [x] `AIController.Refund(ResourceCost)` 按三种资源走现有 `Add`；`CancelLast` 按 `building.team` 选择玩家、AI 或无主银行，不向错误阵营退款。
- [x] 新增 `StartGame_ClearsPendingLoad`：先 `RequestLoad`，再 `StartGame`，`ConsumePendingLoad` 必须返回 false。
- [x] `MainMenu.StartGame` 在离开前调用 `SaveGameService.CancelPendingLoad()`。

### Task 8: 全量验证与交付

- [x] 编译 0 错误；最终全量目标 264/264，日志立即归档。
- [x] 复核场景/真实存档 MD5；`git diff` 只含本轮授权源码、测试、计划/报告。
- [x] 更新 `.superpowers/sdd/full-audit/RESUME.md` 与修复报告，区分已实测修复、未做场景 UI 项、仍需真人验收项。
- [x] 不提交；由用户决定是否合并。
