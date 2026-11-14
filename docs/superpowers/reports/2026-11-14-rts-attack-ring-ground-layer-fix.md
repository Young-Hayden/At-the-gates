# 攻击范围圈地面层修正报告

日期：2026-11-14  
工程：`D:\Unity\projects\At the Gates`  
基线：已推送 `8f420b4`（本地）/ `7f45d2c`（发布副本）及其文档提交。

## 1. 问题

原 `AttackRangeIndicator` 使用 LineRenderer。用户指出攻击圈应像绿色选择圈一样处于地面 SpriteRenderer/Y-sort 图层，并被单位、树木正常遮挡。

## 2. 根因与最终修正

首次替换成 SpriteRenderer 并沿用圆心 Y 排序仍未解决。实际诊断：

- 弓手 `y=4.5`：攻击圈和绿选择圈都是 `954`。
- 圆心上方的树 `y=6` 的 YSort 是 `940`；数值越小越靠前，因此树绘制在攻击圈后面。
- 一个 SpriteRenderer 只能有一个 sortingOrder，大圆跨越不同 Y 的单位/树时，不可能按圆上每一段分别 Y 排序。

最终修正：

- `AttackRangeIndicator` 使用 SpriteRenderer。
- 攻击圈固定为 **ground decal order = 3**：高于 tilemap 最大 order 2，低于所有 YSort 世界物件（base 1000）和绿色选择圈。
- 环形白色 Sprite 按半径生成并缓存；`SpriteRenderer.color` 着色；纹理线宽按半径反算，保持约 0.065 世界单位。
- 绿选择圈继续位于攻击圈之上；蓝圈整体被单位、树木、岩石正常遮挡。
- 无 Collider/RaycastTarget；对象池、选中过滤、移动跟随、半建/死亡/取消隐藏不变。

修改文件：
- `Assets/Scripts/AttackRangeIndicator.cs`
- `Assets/Scripts/SelectionManager.cs`（攻击圈固定 GroundSortingOrder）
- `Assets/Tests/PlayMode/AttackRangeTests.cs`（固定 order 和树遮挡断言）

## 3. 红绿

- 第一轮 RED：新测试在旧 LineRenderer 实现上失败：“must no longer be a world-space LineRenderer”，实际为 `<AttackRangeRing (UnityEngine.LineRenderer)>`。
- 第二轮 RED：SpriteRenderer 仍按圆心 Y 排序时，固定地面顺序测试失败：`Expected: 3 / But was: 971`；同时诊断日志确认树 `940` 被画在圈后。
- GREEN：攻击圈固定 order 3 后通过；测试断言：
  - 存在 SpriteRenderer、无 LineRenderer；
  - sortingOrder == 3（高于 tilemap 2，低于 YSort 单位/树）；
  - 绿选择圈 order 大于攻击圈；
  - 圆心上方的 YSort 树 order 大于攻击圈（树遮圈）。
- 既有单位圈、塔圈/半建隐藏测试均 PASS。

## 4. 完整自检

- 2560×1440 完整 PlayMode：**297/297 PASS**，0 Failed/Skipped/Inconclusive，247.288 秒。
- 日志：`.superpowers/sdd/fixed-display-attack-ranges/final-ground-order3-297.log`，28,841 字节，MD5 `3de2596285512bf3b888ec7d581e203f`。
- 编译：0 errors / 0 warnings；Console：0 Warning / 0 Error。
- 临时 InitTestScene：0。
- 场景 MD5 `3c43b73c3621d3eeebc4976a8b36914d`；真实存档 MD5 `0d34d758ec3bd3eb7ee954eebf411076`，均未变。
- SpriteRenderer 性能复测（8选中 vs 无，3秒交替）：平均约 6.06 vs 6.01 ms，GC 约 8.4 vs 7.67 KB/帧；池稳定为8，无逐帧增长。

## 5. 视觉证据

- `Logs/attack-rings-ground-order3.png` / docs `rts-attack-ranges-units.png`：4兵种近景，蓝圈在树和单位后方，绿选择圈在蓝圈上方。
- `Logs/attack-ranges-units8-2560.png`：8单位范围圈。
- `Logs/attack-range-tower-2560.png`：空箭塔5.5地面圈。
- docs 截图 MD5：units `53867e4e…`、units8 `26b2e72d…`、tower `fd36832e…`。

## 6. 发布状态

本轮未收到新的提交/推送授权。代码、测试、截图均在当前工作区；**未提交、未推送**。
