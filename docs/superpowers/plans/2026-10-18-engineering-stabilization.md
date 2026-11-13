# 工程稳定化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复建造、动态寻路、手动指令和已确认场景占地问题，并建立只读 Project Doctor 与 Unity Test Framework 自动回归。

**Architecture:** 保留现有组件边界，以共享占地校验、显式幽灵清理和 `UnitMover` 路径格校验做定点修复。测试使用 Runtime asmdef、独立 PlayMode asmdef 和隔离场景；Doctor 只读，场景清理由单独菜单显式执行。

**Tech Stack:** Tuanjie 2022.3.62t5、Unity Test Framework 1.1.33、NUnit、C#、Tilemap、TestRunnerApi。

## Global Constraints

- 只实施规格 `docs/superpowers/specs/2026-10-18-engineering-stabilization-design.md` 的五项，不增加玩法。
- 场景清理以当前根格为准；非法目标不强行移动。
- 普通移动后工人保留货物并待命；僧侣只治疗射程内伤兵。
- `RegressionTests.scene` 不加入 Build Settings，不触碰正式地图测试数据。
- Doctor 不自动修复、不保存资产、不重新导入。
- 项目不是 Git 仓库：不初始化仓库，不执行 commit；每个任务以编译或测试结果为检查点。
- 修改编辑器脚本后必须真正请求并等待编译，再运行菜单或测试。

---

### Task 1: 测试程序集、隔离场景与运行入口

**Files:**
- Create: `Assets/Scripts/AtTheGates.Runtime.asmdef`
- Create: `Assets/Tests/PlayMode/AtTheGates.PlayModeTests.asmdef`
- Create: `Assets/Tests/PlayMode/RegressionFixture.cs`
- Create: `Assets/Editor/RegressionTestMenu.cs`
- Create: `Assets/Tests/Scenes/RegressionTests.scene`

**Interfaces:**
- Produces: `RegressionFixture.Grid`, `Placer`, `Economy`, `Selection`, `PaintRect(...)`, `ClearFloor()`, `LoadPrefab<T>(path)`；后续测试类继承 `RegressionFixture`。
- Produces: 菜单 `Tools/Regression/Create Isolated Scene` 和 `Tools/Regression/Run PlayMode Tests`。

- [ ] **Step 1: 写 Runtime asmdef**

