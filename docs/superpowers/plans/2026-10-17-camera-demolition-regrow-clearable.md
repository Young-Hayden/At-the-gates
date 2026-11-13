# 镜头、拆卸、金矿重生与工人清草实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 改为纯鼠标视角，加入安全拆卸、60 秒金矿重生和工人手动清草，并保持既有 RTS 功能不回归。

**Architecture:** `SelectionManager` 负责 Delete 拆卸和右键清草，`Building` 负责拆除与释放占地，`ResourceNode` 沿用现有重生逻辑，`ClearableBush` 只提供清草目标，不参与资源经济。每项先跑失败探针再修改。

**Tech Stack:** 团结引擎 2022.3.62t5、C#、UGUI、Tilemap/MapGrid、Codely Bridge MCP。

## Global Constraints

- 遵循 `docs/superpowers/specs/2026-10-17-camera-demolition-regrow-clearable-design.md`。
- 不修改主基地完工解锁、民房产工人、固定矿不可建造、驻守、近战伤害和小地图逻辑。
- 不把草丛转成 ResourceNode，不给清草资源，不自动清草。
- 项目不是 Git 仓库；备份放 `Backups/2026-10-17-input-economy-clearing`。
- 探针放 `Temp`，完成后删除；测试期间修改的 regrowSeconds 仅用于临时实例，正式值固定 60 秒。

## 文件结构

- 修改：`Assets/Scripts/RTSCamera.cs`、`Building.cs`、`SelectionManager.cs`、`Gatherer.cs`。
- 新建：`Assets/Scripts/ClearableBush.cs`。
- 修改：Bush1～Bush4 prefab、`GoldMine_Active.prefab`、`SampleScene.scene` 两个矿点。
- 修改后复核：`BuildingPlacer.cs`、`AIController.cs`、`HUD.cs`、`GarnishRules.cs` 保持兼容。

---

### Task 1: 备份与失败基线

**Files:**
- Create: `Backups/2026-10-17-input-economy-clearing/`
- Test: 各项失败探针

**Interfaces:**
- Produces: 修改前文件与失败证据。

- [x] **Step 1: 备份目标文件**

备份 `RTSCamera.cs`、`Building.cs`、`SelectionManager.cs`、`Gatherer.cs`、Bush prefab、GoldMine prefab 和 `SampleScene.scene` 及 meta。

- [x] **Step 2: 记录失败证据**

预期当前：RTSCamera 仍读取 WASD/方向键；没有拆卸入口；GoldMine regrowSeconds=0；没有 ClearableBush 类型和右键清草路径。

---

### Task 2: 纯鼠标视角

**Files:**
- Modify: `Assets/Scripts/RTSCamera.cs`
- Test: `Temp/CameraProbe.cs`

**Interfaces:**
- Consumes: 既有 edgePan、middleDragPan、缩放、边界钳制和 GameSettings。

- [x] **Step 1: 写失败探针**

静态检查 RTSCamera 不再包含 `KeyCode.W/A/S/D` 和方向键平移，且仍包含鼠标边缘平移、中键拖动和滚轮缩放。修复前该检查失败。

- [x] **Step 2: 删除键盘平移代码**

只删除 WASD/方向键的 `Input.GetKey` 与 dir 合成，保留 `dir` 供边缘平移复用。不得改 `panSpeed`、`edgeSize`、中键拖动、缩放或 ClampAxis。

- [x] **Step 3: 验证鼠标操作**

验证鼠标靠近左/右/上/下边缘分别产生正确移动，鼠标在屏幕内移动不触发；WASD 静态引用不存在；中键拖动、滚轮和地图钳制不回归。

---

### Task 3: 建筑拆卸

**Files:**
- Modify: `Assets/Scripts/Building.cs`
- Modify: `Assets/Scripts/SelectionManager.cs`
- Test: `Temp/DemolitionProbe.cs`

**Interfaces:**
- Produces: `Building.CanDemolish` 与 `Building.Demolish()`；SelectionManager 的 Delete 拆除入口。

- [x] **Step 1: 写失败探针**

建立己方 House 和一个排队中的 Pawn，记录库存与队列。预期修复前没有 CanDemolish 和 Delete 拆除入口。

- [x] **Step 2: 实现拆除与退款**

`Building.CanDemolish` 仅当 `team != Team.Neutral && !isCommandCenter` 为 true。`Demolish()` 若不可拆则直接返回，否则 `Unregister()` 后销毁对象。

