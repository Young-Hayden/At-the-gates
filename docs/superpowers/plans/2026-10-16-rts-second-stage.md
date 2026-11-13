# 兵临城下第二阶段改造实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 完成民房专属工人、固定金矿、实时小地图、驻守、近战命中时序和草丛修复，并以完整对局证明没有破坏既有功能。

**Architecture:** 六个独立任务依次交付：备份/基线、经济、草丛、小地图、驻守/伤害、总回归。每项先用当前行为或失败探针建立基线，再做定向修改；禁止全量重建 HUD、动画和装饰层。

**Tech Stack:** 团结引擎 2022.3.62t5、Unity 2D/UGUI/Animator、C#、Codely Bridge MCP。

## Global Constraints

- 严格依据 `docs/superpowers/specs/2026-10-16-rts-second-stage-design.md`。
- 事项 6、人口、肉经济、编队、A-move、桥、哥布林均不在本次范围。
- 项目不是 Git 仓库，不初始化 Git；备份放在项目根 `Backups`，不放在 `Assets`。
- 不全量运行 `BuildHUD`、`BuildUnitAnimation`、`BuildPawnAnimation`、`Garnish.Run` 或 `DecorateMap.Run`。
- 一次性探针只放 `Temp`，完成后删除；Play Mode 中的测试对象不得保存进场景。

## 文件结构

- 修改：`Assets/Prefabs/Buildings/Knights/House_Blue.prefab`、`House_Red.prefab`、`Castle_Blue.prefab`、`Castle_Red.prefab`。
- 修改：`Assets/Editor/BuildHUD.cs`、`Assets/Scripts/CombatAI.cs`、`Assets/Scripts/SelectionManager.cs`、`Assets/Scripts/Unit.cs`、`Assets/Editor/Garnish.cs`。
- 修改：`Assets/Prefabs/Garnish/Bush1.prefab` ～ `Bush4.prefab`、Blue/Red Warrior 和 Lancer 的预制体/控制器/新 Hold 剪辑、Blue/Red Pawn 预制体。
- 修改：`Assets/Scenes/SampleScene.scene`。
- 新建：`Assets/Scripts/Minimap.cs`。
- 新建：`Assets/Editor/SecondStageEconomy.cs`、`Assets/Editor/FixBushGrounding.cs`、`Assets/Editor/BuildMinimap.cs`、`Assets/Editor/AddHoldAnimations.cs`，便于定向复跑和审查。
- 新建后删除：`Temp` 下的局部验证探针和截图。

---

### Task 1: 备份并记录现状基线

**Files:**
- Create: `Backups/2026-10-16-second-stage/`
- Read: 本计划文件结构列出的全部现有文件

**Interfaces:**
- Produces: 修改前可恢复副本和基线结果，后续任务不得覆盖备份。

- [x] **Step 1: 复制修改目标到项目外导入区之外的备份目录**

用一次性 Node 文件操作创建 `Backups/2026-10-16-second-stage`，保持相对目录结构，复制本计划“文件结构”中当时已经存在的文件。`Minimap.cs` 和四个新 Editor 脚本尚不存在，不伪造备份。

- [x] **Step 2: 记录经济、动画、草丛和编译基线**

通过只读 C# 输出并保存到 `Backups/2026-10-16-second-stage/baseline.txt`：

```text
Castle=工人, House=无生产, 工人提示=需指挥部
金矿入口=HUD/quickBuild/AI/DefaultBuildList
Bush 底部离地=0.116~0.194, 重复坐标=32, 额外重复=39
Warrior/Lancer 无 Holding 参数和 Hold 状态
近战 Attack 当帧扣血
```

同时确认事项 1 的恢复探针结果仍为全部通过、编辑器停止、场景非 dirty。若基线与这些已验证事实不符，先停止并查明差异，不继续修改。

---

### Task 2: 民房专属工人与固定地图金矿

**Files:**
- Create: `Assets/Editor/SecondStageEconomy.cs`
- Modify: `Assets/Editor/BuildHUD.cs:19-27`
- Modify: 四个 House/Castle prefab 和 `SampleScene.scene`
- Test: `Temp/EconomyProbe.cs`

**Interfaces:**
- Consumes: 既有 `ProductionBuilding.Product`、`HUD.FillUnitOffers`、`BuildingPlacer.quickBuild`、AI `buildOrder`。
- Produces: `House_Blue/Red` 是各自 Pawn 的唯一生产建筑；金矿不再有任何建造入口。

- [x] **Step 1: 写并运行修复前经济探针**

