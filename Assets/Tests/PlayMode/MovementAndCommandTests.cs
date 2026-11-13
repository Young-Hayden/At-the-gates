using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// Regression coverage for stale paths, unreachable gathering, explicit player
/// commands, healing range, and actors that die before their corpse is destroyed.
/// </summary>
public class MovementAndCommandTests : RegressionFixture
{
    [UnityTest]
    public IEnumerator StalePath_NewBlocker_StopsWithoutEnteringOrReportingArrival()
    {
        // A one-cell-wide corridor. The path is deliberately issued before the
        // middle cell becomes blocked, so this exercises the stale-path case.
        PaintRect(0, 0, 3, 1);
        var mover = CreateMover("Stale path unit", new Vector3Int(0, 0, 0));
        mover.speed = 6f;
        int arrivals = 0;
        mover.SetPath(new List<Vector3Int>
        {
            new Vector3Int(0, 0, 0),
            new Vector3Int(1, 0, 0),
            new Vector3Int(2, 0, 0),
        }, () => arrivals++);
        Assert.IsTrue(mover.Moving, "The stale-path control should have been accepted.");
        Assert.IsTrue(Grid.Walkable(new Vector3Int(1, 0, 0)),
            "The corridor must be open when the path is issued.");

        // Register the blocker in the same frame as SetPath, before any Update runs,
        // so the outcome cannot depend on whether this unit moved before the block.
        var blocked = new Vector3Int(1, 0, 0);
        Grid.AddBuildingFootprint(new[] { blocked });
        yield return null;

        bool enteredBlockedCell = false;
        float deadline = Time.time + 1f;
        while (Time.time < deadline)
        {
            if (Grid.WorldToCell(mover.transform.position) == blocked)
                enteredBlockedCell = true;
            yield return null;
        }

        bool stillMoving = mover.Moving;
        Vector3 stoppedAt = mover.transform.position;
        Grid.RemoveBuildingFootprint(new[] { blocked });

        Assert.IsFalse(enteredBlockedCell,
            "The unit entered a blocker added after its path was issued. " +
            "Position=" + stoppedAt + ", arrivals=" + arrivals + ", Moving=" + stillMoving + ".");
        Assert.IsFalse(stillMoving, "A blocked stale path must stop the mover.");
        Assert.AreEqual(0, arrivals, "Stopping on a blocked cell must not report a false arrival.");
    }

    [UnityTest]
    public IEnumerator UnreachableGather_RetriesWithBackoff_AndHarvestsAfterReconnect()
    {
        PaintRect(-12, -1, 5, 3);
        PaintRect(8, -1, 5, 3);
        var gatherer = CreateWorker("Backoff worker", new Vector3Int(-10, 0, 0), false);
        gatherer.GetComponent<UnitMover>().speed = 30f;
        gatherer.unreachableRetrySeconds = 0.75f;
        gatherer.dropoffRetrySeconds = 10f;
        var node = CreateNode("Unreachable wood", new Vector3Int(10, 0, 0));

        gatherer.Assign(node);
        yield return null;
        int attemptsBeforeWait = gatherer.PathAttempts;
        Assert.GreaterOrEqual(attemptsBeforeWait, 1,
            "The control setup should make an initial path request while disconnected.");

        yield return new WaitForSeconds(0.2f);
        int attemptsDuringBackoff = gatherer.PathAttempts;
        Assert.AreEqual(attemptsBeforeWait, attemptsDuringBackoff,
            "An unreachable worker must not request another path during its retry interval.");

        // Join the two regions. The retained job should recover without another order.
        PaintRect(-8, 0, 17, 1);
        int amountBefore = node.amount;
        yield return WaitUntil(() => node.amount < amountBefore, 5f);
        Assert.Less(node.amount, amountBefore,
            "The retained gathering job should recover after connectivity returns. " +
            "PathAttempts=" + gatherer.PathAttempts + ".");
        Assert.AreSame(node, gatherer.TargetNode,
            "Retry backoff must retain the explicitly assigned resource node.");
    }

