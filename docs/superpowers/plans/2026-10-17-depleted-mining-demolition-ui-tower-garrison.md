# 采空停工、UI 拆除与塔楼驻防实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 采空后立即停工，拆除入口改为 UI 按钮，并实现每塔最多一名弓箭手的驻防与退出。

**Architecture:** `Gatherer.StepHarvest` 增加 depleted 检查；`HUD` 和 `SelectionManager` 共用现有拆除逻辑；`TowerGarrison` 独立管理弓箭手引用、驻防攻击和退出，复用 Archer 的属性与投射物。

**Tech Stack:** 团结引擎 2022.3.62t5、C#、UGUI、MapGrid、Codely Bridge MCP。

## Global Constraints

- 遵循 `docs/superpowers/specs/2026-10-17-depleted-mining-demolition-ui-tower-garrison-design.md`。
- 不改变 60 秒重生、主基地完工解锁、民房产工人、清草、驻守和现有近战/远程战斗规则。
- 不让驻防弓箭手重复计入 UnitActor.All，不让退出或塔毁复制单位。
- 项目不是 Git 仓库；备份放 `Backups/2026-10-17-demolition-ui-tower-garrison`。
- 探针放 `Temp`，完成后删除。

## 文件结构

- 修改：`Assets/Scripts/Gatherer.cs`、`HUD.cs`、`SelectionManager.cs`。
- 新建：`Assets/Scripts/TowerGarrison.cs`。
- 修改：`Assets/Editor/BuildHUD.cs`、`Assets/Editor/BuildMinimap.cs`。
- 修改：Tower_Blue/Red prefab、`SampleScene.scene` 的 SelectionPanel/MinimapPanel。
- 新建后删除：`Temp` 下各项探针。

---

### Task 1: 备份与采空立即停工

**Files:**
- Create: `Backups/2026-10-17-demolition-ui-tower-garrison/`
- Modify: `Assets/Scripts/Gatherer.cs`
- Test: `Temp/DepletedHarvestProbe.cs`

**Interfaces:**
- Produces: 采空/销毁时立即结束采集，不影响后续重生。

- [x] **Step 1: 备份目标文件并记录失败基线**

备份 Gatherer、HUD、SelectionManager、BuildHUD、BuildMinimap、Tower prefab 和 SampleScene。记录当前 StepHarvest 只检查 node==null。

- [x] **Step 2: 写失败探针**

让工人在采集中把矿采到 0，预期修复前仍进入一次完整的采集动作间隔后才结束。

- [x] **Step 3: 增加 depleted 检查**

将 `StepHarvest` 第一行改为 `if (node == null || node.Depleted) { FinishTrip(true); return; }`，其余采集与重生逻辑不变。

- [x] **Step 4: 验证停工与复工**

验证采空后立即停止当前动作，不新增库存；重生后仍会自动接单。

---

### Task 2: UI 拆除按钮

**Files:**
- Modify: `Assets/Scripts/HUD.cs`
- Modify: `Assets/Scripts/SelectionManager.cs`
- Modify: `Assets/Editor/BuildHUD.cs`
- Modify: `Assets/Editor/BuildMinimap.cs`
- Modify: `SampleScene.scene`
- Test: `Temp/DemolitionUiProbe.cs`

**Interfaces:**
- Consumes: 现有 SelectionManager.DemolishSelected、Building.CanDemolish、Economy.Refund。
- Produces: HUD.demolishButton 及只在可拆建筑选中时显示的 UI 入口。

- [x] **Step 1: 写失败探针**

检查当前没有 `HUD.demolishButton` 和场景中的 SelectionPanel/Demolish。

- [x] **Step 2: 接入 HUD 按钮行为**

HUD 增加 `public Button demolishButton`，Start 绑定 `SelectionManager.DemolishSelected`。RefreshSelection 仅在 selectedBuilding 属于玩家且 CanDemolish 时显示按钮；其他选择或无选择时隐藏。

- [x] **Step 3: 添加场景按钮并调整布局**

在 SelectionPanel 底部创建 Demolish 按钮，显示“拆除”。SelectionPanel 高度从 150 调至 190；MinimapPanel 的 Y 从 -178 调至 -218。同步修改 BuildHUD 和 BuildMinimap，防止以后重建复原。

- [x] **Step 4: 验证 UI 拆除与退款**

点击按钮后验证退款、队列取消、占地释放与原地重建；主基地和中立矿按钮不出现。检查按钮与小地图不重叠，Delete 仍可工作。

---

### Task 3: 塔楼单弓箭手驻防

**Files:**
- Create: `Assets/Scripts/TowerGarrison.cs`
- Modify: `Assets/Scripts/SelectionManager.cs`
- Modify: Tower_Blue.prefab、Tower_Red.prefab
- Test: `Temp/TowerGarrisonProbe.cs`

**Interfaces:**
- Produces: `TowerGarrison.HasArcher`、`Garrison(UnitActor)`、`Ungarrison()` 和驻防攻击循环。
- Consumes: Archer 的 UnitActor 属性、Projectile.Spawn、Combatants.FindNearestEnemy、MapGrid 出生格。

- [x] **Step 1: 写失败探针**

预期当前没有 TowerGarrison，塔不会攻击，右键塔没有驻防入口。

- [x] **Step 2: 实现驻防数据与退出**

TowerGarrison 保存 archerPrefab 与攻击属性。Garrison 仅接受同阵营、远程 UnitActor，且 HasArcher=false 时成功；成功后停用原弓箭手并记录其 prefab。Ungarrison 在附近合法格重新实例化且只生成一名。塔被拆除/摧毁时也退出，但场景卸载时不生成。

- [x] **Step 3: 实现驻防攻击**

Update 仅在 HasArcher 时按 cooldown 查找本塔阵营敌人，并复用 archer 的 attackDamage、attackRange、projectile、projectileSpeed 和队伍颜色生成投射物。无目标、无驻防或目标已销毁时不攻击。

- [x] **Step 4: 接入 SelectionManager**

IssueMove 在资源/清草和敌人之间检测 TowerGarrison：有驻防时右键退出；无驻防时只有选中恰好一名同阵营 Archer 才能进入，其他兵种或多选不消费命令。为 Tower_Blue/Red 添加组件。

- [x] **Step 5: 验证上限、攻击与退出**

第二名弓箭手和其他兵种被拒绝；塔有驻防时能在范围内发射投射物，无驻防时不发射；退出和拆塔都只返回一名弓箭手，UnitActor.All 无重复或残留。

---

### Task 4: 完整回归与清理

**Files:**
- Create: `Backups/2026-10-17-demolition-ui-tower-garrison/verification.txt`

**Interfaces:**
- Consumes: Task 1～3 的全部实现及之前所有验收结果。

- [ ] **Step 1: 完整 Demo**

从 MainMenu 经 ModeSelect 进入对局，验证采空停工与重生、UI 拆除、塔楼驻防/退出、主基地解锁、民房产工人、清草、AI 建造进攻、驻守、小地图和事项 1 恢复路径。

- [x] **Step 2: 独立审查与最终清理**

独立只读审查本范围，Critical/Important 修复后重跑。确认编译无 C# 错误、无本次新增异常、场景无测试对象或临时文件，保存 verification.txt。
