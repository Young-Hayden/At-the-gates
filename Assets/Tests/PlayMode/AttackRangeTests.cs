using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Task 2 regressions: each selected living unit shows exactly one attack-range
/// ring, a finished tower shows its 5.5 defensive range, a half-built tower shows
/// none, and the rings never carry colliders that could steal selection clicks.
/// </summary>
public class AttackRangeTests : RegressionFixture
{
    const string TowerPath = "Assets/Prefabs/Buildings/Knights/Tower_Blue.prefab";

    [UnityTest]
    public IEnumerator SelectedUnits_ShowOneRingEach_AndUpdateOnDeselect()
    {
        PaintRect(-6, -6, 14, 14);
        var a = new GameObject("Range unit A");
        a.AddComponent<UnitMover>();
        var actorA = a.AddComponent<UnitActor>();
        actorA.attackRange = 3.2f;
        var b = new GameObject("Range unit B");
        b.AddComponent<UnitMover>();
        var actorB = b.AddComponent<UnitActor>();
        actorB.attackRange = 4.7f;

        Selection.selected.Clear();
        Selection.selected.Add(a.GetComponent<UnitMover>());
        Selection.selected.Add(b.GetComponent<UnitMover>());
        InvokeUpdateRings();
        yield return null;

        var rings = Object.FindObjectsByType<AttackRangeIndicator>(FindObjectsSortMode.None);
        Assert.AreEqual(2, rings.Length);
        CollectionAssert.AreEquivalent(new[] { 3.2f, 4.7f }, rings.Select(r => r.Radius).ToArray());
        Assert.IsTrue(rings.All(r => r.GetComponent<Collider2D>() == null));

        Selection.selected.RemoveAt(0);
        InvokeUpdateRings();
        yield return null;
        Assert.AreEqual(1, Object.FindObjectsByType<AttackRangeIndicator>(FindObjectsSortMode.None)
            .Count(r => r.IsVisible));
    }

    [UnityTest]
    public IEnumerator SelectedFinishedTower_ShowsFivePointFive_ButHalfBuiltTowerDoesNot()
    {
        PaintRect(-6, -6, 14, 14);
        var prefab = LoadPrefab<GameObject>(TowerPath);
        var tower = Object.Instantiate(prefab, Grid.CellCenter(new Vector3Int(0, 0)), Quaternion.identity);
        tower.name = "Range tower";
        var building = tower.GetComponent<Building>();
        building.Recompute();
        Selection.selected.Clear();
        Selection.selectedBuilding = building;
        InvokeUpdateRings();
        yield return null;
        var ring = Object.FindFirstObjectByType<AttackRangeIndicator>();
        Assert.IsNotNull(ring);
        Assert.AreEqual(5.5f, ring.Radius, 0.001f);

        var halfBuilt = Object.Instantiate(prefab, Grid.CellCenter(new Vector3Int(3, 0)), Quaternion.identity);
        halfBuilt.GetComponent<Building>().Recompute();
        halfBuilt.GetComponent<Building>().MarkBuiltDuringMatch();
        Selection.selectedBuilding = halfBuilt.GetComponent<Building>();
        InvokeUpdateRings();
        yield return null;
        Assert.IsFalse(Object.FindObjectsByType<AttackRangeIndicator>(FindObjectsSortMode.None)
            .Any(r => r.IsVisible));
    }

    // ---------------------------------------------------------------- helpers

    void InvokeUpdateRings()
    {
        typeof(SelectionManager).GetMethod("UpdateRings",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)
            .Invoke(Selection, null);
    }
}
