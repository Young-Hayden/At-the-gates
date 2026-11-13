# RTS Command Intent Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Separate plain move, attack-move and garrison intent, resume attack-move destinations correctly, and stop automatic unreachable targets from monopolising CombatAI target selection.

**Architecture:** Keep UnitMover as a single-path executor. CombatAI owns a small temporary intent (`Auto/Move/AttackMove/Garrison`) plus the attack-move destination and completion callbacks; SelectionManager wires player commands into that intent. Unreachable automatic targets get a bounded goal-specific retry record, while forced targets retain their explicit order.

**Tech Stack:** Tuanjie 2022.3.62t5, Unity Test Framework 1.1.33, NUnit, C#, existing RegressionFixture, RegressionTestMenu and isolated PlayMode scenes.

## Global Constraints

- Do not rewrite A*, UnitMover, worker/gatherer behaviour, Healer, AI wave dispatch, prices, unit stats, LOS, physics or save format.
- Do not implement A03, A05, Shift queues, AI rebuilding/garrison or performance work in this project.
- Plain move and garrison travel do not auto-acquire, chase or stop to attack. AttackMove fights and then resumes its destination.
- Automatic unreachable targets may be replaced/retried; forced targets never switch targets automatically.
- `UnitMover.Stop()` is not arrival. Replaced callbacks are cancellation, not delayed completion.
- Preserve all pre-existing uncommitted work. No commits, pushes, worktrees or cleanup.
- Use real production Update/path tests. Do not manufacture RED by breaking correct code or rely only on private field inspection.
- Existing 83/83 is the latest reference. Every final result must come from a fresh full run after the last code change.
- Physical GUI input remains outside this project and is not claimed as verified.

## File Map

- `Assets/Scripts/CombatAI.cs`: temporary intent, attack-move destination/resume and unreachable-target retry.
- `Assets/Scripts/SelectionManager.cs`: ground-move split and garrison intent/callback cleanup.
- `Assets/Scripts/IDamageable.cs`: optional candidate filter in the existing nearest-enemy scan.
- `Assets/Tests/PlayMode/CommandIntentTests.cs`: new focused production-path regressions.
- Existing `MovementAndCommandTests`, `TowerGarrisonTests`, `RubbleAndGarrisonCommandTests`, `InputGateTests`, `SaveLoadTests`: regression controls.
- `docs/superpowers/reports/2026-10-28-rts-command-intent-verification.md`: evidence and next-stage handoff.
- `.superpowers/sdd/command-intent/`: briefs, snapshots, logs, reports and no-index diffs.

---

### Task 1: CombatAI move and attack-move intent core

**Files:**
- Modify: `Assets/Scripts/CombatAI.cs`
- Create: `Assets/Tests/PlayMode/CommandIntentTests.cs`

**Interfaces:**
- Produces: `OrderMove(Vector3Int)`, `OrderAttackMove(Vector3Int)`, `CompleteOrder()`, and private `ResumeDestination()`.
- Keeps: `OrderAttack`, `OrderHold`, `ClearOrder`, `Target`, `IsHolding` and existing automatic combat.

- [ ] **Step 1: Create failing core tests**

Create `CommandIntentTests : RegressionFixture` with `const string ArcherPath = "Assets/Prefabs/Units/Archer_Blue.prefab";` and these exact helpers, then add the tests below:

```csharp
UnitActor CreateFighter(string name, Vector3Int cell)
{
    var prefab = LoadPrefab<GameObject>(ArcherPath);
    var go = UnityEngine.Object.Instantiate(prefab, Grid.CellCenter(cell), Quaternion.identity);
    go.name = name;
    var actor = go.GetComponent<UnitActor>();
    actor.showHealthBar = false;
    var mover = go.GetComponent<UnitMover>();
    mover.speed = 8f;
    return actor;
}

static UnitActor CreateEnemy(string name, Vector3 position, int health)
{
    var go = new GameObject(name);
    go.transform.position = position;
    var actor = go.AddComponent<UnitActor>();
    actor.team = Team.Red;
    actor.showHealthBar = false;
    actor.maxHealth = health;
    actor.health = health;
    actor.isCombatant = true;
    return actor;
}

Building SpawnTower(Vector3Int cell)
{
    var prefab = LoadPrefab<GameObject>("Assets/Prefabs/Buildings/Knights/Tower_Blue.prefab");
    var go = UnityEngine.Object.Instantiate(
        prefab, Grid.CellCenter(cell) - new Vector3(0f, 0.5f, 0f), Quaternion.identity);
    var building = go.GetComponent<Building>();
    building.Recompute();
    return building;
}

static IEnumerator WaitUntil(System.Func<bool> condition, float timeout)
{
    float deadline = Time.time + timeout;
    while (!condition() && Time.time < deadline) yield return null;
}
```

