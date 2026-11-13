# RTS Phase A: Caps, Stockpile and Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add per-role and total combat caps, stockpile limits with full-resource gathering diversion, central renewable meat, instant paid repair and AI key-building repair, without changing unit balance or starting resources.

**Architecture:** A central `UnitLimits` service counts live, garrisoned and queued units and is consulted by every production path. A shared `ResourceLimits` clamp is applied at both banks and gathering filters. Repair is a building-level cost/eligibility API, called by the player UI and by an AI repair pass. All new state is derived; the save schema stays unchanged.

**Tech Stack:** Tuanjie 2022.3.62t5, Unity Test Framework 1.1.33, NUnit, C#, UGUI, existing RegressionFixture/RegressionTestMenu, incremental editor tools.

## Global Constraints

- Phase A only. Do not implement house population, worker repair, technology, meat upkeep, balance changes, AI recovery/composition/garrison, or performance rewrites.
- Do not change unit HP/damage/range/speed, wave timing, player starting resources or resource values.
- Player and AI share the same caps and repair cost formula.
- Existing units above a cap are never deleted; only new production is refused.
- No full HUD rebuild. Use an incremental editor tool for the repair button.
- No save version change. Unit/resource caps are derived; repair uses existing building health; sheep use existing resource-node save state.
- Preserve all pre-existing uncommitted work. No commits, pushes or worktrees.
- Use real production-path tests. Do not claim physical GUI verification.
- The starting reference is the current 96/96 suite; every final claim comes from a fresh run after the last code change.

## File Map

- `Assets/Scripts/UnitLimits.cs` (new): roles, counts, cap check, frame cache.
- `Assets/Scripts/ResourceLimits.cs` (new): stockpile caps/clamp helpers.
- `Assets/Scripts/ProductionBuilding.cs`: cap-aware `CanQueue`, refusal reason, cache invalidation.
- `Assets/Scripts/Economy.cs`, `AIController.cs`: clamped `Add`, stockpile caps, AI `Get`.
- `Assets/Scripts/Gatherer.cs`: full-kind automatic assignment and post-delivery diversion.
- `Assets/Scripts/Building.cs`: missing health, repair cost, repair eligibility.
- `Assets/Scripts/SelectionManager.cs`: player repair action.
- `Assets/Scripts/HUD.cs`: cap refusal display and repair button wiring/state.
- `Assets/Editor/BuildRepairButton.cs` (new): incremental selection-panel button.
- `Assets/Editor/AddCentralMeat.cs` (new): adds three sheep and validates connectivity.
- `Assets/Scenes/SampleScene.scene`: sheep and repair button.
- Tests: `UnitLimitTests.cs`, `ResourceLimitTests.cs`, `CentralMeatTests.cs`, `RepairTests.cs`, `AIRepairTests.cs`.
- `docs/superpowers/reports/2026-10-29-rts-phase-a-verification.md`.

---

### Task 1: Unit limits and production enforcement

**Files:**
- Create: `Assets/Scripts/UnitLimits.cs`
- Modify: `Assets/Scripts/ProductionBuilding.cs`
- Modify: `Assets/Scripts/HUD.cs`
- Create: `Assets/Tests/PlayMode/UnitLimitTests.cs`

**Interfaces:**
- Produces: `UnitRole`, `UnitLimits.RoleOf(GameObject)`, `UnitLimits.Count(Team, UnitRole)`, `UnitLimits.TotalCombat(Team)`, `UnitLimits.CanProduce(Team, GameObject, out string reason)`, `UnitLimits.Invalidate()`.
- Produces: `ProductionBuilding.CanQueue(int, bool, out string reason)` and `LastRefusalReason`.

- [ ] **Step 1: Write the failing cap tests**

Create `UnitLimitTests : RegressionFixture` with helpers using existing prefab paths (`Warrior_Blue`, `Archer_Blue`, `Pawn_Blue`, `Lancer_Blue`, `Monk_Blue`) and `CreateActor`-style UnitActor components. Add:

