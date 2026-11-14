# 《兵临城下》2560 固定显示、防御范围与攻击圈交付报告

日期：2026-11-14  
工程：`D:\Unity\projects\At the Gates`  
基线：`main@3e5659a` 加此前未提交的 286/286 审计修复与地图精修。  
状态：**功能、自检、本地提交与 GitHub 推送均已完成。**

## 1. 用户决策

- 运行时固定 **2560×1440 全屏**，不再允许切换分辨率/全屏。
- 实际 2560 输出下，Canvas 参考保持 **1920×1080**，让既有 UI 放大 1.333 倍并铺满；不把现有固定尺寸元素重排成 2560 设计坐标。
- 箭塔最低防御射程 **4.5→5.5**；空塔显示 5.5 预警圈，驻塔按 5.5 射击。
- 攻击范围圈**仅选中时显示**：每个选中 UnitActor 一个，选中完成箭塔一个。

## 2. 实现

### 2.1 固定显示

- `GameSettings`：固定 `2560×1440`、`FullScreenMode.FullScreenWindow`；删除旧分辨率/全屏切换；保留边缘平移和平移速度。
- `MenuSettingsUI`：分辨率区只显示一个锁定的 `2560×1440`，其余隐藏；全屏按钮锁定并显示固定文案。
- `ProjectSettings.asset`：只改 `defaultScreenWidth/Height` 为 2560/1440；`fullscreenMode:1`、`resizableWindow:0` 不变。
- 三个场景与 LoadingScreen 预制体的 CanvasScaler 参考值经字节比较与备份完全一致，仍为 1920×1080；编辑器构建器也保持 1920×1080。

### 2.2 箭塔防御

- `TowerGarrison.defenseRange=5.5`，`ActiveRange=HasArcher?effectiveRange:defenseRange`。
- 驻塔射程 `Max(5.5, archer.attackRange)`，未来更长射程弓手不会被压低。
- HUD 选中完成箭塔时显示 `防御射程 5.5`。
- 测试：5.0 格目标会开火，6.0 格不会。

### 2.3 攻击范围圈

- 新组件 `AttackRangeIndicator`：96 段闭合 LineRenderer、共享 `Sprites/Default` 材质、线宽0.08、队色半透明、无 Collider。
- `SelectionManager` 使用对象池：每个选中活着的 UnitActor 一个圈，选中完成箭塔一个圈；移动跟随；半建/死亡/取消选择隐藏；池稳定不逐帧增长。
- 工人与僧侣也显示各自 attackRange，符合“每个兵种”。

## 3. 红绿与变异证据

- 固定显示：旧代码编译 RED 4 条；实现后常量与菜单固定控件 2/2 PASS。
- 范围/箭塔：旧代码编译 RED 7 条；实现后塔边界、单位圈、塔圈、既有射击 4/4 PASS。
- 行为判别力：临时把驻塔射程改回弓手 4.5，塔测试红（ActiveRange 4.5 vs 5.5）；临时禁用范围圈更新，单位圈测试红（0 vs 2）；两文件按原 MD5 字节还原后复跑均绿。
- 显示：旧 PlayerSettings 1920 RED；四种 Canvas 参考测试均 RED；用户改回1920参考后四测试 RED（实际2560），恢复资产后 5/5 PASS。
- MenuPolish 3/3 PASS，无菜单回归。

## 4. 完整自检

- 在 Game View **2560×1440** 下完整 PlayMode：**296/296 PASS**，0 Failed/Skipped/Inconclusive，262.044 秒。
- 日志：`.superpowers/sdd/fixed-display-attack-ranges/final-full-296.log`，28,734 字节，MD5 `32c86c0e6183a9deee04f54ce252bd48`；296 条 Passed、0 Failed。
- 编译：0 errors / 0 warnings；Console：0 Warning / 0 Error。
- 测试临时 InitTestScene：0。
- 最终场景 MD5 `3c43b73c3621d3eeebc4976a8b36914d`；真实存档仍 424,419 字节、MD5 `0d34d758ec3bd3eb7ee954eebf411076`。
- ProjectSettings MD5 `8ecae4a337dc7434056828039216e7cb`。

## 5. 视觉与性能

最终 2560×1440 截图逐张 Read：

| 图 | 文件 | MD5 |
|---|---|---|
| 设置固定项 | `docs/screenshots/rts-2560-settings.png` | `b1f82c05…` |
| 四兵种范围圈 | `docs/screenshots/rts-attack-ranges-units.png` | `53867e4e…` |
| 8 单位范围圈 | `docs/screenshots/rts-attack-ranges-units8.png` | `26b2e72d…` |
| 空箭塔5.5圈 | `docs/screenshots/rts-attack-range-tower.png` | `fd36832e…` |

> 后续修正：攻击圈已从 LineRenderer 改为 SpriteRenderer，并固定在 ground decal order 3（低于所有 YSort 单位/树），详见 `2026-11-14-rts-attack-ring-ground-layer-fix.md`；上表截图已更新为修正后效果（该修正尚未推送）。

性能探针（8选中 vs 无选中，各3秒交替两次）：

- 平均帧时间：5.54/5.36 ms（选中） vs 4.94/5.03 ms（无）；
- GC：8.42/7.91 KB/帧 vs 7.78/7.86 KB/帧；
- 可见圈 8，池增长到8后保持，无逐帧对象增长。

## 6. 独立审查

最终全功能复审：**Ready to deliver = Yes**，0 Critical / 0 Important / 5 Minor。

Minor：复审包曾漏附新脚本（已补）、计划旧步骤文案（已修正）、GameSettings 注释用词（已修正）、`normalTint` 未使用/固定项仅文字着色（不阻断）、若干范围圈测试覆盖边界（性能与视觉探针已覆盖主要风险）。

## 7. 未验证边界

- 未做真人 OS 键鼠输入、独立播放器、非 2560 显示器/DPI、长期平衡或长时间主动运营。
- `git diff --check` 仍提示早前用户裁定的 SampleScene 基线两条尾空格；不是本功能新增内容。

## 8. 发布

- D 盘本地提交：`8f420b4`（`feat: add fixed 2560 display and attack range indicators`），工作区提交时干净。
- 桌面发布副本提交：`7f45d2c`（同标题）。
- 已推送 GitHub：`https://github.com/Young-Hayden/At-the-gates`，远程 `main` SHA 与发布副本 HEAD 均为 `7f45d2cb9f2d3fd7201d77c46f4b4e6fa541b510`。
- 直连推送在 send-pack 阶段被重置；使用本机 Clash 代理 `127.0.0.1:7897` 重试成功。未使用 force push。
