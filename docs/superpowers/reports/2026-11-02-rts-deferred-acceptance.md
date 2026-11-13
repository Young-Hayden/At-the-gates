# 兵临城下 —— 四项挂账验收（2026-11-02）

验收人：Claude（本机 Tuanjie 编辑器，真实 PlayMode）
工程：`D:\Unity\projects\At the Gates`，main `9910e9b`，验收前后工作区均干净。
范围：用户点名的四项挂账 —— 物理 GUI 目检、编队不存档（设计）、驻塔期间不能治疗（设计）、研究不可取消不可退款（设计）。
**本轮只验收，没有改任何代码、场景、预制体或测试。**

## 结论

| 项 | 结论 | 证据 |
|---|---|---|
| 编队是会话状态、不存档 | **通过**，符合设计 | 存组→保存→读档后召回返回 `False` |
| 驻塔期间不能治疗 | **通过**，符合设计；出塔后立即恢复治疗 | 驻塔 8s 血 35 不变、出塔 6s 后 55 |
| 研究不可取消、不可退款 | **通过**，符合设计；无重复扣费 | 无 Cancel/Refund API；二次研究被拒且银行不变 |
| 物理 GUI 逐屏目检 | **视觉验收通过**，发现 4 条 Minor 观感问题（见下） | 7 张真实渲染截图 |

**边界（必须写明）**：MCP 无法合成真实鼠标/键盘输入，本轮做的是**真实 PlayMode 渲染逐屏目检 + 公开 API 驱动的行为实测**，不是真人键鼠实操。按钮的真实点击路径、ESC/数字键手感仍建议用户随手玩一遍确认。

## 一、编队不存档（设计确认）

操作：禁用 AI → 选中蓝方战斗单位 → `HandleControlGroupKey(1, true)` 存组 → `SaveGameService.Save` → `TryRead` + `RequestLoad` + `LoadingScreen.Load` 读档 → 召回。

```
[ACC] selected blue combatants=1
[ACC] group1 stored=True recalledBeforeSave=True selectedAfterRecall=1
[ACC] save=True err=
[ACC] TryRead=True err=   load issued=True
[ACC] afterLoad selectedAfterLoad=0
[ACC] group1 recallAfterLoad=False (expected False) selectedNow=0
```

读档本身正常（经济随后从 600 继续增长到 1138，工人在采集），编队确实不随存档恢复，与 `RESUME-control-groups.md` 的设计说明一致。

## 二、驻塔期间不能治疗（设计确认）

布景：新开局 → 蓝弓手先扣 20 血（55→35）→ 实例化 `Tower_Blue` 于 (1,2) 并 `Garrison` → 僧侣放到塔旁 1.5 格（healRange 4.5 内）。

```
[ACC] pre-garrison archer hp=35/55
[ACC] garrison=True underConstruction=False
[ACC] garrisoned archer enabled=False inAll=False
--- 驻塔 8 秒（>3 个治疗冷却）---
[ACC] WHILE GARRISONED hp=35 monkPatient=null status=待命
[ACC] hp after direct damage attempt=35      ← TakeDamage(10) 被拒
[ACC] ungarrisoned: enabled=True inAll=True
--- 出塔 6 秒 ---
[ACC] AFTER UNGARRISON hp=55                  ← 35 → 55，治疗恢复
```

机理（读码确认，与实测一致）：驻塔把 `UnitActor.enabled=false`，弓手因此离开 `UnitActor.All`，`Healer.FindWoundedAlly` 遍历的正是这张表；`UnitActor.TakeDamage` 同样以 `!enabled` 拒伤（`Unit.cs:141-150`）。**所以"塔内不能治疗"与"塔内不受伤害"是同一枚硬币的两面，伤害由塔承担，设计自洽，不是缺陷。** 出塔即恢复，无残留状态。

## 三、研究不可取消、不可退款（设计确认）

布景：实例化完工 `Castle_Blue` → `Technology.BeginResearch(Blue, Forge)`。

