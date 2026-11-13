using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// Task 2 regressions: workers are noncombatants, cannot accept attack orders, and
/// run to a completed base when hurt instead of fighting back. While retreating they
/// keep their cargo, count as working, and only resume work once the area is safe.
/// </summary>
public class WorkerBehaviorTests : RegressionFixture
{
    const string BluePawnPath = "Assets/Prefabs/Units/Pawn_Blue.prefab";
    const string RedPawnPath = "Assets/Prefabs/Units/Pawn_Red.prefab";

    // ------------------------------------------------------------ noncombat

    [Test]
    public void WorkerPrefabs_HaveNoCombatStats()
    {
        foreach (var path in new[] { BluePawnPath, RedPawnPath })
        {
            var prefab = LoadPrefab<GameObject>(path);
            var actor = prefab.GetComponent<UnitActor>();
            Assert.IsNotNull(actor, path + " must carry a UnitActor.");
            Assert.IsFalse(actor.isCombatant, path + " must not be a combatant.");
            Assert.AreEqual(0, actor.attackDamage, path + " must not deal attack damage.");
            Assert.IsNull(actor.projectile, path + " must not fire a projectile.");
        }
    }

    [UnityTest]
    public IEnumerator Worker_HasNoCombatDamage_AndCannotAcceptAttackOrder()
    {
        PaintRect(-6, -6, 12, 12);
        var worker = SpawnWorker(new Vector3Int(0, 0), autoStart: false);
        var actor = worker.GetComponent<UnitActor>();
        var ai = worker.gameObject.AddComponent<CombatAI>();
        yield return null;

        Assert.IsFalse(actor.isCombatant, "A spawned worker must be a noncombatant.");
        Assert.AreEqual(0, actor.attackDamage, "A worker must deal no attack damage.");
        Assert.IsNull(actor.projectile, "A worker must not fire a projectile.");

        var enemy = CreateActor("Enemy target", new Vector3Int(4, 0, 0), Team.Red, combatant: true);
        Physics2D.SyncTransforms();
        Selection.selected.Add(worker.GetComponent<UnitMover>());
        Selection.IssueMove(enemy.transform.position);

        Assert.IsNull(ai.Target, "A noncombatant worker must not accept an attack order.");
        Assert.IsFalse(ai.IsHolding, "A worker must not enter a hold order.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Hud_Noncombatant_HidesAttackStats()
    {
        PaintRect(-6, -6, 12, 12);
        var worker = SpawnWorker(new Vector3Int(0, 0), autoStart: false);
        Selection.selected.Add(worker.GetComponent<UnitMover>());

        var panel = new GameObject("Test selection panel");
        var title = CreateText("Test selection title");
        var body = CreateText("Test selection body");
        var hudObject = new GameObject("Test HUD");
        var hud = hudObject.AddComponent<HUD>();
        hud.selectionPanel = panel;
        hud.selectionTitle = title;
        hud.selectionBody = body;
        yield return null;

        Assert.IsTrue(panel.activeSelf, "The living worker control should show the panel.");
        StringAssert.DoesNotContain("攻击", body.text,
            "A noncombatant's selection panel must not advertise attack damage or range.");
        StringAssert.Contains("生命", body.text,
            "The panel should still show the worker's health.");
    }

    // --------------------------------------------------------------- retreat

    [UnityTest]
    public IEnumerator Worker_Damage_InterruptsWork_AndPreservesCargo()
    {
        PaintRect(-6, -6, 12, 12);
        var worker = SpawnWorker(new Vector3Int(0, 0), autoStart: false);
        worker.GetComponent<UnitMover>().speed = 20f;
        worker.carryCapacity = 20;
        var node = CreateNode("Retreat wood", new Vector3Int(1, 0, 0));
        node.perTrip = 5;
        node.harvestSeconds = 0.15f;

        worker.Assign(node);
        yield return WaitUntil(() => worker.Carrying > 0, 3f);
        int carried = worker.Carrying;
        Assert.Greater(carried, 0, "The control worker should harvest before the hit.");

        var actor = worker.GetComponent<UnitActor>();
        int healthBefore = actor.health;
        actor.TakeDamage(1);

        Assert.IsTrue(actor.IsAlive, "One point of damage must not be lethal.");
        Assert.AreEqual(healthBefore - 1, actor.health,
            "Nonlethal damage should be applied before the retreat event fires.");
        Assert.IsTrue(worker.IsRetreating, "Damage must interrupt gathering and start a retreat.");
        Assert.AreEqual(carried, worker.Carrying,
            "A worker must keep the load it was carrying when it was hurt.");
        Assert.IsNull(worker.TargetNode, "Retreating must drop the current gathering target.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Worker_ReachesBase_DeliversCargoExactlyOnce()
    {
        PaintRect(-8, -6, 20, 12);
        Economy.wood = 0;
        var worker = SpawnWorker(new Vector3Int(0, 0), autoStart: false);
        worker.GetComponent<UnitMover>().speed = 20f;
        worker.carryCapacity = 20;
        worker.fleeResumeSeconds = 5f;
        var dropoff = CreateDropoff(new Vector3Int(4, 0, 0), Team.Blue);
        var node = CreateNode("Delivery wood", new Vector3Int(1, 0, 0));
        node.perTrip = 5;
        node.harvestSeconds = 0.15f;

        worker.Assign(node);
        yield return WaitUntil(() => worker.Carrying > 0, 3f);
        int carried = worker.Carrying;
        Assert.Greater(carried, 0, "The control worker should harvest before the hit.");
        worker.GetComponent<UnitActor>().TakeDamage(1);

        yield return WaitUntil(() => worker.Carrying == 0, 6f);
        Assert.AreEqual(0, worker.Carrying, "The load should be handed over at the base.");
        Assert.AreEqual(carried, Economy.wood,
            "The load should be credited to the worker's economy exactly once.");
        Assert.IsTrue(worker.IsRetreating,
            "The worker should still be waiting at its base after delivering.");
        Assert.AreSame(dropoff, worker.RetreatBase,
            "The retreat should target the nearest completed friendly drop-off.");

        yield return new WaitForSeconds(0.4f);
        Assert.AreEqual(carried, Economy.wood,
            "Cargo must not be credited a second time while the worker waits at the base.");
    }

    [UnityTest]
    public IEnumerator Worker_SafeAtBase_ResumesWorkAfterThreatClears()
    {
        PaintRect(-8, -6, 24, 12);
        var worker = SpawnWorker(new Vector3Int(0, 0), autoStart: true);
        worker.GetComponent<UnitMover>().speed = 20f;
        worker.retaskSeconds = 0.1f;
        worker.fleeResumeSeconds = 0.5f;
        var node = CreateNode("Resume wood", new Vector3Int(6, 0, 0));
        CreateDropoff(new Vector3Int(2, 0, 0), Team.Blue);
        var enemy = CreateActor("Enemy soldier", new Vector3Int(2, 3, 0), Team.Red, combatant: true);

        worker.GetComponent<UnitActor>().TakeDamage(1);
        yield return WaitUntil(() => worker.Status == "基地待命", 5f);
        Assert.IsTrue(worker.IsRetreating, "The hurt worker should reach its base.");
        Assert.IsFalse(enemy == null, "The threat control must exist.");

        // The enemy sits inside fleeSafeRadius, so the resume timer keeps resetting.
        yield return new WaitForSeconds(1f);
        Assert.IsTrue(worker.IsRetreating,
            "A living enemy combatant inside the safe radius must keep the worker at base.");

        enemy.transform.position = new Vector3(100f, 100f, 0f);
        yield return WaitUntil(() => !worker.IsRetreating, 3f);
        Assert.IsFalse(worker.IsRetreating, "The worker should stand down after the threat clears.");
        Assert.IsFalse(worker.IsManuallyIdle,
            "An automatically working worker must not become manually idle after a retreat.");

        yield return WaitUntil(() => worker.TargetNode != null, 2f);
        Assert.AreSame(node, worker.TargetNode,
            "An automatic worker should pick up gathering work again after the retreat.");
    }

    [UnityTest]
    public IEnumerator Worker_ManualIdle_StaysManualAfterSafe()
    {
        PaintRect(-8, -6, 24, 12);
        var worker = SpawnWorker(new Vector3Int(0, 0), autoStart: true);
        worker.GetComponent<UnitMover>().speed = 20f;
        worker.retaskSeconds = 0.1f;
        worker.fleeResumeSeconds = 0.5f;
        CreateNode("Manual idle work candidate", new Vector3Int(6, 0, 0));
        CreateDropoff(new Vector3Int(2, 0, 0), Team.Blue);

        var mover = worker.GetComponent<UnitMover>();
        Selection.selected.Add(mover);
        Selection.IssueMove(Grid.CellCenter(new Vector3Int(0, 3, 0)));
        yield return null;
        Assert.IsTrue(worker.IsManuallyIdle, "The manual move control should set manual idle.");

        worker.GetComponent<UnitActor>().TakeDamage(1);
        Assert.IsTrue(worker.IsRetreating, "The hit must interrupt the manual move.");

        yield return WaitUntil(() => !worker.IsRetreating, 4f);
        Assert.IsFalse(worker.IsRetreating, "The worker should stand down once safe.");
        Assert.IsTrue(worker.IsManuallyIdle,
            "A worker that was manually idle before the hit must remain manually idle afterwards.");

        yield return new WaitForSeconds(0.5f);
        Assert.IsNull(worker.TargetNode,
            "Restored manual idle must still block automatic retasking.");
    }

    [UnityTest]
    public IEnumerator Worker_NoBase_WaitsInRetreat_ThenFleesWhenBaseBuilt()
    {
        PaintRect(-8, -6, 20, 12);
        Economy.wood = 0;
        var worker = SpawnWorker(new Vector3Int(0, 0), autoStart: true);
        worker.GetComponent<UnitMover>().speed = 20f;
        worker.retaskSeconds = 0.1f;
        worker.baseRetrySeconds = 0.1f;
        var node = CreateNode("Base-less wood", new Vector3Int(1, 0, 0));
        node.perTrip = 5;
        node.harvestSeconds = 0.15f;

        worker.Assign(node);
        yield return WaitUntil(() => worker.Carrying > 0, 3f);
        int carried = worker.Carrying;
        int nodeAmount = node.amount;
        worker.GetComponent<UnitActor>().TakeDamage(1);
        Assert.IsTrue(worker.IsRetreating, "Damage must start a retreat even without a base.");

        // Several base-retry intervals pass with nothing to flee to.
        yield return new WaitForSeconds(1f);
        Assert.IsTrue(worker.IsRetreating,
            "With no completed base the worker must stay in the retreat state.");
        Assert.AreEqual(carried, worker.Carrying, "The load must be kept while waiting.");
        Assert.IsNull(worker.TargetNode, "No automatic work may override the retreat.");
        Assert.AreEqual(nodeAmount, node.amount,
            "A retreating worker must not resume harvesting while it has no base.");

        var dropoff = CreateDropoff(new Vector3Int(4, 0, 0), Team.Blue);
        yield return WaitUntil(() => worker.Carrying == 0, 6f);
        Assert.AreEqual(carried, Economy.wood,
            "Once a base exists the worker should flee there and deliver its load.");
        Assert.AreSame(dropoff, worker.RetreatBase,
            "The newly built base should become the retreat destination.");
    }

    [UnityTest]
    public IEnumerator Worker_ExplicitGatherOrClear_CancelsRetreat()
    {
        PaintRect(-8, -6, 20, 12);
        var worker = SpawnWorker(new Vector3Int(0, 0), autoStart: false);
        CreateDropoff(new Vector3Int(4, 0, 0), Team.Blue);
        var node = CreateNode("Explicit wood", new Vector3Int(-4, 0, 0));
        var bush = CreateBush("Explicit bush", new Vector3Int(0, 4, 0));

        worker.GetComponent<UnitActor>().TakeDamage(1);
        Assert.IsTrue(worker.IsRetreating, "The hit must start a retreat.");
        worker.Assign(node);
        Assert.IsFalse(worker.IsRetreating, "An explicit gather order must cancel the retreat.");
        Assert.AreSame(node, worker.TargetNode, "The gather order should be accepted at once.");

        worker.GetComponent<UnitActor>().TakeDamage(1);
        Assert.IsTrue(worker.IsRetreating, "A second hit must start a new retreat.");
        worker.AssignClear(bush);
        Assert.IsFalse(worker.IsRetreating, "An explicit clear order must cancel the retreat.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Worker_OrdinaryMove_ClearsRetreatAndSetsManualIdle()
    {
        PaintRect(-8, -6, 20, 12);
        var worker = SpawnWorker(new Vector3Int(0, 0), autoStart: false);
        worker.GetComponent<UnitMover>().speed = 20f;
        CreateDropoff(new Vector3Int(4, 0, 0), Team.Blue);

        worker.GetComponent<UnitActor>().TakeDamage(1);
        Assert.IsTrue(worker.IsRetreating, "The hit must start a retreat.");

        var mover = worker.GetComponent<UnitMover>();
        Selection.selected.Add(mover);
        Selection.IssueMove(Grid.CellCenter(new Vector3Int(0, 3, 0)));

        Assert.IsFalse(worker.IsRetreating, "An ordinary move order must cancel the retreat.");
        Assert.IsTrue(worker.IsManuallyIdle,
            "An ordinary move must leave the worker manually idle, as before.");
        yield return null;
    }

    // ----------------------------------------------------------- threat query

    [UnityTest]
    public IEnumerator ThreatQuery_IgnoresNoncombatantsAndUnarmedBuildings()
    {
        PaintRect(-6, -6, 16, 12);
        var worker = SpawnWorker(new Vector3Int(0, 0), autoStart: false);
        var enemyWorker = CreateActor("Enemy worker", new Vector3Int(3, 0, 0), Team.Red, combatant: false);

        Assert.IsFalse(Combatants.HasThreat(Team.Blue, worker.transform.position, 10f),
            "A noncombatant enemy worker must not count as a threat.");

        enemyWorker.isCombatant = true;
        Assert.IsTrue(Combatants.HasThreat(Team.Blue, worker.transform.position, 10f),
            "A living enemy combatant inside the radius must count as a threat.");
        Assert.IsFalse(Combatants.HasThreat(Team.Blue, worker.transform.position, 2f),
            "An enemy outside the requested radius must not count as a threat.");

        enemyWorker.isCombatant = false;
        enemyWorker.transform.position = new Vector3(100f, 100f, 0f);

        var buildingObject = new GameObject("Enemy unarmed tower");
        buildingObject.transform.position =
            Grid.CellCenter(new Vector3Int(5, 0, 0)) - new Vector3(0f, 0.5f, 0f);
        buildingObject.AddComponent<BoxCollider2D>();
        var building = buildingObject.AddComponent<Building>();
        building.footprint = Vector2Int.one;
        building.depth = 1;
        building.team = Team.Red;
        building.constructionSeconds = 0f;
        var garrison = buildingObject.AddComponent<TowerGarrison>();
        building.Recompute();
        yield return null;

        Assert.IsFalse(Combatants.HasThreat(Team.Blue, worker.transform.position, 10f),
            "An enemy building without a garrisoned archer must not count as a threat.");

        var archerObject = new GameObject("Enemy archer");
        archerObject.transform.position = MapGrid.Instance.CellCenter(new Vector3Int(8, 0, 0));
        var archer = archerObject.AddComponent<UnitActor>();
        archer.team = Team.Red;
        archer.showHealthBar = false;
        archer.attackRange = 0.1f;
        archer.projectile = new GameObject("Archer projectile stand-in");
        Assert.IsTrue(garrison.Garrison(archer),
            "The control archer should be accepted by the tower garrison.");

        Assert.IsTrue(Combatants.HasThreat(Team.Blue, worker.transform.position, 10f),
            "An enemy tower with a garrisoned archer must count as a threat.");
    }

    [UnityTest]
    public IEnumerator Worker_RetreatBaseDestroyedMidRoute_DoesNotDeliverAtRubble_ThenFindsReplacement()
    {
        PaintRect(-8, -6, 24, 16);
        Economy.wood = 0;
        var worker = SpawnWorker(new Vector3Int(0, 0), autoStart: false);
        var mover = worker.GetComponent<UnitMover>();
        mover.speed = 2f;                       // slow enough to destroy the base mid-walk
        worker.carryCapacity = 20;
        worker.fleeResumeSeconds = 5f;
        worker.baseRetrySeconds = 0.1f;
        var node = CreateNode("Rubble wood", new Vector3Int(1, 0, 0));
        node.perTrip = 5;
        node.harvestSeconds = 0.15f;
        var doomed = CreateDropoff(new Vector3Int(6, 0, 0), Team.Blue);

        worker.Assign(node);
        yield return WaitUntil(() => worker.Carrying > 0, 3f);
        int carried = worker.Carrying;
        Assert.Greater(carried, 0, "The control worker should harvest before the hit.");
        worker.GetComponent<UnitActor>().TakeDamage(1);

        yield return WaitUntil(() => worker.RetreatBase == doomed && mover.Moving, 3f);
        Assert.AreSame(doomed, worker.RetreatBase, "The worker should set out for its base.");
        Assert.IsTrue(mover.Moving, "The worker should be walking to its base.");

        doomed.TakeDamage(100000);
        yield return null;
        Assert.IsFalse(doomed.IsAlive, "The doomed base should be wrecked.");

        // Long enough for the old buggy path to finish walking and deliver at the rubble.
        yield return new WaitForSeconds(3.5f);
        Assert.IsTrue(worker.IsRetreating,
            "The worker must stay in retreat after losing the base it was walking to.");
        Assert.AreEqual(carried, worker.Carrying,
            "The load must not be dropped or delivered to a destroyed base.");
        Assert.AreEqual(0, Economy.wood, "Nothing may be credited at the rubble.");
        Assert.IsFalse(mover.Moving, "The worker must stop instead of walking into the rubble.");

        var replacement = CreateDropoff(new Vector3Int(4, 6, 0), Team.Blue);
        yield return WaitUntil(() => worker.Carrying == 0, 8f);
        Assert.AreEqual(0, worker.Carrying,
            "The worker should deliver at a replacement base once one exists.");
        Assert.AreEqual(carried, Economy.wood,
            "The load should be credited exactly once at the replacement base.");
        Assert.AreSame(replacement, worker.RetreatBase,
            "The replacement base should become the new retreat destination.");

        // Losing the base while waiting there must send the worker looking again.
        replacement.TakeDamage(100000);
        yield return null;
        Assert.IsTrue(worker.IsRetreating,
            "The worker must keep retreating after losing the base it was waiting at.");
        Assert.AreEqual("撤退中", worker.Status,
            "A destroyed wait base should put the worker back into the Fleeing phase.");
    }

    [UnityTest]
    public IEnumerator Worker_NormalDropoffDestroyedMidRoute_KeepsCargoUntilReplacement()
    {
        PaintRect(-8, -6, 24, 16);
        Economy.wood = 0;
        var worker = SpawnWorker(new Vector3Int(0, 0, 0), autoStart: false);
        var mover = worker.GetComponent<UnitMover>();
        mover.speed = 2f;
        worker.carryCapacity = 5;
        worker.dropoffRetrySeconds = 0.1f;
        var node = CreateNode("Normal delivery wood", new Vector3Int(1, 0, 0));
        node.perTrip = 5;
        node.harvestSeconds = 0.05f;
        var doomed = CreateDropoff(new Vector3Int(6, 0, 0), Team.Blue);

        worker.Assign(node);
        yield return WaitUntil(() => worker.Carrying == 5 && mover.Moving && worker.transform.position.x > 1.5f, 5f);
        Assert.AreEqual(5, worker.Carrying, "The worker must be carrying a full load before the base dies.");
        Assert.IsTrue(mover.Moving, "The worker must be walking to the ordinary drop-off.");

        doomed.TakeDamage(100000);
        yield return null;
        Assert.IsFalse(doomed.IsAlive, "The ordinary drop-off must really be destroyed.");
        yield return new WaitForSeconds(3.5f); // longer than the remaining old route

        Assert.AreEqual(5, worker.Carrying, "A destroyed ordinary drop-off must not consume the load.");
        Assert.AreEqual(0, Economy.wood, "Nothing may be credited when no living drop-off exists.");

        var replacement = CreateDropoff(new Vector3Int(4, 6, 0), Team.Blue);
        yield return WaitUntil(() => worker.Carrying == 0, 8f);
        Assert.AreEqual(0, worker.Carrying, "The worker must deliver once a replacement exists.");
        Assert.AreEqual(5, Economy.wood, "The retained load must be credited exactly once at the replacement.");
        Assert.IsNotNull(replacement);
    }

    // ---------------------------------------------------------------- helpers


    Gatherer SpawnWorker(Vector3Int cell, bool autoStart)
    {
        var go = new GameObject("Worker");
        go.transform.position = Grid.CellCenter(cell);
        var actor = go.AddComponent<UnitActor>();
        actor.team = Team.Blue;
        actor.showHealthBar = false;
        actor.maxHealth = 70;
        actor.health = 70;

        var gatherer = go.AddComponent<Gatherer>();
        gatherer.autoStart = autoStart;
        gatherer.autoStartRange = 50f;
        gatherer.retaskSeconds = 0.1f;
        return gatherer;
    }

    static UnitActor CreateActor(string name, Vector3Int cell, Team team, bool combatant)
    {
        var go = new GameObject(name);
        go.transform.position = MapGrid.Instance.CellCenter(cell);
        var actor = go.AddComponent<UnitActor>();
        actor.team = team;
        actor.showHealthBar = false;
        actor.maxHealth = 100;
        actor.health = 100;
        actor.isCombatant = combatant;
        return actor;
    }

    static ResourceNode CreateNode(string name, Vector3Int cell)
    {
        var go = new GameObject(name);
        go.transform.position = MapGrid.Instance.CellCenter(cell);
        var node = go.AddComponent<ResourceNode>();
        node.kind = ResourceKind.Wood;
        node.amount = 100;
        node.maxAmount = 100;
        node.perTrip = 5;
        node.harvestSeconds = 0.05f;
        return node;
    }

    static ClearableBush CreateBush(string name, Vector3Int cell)
    {
        var go = new GameObject(name);
        go.transform.position = MapGrid.Instance.CellCenter(cell);
        return go.AddComponent<ClearableBush>();
    }

    static Building CreateDropoff(Vector3Int cell, Team team)
    {
        var go = new GameObject("Test drop-off");
        go.transform.position = MapGrid.Instance.CellCenter(cell) - new Vector3(0f, 0.5f, 0f);
        go.AddComponent<BoxCollider2D>();
        var building = go.AddComponent<Building>();
        building.footprint = Vector2Int.one;
        building.depth = 1;
        building.team = team;
        building.isDropoff = true;
        building.constructionSeconds = 0f;
        building.Recompute();
        return building;
    }

    static Text CreateText(string name)
    {
        return new GameObject(name).AddComponent<Text>();
    }

    static IEnumerator WaitUntil(Func<bool> condition, float timeout)
    {
        float deadline = Time.time + timeout;
        while (!condition() && Time.time < deadline)
            yield return null;
    }
}
