using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Control groups (Ctrl+1..9 to store, 1..9 to recall). Session state only: a load rebuilds
/// every unit, so the groups are deliberately not part of the save.
///
/// The tests drive <see cref="SelectionManager.HandleControlGroupKey"/> directly — the same
/// entry point the Update loop uses — because synthesised keyboard input is not available
/// from the test runner.
/// </summary>
public class ControlGroupTests : RegressionFixture
{
    const string WarriorPath = "Assets/Prefabs/Units/Warrior_Blue.prefab";

    [UnityTest]
    public IEnumerator Assign_ThenRecall_RestoresTheSelection()
    {
        var sm = MakeSelectionManager();
        var a = CreateUnit(new Vector3Int(-4, 0, 0));
        var b = CreateUnit(new Vector3Int(-2, 0, 0));
        yield return null;

        sm.selected.Add(a);
        sm.selected.Add(b);
        Assert.IsTrue(sm.HandleControlGroupKey(1, ctrl: true),
            "Ctrl+1 with two units selected must store the group.");

        sm.selected.Clear();
        Assert.IsTrue(sm.HandleControlGroupKey(1, ctrl: false),
            "Pressing 1 must recall the stored group.");
        CollectionAssert.Contains(sm.selected, a);
        CollectionAssert.Contains(sm.selected, b);
        Assert.AreEqual(2, sm.selected.Count);
    }

    [UnityTest]
    public IEnumerator Recall_PrunesUnitsThatDied()
    {
        var sm = MakeSelectionManager();
        var a = CreateUnit(new Vector3Int(-4, 0, 0));
        var b = CreateUnit(new Vector3Int(-2, 0, 0));
        yield return null;

        sm.selected.Add(a);
        sm.selected.Add(b);
        sm.HandleControlGroupKey(2, ctrl: true);
        sm.selected.Clear();

        // Killed by damage, not Destroy: a corpse object stays non-null for a while, so only
        // the group's own IsLiving prune can drop it. Destroying would let UpdateRings' null
        // sweep hide a missing prune.
        var soldier = a.GetComponent<UnitActor>();
        soldier.TakeDamage(soldier.maxHealth + 1000);
        yield return null;
        Assert.IsFalse(soldier.IsAlive, "Control: the first unit must really be dead.");

        Assert.IsTrue(sm.HandleControlGroupKey(2, ctrl: false),
            "A group with one survivor must still recall.");
        Assert.AreEqual(1, sm.selected.Count, "The dead unit must be pruned.");
        Assert.AreSame(b, sm.selected[0]);
    }

    [UnityTest]
    public IEnumerator Recall_OfADeadGroup_ReturnsFalse_AndDropsIt()
    {
        var sm = MakeSelectionManager();
        var a = CreateUnit(new Vector3Int(-4, 0, 0));
        yield return null;

        sm.selected.Add(a);
        sm.HandleControlGroupKey(3, ctrl: true);
        sm.selected.Clear();

        Object.Destroy(a.gameObject);
        yield return null;

        Assert.IsFalse(sm.HandleControlGroupKey(3, ctrl: false),
            "A group whose units all died must not report a successful recall.");
        Assert.IsFalse(sm.HandleControlGroupKey(3, ctrl: false),
            "And it must have been dropped, so the next press is still a no-op.");
        Assert.AreEqual(0, sm.selected.Count);
    }

    [UnityTest]
    public IEnumerator Assign_WithNothingSelected_KeepsTheOldGroup()
    {
        var sm = MakeSelectionManager();
        var a = CreateUnit(new Vector3Int(-4, 0, 0));
        yield return null;

        sm.selected.Add(a);
        Assert.IsTrue(sm.HandleControlGroupKey(4, ctrl: true));
        sm.selected.Clear();

        Assert.IsFalse(sm.HandleControlGroupKey(4, ctrl: true),
            "Ctrl+4 with an empty selection must be a no-op.");
        Assert.IsTrue(sm.HandleControlGroupKey(4, ctrl: false),
            "The previously stored group must survive that no-op.");
        Assert.AreSame(a, sm.selected[0]);
    }

    [UnityTest]
    public IEnumerator DigitOutsideOneToNine_IsIgnored()
    {
        var sm = MakeSelectionManager();
        var a = CreateUnit(new Vector3Int(-4, 0, 0));
        yield return null;

        sm.selected.Add(a);
        Assert.IsFalse(sm.HandleControlGroupKey(0, ctrl: true), "0 is not a control group.");
        Assert.IsFalse(sm.HandleControlGroupKey(10, ctrl: true), "10 is not a control group.");
        sm.selected.Clear();
        Assert.IsFalse(sm.HandleControlGroupKey(0, ctrl: false));
        Assert.AreEqual(0, sm.selected.Count, "Nothing may be selected by an ignored digit.");
    }

    [UnityTest]
    public IEnumerator Recall_ReplacesABuildingSelection()
    {
        var sm = MakeSelectionManager();
        var a = CreateUnit(new Vector3Int(-4, 0, 0));
        yield return null;

        sm.selected.Add(a);
        sm.HandleControlGroupKey(5, ctrl: true);
        sm.selected.Clear();

        var castlePrefab = LoadPrefab<GameObject>("Assets/Prefabs/Buildings/Knights/Castle_Blue.prefab");
        var castle = Object.Instantiate(castlePrefab,
            new Vector3(4.5f, 4.5f, 0f), Quaternion.identity).GetComponent<Building>();
        sm.selectedBuilding = castle;

        Assert.IsTrue(sm.HandleControlGroupKey(5, ctrl: false));
        Assert.IsNull(sm.selectedBuilding,
            "Recalling a unit group must drop the building selection, or the HUD would keep "
            + "showing that building's production panel.");
        Assert.AreSame(a, sm.selected[0]);
    }

    // ------------------------------------------------------------------ helpers

    SelectionManager MakeSelectionManager()
    {
        var go = new GameObject("Selection probe");
        var sm = go.AddComponent<SelectionManager>();
        sm.enabled = false;       // the update loop is not part of these checks
        return sm;
    }

    UnitMover CreateUnit(Vector3Int cell)
    {
        var prefab = LoadPrefab<GameObject>(WarriorPath);
        var go = Object.Instantiate(prefab, Grid.CellCenter(cell), Quaternion.identity);
        var mover = go.GetComponent<UnitMover>();
        Assert.IsNotNull(mover, WarriorPath + " must carry a UnitMover.");
        return mover;
    }
}
