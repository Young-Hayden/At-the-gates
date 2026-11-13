# 《兵临城下》HUD 观感定点修补验证报告

日期：2026-11-08  
设计：`docs/superpowers/specs/2026-11-08-rts-ui-polish-design.md`  
计划：`docs/superpowers/plans/2026-11-08-rts-ui-polish.md`  
分支：`feat/ui-polish`（从 `main@9b6b016` 切出）

## 1. 完成内容

| 项 | 实施 |
|---|---|
| 主菜单旧快捷键 | “1~6 建造”改为“点击建造栏放置”；同步 `EnhanceMainMenu.UpdatedRules` |
| 英文选中标题 | HUD 仅显示层映射五类单位、六类建筑、树木/金矿/羊；未识别对象保留原名 |
| 兵种提示压边框 | Cost `y=10/h=14`、Name `y=26/h=15`、Button `y=41/h=56`；生成器同步 |
| 攻击文字贴边 | Label 右 inset 改为 6px，按钮、Icon 和点击逻辑不变 |
| 枪兵头像 | 只重做蓝/红两张，人物底部 65% 区域裁切，116×116，完整人物保留、枪尖截去 |

没有重建 HUD，没有修改其他八张单位头像，没有改玩法、ModeSelect 或真实存档。

## 2. 红绿回归证据

新增 `UiPolish20261108Tests` 四项：

| 测试 | 修改前 | 修改后 |
|---|---|---|
| `SelectionTitles_UseChineseNames` | 显示 `Blue_Warrior_1` | “战士”；民房与未知自定义名也通过 |
| `MainMenuRules_DoNotAdvertiseRemovedQuickBuild` | 仍含“1~6” | 不含旧快捷键，含“点击建造栏” |
| `UnitPanel_CostNameAndButtonRects_DoNotOverlap_AndClearBottom` | Cost 底边 6px | 底边 ≥10，Cost/Name/Button 不重叠，文本高度合适 |
| `AttackLabel_KeepsRightInset` | 右边距 0 | 右边距 6px，且不与 Icon 相交 |

第一轮全量：**268/268 通过，224.555s**。清理场景序列化噪声后的最终全量见 §5。

## 3. 场景与资源审计

### 3.1 MainMenu

- YAML 对象 234 → 234，新增 0、删除 0。
- 仅 `!u!114 &1183876073` 的规则文本变化。
- 最终 MD5：`b0361f3760e0a77b753543e9f2ca4280`。

### 3.2 SampleScene

第一次保存后对象级审计发现 Unity 附带写入五个 AI 默认权重，并重掷动画速度。虽然权重与代码默认值相同，仍按授权范围清理：

- 移除额外序列化的 `workerWeight / warriorWeight / lancerWeight / archerWeight / monkWeight`。
- 将所有 `m_AnimationSpeed` 恢复为保存前备份值。
- 重新从磁盘加载场景，不再次保存。

清理后：

- YAML 对象 **1764 → 1764**，新增 **0**、删除 **0**。
- 非动画速度变化对象 **19 个**，全部是目标 RectTransform（6 Cost、6 Name、6 Button、1 攻击 Label）。
- 动画速度变化 **0**，AI 字段变化 **0**。
- 文件 diff 从 869 行降为 **64 行（32+ / 32-）**。
- 最终 MD5：`899f6b21d43c22534eff22b0c5e18fdd`。

备份与清理脚本位于 `.superpowers/sdd/ui-polish/`。场景修补入口 `ApplyUiPolish20261108` 可幂等重跑，但交付文件已清理保存噪声，不应再无条件保存场景。

### 3.3 头像

- `Lancer_Blue.png`、`Lancer_Red.png`：177×177 → 116×116；PPU 同步 177 → 116，其他导入设置保持。
- 其他八张头像 MD5 完全不变。
- 初次裁切误把 `maxY` 当人物底部，生成图不合格；已修正为从 `minY`（脚部）向上裁切并重新生成。最终两张图片均已实际读取检查，人物头盔、躯干和脚部完整。

## 4. 实际渲染目检

| 截图 | 内容 | 结果 |
|---|---|---|
| `Logs/ui-polish-after-1080.png` | 1920×1080 对局 HUD | 中文标题、枪兵人物、兵种提示与攻击右边距正常 |
| `Logs/ui-polish-after-720.png` | 1280×720 对局 HUD | 无出屏或新增重叠，提示不压边框 |
| `Logs/ui-polish-mainmenu-1080.png` | 主菜单 | 新建造说明与部队图标正常 |

场景修补时桥接记录过一次 `Unloading the last loaded scene` 警告；随后状态回读为活动 SampleScene、`dirty=false`，两轮全量均运行完成。这是修补入口关闭 additive 场景的工具警告，不是运行中游戏异常。

## 5. 最终验证

- 清理后最终全量：**268/268 通过，0 失败、0 跳过，234.398s**。
- 日志：`Logs/ui-polish-2026-11-08-full-268.txt`，MD5 `c0faf08ab1c96def480450a935f8e537`。
- `ApplyUiPolish20261108.Apply()` 重跑返回两个 `already up to date`，场景 MD5 未再变化，幂等性通过。
- 最终 SampleScene MD5 `899f6b21d43c22534eff22b0c5e18fdd`；MainMenu `b0361f3760e0a77b753543e9f2ca4280`；ModeSelect 保持 `56e86ab40659949cd117d57b2f825a2c`。
- 真实存档 424,419 字节，MD5 `0d34d758ec3bd3eb7ee954eebf411076`，与本轮开始前一致，未写档。
- `git diff --check` 无输出；编辑器 stopped、SampleScene 非 dirty、未在编译、`lastErrors=[]`。
- 尚未提交或合并。

## 6. 交付边界

- 四种截图已实际读取，但 MCP 不能代替真人键鼠与主观审美验收。
- 其他分辨率、不同 DPI 和极端语言长度未验。
- 小地图降频、难度选择不在本轮范围。
- 场景与头像均有备份；需要回滚时恢复 `.superpowers/sdd/ui-polish/` 中对应 `.before` 文件，并保留新测试以证明回滚后的差异。