```csharp
[UnityTest]
public IEnumerator PlainMove_InRangeEnemy_DoesNotInterruptDestination()
{
    PaintRect(-4, -4, 16, 12);
    var fighter = CreateFighter("Plain mover", new Vector3Int(0, 0, 0));
    var enemy = CreateEnemy("Adjacent enemy", new Vector3(0.5f, 0f, 0f), health: 100);
    var ai = fighter.GetComponent<CombatAI>();
    var mover = fighter.GetComponent<UnitMover>();
    var destination = new Vector3Int(5, 0, 0);
    var path = Grid.FindPath(Grid.WorldToCell(fighter.transform.position), destination);
    Assert.IsNotNull(path);

    ai.OrderMove(destination);
    mover.SetPath(path, ai.CompleteOrder);
    yield return WaitUntil(() => !mover.Moving, 4f);

    Assert.Less(Vector2.Distance(fighter.transform.position, Grid.CellCenter(destination)), 0.2f);
    Assert.AreEqual(100, enemy.health, "Plain move must not stop to attack the adjacent enemy.");
}

[UnityTest]
public IEnumerator AttackMove_EnemyKilled_ResumesOriginalDestination()
{
    PaintRect(-4, -4, 18, 12);
    var fighter = CreateFighter("Attack mover", new Vector3Int(0, 0, 0));
    var enemy = CreateEnemy("Enemy on route", new Vector3(2f, 0f, 0f), health: 12);
    var ai = fighter.GetComponent<CombatAI>();
    var mover = fighter.GetComponent<UnitMover>();
    var destination = new Vector3Int(6, 0, 0);
    var path = Grid.FindPath(Grid.WorldToCell(fighter.transform.position), destination);
    Assert.IsNotNull(path);

    ai.OrderAttackMove(destination);
    mover.SetPath(path, ai.CompleteOrder);
    yield return WaitUntil(() => enemy == null || !enemy.IsAlive, 5f);
    yield return WaitUntil(() => Vector2.Distance(
        fighter.transform.position, Grid.CellCenter(destination)) < 0.25f, 5f);

    Assert.IsFalse(mover.Moving);
    Assert.Less(Vector2.Distance(fighter.transform.position, Grid.CellCenter(destination)), 0.25f,
        "After combat, the unit must resume and finish the original attack-move destination.");
}
```

Helpers must use real `CombatAI`, `UnitMover`, `MapGrid.FindPath` and `UnitActor`; do not directly set a private intent field. Expected before implementation: compile/entry-point RED because `OrderMove`/`OrderAttackMove` do not exist. If a staged API test is preferred, first assert the new public entry points exist, then implement the smallest API and run behavioural RED against old automatic movement.

- [ ] **Step 2: Add temporary intent and order entry points**

In `CombatAI`, add:

```csharp
enum OrderKind { Auto, Move, AttackMove, Garrison }
OrderKind orderKind;
Vector3Int destinationCell;

public void OrderMove(Vector3Int destination) { SetTemporaryOrder(OrderKind.Move, destination); }
public void OrderAttackMove(Vector3Int destination) { SetTemporaryOrder(OrderKind.AttackMove, destination); }
public void OrderGarrison() { SetTemporaryOrder(OrderKind.Garrison, default); }

public void CompleteOrder()
{
    orderKind = OrderKind.Auto;
    destinationCell = default;
    forced = false;
    target = null;
    holdPosition = false;
    lastGoalCell = new Vector3Int(int.MinValue, int.MinValue, 0);
}

void SetTemporaryOrder(OrderKind kind, Vector3Int destination)
{
    orderKind = kind;
    destinationCell = destination;
    holdPosition = false;
    forced = false;
    target = null;
    lastGoalCell = new Vector3Int(int.MinValue, int.MinValue, 0);
    repathTimer = 0f;
    retargetTimer = 0f;
}
```

