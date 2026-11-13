# 弓手、塔驻防与工人验收报告

- 日期：2026-10-18
- 分支：`feature/archer-tower-worker`
- 基线：`404b38b`（Task 1-3 已完成并通过评审）
- 场景：`Assets/Scenes/SampleScene.scene`

## 结果摘要

| 项目 | 结果 |
| --- | --- |
| 全量编译 | 0 errors / 0 warnings |
| PlayMode 套件 | **45 passed / 0 failed / 0 skipped / 0 inconclusive** |
| Project Doctor | Errors=1（仅已接受的 `Decor/Tree1` Footprint），Warnings=0 |
| Doctor 分类 | Footprint=1，Terrain/Ramp/Duplicate/Manager/MissingRef/Texture/Animation=0 |
| SampleScene SHA-256 | `291944e4b26ab0d5d2bebd167d00a98b3ce713ba3424b03b623e3f9bb4743bda`（视觉检查前后一致） |
| SampleScene dirty | `false` |
| Build Settings | MainMenu / ModeSelect / SampleScene，均 enabled |
| 塔顶偏移 | `garrisonLocalOffset` 由 `(0, 3.15)` 校准为 `(0, 2.92)` |

## 1. 编译

用 Tuanjie MCP 执行了强制全量重编译：

- `CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache)`
- `AtTheGates.Runtime.dll` 与 `AtTheGates.PlayModeTests.dll` 均于 2026-10-18 18:52:20 重新生成。
- 编译后 `get_compilation_summary`：`isCompiling=false, errors=0, warnings=0, success=true`。
- Bee 构建日志中没有任何 `error CS` / `warning CS` / `Compilation failed` 行，所有报告了退出码的 Csc 节点均为 `exitcode: 0`。
- 说明：编辑器启动时包 `com.unity.2d.tilemap` 会打印
  `ScriptableSingleton already exists... (GridPaintingState.cs:92)`。
  Tuanjie 桥接层把它计入 console 的 error 数；它不是编译器诊断，清空 console 后
  summary 为 0/0。Task 2/3 已记录同一既有现象。

## 2. 全量 PlayMode 测试

通过菜单 `Tools/Regression/Run PlayMode Tests` 运行。`Logs/RegressionTests.txt`
由 `RegressionTestMenu.WriteLog` 在运行过程中直接写入，不是事后整理：

```
[Regression] PlayMode run started. Test cases: 45
...
[Regression] PlayMode run finished: Passed. Passed=45, Failed=0, Skipped=0, Inconclusive=0, Duration=28.153s
```

套件包含全部既有与新增覆盖：

- `MovementAndCommandTests`（12）
- `PlacementTests`（6）
- `ProductionDependencyTests`（3）
- `SceneLifecycleTests`（1）
- `TowerGarrisonTests`（11）
- `WorkerBehaviorTests`（12）

合计 45。无 skipped / inconclusive。

## 3. 生产依赖验证

测试：

- `ProductionDependencyTests.Archery_RemainsTheArcherFactory` — Passed
- `ProductionDependencyTests.Barracks_DoesNotProduceArchers` — Passed
- `ProductionDependencyTests.Barracks_KeepWarriorAndLancerProducts` — Passed

预制体直查：

- `Assets/Prefabs/Units/Pawn_Blue.prefab`：`isCombatant: 0`、`attackDamage: 0`、`projectile` 为空。
- `Assets/Prefabs/Buildings/Knights/Barracks_Blue.prefab`：产品只有战士、枪兵。
- `Assets/Prefabs/Buildings/Knights/Archery_Blue.prefab`：产品为弓手。

## 4. 工人撤退/返工验证

以下用例均 Passed：

- `Worker_Damage_InterruptsWork_AndPreservesCargo`
- `Worker_ReachesBase_DeliversCargoExactlyOnce`
- `Worker_RetreatBaseDestroyedMidRoute_DoesNotDeliverAtRubble_ThenFindsReplacement`
- `Worker_NoBase_WaitsInRetreat_ThenFleesWhenBaseBuilt`
- `Worker_SafeAtBase_ResumesWorkAfterThreatClears`
- `Worker_ManualIdle_StaysManualAfterSafe`
- `Worker_ExplicitGatherOrClear_CancelsRetreat`
- `Worker_OrdinaryMove_ClearsRetreatAndSetsManualIdle`
- `Worker_HasNoCombatDamage_AndCannotAcceptAttackOrder`
- `WorkerPrefabs_HaveNoCombatStats`
- `ThreatQuery_IgnoresNoncombatantsAndUnarmedBuildings`
- `Hud_Noncombatant_HidesAttackStats`

## 5. 塔上显示与塔毁恢复验证

以下用例均 Passed：

- `Garrison_ShowsRealArcherOnTower_AndUntargetsIt`
- `EnemyInRange_ArcherPlaysAttackAndFacesTarget`
- `EnemyInRange_ProjectileSpawnsAboveTowerAtArcher`
- `Garrison_TowerRemainsDamageable`
- `GarrisonedArcher_IgnoresIncomingArrow_AndIsDamageableAfterRelease`
- `GarrisonedArcher_IsNotSelectable_AndCannotManASecondTower`
- `Ungarrison_RestoresArcherState_AndReturnsItToTheField`
- `TowerWreck_LeavesArcherOnWalkableGround_CombatReady`
- `TowerDestroyed_ArcherIsReleasedBeforeTheObjectDisappears`
- `TowerDemolished_ArcherSurvivesOnWalkableGround`
- `SceneReload_DoesNotResurrectGarrisonedArcher`

