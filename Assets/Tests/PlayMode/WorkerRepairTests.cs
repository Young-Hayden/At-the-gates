using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Phase B Task 2: the second repair path. A worker ordered onto a damaged friendly
/// building walks to a tile beside its footprint and repairs it for free at 20 HP/s.
/// The order is refused (and the click falls through to a plain move) for full health,
/// half-built, wrecked, neutral and enemy buildings. Workers keep the existing retreat
/// logic: a hit sends them home, and they resume the same repair once the area is safe.
/// Worker repair never touches the bank — the instant paid repair stays the fast option.
/// </summary>
public class WorkerRepairTests : RegressionFixture
{
    static Team PlayerTeam =>
        GameState.Instance != null ? GameState.Instance.playerTeam : Team.Blue;

    static Team EnemyTeam => PlayerTeam == Team.Blue ? Team.Red : Team.Blue;

    // ---------------------------------------------------------- core behaviour

    [UnityTest]
    public IEnumerator Worker_OrderedOntoDamagedBuilding_WalksAdjacentAndRepairsAtTwentyPerSecond()
    {
        PaintRect(-8, -8, 24, 16);
        var worker = SpawnWorker(new Vector3Int(0, 0));
        var house = CreateBuilding(new Vector3Int(6, 0), PlayerTeam, maxHealth: 2000);
        house.TakeDamage(1000);

        OrderRepair(worker, house);

        yield return WaitUntil(() => worker.RepairTarget == house, 2f);
        Assert.AreSame(house, worker.RepairTarget,
            "Right-clicking a damaged friendly building must assign it as the repair target.");

        yield return WaitUntil(() => worker.IsRepairing, 5f);
        Assert.IsTrue(worker.IsRepairing, "The worker must walk to the building and start repairing.");
        Assert.LessOrEqual(DistanceToFootprint(worker.transform.position, house),
            worker.repairReach + 0.01f,
            "A worker must stand adjacent to the footprint before repairing, not repair from across the map.");

        int before = house.health;
        float start = Time.time;
        yield return new WaitForSeconds(1f);
        float elapsed = Mathf.Max(0.01f, Time.time - start);
        int healed = house.health - before;
        float rate = healed / elapsed;

        Assert.Greater(healed, 0, "A repairing worker must restore health over the measured second.");
        Assert.That(rate, Is.InRange(12f, 28f),
            "Worker repair must run at about 20 HP/s; measured " + rate.ToString("0.0") +
            " HP/s (" + healed + " HP over " + elapsed.ToString("0.00") + " s).");
    }

    [UnityTest]
    public IEnumerator Worker_RepairStopsAtFullHealth_AndWorkerReturnsToIdle()
    {
        PaintRect(-8, -8, 24, 16);
        var worker = SpawnWorker(new Vector3Int(0, 0));
        var house = CreateBuilding(new Vector3Int(6, 0), PlayerTeam, maxHealth: 200);
        house.TakeDamage(60);

        OrderRepair(worker, house);
        yield return WaitUntil(() => worker.IsRepairing, 5f);
        Assert.IsTrue(worker.IsRepairing, "Control: the worker must begin repairing.");

        yield return WaitUntil(() => house.MissingHealth == 0, 8f);
        Assert.AreEqual(house.maxHealth, house.health,
            "Sustained worker repair must restore the building to full health.");

        yield return null;
        Assert.IsNull(worker.RepairTarget,
            "Repair must stop and clear its target the moment the building is whole.");
        Assert.IsFalse(worker.IsRepairing, "A finished repair must not leave the worker in the repair phase.");
        Assert.AreEqual("空闲", worker.Status, "The worker must report idle after finishing the repair.");
    }

    // --------------------------------------------------------------- refusals

    [UnityTest]
    public IEnumerator Order_FullHealthBuilding_FallsThroughToMove()
    {
        PaintRect(-8, -8, 24, 16);
        var worker = SpawnWorker(new Vector3Int(0, 0));
        var house = CreateBuilding(new Vector3Int(6, 0), PlayerTeam, maxHealth: 200);
        Assert.AreEqual(0, house.MissingHealth, "Control: the building is at full health.");

        OrderRepair(worker, house);
        yield return null;

        AssertFellThroughToMove(worker);
        Assert.AreEqual(house.maxHealth, house.health, "A full-health building must not be repaired.");
    }

