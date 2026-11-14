# 《兵临城下》优先修复与地图精修交付报告

日期：2026-11-14  
工程：`D:\Unity\projects\At the Gates`  
基线：`main@3e5659a`  
状态：**当前工作区已交付验证；未提交、未推送。**  
最终完整 PlayMode：**286/286 PASS，0 Failed，0 Skipped，0 Inconclusive，244.442 s。**

## 1. 完成内容

| 项 | 实施结果 |
|---|---|
| 玩家判负卡死 | 只有当前价格满足且放置前置合法的建筑才计为恢复手段；无 HUD 兜底为 300 金 |
| AI 判负卡死 | 只检查 `nextBuild` 指向的下一项，跳过 null/缺 Building，与 `Build()` 顺序一致 |
| 羊中途存档 | 写档时使用最后一个已登记占地；读档无半格、无错位、资源与阻挡保持 |
| 小地图暂停输入 | 暂停和结算时点击被忽略；正常对局点击保持可用 |
| Tree1 偏移 | x 4.37→3.50，保持原阻挡格 `(3,18)`；ProjectDoctor Footprint=0 |
| 地图草地 | Garnish/Bush 99→50，按 10×10 区域最大余数配额减量；资源、水岩、海岩、云不变 |
| 恢复兜底值 | SampleScene `recoveryGoldFloor` 50→300 |

## 2. 代码与红绿证据

### 2.1 玩家/AI 可恢复性

- `Assets/Scripts/GameState.cs:192-219`：HUD 建造表同时检查价格、指挥中心唯一性、非指挥中心前置。
- `Assets/Scripts/AIController.cs:238-252`：从 `nextBuild` 开始，遇到第一个有效但买不起的项立即返回 false。
- RED：75 金旧玩家结果 `Playing`，旧 AI 判断 `true`。
- GREEN：玩家 75/299/300 + AI 75/299/300 共 6/6；既有 Wipeout 三回归 3/3。

### 2.2 羊步行中途存档

- `Assets/Scripts/SheepWander.cs:60-74`：`StableSavePosition` 由 `Building.Cells` 推导。
- `Assets/Scripts/SaveGameService.cs:221-223`：只替换步行羊存档记录的位置，不快进或修改实时对象。
- RED：旧记录 `x=19.25`，稳定占地为 `19.0`。
- GREEN：新中途存档完整 round-trip 通过；既有锚点与羊重生回归 2/2；临时文件清理为 0。

### 2.3 小地图模态输入

- `Assets/Scripts/Minimap.cs:108-109`：暂停和结算在坐标转换/相机写入前返回。
- RED：暂停和结算点击都把镜头 `(10,10,-10)` 移到 `(0,0,-10)`。
- GREEN：暂停、结算、正常点击正对照 3/3。

## 3. 场景精修对象审计

- 备份：`.superpowers/sdd/priority-fixes-map-pruning/SampleScene.before.scene`，4,287,575 字节，MD5 `24af4b31e398458e49a30f2eac1f1672`。
- Preview：49 条删除、49 条唯一，保留 50；删除清单与实际完全一致。
- 实际删除：49 个 Bush PrefabInstance + 49 个关联 stripped Transform，共 98 个 YAML 对象。
- 新增对象 0；非 Preview 删除 0；未批准修改 0；动画/AI 噪声残留 0。
- 只批准的三处内容变化：Garnish 父列表、目标 Tree1、GameState 恢复兜底值。
- 最终运行态计数：Bush50 / WaterRock94 / SeaRock28 / Cloud7；Building248 / UnitActor14 / ResourceNode159 / Tilemap8，均与基线一致。
- 最终场景：4,033,133 字节，MD5 `3c43b73c3621d3eeebc4976a8b36914d`。
- 更正说明：早期的 WaterRock=122 是 WaterRock 94 + SeaRock 28 的合计笔误；非目标对象不动的范围未变。
- 用户裁定：保留与基线完全一致的 HUD 空白块，优先于 zero-warning diff。因此 `git diff --check` 仅提示 SampleScene:10067-10068 两个基线已存在的尾空格；对象审计为 `SCENE_AUDIT_OK`，该例外已在报告中明确披露。

## 4. 完整验证

- 编译：0 errors / 0 warnings。
- 全量日志：`.superpowers/sdd/priority-fixes-map-pruning/final-full-286.log`，27,749 字节，MD5 `14aac9a2009dc6e46d57fbbcbcf5a870`；286 条 Passed、0 条 Failed。
- 12 个新增用例全部出现并通过：判负 6、羊 1、小地图 3、场景 2。
- ProjectDoctor：Errors=0 Warnings=0，所有类别 0。
- 最终 Console：0 Warning / 0 Error。
- 编辑器 stopped，SampleScene clean、project clean；临时 InitTestScene=0。
- 真实存档未写：424,419 字节，MD5 `0d34d758ec3bd3eb7ee954eebf411076`。
- MainMenu `90bb497775fd5e4c60d0f06d950145ec`、ModeSelect `56e86ab40659949cd117d57b2f825a2c` 未变。
- 截图：`Logs/prune-after-opening-1280.png`、`Logs/prune-after-central-1280.png`，1280×720 逐张 Read；草地仍分布多个区域，资源/HUD/单位无缺失。

## 5. 独立审查

- 四个任务均经过独立 spec + quality 审查并 Approved；Task 4 的 HUD 空白块 Important 已逐字节修复并复审通过。
- 最终全分支复审（独立、最 capable model）：**Ready to deliver = Yes，0 Critical / 0 Important / 3 Minor**。
- Minor：两个未使用 using；草地删除的 density 权重实际主导 spawn-distance（一次性工具，已执行的选择经 Preview/目检批准）；小地图测试使用全局 Find 而非 helper 返回引用（当前场景无干扰）。均不影响交付。

## 6. 边界

- 未做真人 OS 键鼠操作、独立播放器、其他 DPI/宽高比、长期主动运营或平衡压力测试。
- 旧存档中的草仍按其存档内容恢复；本次减量主要作用于新游戏。
- 场景证据来自对象级 YAML 审计、ProjectDoctor 与 1280×720 实图，不是独立播放器重导入。
- 按用户要求未创建 worktree、未提交、未推送。