```json
{
  "name": "AtTheGates.Runtime",
  "references": ["UnityEngine.UI"],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] **Step 2: 写 PlayMode 测试 asmdef**

```json
{
  "name": "AtTheGates.PlayModeTests",
  "references": ["AtTheGates.Runtime", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": ["nunit.framework.dll"],
  "autoReferenced": false,
  "defineConstraints": ["UNITY_INCLUDE_TESTS"],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] **Step 3: 写隔离 fixture**

`RegressionFixture` 使用 `EditorSceneManager.LoadSceneInPlayMode("Assets/Tests/Scenes/RegressionTests.scene", ...)`，等待一帧后缓存场景管理器。运行时创建 `Tile`，只修改测试 Tilemap：

```csharp
protected void PaintRect(int xMin, int yMin, int width, int height)
{
    var tile = ScriptableObject.CreateInstance<Tile>();
    for (int x = xMin; x < xMin + width; x++)
    for (int y = yMin; y < yMin + height; y++)
        Grid.ground0.SetTile(new Vector3Int(x, y, 0), tile);
    Grid.Rescan();
}
```

`ClearFloor()` 对测试边界调用 `SetTile(..., null)` 后 `Rescan()`；`UnityTearDown` 恢复 `Time.timeScale=1`，不保存场景。

- [ ] **Step 4: 写测试场景生成与 TestRunnerApi 菜单**

生成一个 Grid、六个 MapGrid 所需 Tilemap、MapGrid、Economy、GameState、BuildingPlacer、SelectionManager 和正交 Main Camera。`Run PlayMode Tests` 用 `TestRunnerApi.Execute(new ExecutionSettings { filters = ... PlayMode ... })`，回调中输出逐测试及汇总，不写弹窗。

- [ ] **Step 5: 编译并生成场景**

Run: MCP `request_compile` → `wait_for_compile` → `get_compilation_summary`，再执行 `Tools/Regression/Create Isolated Scene`。
Expected: 编译零错误；场景存在于 `Assets/Tests/Scenes/RegressionTests.scene`；Build Settings 仍只有原三场景。

---

### Task 2: 建造稳定测试与实现

**Files:**
- Create: `Assets/Scripts/PlacementRules.cs`
- Create: `Assets/Tests/PlayMode/PlacementTests.cs`
- Modify: `Assets/Scripts/BuildingPlacer.cs`
- Modify: `Assets/Scripts/Building.cs`
- Modify: `Assets/Scripts/MapGrid.cs`
- Modify: `Assets/Scripts/AIController.cs`
- Modify: `Assets/Scripts/SelectionManager.cs`

**Interfaces:**
- Produces: `PlacementRules.ValidateCells(MapGrid, IReadOnlyList<Vector3Int>, bool checkUnits, out string reason): bool`。
- Produces: `Building.MakeGhost(): void`、`BuildingPlacer.LastCancelFrame: int`。
- Changes: `BuildingPlacer.Place(): bool`（私有提交入口），`TryPlaceAt` 返回实际提交结果。

- [ ] **Step 1: 先写重复放置与幽灵回归测试**

测试通过反射调用私有 `Place()`，覆盖鼠标提交路径而非只测已经强制复验的 `TryPlaceAt`：

```csharp
Assert.IsTrue(Placer.TryPlaceAt(new Vector3(0.5f, 0.5f, 0)));
int count = Building.All.Count;
int gold = Economy.gold;
var place = typeof(BuildingPlacer).GetMethod("Place",
    BindingFlags.Instance | BindingFlags.NonPublic);
place.Invoke(Placer, null);
Assert.AreEqual(count, Building.All.Count);
Assert.AreEqual(gold, Economy.gold);
```

幽灵测试在幽灵源位置预放真实建筑，记录周围 `IsBlockedByBuilding` 快照；`Begin` 和 `Cancel` 后均断言真实建筑占地未消失，且自由格没有被幽灵占用。

- [ ] **Step 2: 运行并确认旧实现失败**

Run: `Tools/Regression/Run PlayMode Tests`，过滤 `PlacementTests`。
Expected: 重复放置增加建筑/扣款；幽灵创建或取消改变 blocker 快照。

- [ ] **Step 3: 将动态 blocker 改为引用计数**

把 `dynamicBlockers` 改为 `Dictionary<Vector3Int,int>`。`AddBuildingFootprint` 对每格计数加一，仅首次从 0→1 时重标连通区；`RemoveBuildingFootprint` 减一，仅 1→0 时移除并重标。`WalkableRaw`、`IsBlockedByBuilding` 和 Gizmos 分别使用 `ContainsKey` 和 `Keys`。这样幽灵或重叠实例的注销不会释放别人的占地。

- [ ] **Step 4: 加共享校验和显式幽灵清理**

`PlacementRules.ValidateCells` 依次检查空网格、地形、坡道、草丛、建筑重叠、高度一致和可选单位占位。`Building.MakeGhost()` 设置 `isGhost=true`、`blocksMovement=false`、`isDropoff=false`，从 `Building.All` 移除并调用 `Unregister()`；`Begin` 在实例化后立即调用它。

- [ ] **Step 5: 提交前复验并统一 AI/输入规则**

`Place(): bool` 开头重新 `Evaluate`，失败则更新拒绝原因并返回 `false`；扣款失败返回 `false`；成功后实例化、`Recompute`、令 `lastCheckedCell=int.MinValue` 并返回 `true`。`TryPlaceAt` 直接返回 `Place()`。AI `Fits` 使用共享校验，包含单位占位。`Cancel()` 记录 `LastCancelFrame=Time.frameCount`，`SelectionManager.Update` 在 `IsPlacing || LastCancelFrame==Time.frameCount` 时跳过世界输入。

- [ ] **Step 6: 运行建造测试**

Expected: 第二次同格提交不增加建筑且不扣款；幽灵全生命周期 blocker 快照不变；已有建筑占地保持；编译零错误。

---

### Task 3: 动态路径、不可达采集和手动指令测试与实现

**Files:**
- Create: `Assets/Tests/PlayMode/MovementAndCommandTests.cs`
- Modify: `Assets/Scripts/UnitMover.cs`
- Modify: `Assets/Scripts/Gatherer.cs`
- Modify: `Assets/Scripts/Healer.cs`
- Modify: `Assets/Scripts/SelectionManager.cs`
- Modify: `Assets/Scripts/HUD.cs`

**Interfaces:**
- Produces: `Gatherer.IsManuallyIdle: bool`、`Gatherer.PathAttempts: int`、`Gatherer.StopWork(): void`。
- Produces: `Healer.OrderMove(): void`。
- Changes: `SelectionManager.IssueMove(Vector3): void` 改为 public，供输入和测试共用。

- [ ] **Step 1: 写动态阻挡失败测试**

在一格宽通道创建 `UnitMover`，路径为 `(0,0)→(1,0)→(2,0)`，注册到达回调；移动开始后给 `(1,0)` 添加 blocker。逐帧断言单位没有进入 `(1,0)`、`Moving=false`、回调未触发。

- [ ] **Step 2: 写不可达、待命和死亡选择测试**

不可达测试把工人和资源放在不同连通区，设置较短重试间隔，断言 `PathAttempts` 在间隔内不增长；连通后等待采集发生。命令测试通过 `Selection.IssueMove` 下令，经过原自动派工时间后仍断言工人 `IsManuallyIdle=true`、`TargetNode=null`；僧侣只选择 `healRange` 内病人。死亡测试在 `corpseSeconds` 内击杀已选单位，下一帧断言选择集不含该单位。

- [ ] **Step 3: 运行并确认旧实现失败**

Expected: 单位进入新 blocker；不可达采集持续增加寻路次数；自动任务覆盖手动移动；死亡单位仍被选中。

- [ ] **Step 4: 实现路径格校验**

`UnitMover` 新增 `List<Vector3Int> pathCells`，与 `pts` 同步。每次移动前若 `!MapGrid.Instance.Walkable(pathCells[i])`，调用 `Stop()` 并返回；`Stop()` 继续清除路径和 `onArrived`，不得调用 `FireArrived()`。

- [ ] **Step 5: 实现采集退避和手动待命**

`Gatherer` 增加 `unreachableRetrySeconds`、`nodeRetry`、`manualIdle` 和只读计数。`GoTo` 每次调用增加 `PathAttempts`。`StepToNode` 失败时只设置 `nodeRetry`，保留节点和阶段；倒计时期间不再寻路。自动派工使用同区域且存在接近格的过滤。

`StopWork()` 清除 `node/clearTarget/dropoff`、停止 mover、保留 `carrying`、设 `phase=Idle` 和 `manualIdle=true`。`Assign`/`AssignClear` 设 `manualIdle=false`；持有货物时先进入 `ToDropoff`，卸货后继续新任务。`TryAutoAssign` 在手动待命或持货时返回。

- [ ] **Step 6: 实现僧侣原地治疗和选择清理**

`Healer.OrderMove()` 清空 patient、记录原地状态并停止 mover。原地状态的 `FindWoundedAlly` 搜索范围取 `healRange`，不执行 `Chase`。普通移动时 `SelectionManager` 调用 `Gatherer.StopWork`、`Healer.OrderMove`，再清除 CombatAI 命令并 `SetPath`。选择和 HUD 过滤均跳过 `UnitActor!=null && !IsAlive`。

- [ ] **Step 7: 运行本组测试**

Expected: 所有动态路径和命令用例通过；无每帧寻路；无伪到达；死亡对象不再接收命令。

---

### Task 4: Project Doctor

**Files:**
- Create: `Assets/Editor/ProjectDoctor.cs`
- Output: `Logs/ProjectDoctor.txt`

**Interfaces:**
- Produces: `ProjectDoctor.RunAudit(): Report`，`Report.Issues`、`Report.ErrorCount`、`Report.WarningCount`；菜单 `Tools/Project Doctor/Run Audit`。

- [ ] **Step 1: 实现只读场景与资产审计**

对活动场景检查 blocker 根格是否属于 `Building.Cells`、重复占地、`TerrainWalkable=false`、坡道占地和重复管理器。对预制体检查 Missing Script 和断裂对象引用；对世界 TextureImporter 检查 PPU=64、FilterMode.Point；对 Animator 的普通及对象引用曲线按 Animator 相对路径检查目标 Transform 和组件。

- [ ] **Step 2: 写稳定报告**

报告结构：

```text
Project Doctor
Scene: Assets/Scenes/SampleScene.scene
Errors: N  Warnings: N
[Footprint] object path | root cell | computed cells | reason
[Duplicate] cell | owner A | owner B
[MissingRef] asset | component | property
[Texture] asset | actual PPU/filter
[Animation] prefab | animator | clip | binding path
```

写入 `Logs/ProjectDoctor.txt`，控制台只输出各类别计数和完整报告路径；不显示模态窗口。

- [ ] **Step 3: 编译并运行基线**

Expected: 清理前报告能列出已确认的 19 个根格问题和 2 个无地面占地；不得出现误报所有可空可选引用；场景 `dirty=false`。

---

### Task 5: 场景占地清理与复核

**Files:**
- Create: `Assets/Editor/SceneFootprintCleanup.cs`
- Modify: `Assets/Scenes/SampleScene.scene`
- Backup: `Backups/SampleScene-before-footprint-cleanup-<timestamp>.scene`

**Interfaces:**
- Consumes: `Building.Prepare()/Recompute()`、场景本地 `MapGrid`。
- Produces: 菜单 `Tools/Project Doctor/Align Scene Roots To Current Cells`。

- [ ] **Step 1: 实现带备份的定向清理**

只处理 `SampleScene` 中 `blocksMovement && !Cells.Contains(grid.WorldToCell(position))` 的对象。当前目标均为 1×1；其他尺寸跳过并报告。先在 `Backups/` 复制场景，再按 instanceID 排序处理。目标格必须 `TerrainWalkable && !IsRamp`，且不被其他建筑占用；合法时设置：

```csharp
building.transform.position = grid.CellCenter(rootCell) - new Vector3(0f, 0.5f, 0f);
building.Recompute();
```

非法时保留原对象并输出原因。只在实际移动后保存 `SampleScene`。

- [ ] **Step 2: 执行清理并再次 Doctor**

Run: 先记录 Doctor 基线，再执行清理菜单，再运行 Doctor。
Expected: 19 个目标均被处理或有明确非法原因；两个原无地面占地不再存在；无新增重复占地；其他对象未被重排。

---

### Task 6: 全量回归与正式场景冒烟

**Files:**
- Verify only: `Assets/Scripts/*`、`Assets/Tests/PlayMode/*`、`Assets/Scenes/SampleScene.scene`
- Output: `Logs/RegressionTests.txt`、`Logs/ProjectDoctor.txt`

- [ ] **Step 1: 强制完整编译**

Run: `request_compile` → `wait_for_compile` → `get_compilation_summary` → console error scan。
Expected: 零编译错误，无旧程序集假成功。

- [ ] **Step 2: 运行全部 PlayMode 测试**

覆盖重复建造、幽灵 blocker、动态路径、不可达采集、手动命令、死亡选择、场景重载静态清理。重载测试使用 `EditorSceneManager.LoadSceneInPlayMode` 重载隔离场景，断言旧对象、blocker 和单例不残留。
Expected: 全部通过，失败数 0；结束恢复 `Time.timeScale=1`。

- [ ] **Step 3: 正式场景只读冒烟**

重新打开 `SampleScene`，确认单例数量、Grid 换算、关键预制体引用和场景 dirty 状态；运行一次 Doctor。
Expected: 正式场景无测试对象、无新增编译错误、无本次目标类别未解释问题。

- [ ] **Step 4: 记录最终交付**

汇总修改文件、测试数量与结果、Doctor 前后计数、场景备份路径，以及任何被合法跳过的对象。不得声称未执行的压力测试、连续正式对局或其他玩法已经验证。