    [UnityTest]
    public IEnumerator Order_UnderConstructionBuilding_FallsThroughToMove()
    {
        PaintRect(-8, -8, 24, 16);
        var worker = SpawnWorker(new Vector3Int(0, 0));
        var house = CreateBuilding(new Vector3Int(6, 0), PlayerTeam, maxHealth: 200);
        house.constructionSeconds = 6f;
        house.MarkBuiltDuringMatch();
        Assert.IsTrue(house.IsUnderConstruction, "Control: the building must still be under construction.");
        house.TakeDamage(50);

        OrderRepair(worker, house);
        yield return null;

        AssertFellThroughToMove(worker);
        Assert.AreEqual(150, house.health, "A half-built building must not be repaired by a worker.");
    }

    [UnityTest]
    public IEnumerator Order_WreckedBuilding_FallsThroughToMove()
    {
        PaintRect(-8, -8, 24, 16);
        var worker = SpawnWorker(new Vector3Int(0, 0));
        var house = CreateBuilding(new Vector3Int(6, 0), PlayerTeam, maxHealth: 200);
        // Permanent rubble: keeps its collider and footprint so it can still be clicked.
        house.RestoreWreckedState(keepBlocking: true);
        Assert.IsFalse(house.IsAlive, "Control: the building must be wrecked rubble.");
        Assert.AreEqual(0, house.health, "Control: rubble has no health.");

        OrderRepair(worker, house);
        yield return null;

        AssertFellThroughToMove(worker);
        Assert.AreEqual(0, house.health, "Rubble must never be repaired by a worker.");
    }

    [UnityTest]
    public IEnumerator Order_NeutralScenery_FallsThroughToMove()
    {
        PaintRect(-8, -8, 24, 16);
        var worker = SpawnWorker(new Vector3Int(0, 0));
        var scenery = CreateBuilding(new Vector3Int(6, 0), Team.Neutral, maxHealth: 200);
        scenery.TakeDamage(50);
        Assert.AreEqual(Team.Neutral, scenery.team, "Control: the scenery must be neutral.");

        OrderRepair(worker, scenery);
        yield return null;

        AssertFellThroughToMove(worker);
        Assert.AreEqual(150, scenery.health, "Neutral scenery must never be repaired by a worker.");
    }

    [UnityTest]
    public IEnumerator Order_EnemyBuilding_FallsThroughToMove()
    {
        PaintRect(-8, -8, 24, 16);
        var worker = SpawnWorker(new Vector3Int(0, 0));
        var house = CreateBuilding(new Vector3Int(6, 0), EnemyTeam, maxHealth: 200);
        house.TakeDamage(50);
        Assert.AreEqual(EnemyTeam, house.team, "Control: the building must belong to the other side.");

        OrderRepair(worker, house);
        yield return null;

        AssertFellThroughToMove(worker);
        Assert.AreEqual(150, house.health, "A worker must never repair an enemy building.");
    }

    // ------------------------------------------------------- stacking and bank