```csharp
[Test]
public void RoleOf_KnownPrefabs_MapsEveryProfession()
{
    Assert.AreEqual(UnitRole.Worker, UnitLimits.RoleOf(LoadPrefab<GameObject>("Assets/Prefabs/Units/Pawn_Blue.prefab")));
    Assert.AreEqual(UnitRole.Warrior, UnitLimits.RoleOf(LoadPrefab<GameObject>("Assets/Prefabs/Units/Warrior_Blue.prefab")));
    Assert.AreEqual(UnitRole.Lancer, UnitLimits.RoleOf(LoadPrefab<GameObject>("Assets/Prefabs/Units/Lancer_Blue.prefab")));
    Assert.AreEqual(UnitRole.Archer, UnitLimits.RoleOf(LoadPrefab<GameObject>("Assets/Prefabs/Units/Archer_Blue.prefab")));
    Assert.AreEqual(UnitRole.Monk, UnitLimits.RoleOf(LoadPrefab<GameObject>("Assets/Prefabs/Units/Monk_Blue.prefab")));
}

[UnityTest]
public IEnumerator TotalCombatCap_RefusesFourteenthUnitWithoutSpending()
{
    PaintRect(-8, -8, 32, 24);
    for (int i = 0; i < UnitLimits.CombatCap; i++)
        CreateCombatant(Team.Blue, new Vector3Int(i % 8, i / 8, 0));
    yield return null;

    var barracks = InstantiateFactory("Assets/Prefabs/Buildings/Knights/Barracks_Blue.prefab", new Vector3Int(12, 0, 0));
    int index = barracks.IndexOfProduct(LoadPrefab<GameObject>("Assets/Prefabs/Units/Warrior_Blue.prefab"));
    int gold = Economy.gold;
    Assert.IsFalse(barracks.TryQueue(index));
    Assert.AreEqual(0, barracks.Queue.Count);
    Assert.AreEqual(gold, Economy.gold, "A cap refusal must not spend resources.");
    Assert.IsFalse(barracks.CanQueue(index, true, out string reason));
    StringAssert.Contains("上限", reason);
}

[UnityTest]
public IEnumerator RoleCap_BlocksOnlyThatProfession_AndOverCapUnitsRemain()
{
    PaintRect(-8, -8, 32, 24);
    for (int i = 0; i < UnitLimits.WarriorCap; i++)
        CreateNamedUnit("Assets/Prefabs/Units/Warrior_Blue.prefab", Team.Blue, new Vector3Int(i, 0, 0));
    yield return null;
    Assert.AreEqual(UnitLimits.WarriorCap, UnitLimits.Count(Team.Blue, UnitRole.Warrior));
    Assert.IsFalse(UnitLimits.CanProduce(Team.Blue,
        LoadPrefab<GameObject>("Assets/Prefabs/Units/Warrior_Blue.prefab"), out _));
    Assert.IsTrue(UnitLimits.CanProduce(Team.Blue,
        LoadPrefab<GameObject>("Assets/Prefabs/Units/Archer_Blue.prefab"), out _));
    Assert.AreEqual(UnitLimits.WarriorCap, UnitLimits.Count(Team.Blue, UnitRole.Warrior),
        "Existing over-cap units must not be deleted.");
}
```

`CreateCombatant` may cycle Warrior/Archer/Lancer/Monk prefabs; `CreateNamedUnit` instantiates the prefab, sets team and disables health bars. `InstantiateFactory` must also `Recompute()` and set team. A queued-unit count test is required: queue one archer in a second factory and assert `UnitLimits.Count` increases before spawn. Use `Invalidate()` after every direct unit creation in tests.

- [ ] **Step 2: Run the exact cap tests and verify they fail for the missing service**

Expected: compile failure on missing `UnitLimits`/`CanQueue` overload, then behavioural failure if the API is stubbed. Do not manufacture a production defect.

- [ ] **Step 3: Implement UnitLimits**

