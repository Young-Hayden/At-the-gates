# 弓手生产、塔上驻军与工人撤退 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让弓手只能由靶场生产，驻塔时真实可见并正常射击，工人零伤害且受袭后携带资源撤回基地并在威胁解除后返工。

**Architecture:** 生产依赖通过预制体产品表与 HUD 提示数据修正。塔驻军复用原弓手对象，重挂到塔顶并临时禁用逻辑组件；工人撤退复用 `Gatherer` 的移动与任务状态机，通过 `UnitActor` 受伤事件进入撤退阶段。

**Tech Stack:** Tuanjie 2022.3.62t5、C#、Unity Test Framework 1.1.33、PlayMode、Animator、Tilemap A*。

## Global Constraints

- 弓手只能由对应阵营靶场生产；兵营只保留战士和枪兵。
- 驻塔显示真实弓手，保留 Sprite 和射击动画；驻塔期间不可选中、不可被敌人锁定。
- 箭从塔顶弓手位置发射；塔本体仍可被攻击和摧毁。
- 弓手出塔或塔毁后回到地面并恢复独立攻击能力。
- 工人 `isCombatant=false`、`attackDamage=0`、无投射物。
- 工人受袭后保留货物，逃向最近己方已完工卸货点/基地；到达后先卸货。
- 基地附近无敌方战斗单位且无敌方驻弓塔一段时间后自动返工；手动待命状态保持。
- 无己方基地时中断工作并等待，不自动返工；基地建成后撤退。
- 不新增美术，不调整塔/弓手/工人的生命、伤害、射程、成本，不做编队、塔升级或多弓手驻塔。
- 正式场景仅修改 HUD 弓手提示；其他场景数据不做计划外变更。
- 修改编辑器资产后必须请求并等待真实编译，再运行菜单或测试。

---

### Task 1: 弓手生产依赖靶场

**Files:**
- Modify: `Assets/Prefabs/Buildings/Knights/Barracks_Blue.prefab`
- Modify: `Assets/Prefabs/Buildings/Knights/Barracks_Red.prefab`
- Modify: `Assets/Scenes/SampleScene.scene`（仅 HUD Archer `requiresHint`）
- Create: `Assets/Tests/PlayMode/ProductionDependencyTests.cs`
- Create: `Assets/Tests/PlayMode/ProductionDependencyTests.cs.meta`

**Interfaces:**
- Consumes: `ProductionBuilding.IndexOfProduct(GameObject): int`、`ProductionBuilding.products`、`HUD.UnitOffer.requiresHint`。
- Produces: 兵营产品表不含弓手；靶场产品表包含弓手；HUD 弓手提示“需靶场”。

- [ ] **Step 1: 写失败测试**

```csharp
[Test]
public void Barracks_DoesNotProduceArchers()
{
    Assert.AreEqual(-1, LoadBarracks("Blue").IndexOfProduct(Load<GameObject>("Assets/Prefabs/Units/Archer_Blue.prefab")));
    Assert.AreEqual(-1, LoadBarracks("Red").IndexOfProduct(Load<GameObject>("Assets/Prefabs/Units/Archer_Red.prefab")));
}

[Test]
public void Archery_RemainsTheArcherFactory()
{
    Assert.GreaterOrEqual(LoadArchery("Blue").IndexOfProduct(Load<GameObject>("Assets/Prefabs/Units/Archer_Blue.prefab")), 0);
    Assert.GreaterOrEqual(LoadArchery("Red").IndexOfProduct(Load<GameObject>("Assets/Prefabs/Units/Archer_Red.prefab")), 0);
}
```

测试同时断言兵营仍有战士和枪兵，防止误删产品。

- [ ] **Step 2: 运行并确认失败**

Run: `Tools/Regression/Run PlayMode Tests`
Expected: `ProductionDependencyTests` 中兵营弓手断言失败。

- [ ] **Step 3: 修改产品表与 HUD 提示**

用 `PrefabUtility.LoadPrefabContents` 读取蓝红兵营，按单位预制体引用过滤掉 Archer，保存预制体。打开 `SampleScene`，将弓手 `UnitOffer.requiresHint` 设为 `需靶场`，保存并关闭；不得保存其他场景字段的额外变化。

- [ ] **Step 4: 验证生产依赖**

Run: 全量 PlayMode 测试。
Expected: 新生产测试通过，原有测试全部通过。

以只读方式重新打开 `SampleScene` 并查询 HUD，确认弓手 `UnitOffer.requiresHint == "需靶场"`；随后关闭场景，不保存。

- [ ] **Step 5: 提交**

