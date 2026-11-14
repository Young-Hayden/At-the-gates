# 攻击范围圈地面层修正设计

日期：2026-11-14  
工程：`D:\Unity\projects\At the Gates`  
基线：`main@8f420b4` 功能提交及后续文档提交；本轮只修正范围圈渲染层。

## 1. 问题

当前 `AttackRangeIndicator` 使用 LineRenderer。虽然设置了排序序号，视觉上仍像独立的世界线条，不能稳定获得与绿色选择圈相同的 SpriteRenderer/Y-sort 地面层效果；用户要求攻击圈和绿色选择圈处于同一地面图层，并被单位、树木按 Y 排序正常遮挡。

## 2. 方案

将 `AttackRangeIndicator` 改为 SpriteRenderer：

- 生成白色环形 Sprite，运行时通过 `SpriteRenderer.color` 着色。
- 使用 Default Sorting Layer，排序 `YSort.OrderFor(center.y) - 1`，与绿色选择圈完全一致。
- 圈中心使用对象 transform；精灵半径为 1 世界单位，通过 transform 缩放为实际攻击半径。
- 每个不同半径生成一个环形纹理并缓存；纹理像素线宽按半径反算，保证世界线宽约 0.06–0.07，避免大范围圈变粗。
- 环形纹理使用 Point 过滤，保持像素画风。
- 不添加 Collider/RaycastTarget，不改变选择、寻路、战斗。

## 3. 保持不变

- `AttackRangeIndicator` 公共接口 `Create/Show/Hide/Radius/IsVisible`。
- SelectionManager 对象池、选中对象过滤、移动跟随、半建/死亡/取消隐藏。
- 箭塔防御 5.5 和所有数值。
- 2560×1440 显示、HUD 与截图流程。

## 4. 测试与视觉验收

- 现有选圈数量和半径测试继续通过。
- 新增断言：范围圈组件是 SpriteRenderer 而非 LineRenderer；其 sortingOrder 与同位置绿色选择圈一致。
- 2560 实图：选中僧侣/工人/弓箭手，确认蓝色攻击圈位于草地层，脚部和树木能遮盖线条。
- 完整 296+ 测试套件、编译、Console、场景/存档指纹复核。

## 5. 发布

本轮未收到新的提交/推送授权。实现与自检完成后只报告结果；如用户明确要求再提交并推送。