```csharp
using System.Collections.Generic;
using UnityEngine;

public enum UnitRole { Worker, Warrior, Lancer, Archer, Monk, Other }

public static class UnitLimits
{
    public const int WorkerCap = 6;
    public const int WarriorCap = 8;
    public const int LancerCap = 5;
    public const int ArcherCap = 5;
    public const int MonkCap = 4;
    public const int CombatCap = 14;

    static int cachedFrame = -1;
    static readonly Dictionary<Team, Dictionary<UnitRole, int>> cache = new();

    public static void Invalidate() { cachedFrame = -1; cache.Clear(); }

    public static UnitRole RoleOf(GameObject prefab)
    {
        if (prefab == null) return UnitRole.Other;
        string n = prefab.name;
        if (n.Contains("Pawn")) return UnitRole.Worker;
        if (n.Contains("Warrior")) return UnitRole.Warrior;
        if (n.Contains("Lancer")) return UnitRole.Lancer;
        if (n.Contains("Archer")) return UnitRole.Archer;
        if (n.Contains("Monk")) return UnitRole.Monk;
        return UnitRole.Other;
    }

    public static int Count(Team team, UnitRole role) => Counts(team).TryGetValue(role, out int n) ? n : 0;
    public static int TotalCombat(Team team)
    {
        var c = Counts(team);
        int w = c.TryGetValue(UnitRole.Warrior, out int ww) ? ww : 0;
        int l = c.TryGetValue(UnitRole.Lancer, out int ll) ? ll : 0;
        int a = c.TryGetValue(UnitRole.Archer, out int aa) ? aa : 0;
        int m = c.TryGetValue(UnitRole.Monk, out int mm) ? mm : 0;
        return w + l + a + m;
    }

    public static bool CanProduce(Team team, GameObject prefab, out string reason)
    {
        var role = RoleOf(prefab);
        reason = null;
        var c = Counts(team);
        int count = c.TryGetValue(role, out int n) ? n : 0;
        int cap = CapFor(role);
        if (role == UnitRole.Worker && count >= WorkerCap)
        { reason = "工人已达上限"; return false; }
        if (role != UnitRole.Worker && role != UnitRole.Other)
        {
            if (count >= cap) { reason = role + "已达上限"; return false; }
            if (TotalCombat(team) >= CombatCap) { reason = "总战斗单位已达上限"; return false; }
        }
        if (role == UnitRole.Other && prefab.GetComponent<UnitActor>()?.isCombatant == true &&
            TotalCombat(team) >= CombatCap)
        { reason = "总战斗单位已达上限"; return false; }
        return true;
    }

    static int CapFor(UnitRole role) => role switch
    {
        UnitRole.Worker => WorkerCap,
        UnitRole.Warrior => WarriorCap,
        UnitRole.Lancer => LancerCap,
        UnitRole.Archer => ArcherCap,
        UnitRole.Monk => MonkCap,
        _ => int.MaxValue,
    };

    static Dictionary<UnitRole, int> Counts(Team team)
    {
        if (cachedFrame != Time.frameCount) { cachedFrame = Time.frameCount; cache.Clear(); }
        if (cache.TryGetValue(team, out var counts)) return counts;
        counts = new Dictionary<UnitRole, int>();
        foreach (var u in UnitActor.All)
        {
            if (u == null || !u.IsAlive || u.team != team) continue;
            Add(counts, RoleOf(u.gameObject));
        }
        foreach (var b in Building.All)
        {
            if (b == null || !b.IsAlive) continue;
            var g = b.GetComponent<TowerGarrison>();
            if (g != null && g.HasArcher && g.Archer != null && g.Archer.team == team)
                Add(counts, UnitRole.Archer);
        }
        // Count queues exactly once, including disabled/under-construction factories
        // restored from a save. Do not also count queues from Building.All.
        foreach (var pb in Object.FindObjectsByType<ProductionBuilding>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (pb == null) continue;
            var b = pb.GetComponent<Building>();
            if (b == null || b.isGhost || b.team != team) continue;
            foreach (int index in pb.Queue)
                if (index >= 0 && index < pb.products.Length)
                    Add(counts, RoleOf(pb.products[index].unitPrefab));
        }
        cache[team] = counts;
        return counts;
    }

    static void Add(Dictionary<UnitRole, int> d, UnitRole r)
    {
        d.TryGetValue(r, out int n); d[r] = n + 1;
    }
}
```

Note: queued units are counted from `Building.All` and from the component scan; to avoid double counting, only scan components whose building is not in `Building.All` (ghost/disabled). The implementation must ensure no double count; prefer one scan over `FindObjectsByType<ProductionBuilding>(FindObjectsInactive.Include)` and skip ghosts/team, and do not also count queues from `Building.All`. The plan text is the requirement; the implementer must test queued counts to catch duplication.

- [ ] **Step 4: Enforce caps in ProductionBuilding and HUD**

Add a reason overload and invalidate after successful queue. In `CanQueue`, after product/construction/queue/afford checks, call `UnitLimits.CanProduce(building.team, products[index].unitPrefab, out reason)`. `TryQueue` calls `UnitLimits.Invalidate()` after `queue.Add`.

In `HUD.RefreshUnitPanel`, compute `bool capOk = UnitLimits.CanProduce(PlayerTeam, offer.unitPrefab, out string capReason);` and include it in `ok`; show `capReason` in the cost text when blocked. In `RefreshProduction`, show the factory refusal reason in the cost text when non-null. Do not rebuild the HUD.