```bash
git add Assets/Prefabs/Buildings/Knights/Barracks_Blue.prefab Assets/Prefabs/Buildings/Knights/Barracks_Red.prefab Assets/Scenes/SampleScene.scene Assets/Tests/PlayMode/ProductionDependencyTests.cs Assets/Tests/PlayMode/ProductionDependencyTests.cs.meta
git commit -m "fix: require archery range for archer production" -m "Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: 工人零伤害与受袭撤退

**Files:**
- Modify: `Assets/Scripts/Unit.cs`
- Modify: `Assets/Scripts/Gatherer.cs`
- Modify: `Assets/Scripts/IDamageable.cs`
- Modify: `Assets/Scripts/HUD.cs`
- Modify: `Assets/Prefabs/Units/Pawn_Blue.prefab`
- Modify: `Assets/Prefabs/Units/Pawn_Red.prefab`
- Create: `Assets/Tests/PlayMode/WorkerBehaviorTests.cs`
- Create: `Assets/Tests/PlayMode/WorkerBehaviorTests.cs.meta`

**Interfaces:**
- Produces: `UnitActor.Damaged` 事件；`Gatherer.IsRetreating`、`Gatherer.RetreatBase`、`Gatherer.fleeSafeRadius`、`Gatherer.fleeResumeSeconds`、`Gatherer.baseRetrySeconds`。
- Produces: `Combatants.HasThreat(Team myTeam, Vector3 from, float maxRange): bool`。
- Consumes: `Building.FindNearestDropoff(Vector3, Team)`、现有 `Gatherer.GoTo`、`Credit`、`ShowDelivery`。

- [ ] **Step 1: 写失败测试**

```csharp
[UnityTest]
public IEnumerator Worker_HasNoCombatDamage_AndCannotAcceptAttackOrder()
{
    var worker = SpawnWorker(new Vector3(0, 0));
    yield return null;
    var actor = worker.GetComponent<UnitActor>();
    Assert.IsFalse(actor.isCombatant);
    Assert.AreEqual(0, actor.attackDamage);
    Assert.IsNull(actor.projectile);
}
```

再写四个用例：

1. 工人采集时受伤，保留货物并最终 `IsRetreating=true`。
2. 到达己方基地后货物只结算一次。
3. 敌人离开安全半径并等待后，`IsRetreating=false` 且重新获得采集目标。
4. 手动待命工人受伤撤退，安全后仍 `IsManuallyIdle=true`。

另测无基地时不自动返工；基地建成后开始撤退。

- [ ] **Step 2: 运行并确认失败**

Expected: 新接口缺失或工人仍 `isCombatant=true`、受袭继续采集。

- [ ] **Step 3: 添加受伤事件**

`UnitActor` 增加：

```csharp
public event System.Action Damaged;

public void TakeDamage(int amount)
{
    if (!IsAlive || amount <= 0) return;
    health -= amount;
    RefreshBar();
    if (health <= 0) Die();
    else Damaged?.Invoke();
}
```

`Damaged` 只在非致命伤害后触发。

- [ ] **Step 4: 工人非战斗约束与 HUD**

`Gatherer.Awake` 在取得 `actor` 后：

```csharp
actor.isCombatant = false;
actor.attackDamage = 0;
actor.projectile = null;
```

同步修改蓝红 Pawn 预制体的序列化值。`HUD.RefreshSelection` 仅对 `isCombatant=true` 的单位显示攻击/射程。

- [ ] **Step 5: 实现撤退状态机**

`Gatherer.Phase` 增加 `Fleeing`、`AtBase`。新增字段：

```csharp
[Header("受袭撤退")]
public float fleeSafeRadius = 10f;
public float fleeResumeSeconds = 2f;
public float baseRetrySeconds = 1f;

bool retreating;
bool wasManualIdle;
Building retreatBase;
float baseRetryTimer;
float safeTimer;

