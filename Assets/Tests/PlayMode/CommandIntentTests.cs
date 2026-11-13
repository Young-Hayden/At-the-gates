using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>Production-path regressions for temporary CombatAI command intent.</summary>
public class CommandIntentTests : RegressionFixture
{
    const string ArcherPath = "Assets/Prefabs/Units/Archer_Blue.prefab";

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
        bool arrivalObserved = false;
        float arrivalDistance = float.MaxValue;
        int enemyHealthAtArrival = -1;
        mover.SetPath(path, () =>
        {
            arrivalDistance = Vector2.Distance(fighter.transform.position, Grid.CellCenter(destination));
            enemyHealthAtArrival = enemy.health;
            arrivalObserved = true;
            ai.CompleteOrder();
        });
        yield return WaitUntil(() => arrivalObserved, 4f);

        Assert.IsTrue(arrivalObserved, "The plain-move path must report a real arrival.");
        Assert.Less(arrivalDistance, 0.2f,
            "A plain move must reach its destination rather than merely stop en route.");
        Assert.AreEqual(100, enemyHealthAtArrival,
            "Plain move must not stop to attack the adjacent enemy before arrival.");
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
        Assert.IsTrue(enemy == null || !enemy.IsAlive,
            "AttackMove must kill the route enemy before resuming.");
        yield return WaitUntil(() => !mover.Moving && Vector2.Distance(
            fighter.transform.position, Grid.CellCenter(destination)) < 0.25f, 5f);