    [UnityTest]
    public IEnumerator AutoAssign_SkipsUnreachableNodeAndChoosesReachableWork()
    {
        PaintRect(0, 0, 7, 1);
        PaintRect(0, 3, 1, 1);
        var gatherer = CreateWorker("Region-aware automatic worker", new Vector3Int(0, 0, 0), true);
        gatherer.retaskSeconds = 0f;
        var reachable = CreateNode("Reachable but farther", new Vector3Int(6, 0, 0));
        CreateNode("Nearest but unreachable", new Vector3Int(0, 3, 0));

        yield return null;

        Assert.AreSame(reachable, gatherer.TargetNode,
            "Automatic assignment must ignore a nearer deposit in another connected region.");
    }

    [UnityTest]
    public IEnumerator AutoAssign_ThreeWorkers_ChooseDistinctNearestNodes()
    {
        PaintRect(-4, -4, 20, 12);
        var first = CreateWorker("Distinct worker 0", new Vector3Int(0, 0, 0), true);
        var second = CreateWorker("Distinct worker 1", new Vector3Int(0, 1, 0), true);
        var third = CreateWorker("Distinct worker 2", new Vector3Int(0, 2, 0), true);
        var near = CreateNode("Distinct node near", new Vector3Int(2, 0, 0));
        var middle = CreateNode("Distinct node middle", new Vector3Int(3, 0, 0));
        var far = CreateNode("Distinct node far", new Vector3Int(4, 0, 0));

        yield return null;
        yield return null;

        var targets = new HashSet<ResourceNode>
        {
            first.TargetNode,
            second.TargetNode,
            third.TargetNode,
        };
        Assert.AreEqual(3, targets.Count,
            "Three automatically assigned workers must not all claim the same resource node. " +
            "Targets=" + (first.TargetNode != null ? first.TargetNode.name : "null") + ", " +
            (second.TargetNode != null ? second.TargetNode.name : "null") + ", " +
            (third.TargetNode != null ? third.TargetNode.name : "null") + ".");
        Assert.IsTrue(targets.Contains(near),
            "The nearest node must still be taken by one worker.");
    }