public bool IsRetreating => retreating;
public Building RetreatBase => retreatBase;
```

`OnEnable` 订阅 `actor.Damaged`，`OnDisable` 退订。受伤回调：

```csharp
void OnDamaged()
{
    if (actor == null || !actor.IsAlive) return;
    wasManualIdle = manualIdle;
    retreating = true;
    phase = Phase.Fleeing;
    node = null;
    clearTarget = null;
    dropoff = null;
    retreatBase = null;
    baseRetryTimer = 0f;
    mover.Stop();
}
```

`StepFleeing` 每秒重试寻找 `Building.FindNearestDropoff(transform.position, UnitTeam)`；找到后按现有 `GoTo` 前往。到达回调中若携带资源，调用一次现有 `Credit` 与 `ShowDelivery`，清空货物，进入 `AtBase`，设置 `safeTimer=fleeResumeSeconds`。

`StepAtBase` 使用 `Combatants.HasThreat` 检测安全半径；有威胁则重置计时，无威胁倒计时结束后：

```csharp
retreating = false;
phase = Phase.Idle;
manualIdle = wasManualIdle;
```

`TryAutoAssign`、定时 retask、`Assign`、`AssignClear`、`StopWork` 都必须正确处理 `retreating`：

- retreating 时自动任务不得覆盖。
- 明确采集/清草命令取消撤退。
- 普通移动取消撤退并设 `manualIdle=true`。
- `manualIdle` 原状态在安全后恢复。

- [ ] **Step 6: 实现威胁查询**

在 `Combatants` 新增：

```csharp
public static bool HasThreat(Team myTeam, Vector3 from, float maxRange)
```

遍历存活且 `isCombatant=true` 的敌方 `UnitActor`；再遍历存活敌方建筑，仅当 `TowerGarrison.HasArcher` 为真时视为威胁。非战斗工人、僧侣不算威胁。

- [ ] **Step 7: 运行工人测试与既有回归**

Expected: 新用例通过；现有 19 个测试全部通过；编译 0 errors / 0 warnings。

- [ ] **Step 8: 提交**

```bash
git add Assets/Scripts/Unit.cs Assets/Scripts/Gatherer.cs Assets/Scripts/IDamageable.cs Assets/Scripts/HUD.cs Assets/Prefabs/Units/Pawn_Blue.prefab Assets/Prefabs/Units/Pawn_Red.prefab Assets/Tests/PlayMode/WorkerBehaviorTests.cs Assets/Tests/PlayMode/WorkerBehaviorTests.cs.meta
git commit -m "feat: make workers noncombatant and add retreat behavior" -m "Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: 真实弓手驻塔、射击与出塔恢复

**Files:**
- Modify: `Assets/Scripts/TowerGarrison.cs`
- Create: `Assets/Tests/PlayMode/TowerGarrisonTests.cs`
- Create: `Assets/Tests/PlayMode/TowerGarrisonTests.cs.meta`

**Interfaces:**
- Produces: `TowerGarrison.garrisonLocalOffset: Vector2`、`TowerGarrison.garrisonSortingBonus: int`；`HasArcher` 保持现有 public。
- Consumes: `UnitActor`、`Animator`、`YSort`、`SpriteRenderer`、`Collider2D`、`HealthBar`、`Projectile.Spawn`。

- [ ] **Step 1: 写失败测试**

第一个用例：

```csharp
[UnityTest]
public IEnumerator Garrison_ShowsArcherOnTower_AndRemovesItFromTargeting()
{
    var tower = SpawnTower(new Vector3(3, 3));
    var archer = SpawnArcher(new Vector3(0, 0));
    var garrison = tower.GetComponent<TowerGarrison>();
    Assert.IsTrue(garrison.Garrison(archer));
    yield return null;

    Assert.IsTrue(archer.gameObject.activeInHierarchy);
    Assert.AreEqual(tower.transform, archer.transform.parent);
    Assert.IsTrue(archer.GetComponentInChildren<SpriteRenderer>().enabled);
    Assert.IsFalse(UnitActor.All.Contains(archer.GetComponent<UnitActor>()));
    Assert.IsFalse(archer.GetComponent<Collider2D>().enabled);
}
```

再覆盖：

1. 驻塔时塔仍然可受伤。
2. 有敌人在射程内时生成 `Projectile`，位置高于塔顶、不是塔底。
3. 弓手 Animator 被触发 `Attack`，Sprite 朝目标翻转。
4. `Ungarrison` 恢复父节点、组件、碰撞、`UnitActor.All` 和 YSort/排序。
5. 塔被击毁后弓手对象仍存在、落到可行走格、CombatAI/UnitActor 重新启用且可攻击。

- [ ] **Step 2: 运行并确认失败**

Expected: 当前 `SetActive(false)` 导致弓手不可见、父节点不是塔、`UnitActor.All` 被移除但无恢复视觉/动画断言失败。

- [ ] **Step 3: 实现驻塔视觉状态**

`TowerGarrison` 新增：

```csharp
public Vector2 garrisonLocalOffset = new Vector2(0f, 3.15f);
public int garrisonSortingBonus = 20;
```

`Garrison` 缓存弓手原父节点、局部变换、组件启用状态、Collider、YSort、SpriteRenderer 排序和血条状态。随后：