`OrderAttack` must set `orderKind = Auto`; `OrderHold` must also set Auto. `ClearOrder` calls `CompleteOrder` so existing garrison cleanup remains valid.

- [ ] **Step 3: Gate plain move and resume attack-move destination**

Immediately after the existing single cooldown decrement, add:

```csharp
if (orderKind == OrderKind.Move || orderKind == OrderKind.Garrison)
{
    if (mover.Moving) return;
    CompleteOrder(); // arrival callback may already have done this; an aborted path must not freeze combat.
}
```

After dead-target cleanup and automatic retarget selection, but before chase/attack handling, add:

```csharp
if (orderKind == OrderKind.AttackMove && target == null)
{
    if (mover.Moving) return;
    if (!ResumeDestination()) CompleteOrder();
    else return;
}
```

Implement:

```csharp
bool ResumeDestination()
{
    var grid = MapGrid.Instance;
    if (grid == null) return false;
    var start = grid.ResolveWalkableStart(grid.WorldToCell(transform.position));
    if (!start.HasValue) return false;
    var path = grid.FindPath(start.Value, destinationCell);
    if (path == null) return false;
    mover.SetPath(path, CompleteOrder);
    return true;
}
```

Do not put a destination completion callback on `Chase()` paths. Preserve current target cleanup, forced attack, Hold and noncombatant guards.

- [ ] **Step 4: Run core tests and path-abort control**

Run both exact tests and add a control that adds `Grid.AddBuildingFootprint` over the next cell during `OrderMove`; after the mover stops, place an enemy in range and assert automatic combat resumes. Expected: all pass, no permanent Move state and no new per-frame path allocation loop.

---

### Task 2: SelectionManager attack-move and garrison wiring

**Files:**
- Modify: `Assets/Scripts/SelectionManager.cs:60-75,396-464,557-628`
- Modify: `Assets/Tests/PlayMode/CommandIntentTests.cs`

**Interfaces:**
- Consumes: Task 1 `OrderMove`, `OrderAttackMove`, `OrderGarrison`, `CompleteOrder`.
- Produces: shared ground path assignment for plain move vs attack-move and garrison-safe temporary intent.

- [ ] **Step 1: Add failing attack-button and garrison travel tests**

Add:

```csharp
[UnityTest]
public IEnumerator AttackButton_Ground_EngagesThenResumesDestination()
{
    PaintRect(-4, -4, 18, 12);
    var fighter = CreateFighter("Button attack mover", new Vector3Int(0, 0, 0));
    var enemy = CreateEnemy("Button route enemy", new Vector3(2f, 0f, 0f), health: 12);
    Selection.selected.Clear();
    Selection.selected.Add(fighter.GetComponent<UnitMover>());
    Selection.ArmAttack();
    Selection.HandleAttackArmedInput(Grid.CellCenter(new Vector3Int(6, 0, 0)), true, false);

    yield return WaitUntil(() => enemy == null || !enemy.IsAlive, 5f);
    yield return WaitUntil(() => Vector2.Distance(
        fighter.transform.position, Grid.CellCenter(new Vector3Int(6, 0, 0))) < 0.25f, 5f);
    Assert.IsFalse(fighter.GetComponent<UnitMover>().Moving);
    Assert.Less(Vector2.Distance(fighter.transform.position, Grid.CellCenter(new Vector3Int(6, 0, 0))), 0.25f);
}

[UnityTest]
public IEnumerator GarrisonOrder_EnemyOnRoute_DoesNotDelayOrCancelGarrison()
{
    PaintRect(-8, -8, 24, 24);
    var tower = SpawnTower(new Vector3Int(3, 3, 0));
    var archer = CreateFighter("Garrison traveller", new Vector3Int(-3, 3, 0));
    var enemy = CreateEnemy("Enemy beside garrison path", archer.transform.position + Vector3.right, 100);
    Selection.selected.Clear();
    Selection.selected.Add(archer.GetComponent<UnitMover>());

    Selection.IssueMove(tower.transform.position + Vector3.up * 0.5f);
    yield return WaitUntil(() => tower.GetComponent<TowerGarrison>().HasArcher, 5f);

    Assert.IsTrue(tower.GetComponent<TowerGarrison>().HasArcher);
    Assert.AreEqual(100, enemy.health,
        "Travelling to a tower must not turn into an attack against the route enemy.");
}
```