    [UnityTest]
    public IEnumerator AiAssignGatherers_SkipsUnreachableNodeInIsolatedPocket()
    {
        // The worker and the farther node share one narrow corridor; the nearer
        // node sits on a one-cell island across an unpainted gap.
        PaintRect(0, 0, 7, 1);
        PaintRect(0, 3, 1, 1);

        var worker = CreateWorker("AI pocket worker", new Vector3Int(0, 0, 0), false);
        var actor = worker.gameObject.AddComponent<UnitActor>();
        actor.team = Team.Red;
        actor.showHealthBar = false;

        var unreachable = CreateNode("AI nearest unreachable", new Vector3Int(0, 3, 0));
        var reachable = CreateNode("AI farther reachable", new Vector3Int(6, 0, 0));
        Assert.Less(
            (unreachable.transform.position - worker.transform.position).sqrMagnitude,
            (reachable.transform.position - worker.transform.position).sqrMagnitude,
            "The control setup requires the unreachable node to be the nearer one.");

        var aiObject = new GameObject("AI gather dispatch");
        aiObject.transform.position = Grid.CellCenter(new Vector3Int(0, 0, 0));
        var ai = aiObject.AddComponent<AIController>();
        ai.team = Team.Red;
        ai.active = false;      // invoke the dispatch directly; no build/produce ticks
        ai.homeCell = new Vector2Int(0, 0);

        // AssignGatherers returns early without a living drop-off for its team.
        var dropoffObject = new GameObject("AI drop-off");
        dropoffObject.transform.position =
            Grid.CellCenter(new Vector3Int(3, -3, 0)) - new Vector3(0f, 0.5f, 0f);
        dropoffObject.AddComponent<BoxCollider2D>();
        var dropoff = dropoffObject.AddComponent<Building>();
        dropoff.footprint = Vector2Int.one;
        dropoff.depth = 1;
        dropoff.team = Team.Red;
        dropoff.isDropoff = true;
        dropoff.constructionSeconds = 0f;
        dropoff.Recompute();

        var assign = typeof(AIController).GetMethod(
            "AssignGatherers", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(assign, "AIController.AssignGatherers must remain available.");
        assign.Invoke(ai, null);

        Assert.AreSame(reachable, worker.TargetNode,
            "AI dispatch must use the same region-aware rule as automatic gathering: " +
            "the nearer node is across water and must lose to the reachable one.");
        Assert.AreNotSame(unreachable, worker.TargetNode,
            "AI dispatch must not assign a resource node in another connected region.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator OrdinaryMove_WorkerRemainsManuallyIdle_AutomaticWorkDoesNotOverride()
    {
        PaintRect(-4, -4, 16, 12);
        var gatherer = CreateWorker("Manually idle worker", new Vector3Int(0, 0, 0), true);
        gatherer.retaskSeconds = 0.1f;
        gatherer.autoStartRange = 50f;
        CreateNode("Automatic work candidate", new Vector3Int(6, 0, 0));
        var mover = gatherer.GetComponent<UnitMover>();
        Selection.selected.Add(mover);

        Selection.IssueMove(Grid.CellCenter(new Vector3Int(0, 3, 0)));
        Assert.IsTrue(mover.Moving, "The ordinary move control should issue a valid path.");

        // Start() and several retask intervals run during this wait.
        yield return new WaitForSeconds(0.5f);

        Assert.IsNull(gatherer.TargetNode,
            "Automatic assignment replaced the player's ordinary move with gathering work.");
        Assert.IsTrue(gatherer.IsManuallyIdle,
            "An ordinary move must leave the worker manually idle after automatic retask intervals.");
    }

    [UnityTest]
    public IEnumerator OrdinaryMove_PreservesCargo_ExplicitGatherDeliversBeforeNewWork()
    {
        PaintRect(-8, -4, 20, 12);
        Economy.wood = 0;
        var gatherer = CreateWorker("Cargo worker", new Vector3Int(0, 0, 0), false);
        var mover = gatherer.GetComponent<UnitMover>();
        mover.speed = 20f;
        gatherer.carryCapacity = 5;
        gatherer.dropoffRetrySeconds = 0.1f;
        var first = CreateNode("First wood job", new Vector3Int(1, 0, 0));
        first.perTrip = 5;
        first.harvestSeconds = 0.02f;

        gatherer.Assign(first);
        yield return WaitUntil(() => gatherer.Carrying == 5, 3f);
        Assert.AreEqual(5, gatherer.Carrying,
            "The control worker should harvest a full load before the manual command.");

        Selection.selected.Add(mover);
        Selection.IssueMove(Grid.CellCenter(new Vector3Int(0, 3, 0)));
        Assert.AreEqual(5, gatherer.Carrying,
            "An ordinary move must not discard carried resources.");
        Assert.IsNull(gatherer.TargetNode,
            "An ordinary move must cancel the previous gathering target.");
        Assert.IsTrue(gatherer.IsManuallyIdle,
            "A cargo-carrying worker must still become manually idle after an ordinary move.");

        var second = CreateNode("Explicit second wood job", new Vector3Int(-4, 0, 0));
        second.perTrip = 5;
        second.harvestSeconds = 1f;
        int secondAmountBeforeDelivery = second.amount;
        CreateDropoff(new Vector3Int(3, 0, 0));

        Selection.IssueMove(second.transform.position);
        Assert.AreSame(second, gatherer.TargetNode,
            "The explicit resource click should assign the new node.");
        Assert.AreEqual(5, gatherer.Carrying,
            "A new assignment must retain the old load until it is delivered.");
        Assert.AreEqual("运送中", gatherer.Status,
            "A worker with cargo must enter the drop-off phase before the new gathering phase.");

        yield return WaitUntil(() => Economy.wood >= 5, 5f);
        Assert.AreEqual(5, Economy.wood,
            "The old load should be credited exactly once at the drop-off.");
        Assert.AreEqual(0, gatherer.Carrying,
            "The carried load should be empty immediately after delivery.");
        Assert.AreEqual(secondAmountBeforeDelivery, second.amount,
            "The new resource node must not be harvested before the old cargo is delivered.");
        Assert.AreSame(second, gatherer.TargetNode,
            "After delivery, the worker should resume the explicitly assigned node.");
    }

    [UnityTest]
    public IEnumerator RestoreSaveState_NonManualCargo_DeliversAndResumesWork()
    {
        PaintRect(-8, -4, 20, 12);
        Economy.wood = 0;
        var worker = CreateWorker("Loaded restore worker", new Vector3Int(0, 0, 0), true);
        worker.GetComponent<UnitMover>().speed = 20f;
        worker.dropoffRetrySeconds = 0.1f;
        var node = CreateNode("Resume after delivery", new Vector3Int(1, 0, 0));
        CreateDropoff(new Vector3Int(3, 0, 0));

        // Let the old automatic work path run first so the restore is proven to
        // clear it instead of merely starting from a blank worker.
        yield return null;
        Assert.AreSame(node, worker.TargetNode,
            "The fixture must establish pre-restore work before checking restore cleanup.");

        worker.RestoreSaveState(new Gatherer.SaveState
        {
            carrying = 5,
            carryingKind = ResourceKind.Wood,
            manualIdle = false
        });
        Assert.IsNull(worker.TargetNode,
            "The restored load must clear the old gather target before delivering.");
        Assert.AreEqual("运送中", worker.Status,
            "A loaded, non-manual worker must leave the restore already heading for a drop-off.");

        yield return WaitUntil(() => Economy.wood >= 5, 5f);
        Assert.AreEqual(5, Economy.wood,
            "The restored load must be credited exactly once at the drop-off.");
        Assert.AreEqual(0, worker.Carrying,
            "The restored load must be empty immediately after delivery.");
        yield return WaitUntil(() => worker.TargetNode == node, 2f);
        Assert.AreSame(node, worker.TargetNode,
            "After delivering, the non-manual worker must automatically resume gathering.");
        int amountAtRestore = node.amount;
        yield return WaitUntil(() => node.amount < amountAtRestore, 3f);
        Assert.Less(node.amount, amountAtRestore,
            "Resuming work must harvest below the amount present after restoration.");
    }

    [UnityTest]
    public IEnumerator RestoreSaveState_ManualIdleWithCargo_StaysIdleAndKeepsCargo()
    {
        PaintRect(-8, -4, 20, 12);
        Economy.wood = 0;
        var worker = CreateWorker("Manual loaded restore worker", new Vector3Int(0, 0, 0), true);
        var mover = worker.GetComponent<UnitMover>();
        mover.speed = 20f;
        worker.dropoffRetrySeconds = 0.1f;
        Vector3 start = worker.transform.position;
        // A reachable drop-off makes a wrong branch order visible: if carrying>0
        // were handled before manualIdle, the worker would deliver instead of idling.
        CreateDropoff(new Vector3Int(3, 0, 0));

        worker.RestoreSaveState(new Gatherer.SaveState
        {
            carrying = 5,
            carryingKind = ResourceKind.Wood,
            manualIdle = true
        });

        Assert.AreEqual("待命(持5)", worker.Status,
            "Manual idle must win over the restored cargo even when a drop-off exists.");
        Assert.IsNull(worker.TargetNode,
            "Manual idle must not assign gathering work after restore.");

        yield return new WaitForSeconds(0.25f);

        Assert.AreEqual("待命(持5)", worker.Status,
            "Manual idle must remain in place with its cargo while a drop-off is available.");
        Assert.IsTrue(worker.IsManuallyIdle, "The restored manual-idle choice must be kept.");
        Assert.AreEqual(5, worker.Carrying, "A manually idle worker must keep its restored cargo.");
        Assert.AreEqual(0, Economy.wood, "Manual idle must not credit the restored cargo.");
        Assert.IsFalse(mover.Moving, "Manual idle must not move the worker.");
        Assert.AreEqual(start, worker.transform.position,
            "Manual idle must not move the worker toward a drop-off.");
    }

    [UnityTest]
    public IEnumerator RestoreSaveState_NonManualCargoWithoutDropoff_WaitsWithoutCredit()
    {
        PaintRect(-8, -4, 20, 12);
        Economy.wood = 0;
        var worker = CreateWorker("Loaded worker without drop-off", new Vector3Int(0, 0, 0), false);
        var mover = worker.GetComponent<UnitMover>();
        mover.speed = 20f;
        worker.dropoffRetrySeconds = 0.1f;

        worker.RestoreSaveState(new Gatherer.SaveState
        {
            carrying = 5,
            carryingKind = ResourceKind.Wood,
            manualIdle = false
        });

        yield return new WaitForSeconds(0.25f);

        Assert.AreEqual("运送中", worker.Status,
            "Without a drop-off the worker must remain in the delivery phase, still loaded.");
        Assert.AreEqual(5, worker.Carrying, "The restored cargo must not vanish while waiting.");
        Assert.AreEqual(0, Economy.wood,
            "Waiting for a missing drop-off must never credit resources out of thin air.");
        Assert.IsFalse(mover.Moving, "No drop-off exists, so the worker must not move.");
    }

    [UnityTest]
    public IEnumerator OrdinaryMove_MonkHealsOnlyAlliesAlreadyInsideHealRange()
    {
        PaintRect(-4, -4, 20, 12);
        var monk = CreateActor("Manual monk", new Vector3Int(0, 0, 0));
        monk.isCombatant = false;
        var healer = monk.gameObject.AddComponent<Healer>();
        healer.healRange = 3f;
        healer.searchRange = 20f;
        // Small enough that 0.25s of healing cannot top the ally up: a fully healed
        // patient is correctly dropped, which would race the Patient assertion below.
        healer.healAmount = 1;
        healer.healCooldown = 0.1f;
        healer.retargetInterval = 0.1f;

        var near = CreateActor("Near wounded ally", new Vector3Int(2, 0, 0));
        near.maxHealth = 100;
        near.health = 80;
        var far = CreateActor("Far critically wounded ally", new Vector3Int(10, 0, 0));
        far.maxHealth = 100;
        far.health = 10;

        Selection.selected.Add(monk.GetComponent<UnitMover>());
        Vector3 orderedPosition = monk.transform.position;
        Selection.IssueMove(orderedPosition);
        yield return new WaitForSeconds(0.25f);

        Assert.AreSame(near, healer.Patient,
            "After a manual move, the monk must ignore the more badly hurt ally outside healRange.");
        Assert.Greater(near.health, 80, "The ally already inside healRange should be healed.");
        Assert.AreEqual(10, far.health,
            "The monk must not chase and heal an ally outside manual healRange.");
        Assert.Less(Vector2.Distance(monk.transform.position, orderedPosition), 0.2f,
            "The manual monk should remain at the ordered position instead of chasing.");
        Assert.IsNotNull(typeof(Healer).GetMethod(
            "OrderMove", BindingFlags.Instance | BindingFlags.Public),
            "Healer.OrderMove must be the public manual-command entry point.");
    }

    [UnityTest]
    public IEnumerator DeadSelectedUnit_IsRemovedBeforeDelayedCorpseDestruction()
    {
        PaintRect(-4, -4, 8, 8);
        var actor = CreateActor("Selected doomed unit", new Vector3Int(0, 0, 0));
        actor.corpseSeconds = 5f;
        actor.maxHealth = 20;
        actor.health = 20;
        var mover = actor.GetComponent<UnitMover>();
        Selection.selected.Add(mover);
        yield return null;

        actor.TakeDamage(1000);
        Assert.IsFalse(actor.IsAlive);
        Assert.IsNotNull(actor.gameObject,
            "The corpse delay should keep the dead actor alive as a Unity object for this regression.");

        yield return null;

        Assert.IsFalse(Selection.selected.Contains(mover),
            "A dead but not yet destroyed unit must be pruned from the selection on the next frame.");
    }

    [UnityTest]
    public IEnumerator Hud_DeadSelectedUnit_IsExcludedBeforeDelayedCorpseDestruction()
    {
        PaintRect(-4, -4, 8, 8);
        var actor = CreateActor("HUD doomed unit", new Vector3Int(0, 0, 0));
        actor.corpseSeconds = 5f;
        actor.maxHealth = 20;
        actor.health = 20;
        var mover = actor.GetComponent<UnitMover>();
        Selection.selected.Add(mover);

        var panel = new GameObject("Test selection panel");
        var title = CreateText("Test selection title");
        var body = CreateText("Test selection body");
        var hudObject = new GameObject("Test HUD");
        var hud = hudObject.AddComponent<HUD>();
        hud.selectionPanel = panel;
        hud.selectionTitle = title;
        hud.selectionBody = body;
        yield return null;
        Assert.IsTrue(panel.activeSelf, "The living-unit control should show the selection panel.");

        // Isolate HUD filtering from SelectionManager's own next-frame pruning.
        Selection.enabled = false;
        actor.TakeDamage(1000);
        yield return null;

        Assert.IsTrue(Selection.selected.Contains(mover),
            "The HUD test arrangement should retain the dead, not-yet-destroyed selection entry.");
        Assert.IsFalse(panel.activeSelf,
            "The HUD must filter dead actors even if a dead entry is still present in the selection list.");
    }

    [UnityTest]
    public IEnumerator OrdinaryMove_MonkFinishesDistantMoveBeforeHealingInRangeAllies()
    {
        PaintRect(-4, -4, 20, 12);
        var monk = CreateActor("Distant manual monk", new Vector3Int(0, 0, 0));
        monk.isCombatant = false;
        var healer = monk.gameObject.AddComponent<Healer>();
        healer.healRange = 2.5f;
        healer.searchRange = 20f;
        healer.healAmount = 1;
        healer.healCooldown = 0.05f;
        healer.retargetInterval = 0.05f;
        var mover = monk.GetComponent<UnitMover>();
        mover.speed = 12f;

        var onRoute = CreateActor("On-route wounded ally", new Vector3Int(1, 0, 0));
        onRoute.maxHealth = 100;
        onRoute.health = 80;
        var atDestination = CreateActor("Wounded ally at destination", new Vector3Int(9, 0, 0));
        atDestination.maxHealth = 100;
        atDestination.health = 50;

        Selection.selected.Add(mover);
        Vector3 destination = Grid.CellCenter(new Vector3Int(10, 0, 0));
        Selection.IssueMove(destination);
        Assert.IsTrue(mover.Moving, "The distant order should issue a moving path.");

        // Long enough to walk past the on-route ally but far short of the goal.
        yield return new WaitForSeconds(0.35f);
        Assert.IsTrue(mover.Moving,
            "An in-range patient must not stop an ordered move mid-route.");
        Assert.AreEqual(80, onRoute.health,
            "A manual monk must not heal while an ordered move is still in progress.");

        yield return WaitUntil(
            () => !mover.Moving && Vector2.Distance(mover.transform.position, destination) < 0.3f,
            5f);
        Assert.IsFalse(mover.Moving, "The monk must finish the ordered move.");
        Assert.Less(Vector2.Distance(mover.transform.position, destination), 0.3f,
            "The monk must actually reach the ordered destination, not stop en route.");

        yield return WaitUntil(() => atDestination.health > 50, 2f);
        Assert.Greater(atDestination.health, 50,
            "Once the move ends, an ally inside healRange should be healed.");
        Assert.AreEqual(80, onRoute.health,
            "The on-route ally is outside healRange at the destination and must stay unhealed.");
        Assert.AreSame(atDestination, healer.Patient,
            "Only allies already inside healRange at the destination may be selected as patients.");
    }

    [UnityTest]
    public IEnumerator AssignClear_WithCargo_DeliversOnceBeforeClearing()
    {
        PaintRect(-8, -4, 20, 12);
        Economy.wood = 0;
        var gatherer = CreateWorker("Cargo clear worker", new Vector3Int(0, 0, 0), false);
        var mover = gatherer.GetComponent<UnitMover>();
        mover.speed = 20f;
        gatherer.carryCapacity = 5;
        gatherer.dropoffRetrySeconds = 0.1f;

        var node = CreateNode("Clear cargo source", new Vector3Int(1, 0, 0));
        node.perTrip = 5;
        node.harvestSeconds = 0.02f;
        gatherer.Assign(node);
        yield return WaitUntil(() => gatherer.Carrying == 5, 3f);
        Assert.AreEqual(5, gatherer.Carrying,
            "The control worker should harvest a full load before the clear order.");

        var bush = CreateBush("Clear target", new Vector3Int(0, 3, 0));
        bush.clearSeconds = 0.05f;
        CreateDropoff(new Vector3Int(3, 0, 0));

        gatherer.AssignClear(bush);
        Assert.AreEqual(5, gatherer.Carrying,
            "AssignClear must retain the carried load until it is delivered.");
        Assert.IsNull(gatherer.TargetNode,
            "AssignClear must cancel the previous gathering target.");
        Assert.AreEqual("运送中", gatherer.Status,
            "A cargo-carrying worker must deliver before it starts clearing.");

        yield return WaitUntil(() => Economy.wood >= 5, 5f);
        Assert.AreEqual(5, Economy.wood,
            "The old load should be credited exactly once at the drop-off.");
        Assert.AreEqual(0, gatherer.Carrying,
            "The carried load should be empty immediately after delivery.");
        Assert.IsTrue(gatherer.Status == "去清草" || gatherer.Status == "清草中",
            "After delivery the worker must resume the clear order. Status=" + gatherer.Status + ".");

        yield return WaitUntil(() => bush == null, 5f);
        Assert.IsTrue(bush == null, "The assigned bush should eventually be cleared.");
    }

    [UnityTest]
    public IEnumerator DeadSelection_DirectIssueMove_SkipsDeadEntryButMovesLivingUnit()
    {
        PaintRect(-4, -4, 8, 8);
        var dead = CreateActor("Dead order target", new Vector3Int(0, 0, 0));
        dead.corpseSeconds = 5f;
        dead.maxHealth = 20;
        dead.health = 20;
        var deadMover = dead.GetComponent<UnitMover>();
        dead.TakeDamage(1000);
        Assert.IsFalse(dead.IsAlive);

        var live = CreateActor("Living order target", new Vector3Int(1, 0, 0));
        var liveMover = live.GetComponent<UnitMover>();

        Selection.selected.Add(deadMover);
        Selection.selected.Add(liveMover);
        // Bypass Update pruning: this is the direct IssueMove call the review asked about.
        Selection.enabled = false;
        Assert.IsTrue(Selection.selected.Contains(deadMover),
            "The arrangement must retain the dead entry until IssueMove runs.");

        Selection.IssueMove(Grid.CellCenter(new Vector3Int(0, 3, 0)));

        Assert.IsFalse(deadMover.Moving,
            "IssueMove must not give a corpse a new path, even when called directly.");
        Assert.IsTrue(liveMover.Moving,
            "The living selected unit must still receive the order.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator MixedSelection_EnemyOnResourceOutsideAggro_WorkerGathersAndSoldierAttacks()
    {
        PaintRect(-5, -5, 22, 12);
        var worker = CreateWorker("Mixed gather worker", new Vector3Int(0, 0, 0), autoStart: false);
        var soldier = CreateActor("Mixed attack soldier", new Vector3Int(0, 1, 0));
        soldier.aggroRange = 7f;
        soldier.isCombatant = true;
        var combat = soldier.gameObject.AddComponent<CombatAI>();
        var node = CreateNode("Contested resource", new Vector3Int(10, 0, 0));
        var enemy = CreateActor("Enemy on contested resource", new Vector3Int(10, 0, 0));
        enemy.team = Team.Red;
        enemy.gameObject.AddComponent<BoxCollider2D>().size = Vector2.one;
        Physics2D.SyncTransforms();
        Selection.selected.Clear();
        Selection.selected.Add(worker.GetComponent<UnitMover>());
        Selection.selected.Add(soldier.GetComponent<UnitMover>());

        Selection.IssueMove(node.transform.position);

        Assert.AreSame(node, worker.TargetNode, "The worker must accept the gathering order.");
        Assert.AreSame(enemy, combat.Target,
            "The same click must also give the soldier an explicit attack, even outside automatic aggro.");
        yield return null;
    }

    UnitMover CreateMover(string name, Vector3Int cell)
    {
        var go = new GameObject(name);
        go.transform.position = Grid.CellCenter(cell);
        return go.AddComponent<UnitMover>();
    }

    Gatherer CreateWorker(string name, Vector3Int cell, bool autoStart)
    {
        var mover = CreateMover(name, cell);
        var gatherer = mover.gameObject.AddComponent<Gatherer>();
        gatherer.autoStart = autoStart;
        gatherer.autoStartRange = 50f;
        gatherer.retaskSeconds = 0.1f;
        return gatherer;
    }

    UnitActor CreateActor(string name, Vector3Int cell)
    {
        var mover = CreateMover(name, cell);
        var actor = mover.gameObject.AddComponent<UnitActor>();
        actor.team = Team.Blue;
        actor.showHealthBar = false;
        actor.maxHealth = 100;
        actor.health = 100;
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

    static Building CreateDropoff(Vector3Int cell)
    {
        var go = new GameObject("Test drop-off");
        go.transform.position = MapGrid.Instance.CellCenter(cell) - new Vector3(0f, 0.5f, 0f);
        go.AddComponent<BoxCollider2D>();
        var building = go.AddComponent<Building>();
        building.footprint = Vector2Int.one;
        building.depth = 1;
        building.team = Team.Blue;
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