```
[ACC] centreComplete=True underConstruction=False
[ACC] research start=True  bank 1560/360 → 1310/260   (= −250金 −100木，与 CostOf 一致)
[ACC] active=Forge remaining=45.0
[ACC] second research while busy=False reason=已有研究在进行 bankUnchanged=True
[ACC] cancel/refund API present=False
```

`Technology` 公开静态方法全集：`PlayerTeam, Bit, AllBits, Mask, Has, Restore, Reset, ActiveTech(Of), ActiveBitOf, RemainingOf, RestoreResearch, Progress(Of), CostOf, SecondsOf, BeginResearch×2, Tick, Complete×2, DamageBonus, MaxHealthMultiplier, MaxHealthBonusPercent, CarryBonus` —— **没有任何取消或退款入口**，HUD 科技条也只有三个研究按钮，无取消按钮（见 `02_tech.png`）。进行中被拒时不扣钱，读档续跑不重复收费由 `RestoreResearch` 注释与既有存档 v2 测试保证。

## 四、GUI 逐屏目检

截图目录：`Logs/acceptance_20261102/`

| 截图 | 内容 | 目检结果 |
|---|---|---|
| `01_hud.png` | 对局 HUD 全景 | 资源栏/人口/小地图/建造栏/兵种栏布局正常，无重叠、无出屏 |
| `02_tech.png` | 选中指挥中心 + 科技条 | 科技三按钮、研究中倒计时、维修按钮均在框内，无取消按钮（符合设计） |
| `03_pause.png` | ESC 暂停菜单 | 标题描边生效、五按钮居中、存档时间戳正常 |
| `04_confirm.png` | 退出确认弹窗 | 三按钮居中、遮罩层级正确 |
| `05_gameover.png` | 结算面板（强制显示并填标题） | 「胜利！」居中，「再来一局」「回主菜单」标签居中不溢出 |
| `06_loading.png` | 加载界面 | 标题描边、三兵种帧动画、进度条 + 暗色 Track、提示文字均正常 |
| `07_mainmenu.png` | 主菜单 | 标题/副标题/规则板/三按钮/部队图标正常，设置齿轮在木牌内 |

**发现的 Minor 观感问题（均未修，待用户决定）**：

1. **选中面板标题是英文预制体名**（`Lancer_Blue`、`Castle_Blue`），与全中文界面不一致。生产配方当年已中文化，选中面板这条没跟上。
2. **兵种面板的「需兵营 / 需靶场 / 需修道院 / 需民房」红字压在面板下边框上**（`crop_units.png`），下缘被边框吃掉一部分，可读性差。
3. **「攻击」按钮文字右端贴到按钮右边框**（`crop_sel.png`），图标居左、文字偏右，视觉上有裁切感。
4. **枪兵头像只见一杆长枪**，本体在方形头像里过小（`crop_units.png` 第二槽），与其他兵种头像不一致。

另：主菜单规则仍写「H 驻守　1~6 建造　Esc 取消建造」，`07_mainmenu.png` 已实证 —— 这是既有 backlog 第①项（quickBuild 已清空），不是新发现。

## 五、现场还原证明

- 验收前备份了**真实用户存档**（槽里原有 424,419 字节存档，非空槽）：`Logs/acceptance_20261102/user_save_backup.json`。
- 验收结束后原样写回并核 MD5：`0d34d758ec3bd3eb7ee954eebf411076`，与验收前完全一致。
- 场景 MD5 前后不变：`SampleScene.scene d324369ebd8a183be9e431d36b69da64`、`MainMenu.scene 5c2c94df935aa3cf554bbd6c9a16f446`。
- `git status --short` 无输出（本报告为新建文档，写入前核对）。
- 编辑器 stopped、`dirty=false`、控制台 `lastErrors` 为空。
- 探针用的塔/城堡/弓手命名对象只存在于 PlayMode，退出即消失，未落盘。