## 6. Project Doctor 与场景完整性

`Tools/Project Doctor/Run Audit` 在 SampleScene 上的输出：

```
Project Doctor
Scene: Assets/Scenes/SampleScene.scene
Errors: 1  Warnings: 0
Summary: Footprint=1 Terrain=0 Ramp=0 Duplicate=0 Manager=0 MissingRef=0 Texture=0 Animation=0

[Footprint] Decor/Tree1 | root cell (4, 18) | computed cells (3, 18) | root cell is outside the computed footprint
```

唯一问题是已接受的 `Decor/Tree1` Footprint，其余类别全部为 0。

场景完整性：

- 视觉检查前 SHA-256：`291944e4b26ab0d5d2bebd167d00a98b3ce713ba3424b03b623e3f9bb4743bda`
- 视觉检查后 SHA-256：`291944e4b26ab0d5d2bebd167d00a98b3ce713ba3424b03b623e3f9bb4743bda`（未变化）
- 相对基线 `d6e5955` 的全部差异只有 Task 1 的一行：
  `requiresHint: "需兵营"` → `requiresHint: "需靶场"`（弓手提示改为需要靶场）
- 打开与审计后活动场景为 SampleScene，`dirty=false`，无保存行为。
- 视觉检查未向 SampleScene 写入任何对象；临时对象只存在于进入 PlayMode 后的
  隔离场景 `Assets/Tests/Scenes/RegressionTests.scene` 运行时，退出 PlayMode 后全部消失。

Build Settings 仍为三个正式场景，均 enabled：

1. `Assets/Scenes/MainMenu.scene`
2. `Assets/Scenes/ModeSelect.scene`
3. `Assets/Scenes/SampleScene.scene`

## 7. 塔顶视觉校准

使用实际的 `Tower_Blue.prefab` 与 `Archer_Blue.prefab`，在隔离的 RegressionTests
场景中以 PlayMode 运行（塔位于 `(3.50, 3.00)`，弓手驻塔）：

校准前 `garrisonLocalOffset=(0, 3.15)`：弓手脚底与塔顶可见轮廓之间约有 0.4 世界单位
（约 54 px）的空隙，明显悬空。

校准后 `garrisonLocalOffset=(0, 2.92)` 的实测：

- 塔根：`(3.50, 3.00)`
- 弓手（塔的子物体）世界坐标：`(3.50, 5.92)`，`localPosition=(0, 2.92)`
- 弓手精灵启用，排序 `990`；塔精灵排序 `970` → 弓手绘制在塔身之上，未被遮挡。
- 弓手可见脚底约为世界 y=5.911；塔顶后侧雉堞约为 y=5.857，脚底与雉堞间隙约
  0.054 世界单位（约 7 px），视觉上已落在塔顶。
- 射击帧的动画状态为 `Attack`，连续四帧的 normalizedTime 为
  `0.00 / 0.26 / 0.50 / 0.75`；弓手区域像素在帧间有 8k-13k 像素变化，确认动画在播放。
- 箭矢首次被观测到位于 `(3.49, 6.71)`；实现中发射点为弓手位置 `+0.8`，即
  `(3.50, 6.72)`，位于弓手胸前、塔顶可见轮廓之上，随后向左飞向目标。
- 证据截图（本地 `Logs/`，不进版本库）：
  `Logs/VisualCheck_Final292_Attack_0.png` 至 `_3.png`。

该偏移只改 `TowerGarrison.garrisonLocalOffset` 默认值；`Tower_Blue/Tower_Red`
预制体未序列化该字段，预制的 MonoBehaviour 未改动。修改默认值后对两个塔预制体做了
一次强制 reimport，使 Tuanjie 的 prefab 导入缓存重新采用新默认值（否则运行时会继续
报告旧值 3.15）。测试中固定的期望值同步更新为 2.92。

## 8. 提交

分支已有提交（Task 1-3 及评审修复）：

- `3227c67` feat: make workers noncombatant and add retreat behavior
- `81229e9` fix: abort retreat when the destination base is lost
- `7eeb35f` feat: show real archer on garrisoned towers
- `e0c5f93` fix: make garrisoned archers immune to damage
- `404b38b` fix: keep garrisoned archers out of selection and second towers

本次验收新增：

- `6ce6219` fix: seat garrisoned archer on the tower top
  （`Assets/Scripts/TowerGarrison.cs`、`Assets/Tests/PlayMode/TowerGarrisonTests.cs`）
- 本报告的 `docs:` 提交（紧随 `6ce6219`）

## 9. 未执行项与边界

- 未做压力测试/长时间 soak（大量单位、多塔同时开火、完整对局）。
- 未做完整比赛流程验证（经济、建造、AI 生产、胜负条件的长程联动）。
- 视觉检查使用隔离场景中的静止坦克目标，未包含 UI 遮挡、镜头缩放和实际对局镜头。
- 未评估性能（弹道数量、Animator 开销、排序开销）。
- 未改动塔/弓手/工人的战斗数值，也未处理既有 Minor 发现。