- [ ] **Step 5: Run cap tests and existing production tests**

Expected: new cap tests pass; existing `AIProductionBudgetTests`, `ProductionDependencyTests`, `PlacementTests`, `SaveLoadTests`, and worker tests remain green.

---

### Task 2: Stockpile limits and full-resource gathering diversion

**Files:**
- Create: `Assets/Scripts/ResourceLimits.cs`
- Modify: `Assets/Scripts/Economy.cs`
- Modify: `Assets/Scripts/AIController.cs`
- Modify: `Assets/Scripts/Gatherer.cs`
- Create: `Assets/Tests/PlayMode/ResourceLimitTests.cs`

**Interfaces:**
- Produces: `ResourceLimits.Cap(ResourceKind)`, `ClampAdd(int current, int amount, ResourceKind)`, `IsFull(int, ResourceKind)`, `Clamp(int, ResourceKind)`.
- Produces: `AIController.Get(ResourceKind)`, `AIController.IsFull(ResourceKind)`, `Gatherer.IsKindFull(Team, ResourceKind)` (static helper).

- [ ] **Step 1: Write failing stockpile and diversion tests**

```csharp
[Test]
public void Add_ClampsAtEveryStockpileCap()
{
    Economy.gold = ResourceLimits.GoldCap - 5;
    Economy.Add(ResourceKind.Gold, 50);
    Assert.AreEqual(ResourceLimits.GoldCap, Economy.gold);
    Economy.wood = ResourceLimits.WoodCap - 5;
    Economy.Add(ResourceKind.Wood, 50);
    Assert.AreEqual(ResourceLimits.WoodCap, Economy.wood);
    Economy.meat = ResourceLimits.MeatCap - 5;
    Economy.Add(ResourceKind.Meat, 50);
    Assert.AreEqual(ResourceLimits.MeatCap, Economy.meat);
}

[UnityTest]
public IEnumerator AutoAssign_SkipsFullKind_AndUsesUnfullKind()
{
    PaintRect(-4, -4, 16, 12);
    Economy.gold = ResourceLimits.GoldCap;
    var worker = CreateWorker("Full-kind worker", new Vector3Int(0, 0, 0), true);
    var gold = CreateNode("Full gold", new Vector3Int(1, 0, 0), ResourceKind.Gold);
    var wood = CreateNode("Open wood", new Vector3Int(0, 3, 0), ResourceKind.Wood);
    yield return null;
    yield return null;
    Assert.AreSame(wood, worker.TargetNode);
    Assert.AreNotSame(gold, worker.TargetNode);
}
```

Add a post-delivery diversion test: worker carries gold, gold is at cap, there is a reachable wood node; after delivery the worker must clear the gold job and select wood. Add AI assignment coverage with an AI worker and a full player/red bank.

- [ ] **Step 2: Implement ResourceLimits and bank clamps**

```csharp
public static class ResourceLimits
{
    public const int GoldCap = 2000;
    public const int WoodCap = 1200;
    public const int MeatCap = 1000;

    public static int Cap(ResourceKind kind) => kind switch
    {
        ResourceKind.Gold => GoldCap,
        ResourceKind.Wood => WoodCap,
        _ => MeatCap,
    };

    public static int ClampAdd(int current, int amount, ResourceKind kind)
        => Mathf.Min(Cap(kind), Mathf.Max(0, current + amount));

    public static bool IsFull(int current, ResourceKind kind) => current >= Cap(kind);
    public static int Clamp(int current, ResourceKind kind) => Mathf.Clamp(current, 0, Cap(kind));
}
```

`Economy.Add` uses `ClampAdd`; `Refund` uses `Clamp`; `AIController.Add`/`RestoreSaveState` likewise. Add `AIController.Get` and `IsFull`.

- [ ] **Step 3: Filter automatic gathering and switch after delivery**

Add to `Gatherer`:

```csharp
public static bool IsKindFull(Team team, ResourceKind kind)
{
    var player = GameState.Instance != null ? GameState.Instance.playerTeam : Team.Blue;
    if (team == player)
        return Economy.Instance != null && ResourceLimits.IsFull(Economy.Instance.Get(kind), kind);
    var ai = AIController.For(team);
    return ai != null && ResourceLimits.IsFull(ai.Get(kind), kind);
}
```

