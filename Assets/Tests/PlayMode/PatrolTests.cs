using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Patrol (Shift+right-click). The fighter walks between where it stood when the order was
/// given and the clicked cell, turning around at each end and fighting on the way; any other
/// move or attack order cancels the route, and workers keep their economic orders.
/// </summary>
public class PatrolTests : RegressionFixture
{
    const string WarriorPath = "Assets/Prefabs/Units/Warrior_Blue.prefab";
    const string PawnPath = "Assets/Prefabs/Units/Pawn_Blue.prefab";

    static readonly Vector3Int A = new Vector3Int(-6, 0, 0);
    static readonly Vector3Int B = new Vector3Int(-2, 0, 0);

    [UnityTest]
    public IEnumerator OrderPatrol_WalksBetweenBothEnds()
    {
        PaintRect(-10, -6, 20, 12);
        var ai = CreateFighter(WarriorPath, A);
        yield return null;

        ai.OrderPatrol(B);
        Assert.IsTrue(ai.IsPatrolling, "A patrol order must start a patrol.");
        Assert.AreEqual(B, ai.PatrolTarget, "The unit must walk toward the clicked cell first.");

        // Simulate arriving at B, then call the turn-around the mover would have called.
        ai.transform.position = Grid.CellCenter(B);
        TurnAround(ai);
        Assert.IsTrue(ai.IsPatrolling, "Reaching one end must continue the patrol, not end it.");
        Assert.AreEqual(A, ai.PatrolTarget, "It must now walk back to where it started.");

        ai.transform.position = Grid.CellCenter(A);
        TurnAround(ai);
        Assert.AreEqual(B, ai.PatrolTarget, "And then head out again.");
    }

    [UnityTest]
    public IEnumerator OrderMove_CancelsThePatrol()
    {
        PaintRect(-10, -6, 20, 12);
        var ai = CreateFighter(WarriorPath, A);
        yield return null;

        ai.OrderPatrol(B);
        Assert.IsTrue(ai.IsPatrolling);

        ai.OrderMove(new Vector3Int(4, 0, 0));
        Assert.IsFalse(ai.IsPatrolling, "An ordinary move order must end the patrol.");
    }

    [UnityTest]
    public IEnumerator WorkersIgnorePatrolOrders()
    {
        PaintRect(-10, -6, 20, 12);
        var pawn = CreateFighter(PawnPath, A);
        yield return null;

        pawn.OrderPatrol(B);
        Assert.IsFalse(pawn.IsPatrolling,
            "A worker keeps its economic orders; patrol is a combat order, like Hold.");
    }

    [UnityTest]
    public IEnumerator PatrolOntoItsOwnCell_IsIgnored()
    {
        PaintRect(-10, -6, 20, 12);
        var ai = CreateFighter(WarriorPath, A);
        yield return null;

        ai.OrderPatrol(A);
        Assert.IsFalse(ai.IsPatrolling,
            "Patrolling to the cell it already stands on would ping-pong in place.");
    }

    [UnityTest]
    public IEnumerator IssuePatrol_CommandsOnlyCombatants()
    {
        PaintRect(-10, -6, 20, 12);
        var warrior = CreateFighter(WarriorPath, new Vector3Int(-6, 2, 0));
        var pawn = CreateFighter(PawnPath, new Vector3Int(-6, -2, 0));
        pawn.GetComponent<UnitActor>().isCombatant = false;    // the Gatherer does this at runtime
        yield return null;

        var sm = new GameObject("Selection probe").AddComponent<SelectionManager>();
        sm.enabled = false;
        sm.selected.Add(warrior.GetComponent<UnitMover>());
        sm.selected.Add(pawn.GetComponent<UnitMover>());

        sm.IssuePatrol(Grid.CellCenter(new Vector3Int(2, 2, 0)));

        Assert.IsTrue(warrior.IsPatrolling, "The fighter must receive the patrol.");
        Assert.IsFalse(pawn.IsPatrolling, "The worker must not be pulled off its economy.");
    }

    // ------------------------------------------------------------------ helpers

    CombatAI CreateFighter(string path, Vector3Int cell)
    {
        var prefab = LoadPrefab<GameObject>(path);
        var go = Object.Instantiate(prefab, Grid.CellCenter(cell), Quaternion.identity);
        var ai = go.GetComponent<CombatAI>();
        Assert.IsNotNull(ai, path + " must carry a CombatAI.");
        UnitLimits.Invalidate();
        Population.Invalidate();
        return ai;
    }

    /// <summary>Calls the turn-around the mover's arrival callback would call.</summary>
    static void TurnAround(CombatAI ai)
    {
        var method = typeof(CombatAI).GetMethod("ArrivePatrol",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(method, "CombatAI.ArrivePatrol must exist for the patrol tests.");
        method.Invoke(ai, null);
    }
}