    [UnityTest]
    public IEnumerator TwoWorkers_RepairFasterThanOne()
    {
        PaintRect(-10, -8, 28, 16);
        var first = SpawnWorker(new Vector3Int(0, 0));
        var second = SpawnWorker(new Vector3Int(0, 2));
        var house = CreateBuilding(new Vector3Int(8, 0), PlayerTeam, maxHealth: 4000);
        house.TakeDamage(2000);

        OrderRepair(first, house);
        yield return WaitUntil(() => first.IsRepairing, 6f);
        Assert.IsTrue(first.IsRepairing, "Control: one worker must reach the building and repair.");

        int beforeOne = house.health;
        float oneStart = Time.time;
        yield return new WaitForSeconds(1f);
        float rateOne = (house.health - beforeOne) / Mathf.Max(0.01f, Time.time - oneStart);

        OrderRepair(second, house);
        yield return WaitUntil(() => second.IsRepairing, 6f);
        Assert.IsTrue(second.IsRepairing, "The second worker must reach the building and join the repair.");
        Assert.AreSame(house, first.RepairTarget, "The first worker must still hold the repair task.");
        Assert.AreSame(house, second.RepairTarget, "Both workers must hold the same repair target.");

        int beforeTwo = house.health;
        float twoStart = Time.time;
        yield return new WaitForSeconds(1f);
        float rateTwo = (house.health - beforeTwo) / Mathf.Max(0.01f, Time.time - twoStart);

        Assert.Greater(rateOne, 0f, "One worker must make progress.");
        Assert.Greater(rateTwo, rateOne * 1.5f,
            "Two workers must stack: the pair repaired at " + rateTwo.ToString("0.0") +
            " HP/s against " + rateOne.ToString("0.0") + " HP/s for one.");
    }

    [UnityTest]
    public IEnumerator WorkerRepair_DoesNotSpendTheBank()
    {
        PaintRect(-8, -8, 24, 16);
        var worker = SpawnWorker(new Vector3Int(0, 0));
        var house = CreateBuilding(new Vector3Int(6, 0), PlayerTeam, maxHealth: 2000);
        house.TakeDamage(1000);
        Economy.gold = 41;
        Economy.wood = 17;
        Economy.meat = 23;

        OrderRepair(worker, house);
        yield return WaitUntil(() => worker.IsRepairing, 5f);
        int before = house.health;
        yield return new WaitForSeconds(0.8f);

        Assert.Greater(house.health, before, "Control: the worker must actually be repairing.");
        Assert.AreEqual(41, Economy.Instance.gold, "Worker repair is free: gold must not change.");
        Assert.AreEqual(17, Economy.Instance.wood, "Worker repair is free: wood must not change.");
        Assert.AreEqual(23, Economy.Instance.meat, "Worker repair is free: meat must not change.");
    }

    // ------------------------------------------------------ retreat and resume

    [UnityTest]
    public IEnumerator Worker_DamagedMidRepair_FleesAndResumesRepair()
    {
        PaintRect(-10, -8, 28, 16);
        var worker = SpawnWorker(new Vector3Int(0, 0));
        worker.GetComponent<UnitMover>().speed = 20f;
        worker.fleeResumeSeconds = 0.2f;
        var house = CreateBuilding(new Vector3Int(7, 0), PlayerTeam, maxHealth: 2000);
        house.TakeDamage(1000);
        var retreatBase = CreateDropoff(new Vector3Int(-4, -3));

        OrderRepair(worker, house);
        yield return WaitUntil(() => worker.IsRepairing, 6f);
        Assert.IsTrue(worker.IsRepairing, "Control: the worker must be repairing before the hit.");

        worker.GetComponent<UnitActor>().TakeDamage(1);
        Assert.IsTrue(worker.IsRetreating, "Damage must send the repairing worker into the existing retreat.");
        Assert.IsNull(worker.RepairTarget, "A fleeing worker must drop the active repair target.");

        yield return WaitUntil(() => worker.RetreatBase == retreatBase, 2f);
        Assert.AreSame(retreatBase, worker.RetreatBase, "The worker must flee to the friendly drop-off.");

        yield return WaitUntil(() => worker.RepairTarget == house && worker.IsRepairing, 12f);
        Assert.IsTrue(worker.IsRepairing, "The worker must resume repairing the same building after the retreat.");
        Assert.IsFalse(worker.IsRetreating, "The resumed worker must no longer be retreating.");

        int before = house.health;
        yield return new WaitForSeconds(0.5f);
        Assert.Greater(house.health, before, "Resumed repair must keep restoring health.");
    }

    // ----------------------------------------------------- non-worker fall-through