探针使用 `AssetDatabase` 和场景引用，不断言猜测，直接输出：Castle/House 产品表、工人 `requiresHint`、四个金矿入口和红蓝矿节点数。预期修复前为“Castle 有工人、House 无、提示需指挥部、金矿入口均存在、场景金矿=1”。

- [x] **Step 2: 编写定向迁移 Editor 脚本**

`SecondStageEconomy.Apply()` 必须执行且只执行以下操作：

```csharp
// 对每个 colour = Blue, Red：
// 1. 读取 Castle 的 Pawn 产品，深拷贝 label/unitPrefab/icon/cost/buildSeconds/queueLimit。
// 2. 在 House 添加或更新 ProductionBuilding，使 products 只含该 Pawn，queueLimit 保持 Castle 原值。
// 3. 从 Castle 移除 ProductionBuilding，而不改变 Building、SpriteRenderer、Collider 或动画。
// 4. 从当前 HUD buildPrefabs/buildButtons/buildButtonImages 删除 GoldMine 索引并销毁对应 Build 按钮；
//    BuildBar 宽度改为 6 * 74 + 20 = 464。
// 5. 从 BuildingPlacer.quickBuild 和 AIController.buildOrder 删除所有 GoldMine_Active。
// 6. 若红矿不存在，在 (35.5, 6, 0) 实例化 GoldMine_Active，命名 Mine_Gold_Red，team=Neutral。
// 7. 保存 prefab 和 SampleScene；脚本可重复运行，不重复加产品或红矿。
```

同步从 `BuildHUD.DefaultBuildList` 删除这一行：

```csharp
"Assets/Prefabs/Resources/GoldMine_Active.prefab",
```

场景中的 `unitOffers` 保留现有顺序，只把 Pawn 那一项的 `requiresHint` 改为 `需民房`；其余报价、图标、价格和顺序保持原值。以后若有人主动重建 HUD，`BuildHUD.FillUnitOffers` 仍会从新的 House 产品表自动生成相同解锁规则。

- [x] **Step 3: 编译并运行静态经济验收**

预期全部成立：

```text
House_Blue/Red products=Pawn only, queueLimit=5
Castle_Blue/Red ProductionBuilding=null
工人提示=需民房
HUD/quickBuild/AI/DefaultBuildList 均无 GoldMine
场景 Gold 节点=2，位置分别为 (-2.5,6) 和 (35.5,6)
AI 仍有 2 座 House_Red
```

- [x] **Step 4: 运行态验证生产和采集**

在无完工 House 时工人按钮不可下单；放置并完成 House 后可以生产对应阵营 Pawn；Castle 无生产面板和工人产品。红蓝工人分别从两座固定矿采集并将金送入各自库存。原有兵营、靶场、修道院产品和 AI 建造继续进行。

---

### Task 3: 修复草丛接地点、遮挡和同格重复

**Files:**
- Create: `Assets/Editor/FixBushGrounding.cs`
- Modify: `Assets/Editor/Garnish.cs:51-83,127-133,227-240`
- Modify: Bush1～Bush4 prefab 和 `SampleScene.scene`
- Test: `Temp/BushProbe.cs`、上方/同格/下方三张截图

**Interfaces:**
- Consumes: `Sprite.bounds`、现有 `YSort.OrderFor` 和 `Garnish.Place`。
- Produces: 草丛根部即地面点；Bush offset=-8；场景无重复坐标；生成器不会再次同格放草。

- [x] **Step 1: 保存修复前量化结果**

记录四种 Bush 的 `bottomAboveRoot`、同格草丛与单位的 sortingOrder、重复坐标数，并保留已复现的同格截图。预期重复坐标=32、额外实例=39，同格草 order 高于单位。

- [x] **Step 2: 修正生成器的接地点和重复规则**

把 `MakeProp` 中的高度一半计算替换为实际边界计算：

```csharp
float lift = -spr.bounds.min.y * scale;
child.transform.localPosition = new Vector3(0f, lift, 0f);
child.transform.localScale = Vector3.one * scale;
// ...
ys.baseOrder = order + Mathf.RoundToInt(lift * 10f);
ys.offset = ysortOffset;
```

四个 Bush 的 `ysortOffset` 全部改为 `-8`。在成功 `Place` 一丛草后立即 `occupied.Add(c)`，使后续循环跳过该坐标；不改变水岩、云、树或建筑逻辑。

- [x] **Step 3: 定向修复现有 prefab 和场景，不重跑散布**