- `mover.Stop()` 并清除 CombatAI 命令。
- 将弓手 `SetParent(transform, false)`，设置驻守局部位置和缩放。
- 禁用 YSort，设置 Sprite 排序高于塔。
- 禁用 UnitMover、CombatAI、UnitActor、Gatherer、Healer 和碰撞体。
- 血条隐藏。
- 弓手保持 active，SpriteRenderer/Animator 保持启用。

禁用 `UnitActor` 通过 `OnDisable` 自动退出 `UnitActor.All`，不得再手动重复移除。

- [ ] **Step 4: 实现塔顶射击表现**

塔开火时：

```csharp
if (hasAttackTrigger) archerAnimator.SetTrigger("Attack");
if (archerSprite != null)
    archerSprite.flipX = target.Transform.position.x < archer.transform.position.x;
Vector3 from = archer.transform.position + Vector3.up * 0.8f;
Projectile.Spawn(projectile, from, target, damage, projectileSpeed, tint);
```

塔攻击伤害、射程、冷却继续使用驻塔时缓存的弓手数值。塔本体生命和摧毁逻辑不变。

- [ ] **Step 5: 实现出塔和塔毁恢复**

`Ungarrison` 必须先恢复对象再重新启用逻辑：

1. 从塔下解绑并恢复原父节点、局部位置、旋转、缩放。
2. 恢复 Sprite 排序和 YSort 启用状态。
3. 恢复血条原始显示状态。
4. 重新启用 Collider、Gatherer、Healer、CombatAI、UnitMover、UnitActor。
5. 用最近可行走格放在塔附近。
6. `HasArcher=false`，清理缓存。

塔毁时 `Building.Wreck` 现有流程会先调用 `Ungarrison`；必须保证对象在塔 Destroy 前完成解绑。场景卸载时不要错误复活弓手。

- [ ] **Step 6: 运行塔测试与全量回归**

Expected: 塔测试全部通过；现有 19 个测试和生产/工人新增测试全部通过；编译 0/0。

- [ ] **Step 7: 提交**

```bash
git add Assets/Scripts/TowerGarrison.cs Assets/Tests/PlayMode/TowerGarrisonTests.cs Assets/Tests/PlayMode/TowerGarrisonTests.cs.meta
git commit -m "feat: show real archer on garrisoned towers" -m "Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: 全量验收与视觉校准

**Files:**
- Verify: `Assets/Scenes/SampleScene.scene`
- Verify: `Assets/Prefabs/Buildings/Knights/*`
- Verify: `Assets/Scripts/{Unit,Gatherer,TowerGarrison,HUD,IDamageable}.cs`
- Output: `Logs/RegressionTests.txt`、`Logs/ProjectDoctor.txt`

- [ ] **Step 1: 编译与全量测试**

Run: MCP `request_compile` → `wait_for_compile` → `get_compilation_summary` → `Tools/Regression/Run PlayMode Tests`。
Expected: 0 errors/0 warnings；全部旧测试与新增测试通过，0 skipped/inconclusive。

- [ ] **Step 2: Doctor 与场景完整性**

运行 Project Doctor。
Expected: 仍仅 `Decor/Tree1` 那一条已接受的 Footprint 问题；Terrain/Duplicate/Ramp/Manager/MissingRef/Texture/Animation 均为 0。
验证 SampleScene 除了 Task 1 的弓手提示外无计划外变动；Build Settings 仍为三个正式场景。

- [ ] **Step 3: 塔顶视觉校准**

在隔离 PlayMode 场景或临时运行时对象中创建 `Tower_Blue` 与 `Archer_Blue`，执行驻塔并截图检查：

- 弓手站在塔顶可见；
- 不被塔身遮挡；
- 箭矢从弓手附近发出；
- 射击时可见 `Attack` 动画。

必要时只调整 `garrisonLocalOffset` 默认值或塔预制体上的该字段，然后重新编译和测试。不得保存测试临时对象到 SampleScene。

- [ ] **Step 4: 最终记录**

创建 `docs/superpowers/reports/2026-10-18-archer-tower-worker-verification.md`，列出：

- 新增测试数与最终通过数；
- 生产依赖验证；
- 工人撤退/返工验证；
- 塔上显示与塔毁恢复验证；
- Doctor 结果；
- 视觉校准采用的塔顶偏移；
- 未执行的压力测试或其他边界。

- [ ] **Step 5: 提交验证记录**

```bash
git add docs/superpowers/reports/2026-10-18-archer-tower-worker-verification.md
git commit -m "docs: record archer, tower, and worker verification" -m "Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```
