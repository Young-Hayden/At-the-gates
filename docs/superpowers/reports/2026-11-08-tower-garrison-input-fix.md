# 《兵临城下》防御塔右键入塔/出塔修复报告

日期：2026-11-08；真实地图受控验证续作：2026-11-09
工程：`D:\Unity\projects\At the Gates`
分支：`fix/tower-top-garrison`（基线 `main=140530c`）
状态：已交付并本地合并到 `main`；驻塔提交 `c252edd`，HUD/验收提交 `eef6c63`；未推送。

## 1. 用户问题与结论

用户反馈：选择弓箭手后点击防御塔，弓箭手不会到塔顶防御。

本轮通过正式预制体和真实生产命令入口复现并修复两个输入缺陷：

1. **点击可见塔顶不能驻塔**：塔贴图约 4 格高，但右键查找只覆盖约 1 格高的基座碰撞框。
2. **入塔后空选择不能出塔**：入塔清空选择，而右键入口在空选择时提前返回。

未修改塔碰撞、占地、寻路、伤害、场景、预制体或真实存档。

## 2. 修改

### `Assets/Scripts/SelectionManager.cs`

- `FindTowerAt` 保留原来的 `Physics2D.OverlapPointAll`。
- 碰撞未命中时，遍历活动 `TowerGarrison`，用正式塔的 `SpriteRenderer.bounds` 识别可见塔身。
- `HandleRightClickInput` 在空选择时只额外放行“非 Shift 右键已占塔”的出塔命令；其他空选择输入仍返回。

### `Assets/Tests/PlayMode/RubbleAndGarrisonCommandTests.cs`

新增两项用户-facing 回归：

- `RightClickVisibleTowerTop_DistantArcherWalksToBaseThenGarrisons`
- `RightClickOccupiedTower_WithoutAnySelection_ReleasesArcher`

Diff：2 个受跟踪文件，80 行新增、2 行删除；`git diff --check` 无输出。

## 3. 红绿证据

| 用例 | 修复前 | 修复后 |
|---|---|---|
| 可见塔顶点击，远处弓箭手接近并入塔 | **FAIL**：5 秒后 `HasArcher=False`；控制断言确认点击在贴图内、基座碰撞框外 | **PASS**，0.819 秒；同一弓箭手到塔基后再绑定塔顶 |
| 占塔后空选择右键出塔 | **FAIL**：`HasArcher=True` | **PASS**；UnitActor、UnitMover 和单位注册恢复 |

证据文件：

- `Logs/tower-click-2026-11-08-red.txt`，MD5 `023f8aeeebd06993003e62fa72f36f15`
- `Logs/tower-click-2026-11-08-green.txt`，MD5 `57ade1f68a7091de5e0439d3709c2282`
- `Logs/tower-exit-2026-11-08-red.txt`，MD5 `d57806492df7e67db79ae12d360f1edd`
- `Logs/tower-exit-2026-11-08-green.txt`，MD5 `50114d5a3388639f6c2cc91a95e90a59`

## 4. 全量功能回归

| 阶段 | 结果 | 日志 |
|---|---:|---|
| 原始基线 | **268/268 PASS**，226.770 秒 | `Logs/tower-click-2026-11-08-baseline-268.txt` |
| 入塔修复后 | **269/269 PASS**，239.740 秒 | `Logs/tower-click-2026-11-08-full-269.txt` |
| 两项修复后 | **270/270 PASS**，0 失败、0 跳过，231.329 秒 | `Logs/tower-fixes-2026-11-08-full-270.txt` |

最终全量日志 MD5：`b2e5f8129095042306e567c0c7f1dba1`。
编译 0 错误、0 警告；2026-11-08 回归后的 Console 共 19 条 Log，无 Error/Warning，`lastErrors=[]`。2026-11-09 受控探针后的 Console 另经全量 JSON 解析，265 条均为 Log，无 Error/Warning。