`FixBushGrounding.Apply()` 对 Bush1～Bush4 逐一重新计算 `lift`、子物体位置和 YSort 基准，设置 offset=-8 并保存 prefab。然后按 `Garnish` 下每个 Bush 根节点世界坐标分组，保留每组第一个，销毁其余实例并保存场景。预期删除 39 个，不重排剩余草丛。

- [x] **Step 4: 运行数值和视觉验收**

预期：

```text
四种 Bush bottomAboveRoot 绝对值 < 0.001
重复坐标=0，额外重复=0，Collider 数量=0
同格（单位根部=草根部+0.5）：unit order > bush order
上方 1.5 格：bush order > unit order
下方 0.5 格：unit order > bush order
```

截取上方、同格、下方三张隔离图，确认不再浮空、同格不盖头、从上方经过仍有合理遮挡；单位穿过草丛的移动和寻路不受影响。

---

### Task 4: 增加实时小地图

**Files:**
- Create: `Assets/Scripts/Minimap.cs`
- Create: `Assets/Editor/BuildMinimap.cs`
- Modify: `SampleScene.scene`
- Test: `Temp/MinimapProbe.cs` 和游戏视图截图

**Interfaces:**
- Consumes: `RTSCamera.mapBounds`、主 Camera、`RectTransformUtility`。
- Produces: `Minimap : MonoBehaviour, IPointerClickHandler`，运行时创建第二相机和 RenderTexture；主相机控制逻辑不改。

- [x] **Step 1: 编写 Minimap 组件**

组件要求挂在小地图 RawImage 上，公开 `mainCamera`、`rtsCamera`、`viewport`、`textureWidth=900`、`textureHeight=500`。实现以下完整行为：

```csharp
// Awake：缓存 RawImage/RectTransform，引用主相机和 RTSCamera；创建名为
// "Minimap Camera" 的正交相机，depth=-10、clearFlags=SolidColor、
// allowHDR/allowMSAA=false，并创建 900x500、FilterMode.Point 的 RenderTexture。
// LateUpdate：按 RTSCamera.mapBounds 和纹理宽高比让全图恰好进入视野；
// 用主相机 centre ± orthographicSize/aspect 计算视野框的归一化 anchors。
// OnPointerClick：仅左键；把 RawImage 内 local point 转成 0..1 UV，
// 再映射到 mapBounds，设置主相机 x/y，z 保持 -10，由 RTSCamera 下帧钳制。
// OnDestroy：释放 RenderTexture、销毁第二相机、清空 RawImage.texture。
```

视野框使用一个无 Image 的 RectTransform 容器和四个 raycastTarget=false 的细条 Image，不能用会遮住小地图的实心半透明矩形。

- [x] **Step 2: 定向添加 UI，不重建 HUD**

`BuildMinimap.Apply()` 在 `HUD Canvas/Root` 下创建或更新 `MinimapPanel`：右上角 `(-16,-178)`、外框 `340x204`、内部 RawImage `320x180`。使用现有 `Carved_9Slides` 外框，关联主相机、RTSCamera 和视野框，保存场景。若重复运行，不重复创建相机、面板或视野条。

- [x] **Step 3: 验证显示、实时性和输入隔离**

预期：RenderTexture 非空且正在渲染；全图无拉伸；移动任一单位后小地图位置变化；主相机平移/缩放时视野框同步；点击小地图后主相机移到对应区域且不越界；点击不会选中世界单位、发出移动或放置建筑。原 WASD、滚轮和边缘平移继续工作。

---

### Task 5: 驻守和近战命中时序

**Files:**
- Create: `Assets/Editor/AddHoldAnimations.cs`
- Modify: `Assets/Scripts/CombatAI.cs`、`Assets/Scripts/SelectionManager.cs`、`Assets/Scripts/Unit.cs`
- Modify: Blue/Red Warrior、Lancer、Pawn prefab；Warrior/Lancer controller；新建 Hold clips
- Test: `Temp/CombatProbe.cs`

**Interfaces:**
- Produces: `CombatAI.OrderHold()`、`UnitActor.attackHitDelay`、Animator `Holding` bool。
- Consumes: 既有 `OrderAttack`、`ClearOrder`、冷却和 `LastAttackTime`。

- [x] **Step 1: 写修复前战斗探针**

预期修复前：没有 `OrderHold`；Warrior/Lancer 无 Holding/Hold；近战在 `Attack()` 返回时目标已经掉血。平地追击、远程投射物和原 Attack 状态作为对照必须正常。

- [x] **Step 2: 实现驻守行为**

在 `CombatAI` 增加 `bool holdPosition` 和 `Holding` 参数支持：