    [UnityTest]
    public IEnumerator Soldier_WithoutGatherer_DoesNotRepair_AndStillMoves()
    {
        PaintRect(-8, -8, 24, 16);
        var soldier = CreateSoldier(new Vector3Int(0, 0));
        var house = CreateBuilding(new Vector3Int(6, 0), PlayerTeam, maxHealth: 200);
        house.TakeDamage(50);

        var mover = soldier.GetComponent<UnitMover>();
        Selection.selected.Clear();
        Selection.selected.Add(mover);
        Physics2D.SyncTransforms();
        Selection.IssueMove(ClickPoint(house));
        yield return null;

        Assert.IsNull(soldier.GetComponent<Gatherer>(), "Control: the soldier carries no Gatherer.");
        Assert.IsTrue(mover.Moving,
            "Only units with a Gatherer accept a repair order; a soldier's click must stay a move.");
        yield return new WaitForSeconds(0.5f);
        Assert.AreEqual(150, house.health, "A unit without a Gatherer must never repair a building.");
    }

    // ------------------------------------------------------------------ helpers

    void AssertFellThroughToMove(Gatherer worker)
    {
        Assert.IsNull(worker.RepairTarget, "A refused repair click must not become a repair task.");
        Assert.IsFalse(worker.IsRepairing, "A refused repair click must not enter the repair phase.");
        Assert.IsTrue(worker.IsManuallyIdle,
            "A refused repair click must fall through to a plain move, exactly like other refused orders.");
    }

    Gatherer SpawnWorker(Vector3Int cell)
    {
        var go = new GameObject("Worker");
        go.transform.position = Grid.CellCenter(cell);
        var actor = go.AddComponent<UnitActor>();
        actor.team = PlayerTeam;
        actor.showHealthBar = false;
        actor.maxHealth = 70;
        actor.health = 70;

        var gatherer = go.AddComponent<Gatherer>();
        gatherer.autoStart = false;
        gatherer.autoStartRange = 50f;
        gatherer.retaskSeconds = 0.1f;
        gatherer.repairRate = 20f;
        gatherer.GetComponent<UnitMover>().speed = 20f;
        return gatherer;
    }

    UnitActor CreateSoldier(Vector3Int cell)
    {
        var go = new GameObject("Soldier");
        go.transform.position = Grid.CellCenter(cell);
        go.AddComponent<UnitMover>().speed = 20f;
        var actor = go.AddComponent<UnitActor>();
        actor.team = PlayerTeam;
        actor.showHealthBar = false;
        actor.isCombatant = true;
        actor.maxHealth = 100;
        actor.health = 100;
        return actor;
    }

    Building CreateBuilding(Vector3Int cell, Team team, int maxHealth)
    {
        var go = new GameObject("Test building");
        go.transform.position = Grid.CellCenter(cell) - new Vector3(0f, 0.5f, 0f);
        go.AddComponent<BoxCollider2D>();
        var building = go.AddComponent<Building>();
        building.footprint = Vector2Int.one;
        building.depth = 1;
        building.team = team;
        building.constructionSeconds = 0f;
        building.maxHealth = maxHealth;
        building.health = maxHealth;
        building.Recompute();
        return building;
    }

    Building CreateDropoff(Vector3Int cell)
    {
        var building = CreateBuilding(cell, PlayerTeam, maxHealth: 500);
        building.isDropoff = true;
        return building;
    }

    void OrderRepair(Gatherer worker, Building building)
    {
        Physics2D.SyncTransforms();
        Selection.selected.Clear();
        Selection.selected.Add(worker.GetComponent<UnitMover>());
        Selection.IssueMove(ClickPoint(building));
    }

    /// <summary>An interior point of a 1x1 footprint building, so the physics
    /// overlap that recognises a click lands inside the collider.</summary>
    static Vector3 ClickPoint(Building building) =>
        building.transform.position + new Vector3(0f, 0.25f, 0f);

    float DistanceToFootprint(Vector3 from, Building building)
    {
        float best = float.MaxValue;
        for (int i = 0; i < building.Cells.Count; i++)
            best = Mathf.Min(best, Vector2.Distance(from, Grid.CellCenter(building.Cells[i])));
        return best;
    }

    static IEnumerator WaitUntil(Func<bool> condition, float timeout)
    {
        float deadline = Time.time + timeout;
        while (!condition() && Time.time < deadline)
            yield return null;
    }
}