        Assert.IsFalse(mover.Moving);
        Assert.Less(Vector2.Distance(fighter.transform.position, Grid.CellCenter(destination)), 0.25f,
            "After combat, the unit must resume and finish the original attack-move destination.");
    }

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
        Assert.IsTrue(enemy == null || !enemy.IsAlive,
            "An armed attack click on open ground must engage the route enemy.");
        yield return WaitUntil(() => !fighter.GetComponent<UnitMover>().Moving && Vector2.Distance(
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

    [Test]
    public void RegionVersion_IncrementsWhenBuildingFootprintChangesConnectivity()
    {
        PaintRect(-4, -4, 8, 8);
        int before = Grid.RegionVersion;
        var cell = new Vector3Int(0, 0, 0);
        Grid.AddBuildingFootprint(new[] { cell });
        Assert.Greater(Grid.RegionVersion, before,
            "Adding a dynamic blocker must bump the connected-region version.");
        int afterAdd = Grid.RegionVersion;
        Grid.RemoveBuildingFootprint(new[] { cell });
        Assert.Greater(Grid.RegionVersion, afterAdd,
            "Removing the last blocker must bump the connected-region version again.");
    }

    [UnityTest]
    public IEnumerator AutoTarget_UnreachableNearest_SelectsReachableEnemy()
    {
        PaintRect(-2, -2, 5, 10);
        PaintRect(6, 0, 1, 1);
        var fighter = CreateFighter("Region-aware fighter", new Vector3Int(0, 0, 0));
        var unreachable = CreateEnemy("Isolated nearest enemy", new Vector3(6f, 0f, 0f), 100);
        var reachable = CreateEnemy("Reachable alternate", new Vector3(0f, 6.5f, 0f), 100);
        var ai = fighter.GetComponent<CombatAI>();
        Assert.Greater(Vector2.Distance(fighter.transform.position, unreachable.transform.position), fighter.attackRange);
        Assert.Less(Vector2.Distance(fighter.transform.position, unreachable.transform.position), fighter.aggroRange);
        Assert.AreNotEqual(Grid.Region(Grid.WorldToCell(fighter.transform.position)),
            Grid.Region(Grid.WorldToCell(unreachable.transform.position)), "The nearest enemy must be isolated.");

        yield return WaitUntil(() => ReferenceEquals(ai.Target, reachable), 2f);
        Assert.AreSame(reachable, ai.Target);
    }

    [UnityTest]
    public IEnumerator AutoTarget_ReconnectedNearest_RetriesWithinFailureWindow()
    {
        PaintRect(-2, -2, 5, 25);
        PaintRect(6, 0, 1, 1);
        var fighter = CreateFighter("Reconnecting fighter", new Vector3Int(0, 0, 0));
        var unreachable = CreateEnemy("Temporarily isolated nearest enemy", new Vector3(6f, 0f, 0f), 100);
        var reachable = CreateEnemy("Temporary reachable alternate", new Vector3(0f, 6.5f, 0f), 100);
        var ai = fighter.GetComponent<CombatAI>();
        var mover = fighter.GetComponent<UnitMover>();
        ai.retargetInterval = 0.1f;
        ai.autoTargetRetrySeconds = 30f;
        yield return WaitUntil(() => ReferenceEquals(ai.Target, reachable), 2f);
        Assert.AreSame(reachable, ai.Target, "The control must first reject the isolated target.");

        // Keep both enemies alive, enabled and on walkable cells. Moving the alternate
        // out of aggro must not clear or overwrite the original unreachable record.
        reachable.transform.position = new Vector3(0f, 20f, 0f);
        yield return WaitUntil(() => ai.Target == null, 1f);
        Assert.IsNull(ai.Target, "Moving the alternate out of aggro must make the fighter retarget.");
        yield return new WaitForSeconds(0.3f);
        Assert.IsNull(ai.Target,
            "The unchanged target must remain rejected inside the retry window before reconnection.");
        Assert.IsTrue(reachable.gameObject.activeSelf && reachable.IsAlive);
        int versionBeforeReconnect = Grid.RegionVersion;
        float distanceBeforeReconnect = Vector2.Distance(fighter.transform.position, unreachable.transform.position);
        float reconnectTime = Time.time;
        PaintRect(3, 0, 4, 1);

        yield return WaitUntil(() => ReferenceEquals(ai.Target, unreachable) &&
            Vector2.Distance(fighter.transform.position, unreachable.transform.position) < distanceBeforeReconnect - 0.25f, 2f);

        Assert.Less(Time.time - reconnectTime, 1f,
            "The reconnect must wake the original target promptly, not after the long retry window.");
        Assert.Greater(Grid.RegionVersion, versionBeforeReconnect,
            "Reconnecting the map must bump the connected-region version.");
        Assert.AreSame(unreachable, ai.Target,
            "Restored connectivity must permit the unchanged target and goal inside the retry window.");
        Assert.Less(Vector2.Distance(fighter.transform.position, unreachable.transform.position),
            distanceBeforeReconnect - 0.25f,
            "The reacquired target must produce an effective pursuit, not a one-frame Target assignment.");
        Assert.IsTrue(mover.Moving, "The fighter must be following a valid path toward the reconnected target.");
    }

    [UnityTest]
    public IEnumerator AutoTarget_UnreachableNearest_RetriesAfterWindowAndClearsAgain()
    {
        PaintRect(-2, -2, 5, 25);
        PaintRect(6, 0, 1, 1);
        var fighter = CreateFighter("Expiry retry fighter", new Vector3Int(0, 0, 0));
        var unreachable = CreateEnemy("Still isolated nearest enemy", new Vector3(6f, 0f, 0f), 100);
        var reachable = CreateEnemy("Distant but living alternate", new Vector3(0f, 6.5f, 0f), 100);
        var ai = fighter.GetComponent<CombatAI>();
        ai.retargetInterval = 0.1f;
        ai.autoTargetRetrySeconds = 0.3f;
        ai.repathInterval = 1.25f; // leaves the retried target observable before its next failed Chase
        yield return WaitUntil(() => ReferenceEquals(ai.Target, reachable), 2f);
        Assert.AreSame(reachable, ai.Target, "The control must first reject the isolated target.");

        reachable.transform.position = new Vector3(0f, 20f, 0f);
        yield return new WaitForSeconds(0.1f);
        Assert.IsNull(ai.Target,
            "Before the retry window expires, the unchanged target must stay rejected.");
        yield return WaitUntil(() => ReferenceEquals(ai.Target, unreachable), 2f);
        Assert.AreSame(unreachable, ai.Target,
            "Without reconnection, the unchanged target must become eligible when the retry window expires.");

        yield return WaitUntil(() => ai.Target == null, 2f);
        Assert.IsNull(ai.Target, "The next failed Chase must clear the target again rather than locking onto it.");

        yield return WaitUntil(() => ReferenceEquals(ai.Target, unreachable), 2f);
        Assert.AreSame(unreachable, ai.Target,
            "A renewed failure window must expire and retry again; the target must not be permanently blacklisted.");
    }

    [UnityTest]
    public IEnumerator ForcedAttack_UnreachableTarget_IsNotReplaced()
    {
        PaintRect(-2, -2, 5, 10);
        PaintRect(6, 0, 1, 1);
        var fighter = CreateFighter("Forced attacker", new Vector3Int(0, 0, 0));
        var unreachable = CreateEnemy("Forced isolated enemy", new Vector3(6f, 0f, 0f), 100);
        var reachable = CreateEnemy("Ignored reachable alternate", new Vector3(0f, 6.5f, 0f), 100);
        var ai = fighter.GetComponent<CombatAI>();

        ai.OrderAttack(unreachable);
        yield return new WaitForSeconds(1.2f);

        Assert.AreSame(unreachable, ai.Target,
            "An explicit unreachable attack must retain its target and back off instead of auto-selecting another enemy.");
        Assert.AreEqual(100, reachable.health);
    }

    [UnityTest]
    public IEnumerator AttackMove_UnreachableOnly_ResumesOriginalDestination()
    {
        PaintRect(-2, -2, 5, 10);
        PaintRect(6, 0, 1, 1);
        var fighter = CreateFighter("Unreachable attack mover", new Vector3Int(0, 0, 0));
        CreateEnemy("Only isolated enemy", new Vector3(6f, 0f, 0f), 100);
        var ai = fighter.GetComponent<CombatAI>();
        var mover = fighter.GetComponent<UnitMover>();
        var destination = new Vector3Int(1, 0, 0);

        ai.OrderAttackMove(destination);
        yield return WaitUntil(() => !mover.Moving && Vector2.Distance(
            fighter.transform.position, Grid.CellCenter(destination)) < 0.2f, 4f);

        Assert.Less(Vector2.Distance(fighter.transform.position, Grid.CellCenter(destination)), 0.2f,
            "With no reachable alternate, AttackMove must resume its original destination after the failed chase.");
        Assert.IsFalse(mover.Moving);
    }

    [UnityTest]
    public IEnumerator ForcedAttack_DuringMoveAndAttackMove_DropsStaleArrivalCallback()
    {
        // Long lanes keep both units moving when the explicit attack order arrives. The
        // island enemy sits more than the archer's 4.5 attack range beyond the main
        // region, so a forced order cannot satisfy itself with a new path.
        PaintRect(-6, -3, 25, 6);          // x -6..18
        PaintRect(26, -1, 1, 1);           // isolated island at x 26
        var forced = CreateEnemy("Isolated forced target",
            Grid.CellCenter(new Vector3Int(26, -1, 0)), 100);

        var moverUnit = CreateFighter("Move interrupted by attack", new Vector3Int(0, -1, 0));
        var moverAi = moverUnit.GetComponent<CombatAI>();
        var mover = moverUnit.GetComponent<UnitMover>();
        mover.speed = 3f;
        var moveDestination = new Vector3Int(8, -1, 0);

        var attackUnit = CreateFighter("AttackMove interrupted by attack", new Vector3Int(0, 1, 0));
        var attackAi = attackUnit.GetComponent<CombatAI>();
        var attackMover = attackUnit.GetComponent<UnitMover>();
        attackMover.speed = 3f;
        var attackDestination = new Vector3Int(8, 1, 0);

        var movePath = Grid.FindPath(Grid.WorldToCell(moverUnit.transform.position), moveDestination);
        var attackPath = Grid.FindPath(Grid.WorldToCell(attackUnit.transform.position), attackDestination);
        Assert.IsNotNull(movePath, "The control plain-move path must exist.");
        Assert.IsNotNull(attackPath, "The control attack-move path must exist.");

        moverAi.OrderMove(moveDestination);
        mover.SetPath(movePath, moverAi.CompleteOrder);
        attackAi.OrderAttackMove(attackDestination);
        attackMover.SetPath(attackPath, attackAi.CompleteOrder);
        yield return null;
        Assert.IsTrue(mover.Moving, "The plain mover must still be travelling when attacked.");
        Assert.IsTrue(attackMover.Moving, "The attack mover must still be travelling when attacked.");

        moverAi.OrderAttack(forced);
        attackAi.OrderAttack(forced);

        // The old paths needed about 2.7s at speed 3. If their arrival callbacks survive,
        // CompleteOrder clears both forced targets before this wait expires.
        yield return new WaitForSeconds(3.4f);

        Assert.AreSame(forced, moverAi.Target,
            "An explicit attack must not be cleared by a stale plain-move arrival callback.");
        Assert.AreSame(forced, attackAi.Target,
            "An explicit attack must not be cleared by a stale attack-move arrival callback.");
        Assert.Greater(Vector2.Distance(moverUnit.transform.position, Grid.CellCenter(moveDestination)), 0.5f,
            "The cancelled plain-move path must not keep travelling toward its old destination.");
        Assert.Greater(Vector2.Distance(attackUnit.transform.position, Grid.CellCenter(attackDestination)), 0.5f,
            "The cancelled attack-move path must not keep travelling toward its old destination.");
        Assert.IsFalse(mover.Moving, "Ordering an attack must stop the stale plain-move path.");
        Assert.IsFalse(attackMover.Moving, "Ordering an attack must stop the stale attack-move path.");
    }

    [UnityTest]
    public IEnumerator ForcedAttack_DuringGarrisonApproach_DoesNotGarrisonOnArrival()
    {
        PaintRect(-10, -6, 19, 12);        // x -10..8
        PaintRect(15, 0, 1, 1);            // isolated island at x 15
        var tower = SpawnTower(new Vector3Int(5, 3, 0));
        var archer = CreateFighter("Garrison interrupted by attack", new Vector3Int(-5, 3, 0));
        var ai = archer.GetComponent<CombatAI>();
        var mover = archer.GetComponent<UnitMover>();
        mover.speed = 3f;
        var garrison = tower.GetComponent<TowerGarrison>();
        var forced = CreateEnemy("Isolated garrison target",
            Grid.CellCenter(new Vector3Int(15, 0, 0)), 100);

        Selection.selected.Clear();
        Selection.selected.Add(mover);
        Selection.IssueMove(tower.transform.position + Vector3.up * 0.5f);
        yield return null;
        Assert.IsTrue(mover.Moving, "The garrison order must give the archer a real approach path.");
        Assert.IsFalse(garrison.HasArcher, "The archer must still be outside the tower when attacked.");

        ai.OrderAttack(forced);

        // The garrison approach needed about 3.3s at speed 3. A surviving arrival
        // callback would run tower.Garrison() and pin the archer after this wait.
        yield return new WaitForSeconds(4.2f);

        Assert.AreSame(forced, ai.Target,
            "An explicit attack must not be cleared by the cancelled garrison approach.");
        Assert.IsFalse(garrison.HasArcher,
            "A cancelled garrison approach must not garrison the archer later on arrival.");
        Assert.IsFalse(mover.Moving, "The cancelled garrison path must not keep travelling.");
    }

    [UnityTest]
    public IEnumerator PlainMove_PathAbortedByBuilding_AutoCombatResumes()
    {
        PaintRect(-4, -4, 16, 12);
        var fighter = CreateFighter("Aborted plain mover", new Vector3Int(0, 0, 0));
        var ai = fighter.GetComponent<CombatAI>();
        var mover = fighter.GetComponent<UnitMover>();
        var destination = new Vector3Int(5, 0, 0);
        var path = Grid.FindPath(Grid.WorldToCell(fighter.transform.position), destination);
        Assert.IsNotNull(path);

        ai.OrderMove(destination);
        mover.SetPath(path, ai.CompleteOrder);
        Assert.IsTrue(mover.Moving, "The control requires an accepted path before blocking it.");
        Grid.AddBuildingFootprint(new[] { new Vector3Int(1, 0, 0) });
        yield return WaitUntil(() => !mover.Moving, 2f);

        Assert.IsFalse(mover.Moving, "The new building footprint must abort the stale path.");
        Assert.Greater(Vector2.Distance(fighter.transform.position, Grid.CellCenter(destination)), 0.2f,
            "The control must abort before the destination, not report a false arrival.");

        var enemy = CreateEnemy("Enemy after aborted path", fighter.transform.position + Vector3.right * 0.5f, 100);
        yield return WaitUntil(() => enemy == null || enemy.health < 100, 3f);

        Assert.IsNotNull(enemy);
        Assert.Less(enemy.health, 100,
            "An aborted move without an arrival callback must restore automatic combat.");
        Assert.AreSame(enemy, ai.Target);
    }

    [UnityTest]
    public IEnumerator AiWave_EnemyKilled_ResumesWaveDestination()
    {
        PaintRect(-4, -5, 26, 12);
        var fighter = CreateFighter("AI wave attacker", new Vector3Int(0, 0, 0));
        fighter.team = Team.Red;
        var enemy = CreateEnemy("Blue route defender", Grid.CellCenter(new Vector3Int(4, 0, 0)), 5);
        enemy.team = Team.Blue;
        var goal = new Vector3Int(10, 0, 0);
        var start = Grid.ResolveWalkableStart(Grid.WorldToCell(fighter.transform.position)).Value;
        var destination = Grid.NearestWalkableTo(start, goal, 5, Grid.Region(start));
        Assert.IsNotNull(destination, "The test must assert the actual cell SendTo dispatches.");
        var send = typeof(AIController).GetMethod("SendTo",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(send, "AIController.SendTo must remain the wave dispatch entry point.");

        send.Invoke(null, new object[] { fighter, goal });
        yield return WaitUntil(() => enemy == null || !enemy.IsAlive, 5f);
        Assert.IsTrue(enemy == null || !enemy.IsAlive, "The route defender must die before resuming.");

        yield return WaitUntil(() => Vector2.Distance(fighter.transform.position, Grid.CellCenter(destination.Value)) < 0.25f, 6f);
        Assert.Less(Vector2.Distance(fighter.transform.position, Grid.CellCenter(destination.Value)), 0.25f,
            "An AI wave must resume its dispatched destination after killing a route enemy.");
    }

    [UnityTest]
    public IEnumerator AiWave_PathBlocked_RepathsAround()
    {
        PaintRect(-4, -5, 26, 12);
        var fighter = CreateFighter("AI wave blocked attacker", new Vector3Int(0, 0, 0));
        fighter.team = Team.Red;
        var mover = fighter.GetComponent<UnitMover>();
        var goal = new Vector3Int(10, 0, 0);
        var start = Grid.ResolveWalkableStart(Grid.WorldToCell(fighter.transform.position)).Value;
        var destination = Grid.NearestWalkableTo(start, goal, 5, Grid.Region(start));
        Assert.IsNotNull(destination, "The test must assert the actual cell SendTo dispatches.");
        var send = typeof(AIController).GetMethod("SendTo",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(send);

        send.Invoke(null, new object[] { fighter, goal });
        yield return WaitUntil(() => mover.Moving && fighter.transform.position.x > 1.5f, 3f);
        Assert.IsTrue(mover.Moving, "The wave must be travelling before the route is blocked.");
        Grid.AddBuildingFootprint(new[] { new Vector3Int(4, 0, 0) });

        yield return WaitUntil(() => Vector2.Distance(fighter.transform.position, Grid.CellCenter(destination.Value)) < 0.25f, 6f);
        Assert.Less(Vector2.Distance(fighter.transform.position, Grid.CellCenter(destination.Value)), 0.25f,
            "An AI wave must find another route to its dispatched destination when a building blocks the old path.");
    }
}