```csharp
public void OrderHold()
{
    holdPosition = true;
    forced = false;
    target = null;
    lastGoalCell = new Vector3Int(int.MinValue, int.MinValue, 0);
    mover.Stop();
}
```

`OrderAttack` 和 `ClearOrder` 必须设 `holdPosition=false`。目标搜索和丢失距离在驻守时使用 `unit.attackRange`，非驻守仍用 `unit.aggroRange`；驻守且距离大于攻击范围时停止并不执行 `Chase`。`SelectionManager` 在未处于建筑摆放、指针不在 UI 上且按 H 时，对选中的存活战斗单位调用 `OrderHold`。

- [x] **Step 3: 定向添加 Holding 动画，不全量重建**

`AddHoldAnimations.Apply()` 对 Blue/Red：

- 从现有 Attack clip 复制 Sprite EditorCurveBinding，创建 Warrior Guard（6 帧、12fps、循环）和 Lancer 五方向 Defence（每向 6 帧、12fps、循环）Hold clips。
- 在现有 controller 添加 bool `Holding`，不全量删除参数或状态。
- Warrior：Idle/Run 在 Holding=true 到 Hold；Holding=false 回 Idle；Attack 的退出转换先尝试 Hold(Holding=true)，否则走原 Idle。
- Lancer：按 Dir 进入五个 Hold 状态；每个 Attack 方向返回对应 Hold 或原 Idle。
- 保持既有 AnyState Attack、Speed、Dir、clip GUID、controller GUID 和非 Hold 状态不变；重复运行不重复状态或参数。

`CombatAI` 仅在参数存在时写 `Holding`，其他兵种不报警。

- [x] **Step 4: 延迟近战直接伤害**

在 `UnitActor` 增加：

```csharp
[Tooltip("近战从挥击开始到命中结算的秒数；0 = 当帧。远程忽略。")]
public float attackHitDelay = 0f;
```

预制体设置：Warrior Blue/Red=`1f/6f`，Lancer Blue/Red=`1f/8f`，Pawn Blue/Red=`0.30f`。`CombatAI.Attack()` 捕获当次目标，远程仍立即 Spawn；近战启动唯一协程，等待对应秒数后检查 `unit.IsAlive` 和 `!Combatants.IsGone(hitTarget)`，再执行一次 `TakeDamage` 和 `FxBank.Hit`。攻击开始仍立即设置冷却、LastAttackTime、朝向和 Attack trigger。

- [x] **Step 5: 运行战斗回归**

预期：H 后单位不移动且只打射程内敌人；右键移动/强攻取消驻守；Guard/Defence 可见，原 Idle/Run/Attack 不消失。Warrior/Lancer/Pawn 挥击当帧 HP 不变，各自延迟后只扣一次；攻击者或目标在等待期死亡时不扣血；Archer 投射物时机不变；事项 1 的四条恢复路径仍通过。

---

### Task 6: 出生区复测与完整对局

**Files:**
- Modify only if evidence requires: `SampleScene.scene`
- Create then delete: `Temp/SecondStageFinalProbe.cs` 和验证截图
- Create: `Backups/2026-10-16-second-stage/final-verification.txt`

**Interfaces:**
- Consumes: Task 2～5 的全部公开行为和事项 1 的恢复逻辑。
- Produces: 最终验证记录和无临时残留的保存场景。

- [x] **Step 1: 复测出生区**

连续观察蓝方工人到矿/树、近战出击和红方 AI 进攻。只有出现可重复的异常阻挡时才移动对应树到最近合法空格；每次移动后重跑连通性和采集/出击测试。没有明确证据则不改树。

- [x] **Step 2: 跑完整连续对局**

覆盖：建 House→工人生产；Castle 不产工人；红蓝固定矿采集入库；兵营/靶场/修道院生产；右键移动与强攻；H 驻守及取消；小地图平移/缩放/点击；Warrior/Lancer/Pawn 命中时序；Archer 远程；草丛同格与穿越；AI 建造、生产和进攻；事项 1 障碍格恢复。

- [x] **Step 3: 检查编译、控制台、动画和场景残留**

要求编译 0 错误、无本次异常；Warrior/Lancer 原 Idle/Run/Attack 与 Holding 共存；场景金矿=2、草丛重复=0、TEST/Probe 对象=0；退出 Play Mode 后场景非 dirty，`Temp` 无本次探针或截图。

- [x] **Step 4: 写最终验证记录并核对范围**

`final-verification.txt` 记录每项实际输出、是否移动了出生区树木、修改文件清单和独立审查结果。任何失败必须修复后重跑完整对局，不能只报告局部通过。
