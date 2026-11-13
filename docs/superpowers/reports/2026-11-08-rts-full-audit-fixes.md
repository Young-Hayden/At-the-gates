# 《兵临城下》全面体检优先级修复报告

日期：2026-11-08  
计划：`docs/superpowers/plans/2026-11-08-rts-full-audit-fixes.md`  
范围：按审计优先级修复功能问题、完成两项性能优化，并处理无需写场景的 P2/潜伏项。没有提交 git。

## 1. 交付摘要

- AI 攻击波改为真正的 `AttackMove`，支持交战后续走和新建筑挡路后重寻路。
- 普通送货在卸货点失效时停止旧路径，到达时再次校验，货物保留到替代卸货点。
- 存档使用原子替换并保留 `.bak`；直接内存 API 的 null 容器被拒绝；恢复器用 `try/finally` 保护时间缩放。
- `UnitLimits` 从每帧重算改为变更失效缓存，并补齐单位出生/死亡/驻塔/读档失效。
- AI 选址共享一次单位占格快照，保留原 `Fits` 私有签名兼容既有反射测试。
- 混合选择右键资源与敌人重叠处时，工人采集、士兵同时收到显式攻击。
- 结算后关闭暂停菜单保持 0 倍速；AI 队列取消退回 AI 银行；新游戏清除陈旧 pendingLoad。
- 新增 12 项 PlayMode 回归；功能缺陷均先观察旧代码失败，再实现并复测通过。性能项使用前后微基准。

最终全量结果与指纹见 §5（运行结束后填写）。

## 2. 功能修复与红绿证据

### 2.1 AI 攻击波

**位置**：`AIController.cs` 的 `SendTo`。  
**改法**：有 `CombatAI` 时先 `OrderAttackMove(dest)`，再让 mover 使用 `ai.CompleteOrder` 作为到达回调；无 CombatAI 保持旧路径。

| 回归 | 修复前 | 修复后 |
|---|---|---|
| `AiWave_EnemyKilled_ResumesWaveDestination` | 交战后未继续，距错误假设的目标格 9.75 | 1.258 s，PASS |
| `AiWave_PathBlocked_RepathsAround` | 挡路后距目标格 7.0 | 1.111 s，PASS |

测试自审发现 `SendTo` 本来就把目的格设为“目标 5 格内离攻击者最近处”，不是目标格本身。随后把断言改为 `NearestWalkableTo` 算出的**实际派发目的格**；没有通过放宽距离让测试变绿。修正断言后分别于 1.258 s、1.111 s 到达。

### 2.2 普通卸货点失效

**位置**：`Gatherer.cs` 的 `StepToDropoff` / `OnReachedDropoff`。  
**改法**：失效时 `mover.Stop()`；到达回调在清货物前重新检查存活、完工和 `isDropoff`，失败则保留 carrying 并重新等待。

`Worker_NormalDropoffDestroyedMidRoute_KeepsCargoUntilReplacement`：
- 修复前：唯一基地销毁后，carrying 由 5 变 0，测试按预期失败。
- 修复后：旧基地不扣货、不入账；替代基地建立后只入账 5 木一次，7.769 s，PASS。

### 2.3 存档硬化

**位置**：`SaveGameService.cs`。

1. 已存在正式档时 `File.Replace(temp,path,path+".bak")`，不再先 Delete 唯一旧档。
2. buildings/units/ai/bushes 为 null 时，内存 API 在写入/恢复前给出明确错误。
3. `RestoreSnapshot` 的 0 倍速区段使用 `try/finally`，异常也恢复原 timeScale。

| 回归 | 旧代码表现 | 修复后 |
|---|---|---|
| `NullContainers_AreRejectedBeforeWriting` | null 被接受 | 四个列表均拒绝，PASS |
| `RestoreNullContainer_ReturnsFalseAndPreservesTimeScale` | `NullReferenceException` at line 369 | 回调失败、错误明确、timeScale=1，PASS |
| `RewritingExistingSave_KeepsPreviousFileAsBackup` | `.bak` 不存在 | 正式档 222、备份 111，均可读取，PASS |

**重要更正**：编写文件级测试时证实 `JsonUtility.ToJson` 把 null 列表写成 `[]`，`FromJson` 也会把 JSON null 归一化为空数组。因此本轮实际文件不会出现旧探针手工构造的 null 列表。原审计中的“损坏 JSON 文件已复现”已撤回并降级为**直接内存 API 加固**；原子换档和 `try/finally` 仍保留。

### 2.4 混合选择、暂停、退款与新局