Use it in `Gatherer.TryAutoAssign`'s candidate filter and `AIController.AssignGatherers`. After a delivery in `OnReachedDropoff`, if the retained `node` has a full kind, clear the node and enter `Idle`; if non-manual, call `TryAutoAssign` so the worker moves to an unfull kind. Do not drop a manually assigned full-kind job.

- [ ] **Step 4: Run stockpile tests and gathering/AI controls**

Expected: new tests pass; `WorkerBehaviorTests`, `MovementAndCommandTests`, `AIProductionBudgetTests`, `SaveLoadTests` remain green.

---

### Task 3: Central sheep and scene validation

**Files:**
- Create: `Assets/Editor/AddCentralMeat.cs`
- Modify: `Assets/Scenes/SampleScene.scene`
- Create: `Assets/Tests/PlayMode/CentralMeatTests.cs`

**Interfaces:**
- Produces: three neutral `HappySheep_Idle` instances at `(18,2)`, `(19,5)`, `(20,7)`, each 300 meat, 20 per trip, 120-second regrow.

- [ ] **Step 1: Add the failing scene test**

Load `SampleScene`, find `ResourceNode.All` with `kind == Meat`, assert exactly three, named `Sheep_Meat_1..3`, each `amount/maxAmount == 300`, `perTrip == 20`, `regrowSeconds == 120`, and both starting unit regions still equal the sheep region. Expected before scene edit: zero meat nodes.

- [ ] **Step 2: Add the incremental scene tool**

Create `Assets/Editor/AddCentralMeat.cs` with menu `Tools/阶段A/添加中央羊群`. Open SampleScene additively, skip if named sheep already exist, instantiate `HappySheep_Idle.prefab` at `MapGrid.CellCenter(cell) - Vector3.up * 0.5f`, name them `Sheep_Meat_1..3`, set `node.regrowSeconds = 120f`, set `node.blockingBuilding = building` (so depleted sheep stop blocking), set team neutral, validate `MapGrid.Region` for both bases and all sheep is the same, then save and close. If validation fails, destroy the new objects and throw without saving.

- [ ] **Step 3: Run the scene test and connectivity check**

Run `CentralMeatTests` and verify the main region still contains blue/red initial units and all three sheep. Then run the save/load fixture to ensure resource nodes survive.

---

### Task 4: Player repair and HUD button

**Files:**
- Modify: `Assets/Scripts/Building.cs`
- Modify: `Assets/Scripts/SelectionManager.cs`
- Modify: `Assets/Scripts/HUD.cs`
- Create: `Assets/Editor/BuildRepairButton.cs`
- Modify: `Assets/Scenes/SampleScene.scene`
- Create: `Assets/Tests/PlayMode/RepairTests.cs`

**Interfaces:**
- Produces: `Building.MissingHealth`, `Building.RepairCost`, `Building.CanRepair`, `SelectionManager.RepairSelected()`.
- Produces: `HUD.repairButton` and an incremental `Repair` button in `SelectionPanel`.

- [ ] **Step 1: Write failing repair tests**

```csharp
[UnityTest]
public IEnumerator RepairSpendsExactlyOnceAndRestoresHealth()
{
    PaintRect(-6, -6, 16, 16);
    var house = Instantiate("Assets/Prefabs/Buildings/Knights/House_Blue.prefab", new Vector3Int(0, 0, 0), "Repair house");
    yield return null;
    var b = house.GetComponent<Building>();
    b.TakeDamage(100);
    Economy.gold = Economy.wood = Economy.meat = 1000;
    int missing = b.MissingHealth;
    var cost = b.RepairCost;
    Selection.selectedBuilding = b;
    Selection.RepairSelected();
    Assert.AreEqual(b.maxHealth, b.health);
    Assert.AreEqual(1000 - cost.gold, Economy.gold);
    Assert.AreEqual(1000 - cost.wood, Economy.wood);
    Assert.AreEqual(1000 - cost.meat, Economy.meat);
    Assert.AreEqual(0, b.MissingHealth);
    Selection.RepairSelected();
    Assert.AreEqual(1000 - cost.gold, Economy.gold, "A full-health repair must not spend again.");
}
```

Add controls for under-construction, wrecked, neutral, and insufficient resources (all leave health/resources unchanged), and the repair cost formula (`missing=25` -> 3 each).

- [ ] **Step 2: Implement Building repair API**

Add:

