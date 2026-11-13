using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Regression coverage for placement commit, preview cleanup, and the occupancy
/// rules shared by player and AI building placement.
/// </summary>
public class PlacementTests : RegressionFixture
{
    const string CastlePath = "Assets/Prefabs/Buildings/Knights/Castle_Blue.prefab";
    const string HousePath = "Assets/Prefabs/Buildings/Knights/House_Blue.prefab";

    [Test]
    public void TryQuickBuild_ValidIndex_BeginsRequestedPrefab()
    {
        var house = LoadPrefab<GameObject>(HousePath);
        var castle = LoadPrefab<GameObject>(CastlePath);
        Placer.quickBuild = new[] { house, castle };

        Assert.IsTrue(Placer.TryQuickBuild(1));
        Assert.IsTrue(Placer.IsPlacing);
        Assert.IsNotNull(GameObject.Find("__Ghost_Castle_Blue"));
        Assert.IsNull(GameObject.Find("__Ghost_House_Blue"));
    }

    [UnityTest]
    public IEnumerator TryQuickBuild_InvalidIndices_ReturnFalseAndPreservePlacement()
    {
        var prefab = LoadPrefab<GameObject>(CastlePath);
        Placer.quickBuild = new[] { prefab };
        Placer.Begin(prefab);
        var existingGhost = GameObject.Find("__Ghost_" + prefab.name);
        Assert.IsNotNull(existingGhost);

        Assert.IsFalse(Placer.TryQuickBuild(-1));
        Assert.IsFalse(Placer.TryQuickBuild(1));
        Assert.IsTrue(Placer.IsPlacing);
        yield return null;
        var ghosts = Object.FindObjectsByType<Building>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(b => b != null && b.isGhost && b.gameObject.name == "__Ghost_Castle_Blue")
            .ToArray();
        Assert.AreEqual(1, ghosts.Length);
        Assert.AreSame(existingGhost, ghosts[0].gameObject);
    }

