# 《兵临城下》HUD 观感定点修补设计

日期：2026-11-08  
分支：`feat/ui-polish`（从 `main@9b6b016` 切出）  
状态：方案 A 已获用户批准；本文是实现前设计基线。

## 1. 目标与范围

修复五项已知观感问题，不重建 HUD、不改变玩法：

1. 主菜单仍提示已移除的“1~6 建造”。
2. 选中面板直接显示 `Blue_Archer_1`、`Castle_Blue` 等英文对象名。
3. 兵种面板的“需兵营/需靶场/需修道院/需民房”贴近或压住下边框。
4. 攻击按钮文字右端贴边。
5. 枪兵头像把整杆长枪纳入正方形，人物在 42px 槽内过小。

不包含：小地图降频、难度选择、其他 HUD 重构、玩法平衡调整。

## 2. 方案比较与决定

- **采用 A：定点修补**。只修改目标 Text/RectTransform、HUD 显示名映射和 Lancer 两张头像，风险与 diff 最小。
- 不采用 B：仅放大头像 Image。不能解决人物在长枪画布中占比过小的问题。
- 不采用 C：重跑 BuildHUD 或重写全部头像。会覆盖手修布局和其他合格头像，风险过大。

## 3. 具体设计

### 3.1 主菜单操作说明

文件：`Assets/Scenes/MainMenu.scene`、`Assets/Editor/EnhanceMainMenu.cs`。

将：

```text
　H 驻守　1~6 建造　Esc 取消建造
```

改为：

```text
　H 驻守　点击建造栏放置　Esc 取消建造
```

同时更新生成器中的同一句，避免以后重建菜单时回退。只修改目标文本，不动菜单布局、按钮或场景流。

### 3.2 中文显示名

文件：`Assets/Scripts/HUD.cs` 的 `RefreshSelection`。

在 HUD 内增加私有映射，不改存档字段、预制体名或 `UnitActor.DisplayName` 的持久化语义：

- Worker/Pawn → 工人；Warrior → 战士；Lancer → 枪兵；Archer → 弓手；Monk → 僧侣。
- Castle → 指挥中心；House → 民房；Barracks → 兵营；Archery → 靶场；Monastery → 修道院；Tower → 箭塔。
- Tree → 树木；GoldMine → 金矿；Sheep → 羊。
- 未识别对象继续使用原 `DisplayName` / `Building.name`，保证未来兵种和中立物体不会变成空白。

映射只用于当前选中面板标题；多选标题与战斗数值不变。

### 3.3 兵种面板间距

当前实测：

- Cost：`y=6`、高 15，字体 12、preferredHeight 14。
- Name：`y=22`、高 16，字体 13、preferredHeight 15。
- Button：底边 `y=40`、高 56。

目标值（五个真实使用槽和第六个隐藏槽一致处理）：

| 对象 | anchoredPosition.y | height | 约束 |
|---|---:|---:|---|
| Cost | **10** | **14** | 底边距面板底 10px |
| Name | **26** | **15** | 与 Cost 间隔 2px |
| Button | **41** | 56（不变） | 按钮底边与 Name 顶边相接但不重叠 |

这些值满足：`Cost 10..24`、`Name 26..41`、`Button 41..97`。不能只把 Cost 上移 6px，否则会与 Name 重叠。生产进度 Fill 仍位于按钮内部，不改动。

同步更新 `Assets/Editor/PolishHUD.cs` 中的布局常量/目标值，保持以后工具生成结果与场景一致；绝不重跑完整 HUD 重建。

### 3.4 攻击按钮文字

文件：`Assets/Scenes/SampleScene.scene`、`Assets/Editor/BuildAttackButton.cs`。

按钮保持 96×34、左侧 Icon 保持 24×24 和 x=6。Label 左 inset 维持 30，右 inset 从 0 改为 6：

```csharp
labelRt.offsetMax = new Vector2(-6f, 0f);
```

文字居中区域略向左收，右端距按钮边缘 6px，不改变 `Button.onClick`、IconSwap 或攻击命令逻辑。

### 3.5 枪兵头像

文件：`Assets/Editor/MakeUnitIcons.cs`；输出仅 `Assets/Art/UI/UnitIcons/Lancer_Blue.png` 与 `Lancer_Red.png`。

现有生成器按所有非透明内容（含枪尖）取正方形，导致人物占比低。为 Lancer 增加专用“人物优先”裁切：以内容底部为基准，取包含头盔、躯干和脚部的小正方形，允许截去枪尖上半部；其他八个头像完全不变，不重跑全量生成。

裁切结果必须：

- 人物完整，不截头、不截脚；保留足够武器特征以辨认枪兵。
- 蓝/红使用同一几何规则，保留原透明背景、Point 过滤和未压缩设置。
- 原两张 PNG 与 `.meta` 先备份；生成后分别读取图片检查，再进场景截图检查。

此项以图像与截图验收为主，不用脆弱的“某像素必须等于某颜色”自动化断言。

## 4. 场景风险与回滚

| 改动 | 收益 | 风险 | 缓解与回滚 | 不可验证项 |
|---|---|---|---|---|
| MainMenu 文案 | 说明与实际操作一致 | 低 | 只改目标 Text；`mainmenu-before.scene` 整文件还原 | 真人阅读确认 |
| 兵种面板三行位置 | 提示离开下边框 | 中低：名称、按钮、Cost 相互重叠 | 对象级 diff；矩形不相交测试；720p/1080p 截图；`sample-before.scene` 还原 | 非 16:9 与极端分辨率 |
| 攻击 Label inset | 右端不贴边 | 低 | 仅 Label 修改；场景备份还原 | 真人点击手感 |
| Lancer 两张头像 | 人物在槽内更大 | 中：构图主观、可能误裁人物 | 只处理两张，原图备份可独立还原 | 最终审美与不同缩放下的清晰度 |

所有备份放在 `.superpowers/sdd/ui-polish/`，保存前记录 MD5。场景保存后不得出现其他对象增删；动画速度等随机序列化变化须通过对象级审计排除，不能只比较 `git diff` 行数。

## 5. 测试与验收

新增 4 项，基线 264 → 目标 **268/268**：

1. `SelectionTitles_UseChineseNames`：真实 HUD 选中战士与民房时标题分别为“战士”“民房”；未知名称回退不为空。
2. `MainMenuRules_DoNotAdvertiseRemovedQuickBuild`：规则不再含“1~6”，包含“点击建造栏”。
3. `UnitPanel_CostNameAndButtonRects_DoNotOverlap_AndClearBottom`：使用真实场景 RectTransform，验证第 3 节所有边界。
4. `AttackLabel_KeepsRightInset`：Label 右边界距按钮右边界至少 6px，且不与 Icon 相交。

流程：先写并运行失败测试 → 定点修改场景/资源/代码 → 聚焦测试通过 → 全量 268。头像另做蓝/红图片读取与两种分辨率截图。

必须保留的既有验证：`HudPolishTests`、`MenuPolishTests`、`RepairButtonSceneTests`、输入/攻击命令测试、全量 PlayMode。

## 6. 交付边界

- 保存前完成场景与头像字节备份和 MD5 记录。
- 交付前核对对象级场景差异、测试日志、场景/存档 MD5；真实用户存档不得写入。
- 不推送、不合并，是否提交/合并由用户另行决定。
- MCP 截图与 API 驱动不等于真人键鼠验收；头像审美最终由用户确认。
