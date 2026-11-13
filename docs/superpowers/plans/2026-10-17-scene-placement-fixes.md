# 场景落点与遮挡修复实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复已实证的草丛/建筑重叠、装饰/树同占地和水岩重复，并阻止以后再次把建筑放到草丛或重复放置水岩。

**Architecture:** 用一个幂等的 `ScenePlacementFixes` 做最小搬迁与重复清理，`GarnishRules` 提供草丛格查询供玩家和 AI 建造共用，`Garnish.Run` 增加水岩占位规则。修复后重跑同一扫描器和完整 Demo。

**Tech Stack:** 团结引擎 2022.3.62t5、C#、Tilemap/MapGrid、Codely Bridge MCP。

## Global Constraints

- 遵循总规格 `docs/superpowers/specs/2026-10-17-main-menu-mode-select-deep-audit-design.md`。
- 只修已证实问题，不全量重撒地图、树木或装饰。
- 不把水岩固定 order 6/7、负底部阴影或大透明包围盒重叠误判为缺陷。
- 不添加 Rigidbody 或新的物理碰撞，不改变草丛可穿行设计。
- 项目不是 Git 仓库；备份放 `Backups/2026-10-17-scene-fixes`。
- 探针放 `Temp`，完成后删除。

## 已确认问题

- `Bush2 (35.5,6)` 与红矿同格 `(35,6)`。
- `Deco_01 (-35.5,-9)` 与 `Tree5 (-35.5,-8.35)` 同占地 `(-36,-9)`。
- 7 个额外水岩与另一水岩同坐标。
- `BuildingPlacer` 和 `AIController.Fits` 没有草丛格检查，问题可复发。

---

### Task 1: 备份与修复前失败基线

**Files:**
- Create: `Backups/2026-10-17-scene-fixes/`
- Read: `Temp/SceneAudit.cs` 的当前结果

**Interfaces:**
- Produces: 修改前场景、生成器和两个建造入口的备份。

- [x] **Step 1: 备份目标文件**

复制 `SampleScene.scene`、`Garnish.cs`、`BuildingPlacer.cs`、`AIController.cs` 及 meta；不覆盖已有备份。

- [x] **Step 2: 固化失败探针**

复用 `Temp/SceneAudit.cs`，预期当前：`footprintOverlapCells=1`、`bushBad=1`、`garnishDuplicate=7`，其余 terrain/resource 项为 0。另验证红矿格和两个建造入口都能接受草丛格，作为防复发失败证据。

---

### Task 2: 最小搬迁、去重与生成器防复发

**Files:**
- Create: `Assets/Editor/ScenePlacementFixes.cs`
- Modify: `Assets/Editor/Garnish.cs`
- Modify: `Assets/Scenes/SampleScene.scene`

**Interfaces:**
- Produces: `ScenePlacementFixes.Apply()`，可重复运行且不会重复移动/删除对象。

- [x] **Step 1: 实现合法格搜索与两处搬迁**

`ScenePlacementFixes.Apply()` 使用全部 Building.Cells 作为阻挡，并排除已有草丛坐标、坡地和不可走地形：

- 将红矿格 Bush2 移到 4 格内最近的合法地面格，位置采用 `CellCenter(c) - (0,0.5,0)`。
- 将 Deco_01 移到 4 格内最近、不属于任何建筑占地和装饰占位的合法格，并调用 `Building.Recompute()` 更新其占地。
- 搜索必须包含原格，若原格已合法则不移动，保证幂等。

- [x] **Step 2: 清理 7 个额外水岩**

按 Garnish 根节点直接子物体的世界坐标分组，每组保留第一个，其余删除。预期删除总数=7，不改变其他 Bush、Cloud、SeaRock/WaterRock 的位置。

- [x] **Step 3: 修水岩生成规则**

在 `Garnish.Run` 的水岩循环中，放置前跳过 `occupied.Contains(c)`，成功放置后立即 `occupied.Add(c)`。保持 Bush 现有占位逻辑不变。

- [x] **Step 4: 保存并重跑静态扫描**

预期：`footprintOverlapCells=0`、`bushBad=0`、`garnishDuplicate=0`，且 `waterRockBad=0`、`resourceSealed=0`、地面与 YSort 项不回归。再次 Apply 不重复删除或移动。

---

### Task 3: 建造入口防止建筑落进草丛

**Files:**
- Create: `Assets/Scripts/GarnishRules.cs`
- Modify: `Assets/Scripts/BuildingPlacer.cs`
- Modify: `Assets/Scripts/AIController.cs`
- Test: `Temp/BuildOnBushProbe.cs`

**Interfaces:**
- Produces: `GarnishRules.IsBushCell(Vector3Int cell) : bool`、`GarnishRules.Invalidate() : void`。

- [x] **Step 1: 写失败探针**

在红矿修复前的草丛格调用 `GarnishRules.IsBushCell` 应为 true；验证玩家 `BuildingPlacer.TryPlaceAt` 和 AI `Fits` 修复前都允许该格。

- [x] **Step 2: 实现共享草丛格查询**

`GarnishRules` 首次查询时从 `GameObject.Find("Garnish")` 的直接子物体收集名称以 Bush 开头的格子；`Invalidate()` 清空缓存。`ScenePlacementFixes.Apply()` 完成搬迁/删除后必须调用它。

- [x] **Step 3: 接入玩家与 AI 放置检查**

`BuildingPlacer.Evaluate` 在坡地/建筑占地检查附近增加：`GarnishRules.IsBushCell(c)` 时拒绝并返回“草丛中不能建造”。`AIController.Fits` 同步拒绝，其他地形、层高、间距和建筑规则不变。

- [x] **Step 4: 验证拒绝与正常建造**

预期：所有建造入口都拒绝草丛格；相邻合法格仍能放置并完成建筑；原有 Castle/House/Barracks 生产与 AI 建造序列不回归。修复后红矿格也不再被草丛覆盖。

---

### Task 4: 视觉、玩法与完整 Demo 回归

**Files:**
- Create: `Backups/2026-10-17-scene-fixes/verification.txt`
- Test: 修复点截图和连续对局

**Interfaces:**
- Consumes: Task 2/3 的全部修复及前一阶段 UI 流程。

- [x] **Step 1: 修复点视觉复核**

重拍红矿和 Deco/Tree 位置，确认不再同格遮挡；检查 Bush 接地点仍为 0、offset=-8、Collider=0，水岩 order 6/7 不变。

- [x] **Step 2: 完整运行回归**

从 MainMenu 经 ModeSelect 进入人机对局，验证设置保存、民房产工人、红蓝固定矿采集、其他生产、移动/强攻/驻守、小地图、AI 建造与两波进攻、事项 1 障碍格恢复。不得出现新的封路、空引用或生产入口失效。

- [x] **Step 3: 最终清理与独立审查**

确认编译 0 C# 错误、无本次新增异常、场景无临时对象。独立只读审查仅针对本修复范围，Critical/Important 修复后重跑。记录实际坐标、数量、截图和未修改的正常设计。