    [Test]
    public void MouseCommitPath_WhenCellIsAlreadyOccupied_DoesNotPlaceOrSpendAgain()
    {
        PaintRect(-8, -4, 32, 12);
        Economy.gold = 5000;
        var prefab = LoadPrefab<GameObject>(CastlePath);

        Placer.Begin(prefab);
        // Keep the first real footprint away from the prefab-origin ghost. This
        // isolates the second-commit bug from the ghost's own registration bug.
        Assert.IsTrue(Placer.TryPlaceAt(new Vector3(10.5f, 0.5f, 0f)),
            "The initial placement should be valid: " + Placer.LastRefusalReason);

        int count = Building.All.Count;
        int gold = Economy.gold;
        var place = typeof(BuildingPlacer).GetMethod(
            "Place", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(place, "BuildingPlacer.Place() must remain the private mouse-commit entry point.");

        // Invoke the same entry point used by the mouse path. Unlike TryPlaceAt,
        // this must perform its own validation immediately before spending.
        bool placeResult = (bool)place.Invoke(Placer, null);
        Assert.IsFalse(placeResult, "The occupied private Place() commit must return false.");
        Assert.IsFalse(Placer.TryPlaceAt(new Vector3(10.5f, 0.5f, 0f)),
            "A second TryPlaceAt on the occupied position must return the failed commit result.");

        Assert.IsTrue(count == Building.All.Count && gold == Economy.gold,
            "Both refused commits must be atomic. " +
            "Building count expected=" + count + " actual=" + Building.All.Count +
            "; gold expected=" + gold + " actual=" + Economy.gold + ".");
    }

    [Test]
    public void TryPlaceAt_PreviewValidButSourceCostUnaffordable_ReturnsFalseWithoutSideEffects()
    {
        PaintRect(-8, -4, 32, 12);
        Economy.gold = 0;
        var prefab = LoadPrefab<GameObject>(CastlePath);
        Vector3 target = new Vector3(10.5f, 0.5f, 0f);

        Placer.Begin(prefab);
        var ghostField = typeof(BuildingPlacer).GetField(
            "ghostBuilding", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(ghostField);
        var ghost = (Building)ghostField.GetValue(Placer);
        Assert.IsNotNull(ghost);

        // Only the preview becomes free. The source prefab keeps its real, unaffordable cost.
        var costField = typeof(Building).GetField("cost", BindingFlags.Instance | BindingFlags.Public);
        Assert.IsNotNull(costField);
        costField.SetValue(ghost, new ResourceCost(0));
        Assert.IsTrue(ghost.cost.IsZero);
        Assert.IsFalse(Economy.CanAfford(prefab.GetComponent<Building>().cost),
            "The source building must remain unaffordable for this regression setup.");

        // Establish that the preview itself is green before entering the commit path.
        var snap = typeof(BuildingPlacer).GetMethod(
            "SnapGhostTo", BindingFlags.Instance | BindingFlags.NonPublic);
        var refresh = typeof(BuildingPlacer).GetMethod(
            "RefreshValidation", BindingFlags.Instance | BindingFlags.NonPublic);
        var lastValid = typeof(BuildingPlacer).GetField(
            "lastValid", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(snap);
        Assert.IsNotNull(refresh);
        Assert.IsNotNull(lastValid);
        snap.Invoke(Placer, new object[] { target });
        refresh.Invoke(Placer, null);
        Assert.IsTrue((bool)lastValid.GetValue(Placer),
            "The zero-cost ghost preview must be valid: " + Placer.LastRefusalReason);

        int count = Building.All.Count;
        int gold = Economy.gold;
        bool result = Placer.TryPlaceAt(target);

        Assert.IsFalse(result,
            "TryPlaceAt must return the failed source-cost commit, not true after preview validation.");
        Assert.AreEqual(count, Building.All.Count,
            "A failed source-cost commit must not instantiate a building.");
        Assert.AreEqual(gold, Economy.gold,
            "A failed source-cost commit must not change gold.");
    }

    [UnityTest]
    public IEnumerator Ghost_OverExistingRealFootprint_PreservesBlockersThroughBeginAndCancel()
    {
        PaintRect(-8, -4, 16, 12);
        var prefab = LoadPrefab<GameObject>(CastlePath);

        // The real building deliberately shares the prefab-origin position with
        // the ghost that Begin() is about to instantiate.
        var real = Object.Instantiate(prefab, prefab.transform.position, Quaternion.identity);
        real.name = "Real building under ghost";
        real.GetComponent<Building>().Recompute();
        var expected = SnapshotBlockers();
        var expectedAll = new List<Building>(Building.All);

        Placer.Begin(prefab);
        var afterBegin = BlockerChanges(expected);
        bool allUnchangedAfterBegin = SameBuildings(expectedAll, Building.All);

        Placer.Cancel();
        yield return null; // let Destroy run OnDisable/OnDestroy on the ghost
        var afterCancel = BlockerChanges(expected);
        bool allUnchangedAfterCancel = SameBuildings(expectedAll, Building.All);

        Assert.IsTrue(allUnchangedAfterBegin,
            "Begin() must leave Building.All unchanged and exclude the ghost.");
        Assert.IsTrue(allUnchangedAfterCancel,
            "Cancel() must leave Building.All unchanged and must not resurrect the ghost.");
        Assert.IsTrue(afterBegin.Count == 0 && afterCancel.Count == 0,
            "Ghost blocker lifecycle changed the snapshot. " +
            "After Begin: [" + string.Join("; ", afterBegin) + "]. " +
            "After Cancel: [" + string.Join("; ", afterCancel) + "].");
    }

    [Test]
    public void DynamicBlockers_RemovingOneRegistration_KeepsCellBlockedUntilLastRemove()
    {
        PaintRect(-2, -2, 8, 8);
        var cells = new[] { new Vector3Int(2, 2, 0) };

        Grid.AddBuildingFootprint(cells);
        Grid.AddBuildingFootprint(cells);
        Grid.RemoveBuildingFootprint(cells);
        Assert.IsTrue(Grid.IsBlockedByBuilding(cells[0]),
            "One overlapping footprint is still registered after the first removal.");

        Grid.RemoveBuildingFootprint(cells);
        Assert.IsFalse(Grid.IsBlockedByBuilding(cells[0]),
            "The cell should become free after the final registration is removed.");
    }

    [Test]
    public void AIPlacementValidation_RejectsUnitInsideFootprint()
    {
        PaintRect(-4, -4, 16, 12);
        var fits = typeof(AIController).GetMethod(
            "Fits", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(fits, "AIController.Fits must remain available for regression coverage.");
        var args = new object[] { Grid, 0, 0, 2, 1, 0, new HashSet<Vector3Int>() };

        bool validSiteControl = (bool)fits.Invoke(null, args);
        Assert.IsTrue(validSiteControl, "The same candidate must be valid before a unit occupies it.");

        var occupier = new GameObject("AI placement occupier");
        occupier.transform.position = Grid.CellCenter(new Vector3Int(1, 0, 0));
        occupier.AddComponent<UnitMover>();

        bool occupiedResult = (bool)fits.Invoke(null, args);
        Assert.IsFalse(occupiedResult, "The AI must not place a building on a cell occupied by a unit.");
    }

    [Test]
    public void AiFindSite_UnitOnFirstCandidate_SelectsAnotherSite()
    {
        PaintRect(-8, -8, 16, 16);
        var ai = new GameObject("AI site selection").AddComponent<AIController>();
        ai.homeCell = Vector2Int.zero;
        ai.buildSearchRadius = 4;
        var prefab = LoadPrefab<GameObject>(HousePath);
        var find = typeof(AIController).GetMethod("FindSite", BindingFlags.Instance | BindingFlags.NonPublic);
        var footprint = typeof(AIController).GetMethod("Footprint", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(find);
        Assert.IsNotNull(footprint);

        object[] args = { prefab, Vector3.zero };
        Assert.IsTrue((bool)find.Invoke(ai, args), "The empty map must offer a first site.");
        var first = (Vector3)args[1];
        object[] size = { prefab, 0, 0 };
        footprint.Invoke(null, size);
        int w = (int)size[1];
        int ax = Mathf.FloorToInt(first.x - w / 2f), ay = Mathf.FloorToInt(first.y);
        var occupier = new GameObject("Unit on the first AI site");
        occupier.transform.position = Grid.CellCenter(new Vector3Int(ax, ay, 0));
        occupier.AddComponent<UnitMover>();

        args[1] = Vector3.zero;
        Assert.IsTrue((bool)find.Invoke(ai, args), "Another site must remain available.");
        var second = (Vector3)args[1];
        Assert.AreNotEqual(first, second, "The occupied first candidate must be skipped.");
    }

    [Test]
    public void Cancel_RightClickInSameFrame_DoesNotIssueMoveOrder()
    {
        PaintRect(-4, -4, 16, 12);
        var unit = new GameObject("Selected cancel-path unit");
        unit.transform.position = Grid.CellCenter(new Vector3Int(0, 0, 0));
        var mover = unit.AddComponent<UnitMover>();
        Selection.selected.Add(mover);

        var rightClick = typeof(SelectionManager).GetMethod(
            "HandleRightClickInput", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(rightClick, "SelectionManager must expose its production right-click handler to deterministic regression coverage.");
        Vector3 destination = Grid.CellCenter(new Vector3Int(3, 0, 0));

        // Valid-site control: this exact unit, destination, and handler can issue a path.
        rightClick.Invoke(Selection, new object[] { destination, true });
        Assert.IsTrue(mover.Moving, "The right-click control should issue a path before placement cancel.");
        mover.Stop();

        var prefab = LoadPrefab<GameObject>(CastlePath);
        Placer.Begin(prefab);
        Placer.Cancel();
        Assert.AreEqual(Time.frameCount, Placer.LastCancelFrame);

        // Same production handler as Update, with right-click injected deterministically.
        rightClick.Invoke(Selection, new object[] { destination, true });
        Assert.IsFalse(mover.Moving,
            "A right-click in the cancel frame must not issue a move order or path.");
    }

    Dictionary<Vector3Int, bool> SnapshotBlockers()
    {
        var snapshot = new Dictionary<Vector3Int, bool>();
        // Includes the Castle's five-cell footprint and free cells on every side.
        for (int x = -6; x <= 6; x++)
        for (int y = -2; y <= 3; y++)
        {
            var cell = new Vector3Int(x, y, 0);
            snapshot[cell] = Grid.IsBlockedByBuilding(cell);
        }
        return snapshot;
    }

    List<string> BlockerChanges(Dictionary<Vector3Int, bool> expected)
    {
        var changes = new List<string>();
        foreach (var pair in expected)
        {
            bool actual = Grid.IsBlockedByBuilding(pair.Key);
            if (actual != pair.Value)
                changes.Add(pair.Key + " expected=" + pair.Value + " actual=" + actual);
        }
        return changes;
    }

    static bool SameBuildings(IReadOnlyList<Building> expected, IReadOnlyList<Building> actual)
    {
        if (expected.Count != actual.Count) return false;
        for (int i = 0; i < expected.Count; i++)
            if (!ReferenceEquals(expected[i], actual[i])) return false;
        return true;
    }
}