Delete 处理仅在 selectedBuilding 属于 GameState.playerTeam 时执行：先对 ProductionBuilding.Queue 逐项 `CancelLast()` 退款，再按 gold/wood/meat 各 50% 向下取整返还建筑成本，清空 selectedBuilding 并调用 Demolish。

- [x] **Step 3: 验证退款与重建**

预期：House 50 金返还 25，Pawn 40 金全额返还；主基地和 Mine 不可拆；被拆格可以重新放置并完成建筑。检查幽灵、队列和 selectedBuilding 引用无残留。

---

### Task 4: 金矿 60 秒重生

**Files:**
- Modify: `Assets/Prefabs/Resources/GoldMine_Active.prefab`
- Modify: `SampleScene.scene` 的 Mine_Gold_1 与 Mine_Gold_Red
- Test: `Temp/GoldRegrowProbe.cs`

**Interfaces:**
- Consumes: 既有 ResourceNode.Harvest/Update/RefreshVisual 和 Gatherer 自动派工。

- [x] **Step 1: 写失败探针**

读取 prefab 和两个场景矿点，预期 regrowSeconds 均为 60；修复前为 0。

- [x] **Step 2: 设置 60 秒重生**

仅修改 regrowSeconds，保留 amount/maxAmount=1500、harvestSeconds=1.2、perTrip=20、spriteFull/Half/Empty 和 blockingBuilding 现状。

- [x] **Step 3: 用临时短周期验证重生**

临时把测试实例 regrowSeconds 调为 0.5 秒，Harvest 到 0，验证 Depleted 后等待恢复 amount=maxAmount、regrowing=false、外观恢复、Gatherer 能重新采集。正式配置仍为 60 秒。

---

### Task 5: 工人手动清草

**Files:**
- Create: `Assets/Scripts/ClearableBush.cs`
- Modify: `Assets/Scripts/Gatherer.cs`
- Modify: `Assets/Scripts/SelectionManager.cs`
- Modify: Bush1～Bush4 prefab 与当前场景实例
- Test: `Temp/ClearBushProbe.cs`

**Interfaces:**
- Produces: `ClearableBush.All`、`Gatherer.AssignClear(ClearableBush)`、SelectionManager 的右键清草路径。

- [x] **Step 1: 写失败探针**

预期当前没有 ClearableBush 类型、AssignClear 和右键清草入口。

- [x] **Step 2: 实现 ClearableBush 和 Gatherer 清草状态**

`ClearableBush` 维护 All、clearSeconds=1 和 visual；OnDisable 移除并调用 GarnishRules.Invalidate。

Gatherer 增加 ToClear/Clearing 阶段：AssignClear 设置目标，GoTo 到草丛，距离检查后用 1 秒清除并销毁目标。携带资源时先 ToDropoff，卸货后回到 ToClear。Assign/StopWork 必须清除 clearTarget 和 clearTimer。

- [x] **Step 3: 接入 SelectionManager 与 prefab**

在 IssueMove 的资源与敌人检查之间加入 FindClearableBushAt/AssignClearers：只有选中包含 Gatherer 时才消费该右键，其他选择仍按移动/攻击处理。给 Bush1～Bush4 添加组件，确认场景实例同步，不新增 ResourceNode 或 Collider。

- [x] **Step 4: 验证清草与建造**

预期：工人能走到并清除目标；库存不因清草增加；GarnishRules.IsBushCell 变为 false；完成主基地后玩家和 AI 均可在该格建造，其他草丛仍在，普通采集继续工作。

---

### Task 6: 完整回归与清理

**Files:**
- Create: `Backups/2026-10-17-input-economy-clearing/verification.txt`

**Interfaces:**
- Consumes: Task 2～5 的全部功能及之前所有验收结果。

- [x] **Step 1: 运行完整 Demo**

从 MainMenu 经 ModeSelect 进入对局，验证主基地解锁、民房产工人、固定矿采集与重生、鼠标视角、拆除与退款、工人清草、AI 建造进攻、驻守、小地图和事项 1 恢复路径。

- [x] **Step 2: 独立审查与最终清理**

独立只读审查本范围，Critical/Important 修复后重跑。确认编译无 C# 错误、无本次新增异常、场景无测试对象或临时文件，保存 verification.txt。
