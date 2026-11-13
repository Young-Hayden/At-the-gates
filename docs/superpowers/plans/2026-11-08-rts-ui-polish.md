# RTS HUD 观感定点修补 Implementation Plan

> **For agentic workers:** execute inline with test-driven-development. No subagents; no commit/merge/push unless the user explicitly chooses them later.

**Goal:** 按已批准的 `2026-11-08-rts-ui-polish-design.md` 修复五项 UI 观感问题，保持全部玩法与既有布局不变。

**Architecture:** 新增一个只针对现有对象的编辑器修补入口，不重跑 HUD 重建；HUD 只改标题映射；头像生成器只为 Lancer 增加人物优先裁切，并只生成蓝/红两张。所有场景写入都有字节备份与对象级差异审计。

**Tech Stack:** 团结引擎 2022.3.62t5、Unity UI、C#、NUnit PlayMode。

## Global Constraints

- 分支 `feat/ui-polish`；基线 `main@9b6b016`，264/264。
- 保存场景前必须备份到 `.superpowers/sdd/ui-polish/` 并记录 MD5；不得写真实存档。
- 不改 `ModeSelect`，不重建 HUD，不修改其他八张单位头像。
- 每个代码/布局问题先看到对应测试失败，再实施；头像以生成图和 720p/1080p 截图验收。
- 最终目标 268/268；不提交，是否合并由用户决定。

## File Structure

- Create `Assets/Tests/PlayMode/UiPolish20261108Tests.cs`：四项回归。
- Create `Assets/Editor/ApplyUiPolish20261108.cs`：幂等、定点场景修补。
- Modify `HUD.cs`、`EnhanceMainMenu.cs`、`PolishHUD.cs`、`BuildAttackButton.cs`、`MakeUnitIcons.cs`。
- Modify only `SampleScene.scene`、`MainMenu.scene`、`Lancer_Blue.png`、`Lancer_Red.png`（头像 meta 仅保留原设置）。

### Task 1: 失败测试与备份

- [x] 写 `SelectionTitles_UseChineseNames`：选中场景战士和动态民房分别显示“战士”“民房”，未识别单位回退自定义名。
- [x] 写 `MainMenuRules_DoNotAdvertiseRemovedQuickBuild`：主菜单 Text 不含“1~6”，含“点击建造栏”。
- [x] 写 `UnitPanel_CostNameAndButtonRects_DoNotOverlap_AndClearBottom`：真实场景中 Cost 底边 ≥10，Cost/Name/Button 不重叠，文本 preferredHeight 不超过槽高。
- [x] 写 `AttackLabel_KeepsRightInset`：Label 距按钮右边 ≥6px，且不与 Icon 相交。
- [x] 分别运行并确认旧实现按预期失败；保存日志。
- [x] 备份两场景、Lancer 两 PNG/meta、其他八张头像的 MD5 清单。

### Task 2: 中文显示名

- [x] 在 `HUD.RefreshSelection` 只对标题调用私有映射；识别五类单位、六类建筑和 Tree/GoldMine/Sheep，未识别保持旧值。
- [x] 复跑 `SelectionTitles_UseChineseNames`，通过后复跑 WorkerBehavior/HUD 相关测试。

### Task 3: 主菜单文案

- [x] `EnhanceMainMenu.cs` 把“1~6 建造”改为“点击建造栏放置”。
- [x] `ApplyUiPolish20261108` 只查找包含旧文案的 Text 并替换；无目标时抛错，不保存。
- [x] 保存 MainMenu 后聚焦测试通过；审计只有目标文本和允许的序列化噪声。

### Task 4: SampleScene 布局

- [x] `PolishHUD.cs` 与场景统一为 Cost y=10/h=14、Name y=26/h=15、Button y=41/h=56。
- [x] `BuildAttackButton.cs` 与场景统一 Label `offsetMax=(-6,0)`；Icon 和按钮不动。
- [x] `ApplyUiPolish20261108` 只修改这些现有 RectTransform，幂等，不创建对象。
- [x] 聚焦两项测试通过；720p/1080p 截图检查兵种提示、进度条、攻击按钮和小地图/选中面板。

### Task 5: Lancer 人物优先头像

- [x] `MakeUnitIcons` 保留现有默认算法；Lancer 专用规则只在内容底部 65% 区域求人物横向中心和边长，裁切以底部对齐并留 18% 边距。
- [x] 提供只生成 Lancer 蓝/红的入口，不重写其他八张头像。
- [x] 运行后读取两张 PNG，确认人物完整、武器仍可辨认；不合要求则调整生成器后仅重做这两张。
- [x] 核对其他八张头像 MD5 完全不变，检查 Lancer 的 Point/透明/未压缩导入设置。

### Task 6: 全量验证与交付

- [x] 全量 268/268，日志立即归档；`git diff --check` 无输出。
- [x] 对比备份，列出场景实际修改对象；不得有其他对象增删或玩法字段变化。
- [x] 核对真实存档 MD5、场景最终 MD5、头像备份；编辑器 stopped 且无编译错误。
- [x] 写验证报告与 `.superpowers/sdd/ui-polish/RESUME.md`，明确截图不是实际键鼠、头像审美待用户确认。
- [x] 不提交；向用户提供合并/保留分支选择。