| 回归 | 修复前 | 修复后 |
|---|---|---|
| `MixedSelection_EnemyOnResourceOutsideAggro_WorkerGathersAndSoldierAttacks` | 士兵 Target=null | 工人采集、士兵 Target=敌人，PASS |
| `Close_AfterMatchEnded_KeepsTimeFrozen` | timeScale=1 | timeScale=0，PASS |
| `CancelLast_RedFactoryOrder_RefundsAiInsteadOfPlayer` | AI 仍 920，玩家收到退款 | AI 回到 1000，玩家不变，PASS |
| `StartGame_ClearsPendingLoad` | ConsumePendingLoad=true | false，PASS |

## 3. 性能优化与复测

### 3.1 UnitLimits 变更失效缓存

**改法**：删除按 `Time.frameCount` 强制清空名簿的逻辑；缓存只被 `Invalidate` 清除。`UnitActor.OnEnable/OnDisable/Die/RestoreSaveState` 补齐失效，队列、生产、建筑毁坏沿用既有 `Population.Invalidate → UnitLimits.Invalidate`。

`RosterCache_SurvivesFrames_AndDeathInvalidatesImmediately`：
- 修复前：跨帧字典引用不同，测试失败。
- 修复后：无变化时引用相同；死亡当帧数量变 0 且字典重建，PASS。

真实 SampleScene、正常 HUD、正常小地图、timeScale=1：

| 指标 | 修复前 | 修复后 |
|---|---:|---:|
| 稳态 GC | 约 153.3 KB/帧 | **8.20 KB/帧** |
| 15 秒平均帧时间 | 正常三段均值约 5.413 ms | **5.348 ms** |

GC 降幅是实测结果；平均帧时间没有同等幅度变化，不能宣传为帧率翻倍。原始日志：`Logs/fix-2026-11-08-performance-after.txt`。

### 3.2 AI 选址单位快照

**改法**：`FindSite` 每次只构造一次 `HashSet<Vector3Int>`；共享的 `FitsWithUnits` 做地形验证后查集合。原 `Fits` 签名保留，单次调用时自建集合，避免旧反射测试出现 `AmbiguousMatchException`。

| 回归/基准 | 结果 |
|---|---|
| `AiFindSite_UnitOnFirstCandidate_SelectsAnotherSite` | PASS |
| `AIPlacementValidation_RejectsUnitInsideFootprint` | PASS，原反射签名兼容 |
| House_Red `FindSite`，真实地图 | **17.8–20.0 ms → 1.356 ms/次** |

选址结果仍跳过有单位的首选位置；地形、坡度、草丛、高度和建筑间距规则未改。

## 4. 未包含的场景 UI 项

没有写任何场景或预制体，因此以下历史观感项不在本轮代码修复内：英文预制体名、兵种提示压边框、攻击按钮文字贴边、枪兵头像构图，以及主菜单“1~6 建造”旧文案。若要处理，须先按用户风控闸给出场景改动风险/收益表。

小地图降频也未实施：它属于视觉折衷，需用户确认可接受的刷新率；本轮只修复不依赖场景改动的 Top 5 与代码边界项。

## 5. 最终验证与现场

- **全量 PlayMode：264/264 通过，0 失败、0 跳过，207.320 s**（原 252 + 新 12）。
- 全量日志：`Logs/fix-2026-11-08-full-264.txt`，MD5 `366f569c299766ae4a17c048c47f01d6`。
- 修复后真实场景 **300 游戏秒长局**：0 错误、0 警告；真实 AI 波次出现 5 个 AttackMove 命令。
- 每 30 游戏秒独立遍历单位、驻塔和队列重算名簿，与缓存比较：**所有采样点、所有阵营和职业均 0 不一致**。日志 `Logs/fix-2026-11-08-live-smoke.txt`。
- 性能复测日志：`Logs/fix-2026-11-08-performance-after.txt`。
- 编辑器最终 stopped，SampleScene 非 dirty，未在编译，状态读取 `lastErrors=[]`。

| 对象 | 最终 MD5 | 状态 |
|---|---|---|
| SampleScene | `d324369ebd8a183be9e431d36b69da64` | 与审计前一致 |
| MainMenu | `5c2c94df935aa3cf554bbd6c9a16f446` | 与审计前一致 |
| ModeSelect | `56e86ab40659949cd117d57b2f825a2c` | 与审计前一致 |
| 真实存档（424,419 字节） | `0d34d758ec3bd3eb7ee954eebf411076` | 与审计前一致，未写档 |

`git diff --check` 无输出。工作区只包含本计划/两份报告、9 个授权运行时脚本和 8 个测试文件的修改；没有场景、预制体、真实存档或提交变化。

## 6. 仍需用户验收

- AI 波次现在会真正续走和绕行，实战压力可能高于旧版本；需要实玩确认平衡。
- 混合选择新语义、Ctrl+数字、Shift+右键、ESC 等仍需真人键鼠验收。
- 性能数字来自本机编辑器，不是独立播放器。
- 场景 UI 观感项与小地图降频未做，原因见 §4。