Expected before wiring: attack button still calls plain IssueMove and garrison travel is vulnerable to automatic target/path replacement. Keep existing delayed-garrison and under-construction controls as regression checks.

- [ ] **Step 2: Split ground command routing without duplicating formation/path logic**

Change armed ground handling from `IssueMove(mw)` to `IssueGroundMove(mw, attackMove: true)`. Make `IssueMove` retain its resource/bush/tower/enemy priority and finish with `IssueGroundMove(mw, attackMove: false)`.

Move the existing grid/formation loop into `IssueGroundMove(Vector3 mw, bool attackMove)`. In that loop:

- For attackMove, skip units without a live combatant `UnitActor` and `CombatAI`.
- For plain move, preserve `Gatherer.StopWork()` and `Healer.OrderMove()`.
- Call `ai.OrderAttackMove(dest.Value)` for attackMove and `ai.OrderMove(dest.Value)` for plain move.
- Use `u.SetPath(path, ai.CompleteOrder)` when CombatAI exists, otherwise `u.SetPath(path)`.
- Preserve claimed destinations, unreachable refusal counts and blocked markers.

- [ ] **Step 3: Set and clean garrison intent at every callback outcome**

Before the existing garrison `SetPath`, call `ai.OrderGarrison()` when CombatAI exists. Replace the callback with:

```csharp
mover.SetPath(bestPath, () =>
{
    if (tower == null || building == null || archer == null || !building.IsAlive)
    {
        if (ai != null) ai.CompleteOrder();
        return;
    }
    if (tower.Garrison(archer)) selected.Remove(mover);
    else if (ai != null) ai.CompleteOrder();
});
```

Do not change TowerGarrison, A04 construction checks or the actual arrival requirement. Existing `TowerGarrison.ClearOrder()` remains the final successful cleanup.

- [ ] **Step 4: Run wiring and compatibility controls**

Run the two new tests plus `RubbleAndGarrisonCommandTests.RightClickTower_DistantArcherWalksToBaseThenGarrisonsAndDamagesEnemy`, `TowerGarrisonTests.UnderConstructionTower_PlayerGarrisonOrder_IsConsumedWithoutPathOrGarrison`, `MovementAndCommandTests.OrdinaryMove_WorkerRemainsManuallyIdle_AutomaticWorkDoesNotOverride`, and the two existing monk move tests. Expected: all pass; no worker or healer behaviour change.

---

### Task 3: A02 unreachable automatic targets

**Files:**
- Modify: `Assets/Scripts/IDamageable.cs:41-84`
- Modify: `Assets/Scripts/CombatAI.cs`
- Modify: `Assets/Tests/PlayMode/CommandIntentTests.cs`

**Interfaces:**
- Produces: optional `Func<IDamageable,bool> accept = null` on the central nearest-enemy overload; automatic goal-specific retry and forced-target preservation.

- [ ] **Step 1: Add failing unreachable-target tests**

Create one painted main region and a separate isolated cell. Put the automatic fighter in the main region, the nearest enemy on the isolated cell more than attackRange but within aggroRange, and a slightly farther reachable enemy in the main region:

```csharp
[UnityTest]
public IEnumerator AutoTarget_UnreachableNearest_SelectsReachableEnemy()
{
    PaintRect(-2, -2, 5, 10);
    PaintRect(6, 0, 1, 1);
    var fighter = CreateFighter("Region-aware fighter", new Vector3Int(0, 0, 0));
    var unreachable = CreateEnemy("Isolated nearest enemy", new Vector3(6f, 0f, 0f), 100);
    var reachable = CreateEnemy("Reachable alternate", new Vector3(0f, 6.5f, 0f), 100);

    yield return WaitUntil(() => fighter.GetComponent<CombatAI>().Target == reachable, 2f);
    Assert.AreSame(reachable, fighter.GetComponent<CombatAI>().Target);
}
```

Add a second test starting with the same setup: after the alternate is selected, paint `(3,0)-(6,0)` to connect the island and assert the original target can be selected again after the retry interval. Add a forced control using `ai.OrderAttack(unreachable)` and assert the forced target is not replaced by the alternate.

- [ ] **Step 2: Add optional candidate filtering without changing existing callers**

Extend the central overload to:

```csharp
public static IDamageable FindNearestEnemy(Team myTeam, float maxRange,
    System.Func<IDamageable, float> distanceSquared,
    bool includeBuildings = true,
    System.Func<IDamageable, bool> accept = null)
```

In both unit and building loops, after team/alive checks add:

```csharp
if (accept != null && !accept(u)) continue; // use b in the building loop
```

The origin-based overload keeps its current signature and forwards `includeBuildings`; no accept argument is required by other systems.

- [ ] **Step 3: Record automatic failures and retry by goal**

Add `unreachableTarget`, `unreachableGoal`, and `unreachableTimer` to CombatAI. Decrement the timer once per Update. On any non-forced Chase failure (no start, no destination or null path), record the current target and goal, set the timer to `retargetInterval`, clear target and allow immediate alternate selection.

Automatic target acquisition uses the optional accept filter: reject only when the candidate is the recorded target, its current goal is unchanged and the timer remains positive. Moved targets, changed building footprints and expired windows may retry.

Forced targets never call this failure recorder, so their current target and forced flag remain intact. Clear the record on new orders, explicit `OrderAttack`, target death and `CompleteOrder`. Use the same building nearest-cell/unit-cell goal computation in Chase and retry comparison.

- [ ] **Step 4: Run A02 tests and automatic/forced controls**

Expected: the nearest isolated target no longer blocks the reachable alternate; after reconnection the original can return; the forced target is retained. Also run existing worker threat, hold and AI wave controls to prove the optional filter did not change default nearest-enemy behaviour.

---

### Task 4: Integration verification and independent review

**Files:**
- Modify: `docs/superpowers/reports/2026-10-28-rts-command-intent-verification.md`
- Review only: `CombatAI.cs`, `SelectionManager.cs`, `IDamageable.cs`, `CommandIntentTests.cs`

- [ ] **Step 1: Run all focused controls**

Run every `CommandIntentTests` leaf, all Movement/Input/TowerGarrison/Rubble/SaveLoad controls, and AI production/assignment tests. Investigate timing-sensitive tests by exact leaf name; do not weaken a behavioural threshold merely to obtain a pass.

- [ ] **Step 2: Run fresh full PlayMode**

Compile and wait idle, then call `RegressionTestMenu.RunPlayModeTests()`. Save the actual `Logs/RegressionTests.txt` to `.superpowers/sdd/command-intent/final-full-regression.log` and report total/passed/failed/skipped/inconclusive/duration.

- [ ] **Step 3: Verify production semantics and scope**

Use read-only runtime probes or focused PlayMode checks for plain move, attack-move, garrison and automatic/forced unreachable targets. Do not inject physical OS input. Confirm stopped/SampleScene dirty=false, no formal save writes, no changes outside the four runtime/test files and reports/evidence.

- [ ] **Step 4: Final review and next-stage handoff**

Generate earliest-snapshot-to-current no-index diffs and request independent spec/quality review. Resolve Critical/Important findings before delivery. Record remaining Minor items, the physical GUI blocker, and the recommendation to evaluate AI recovery/composition or measured performance only after this command-intent project is stable. Do not start those systems in this plan.