测试覆盖选择/编队/巡逻/A-move、网格与寻路、采集/维修、生产/人口、战斗/驻塔/治疗、科技/维护费、AI 建造/生产/研究/进攻、存档与场景生命周期、HUD/菜单等既有项目套件。

**这不等于证明项目不存在任何其他 bug**：本轮没有重新做长局压力、独立播放器、真人键鼠或所有 UI 分辨率验收。

## 5. 真实 SampleScene：自然探针 SKIP，受控验证 PASS

### 2026-11-08 自然场景探针

- 当时运行中的场景没有现成“空闲已完工蓝塔 + 蓝弓箭手”组合；唯一塔是 `Tower_Red_AI`，蓝弓箭手为 0，因此自然探针明确记录 `SKIP`。
- 没有把红塔改队，也没有把这次 SKIP 写成通过。

### 2026-11-09 真实地图受控夹具

在重新加载的真实 `SampleScene` 中进一步确认：初始布局没有蓝城堡，但有 8 个蓝方单位（含 2 个蓝弓箭手）；249/249 个 Building 注册一致。随后以蓝方起始组为中心寻找真实连通空地，**仅在 PlayMode** 临时实例化正式蓝塔和蓝弓箭手，并暂时禁用 AI 防止干扰。停止播放后夹具全部消失。

- **入塔 PASS**：临时塔占地锚点 `(5,7)`，弓箭手起点 `(2,5)`，初始距离 3.808；通过生产 `HandleRightClickInput` 点击可见塔顶 `(6.00,9.67,0)`，该点在贴图内且在基座碰撞框外。弓箭手先接近再入塔，`localPosition=(0,2,0)`、`pinned=True`、`atTop=True`、`visible=True`、`logicDisabled=True`、`selectionCount=0`。
- **出塔 PASS**：空选择右键同一塔顶，`HasArcher=False`；UnitActor、UnitMover、CombatAI、碰撞和单位注册全部恢复，弓箭手落在可走格 `(5.5,6.5)`。
- 入塔日志：`Logs/tower-live-controlled-entry-2026-11-09.txt`。
- 出塔日志：`Logs/tower-live-controlled-exit-2026-11-09.txt`。
- 截图：`Logs/tower-controlled-garrisoned-2026-11-09.png`，已目检弓箭手站在蓝塔顶部。
- 探针脚本：`.superpowers/sdd/tower-click/LiveGarrisonControlledEntryProbe.cs`、`LiveGarrisonControlledExitProbe.cs`。
- 续作 Console 导出经 JSON 全量解析：265 条 Log、0 Error、0 Warning。

这证明正式预制体、真实地图网格与生产右键方法组成的受控链路通过；它**不等于**验证原场景天然存在蓝塔，也不替代真人操作系统级鼠标验收。

原始自然探针脚本：`.superpowers/sdd/tower-click/LiveGarrisonProbe.cs`。
断点：`.superpowers/sdd/tower-click/RESUME.md`。

## 6. 最终环境与文件完整性

- 编辑器 stopped，未编译、未暂停。
- 活动场景已还原为 `Assets/Scenes/MainMenu.scene`，`dirty=false`。
- `SampleScene.scene` MD5 前后均为 `61ccb5b7c8958b3cbca60062ab8cb81c`。
- `MainMenu.scene` MD5 前后均为 `90bb497775fd5e4c60d0f06d950145ec`。
- 真实存档 424419 字节，MD5 前后均为 `0d34d758ec3bd3eb7ee954eebf411076`。
- 未调用场景保存或游戏写档 API。

## 7. 后续由用户决定

- 可直接在当前分支实玩：选择一个蓝弓箭手，右键蓝塔可见塔身/塔顶；入塔后空选择右键同一塔可出塔。
- 用户随后选择本地合并；驻塔提交 `c252edd` 已与后续 HUD/验收提交 `eef6c63` fast-forward 到 `main`，合并后完整套件 274/274 PASS。功能分支已删除，未推送。