```csharp
public int MissingHealth => Mathf.Max(0, maxHealth - health);
public ResourceCost RepairCost
{
    get
    {
        int units = Mathf.CeilToInt(MissingHealth / 10f);
        return new ResourceCost(units, units, units);
    }
}
public bool CanRepair => IsAlive && !IsUnderConstruction && team != Team.Neutral && MissingHealth > 0;
```

`Repair(int amount)` stays and clamps. Do not couple Building to Economy or AIController.

- [ ] **Step 3: Add player action and HUD wiring**

`SelectionManager.RepairSelected()` checks `selectedBuilding != null`, `CanRepair`, team == player, and `Economy.Instance.TrySpend(b.RepairCost)` before calling `b.Repair(b.MissingHealth)`. Add `HUD.repairButton`; wire it in `Start`; in `RefreshSelection` show it for a selected friendly completed building, set interactable from `CanRepair` and affordability, and append `维修：<cost>` or `维修：已满血` to the body.

Create `BuildRepairButton.cs` modeled on `BuildAttackButton`, adding `Repair` at `(14,10)` size `84x34` in SelectionPanel without rebuilding HUD. It must be idempotent and save only SampleScene.

- [ ] **Step 4: Run repair tests and existing save/HUD controls**

Expected: repair tests pass; `SaveLoadTests`, `PlacementTests`, `PauseMenuTests` remain green. Confirm the scene diff contains only the Repair button and its label/wiring.

---

### Task 5: AI key-building repair

**Files:**
- Modify: `Assets/Scripts/AIController.cs`
- Create: `Assets/Tests/PlayMode/AIRepairTests.cs`

**Interfaces:**
- Produces: AI repair pass with 3-second interval, priority command centre > production > house, 70% trigger and reserve floor 100 gold / 50 wood / 25 meat.

- [ ] **Step 1: Write failing AI repair tests**

Create an inactive AI with gold/wood/meat and a damaged production building; invoke `RepairBuildings` through reflection. Assert health restored, bank decreased by exactly `RepairCost`, no tower repair, no repair above 70% health, and no repair if the reserve would be violated. Add a control that a player building is never repaired.

- [ ] **Step 2: Implement AI repair**

Add `repairTimer` and `RepairBuildings()` to `AIController.Update`. Iterate `Building.All` for same-team, alive, completed, non-neutral damaged buildings; priority:

```csharp
int Priority(Building b)
{
    if (b.isCommandCenter) return 0;
    if (b.GetComponent<ProductionBuilding>() != null) return 1;
    if (b.name.Contains("House")) return 2;
    return 99; // towers and other key-less buildings are not auto-repaired
}
```

Pick the highest priority, most damaged eligible building at or below 70% health. Compute `b.RepairCost`; require `gold - cost.gold >= 100`, `wood - cost.wood >= 50`, `meat - cost.meat >= 25`; then `TrySpend` and `b.Repair(b.MissingHealth)`. Do not touch player buildings or `Economy`.

- [ ] **Step 3: Run AI repair and existing AI controls**

Expected: new tests pass; `AIProductionBudgetTests`, `WipeoutDefeatTests`, `Ranged...`/`Tower...`, and save tests remain green.

---

### Task 6: Phase A integration and independent review

**Files:**
- Create: `docs/superpowers/reports/2026-10-29-rts-phase-a-verification.md`
- Review: all Phase A source/test/scene changes

- [ ] **Step 1: Run the full suite after the last code change**

Run `RegressionTestMenu.RunPlayModeTests()`, save `Logs/RegressionTests.txt` to `.superpowers/sdd/phase-a/final-full-regression.log`, and report actual total/passed/failed/skipped/inconclusive/duration. Do not infer from added tests.

- [ ] **Step 2: Run focused production-path checks**

Verify unit caps for player/AI, stockpile clamp and gathering diversion, sheep harvest/regrow, player repair button behavior, and AI repair. Confirm no formal save-slot writes and that the scene is stopped/dirty=false. Physical GUI acceptance remains explicitly unperformed.

- [ ] **Step 3: Scope audit and independent review**

Generate earliest-snapshot-to-current no-index diffs. Review that Phase A did not add population, worker repair, tech, upkeep, stat/wave/starting-resource changes, full HUD rebuild or save-version changes. Resolve Critical/Important findings; record remaining Minor items and the physical GUI boundary.

- [ ] **Step 4: Save progress**

Update `.superpowers/sdd/progress.md`, write `.superpowers/sdd/phase-a/RESUME.md`, and report completed tasks, final machine result, remaining Minor items and the next-phase recommendation (Phase B strategy systems). Do not start Phase B.
