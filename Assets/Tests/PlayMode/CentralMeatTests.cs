using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Phase A Task 3: the three neutral meat sheep at the centre of SampleScene.
/// Validates the brief's amended layout — nominal cells (18,3), (19,5), (22,3),
/// each a 2x1 footprint placed on the grid line (origin at nominal.x + 1), and
/// both starting armies still sharing one connected region from which every
/// sheep is harvest-reachable.
///
/// Sheep_Meat_3 was relocated from (22,7)+(23,7) to (22,3)+(23,3) on 2026-10-31
/// (user-approved): its old home was fully boxed in and it could not wander.
/// </summary>
public class CentralMeatTests : RegressionFixture
{
    const string SampleScenePath = "Assets/Scenes/SampleScene.scene";

    static readonly string[] SheepNames =
    {
        "Sheep_Meat_1", "Sheep_Meat_2", "Sheep_Meat_3",
    };

    static readonly Vector3Int[] NominalCells =
    {
        new Vector3Int(18, 3, 0),
        new Vector3Int(19, 5, 0),
        new Vector3Int(22, 3, 0),
    };

    IEnumerator LoadSampleScene()
    {
        EditorSceneManager.LoadSceneInPlayMode(
            SampleScenePath,
            new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;
    }

    static ResourceNode[] FindMeatSheep()
    {
        return Object.FindObjectsByType<ResourceNode>(FindObjectsSortMode.None)
            .Where(n => n.kind == ResourceKind.Meat)
            .OrderBy(n => n.name)
            .ToArray();
    }

    [UnityTest]
    public IEnumerator SampleScene_CarriesThreeCentralSheep_WithBriefStats()
    {
        yield return LoadSampleScene();

        var meat = FindMeatSheep();
        Assert.AreEqual(3, meat.Length,
            "SampleScene must carry exactly three meat resource nodes (the central sheep).");

        for (int i = 0; i < 3; i++)
        {
            var node = meat[i];
            Assert.AreEqual(SheepNames[i], node.name, $"Meat node {i} name");
            Assert.AreEqual(300, node.amount, $"{node.name} amount");
            Assert.AreEqual(300, node.maxAmount, $"{node.name} maxAmount");
            Assert.AreEqual(20, node.perTrip, $"{node.name} perTrip");
            Assert.AreEqual(120f, node.regrowSeconds, 0.001f, $"{node.name} regrowSeconds");
            Assert.IsFalse(node.Depleted, $"{node.name} must start full");
            Assert.IsNull(node.GetComponent<UnitActor>(),
                $"{node.name} is scenery, not a unit.");

            var building = node.GetComponent<Building>();
            Assert.IsNotNull(building, $"{node.name} must carry a Building for its footprint.");
            Assert.AreEqual(Team.Neutral, building.team, $"{node.name} team");
            Assert.AreSame(building, node.blockingBuilding,
                $"{node.name} must stop blocking through its own Building when depleted.");

            // Amendment A2: even-width footprints sit on the grid line, so the
            // origin is the right edge of the nominal cell (nominal.x + 1, nominal.y).
            var expected = new Vector3(NominalCells[i].x + 1f, NominalCells[i].y, 0f);
            Assert.AreEqual(expected.x, node.transform.position.x, 0.001f, $"{node.name} position.x");
            Assert.AreEqual(expected.y, node.transform.position.y, 0.001f, $"{node.name} position.y");
            Assert.AreEqual(0f, node.transform.position.z, 0.001f, $"{node.name} position.z");
        }
    }

    [UnityTest]
    public IEnumerator CentralSheep_BlockExactlyTheirTwoCells_InTheStartingUnitsRegion()
    {
        yield return LoadSampleScene();

        var grid = Object.FindFirstObjectByType<MapGrid>();
        Assert.IsNotNull(grid, "SampleScene must carry a MapGrid.");

        var meat = FindMeatSheep();
        Assert.AreEqual(3, meat.Length,
            "SampleScene must carry exactly three meat resource nodes (the central sheep).");

        var blocked = new HashSet<Vector3Int>();
        for (int i = 0; i < 3; i++)
        {
            var node = meat[i];
            Assert.AreEqual(SheepNames[i], node.name, $"Meat node {i} name");
            var building = node.GetComponent<Building>();
            Assert.IsNotNull(building, $"{node.name} must carry a Building.");
            Assert.AreEqual(new Vector2Int(2, 1), building.footprint,
                $"{node.name} footprint must be pinned to 2x1, not sprite-measured.");
            Assert.AreEqual(2, building.Width, $"{node.name} runtime width");

            // Read the blocked cells back from the live component rather than
            // recomputing the arithmetic: art/collision must land on the nominal pair.
            var cells = building.Cells.ToArray();
            Assert.AreEqual(2, cells.Length, $"{node.name} cell count");
            var expectedCells = new[]
            {
                NominalCells[i],
                NominalCells[i] + new Vector3Int(1, 0, 0),
            };
            CollectionAssert.AreEquivalent(expectedCells, cells,
                $"{node.name} must block exactly {expectedCells[0]} and {expectedCells[1]}.");

            foreach (var cell in cells)
            {
                Assert.IsTrue(grid.TerrainWalkable(cell),
                    $"{node.name}: {cell} must be walkable terrain underneath.");
                Assert.IsTrue(grid.IsBlockedByBuilding(cell),
                    $"{node.name}: {cell} must be registered as a building blocker.");
                Assert.IsFalse(grid.Walkable(cell),
                    $"{node.name}: {cell} must not be walkable while the sheep stands on it.");
                blocked.Add(cell);
            }
        }

        Assert.AreEqual(6, blocked.Count, "The three sheep must block six distinct cells.");

        // The real invariant: with the sheep standing there, both starting armies
        // must still share one connected region — the sheep must not wall off
        // either base — and every sheep must be harvest-reachable from that
        // region, probed with the same NearestWalkableWithin call that
        // ResourceNode.FindNearestReachable (Gatherer dispatch) uses.
        var units = Object.FindObjectsByType<UnitActor>(FindObjectsSortMode.None)
            .Where(u => u.team == Team.Blue || u.team == Team.Red)
            .ToArray();
        Assert.IsTrue(units.Any(u => u.team == Team.Blue),
            "SampleScene must still carry the blue starting units.");
        Assert.IsTrue(units.Any(u => u.team == Team.Red),
            "SampleScene must still carry the red starting units.");

        int mainRegion = -1;
        foreach (var unit in units)
        {
            var unitCell = grid.WorldToCell(unit.transform.position);
            int region = grid.Region(unitCell);
            Assert.GreaterOrEqual(region, 0,
                $"Starting unit {unit.name} at {unitCell} must stand in a walkable region.");
            if (mainRegion < 0) mainRegion = region;
            Assert.AreEqual(mainRegion, region,
                $"Starting unit {unit.name} at {unitCell} is in region {region}, but its allies hold region {mainRegion} — the central sheep must not split the two starting territories.");
        }

        var probeStart = grid.WorldToCell(units[0].transform.position);
        foreach (var node in meat)
        {
            var approach = grid.NearestWalkableWithin(
                probeStart, grid.WorldToCell(node.transform.position), 4, mainRegion, 1.6f);
            Assert.IsTrue(approach.HasValue,
                $"{node.name} must be harvest-reachable from the shared starting region.");
        }
    }

    /// <summary>
    /// A real SaveGameService capture → restore destroys every Building and rebuilds
    /// the sheep from the prefab catalog. The regrow / blocking / pinned-footprint
    /// settings therefore have to live on HappySheep_Idle.prefab, not only on the
    /// scene instances, or continuing a saved match silently turns the sheep into
    /// non-renewable permanent walls.
    /// </summary>
    [UnityTest]
    public IEnumerator CentralSheep_SurviveSaveLoad_WithRegrowBlockingAndFootprintIntact()
    {
        yield return LoadSampleScene();

        var before = FindMeatSheep();
        Assert.AreEqual(3, before.Length,
            "SampleScene must carry exactly three meat resource nodes before saving.");

        // Capture and read back through the production path the ESC menu uses.
        var snapshot = SaveGameService.CaptureSnapshot(out string captureError);
        Assert.IsNotNull(snapshot, "SaveGameService capture failed: " + captureError);
        string path = Path.Combine(
            Application.temporaryCachePath, "at_the_gates_central_meat_roundtrip.json");
        Assert.IsTrue(SaveGameService.SaveToPath(snapshot, path, out string writeError),
            "SaveToPath failed: " + writeError);
        Assert.IsTrue(SaveGameService.TryReadFromPath(path, out var loaded, out string readError),
            "TryReadFromPath failed: " + readError);

        bool restored = false;
        string restoreError = null;
        yield return SaveGameService.RestoreSnapshot(loaded, (ok, error) =>
        {
            restored = ok;
            restoreError = error;
        });
        Assert.IsTrue(restored, "SaveGameService restore failed: " + restoreError);

        var grid = Object.FindFirstObjectByType<MapGrid>();
        Assert.IsNotNull(grid, "SampleScene must still carry a MapGrid after the restore.");

        var meat = FindMeatSheep();
        Assert.AreEqual(3, meat.Length,
            "Restore must rebuild exactly three meat resource nodes from the prefab catalog.");

        foreach (var node in meat)
        {
            var building = node.GetComponent<Building>();
            Assert.IsNotNull(building, $"{node.name} must carry a Building after the restore.");

            Assert.AreEqual(120f, node.regrowSeconds, 0.001f,
                $"{node.name} must keep regrowSeconds=120 after a save/load round trip — " +
                "the value must be on the prefab, not only on the scene instance.");
            Assert.IsNotNull(node.blockingBuilding,
                $"{node.name} must keep a blockingBuilding after a save/load round trip.");
            Assert.AreSame(building, node.blockingBuilding,
                $"{node.name} blockingBuilding must be its own Building component after the restore.");

            Assert.AreEqual(new Vector2Int(2, 1), building.footprint,
                $"{node.name} must keep its pinned 2x1 footprint after a save/load round trip.");

            var cells = building.Cells.ToArray();
            Assert.AreEqual(2, cells.Length,
                $"{node.name} must still occupy exactly two cells after the restore.");
            int row = Mathf.FloorToInt(node.transform.position.y);
            Assert.AreEqual(row, cells[0].y, $"{node.name} first cell row");
            Assert.AreEqual(row, cells[1].y, $"{node.name} second cell row");
            CollectionAssert.AreEquivalent(
                new[] { cells[0], cells[0] + new Vector3Int(1, 0, 0) }, cells,
                $"{node.name} must block two adjacent cells in its row after the restore.");
            foreach (var cell in cells)
                Assert.IsFalse(grid.Walkable(cell),
                    $"{node.name}: restored cell {cell} must be blocked while the sheep stands on it.");
        }

        // Observable consequence: a restored sheep can still be harvested to zero,
        // frees its cells, and re-blocks them when it grows back.
        var first = meat[0];
        var firstBuilding = first.GetComponent<Building>();
        var firstCells = firstBuilding.Cells.ToArray();
        Assert.IsTrue(first.regrowSeconds > 0f,
            $"{first.name} must be renewable before the depletion check.");

        first.Harvest(first.amount);
        Assert.IsTrue(first.Depleted, $"{first.name} must be empty after harvesting it fully.");
        Assert.IsTrue(first.IsRegrowing,
            $"{first.name} must start regrowing after being harvested to zero.");
        foreach (var cell in firstCells)
            Assert.IsTrue(grid.Walkable(cell),
                $"{first.name}: depleted cell {cell} must become walkable through blockingBuilding.");

        // Fast-forward the regrow timer instead of waiting the full 120 s; the
        // production Update path still performs the actual regrow.
        var timerField = typeof(ResourceNode).GetField(
            "regrowTimer", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(timerField, "ResourceNode.regrowTimer must still exist.");
        timerField.SetValue(first, 0.05f);
        float deadline = Time.realtimeSinceStartup + 3f;
        while (first.IsRegrowing && Time.realtimeSinceStartup < deadline)
            yield return null;

        Assert.IsFalse(first.IsRegrowing,
            $"{first.name} must finish regrowing after the timer elapses.");
        Assert.AreEqual(first.maxAmount, first.amount,
            $"{first.name} must be full again after regrowing.");
        foreach (var cell in firstCells)
        {
            Assert.IsTrue(grid.IsBlockedByBuilding(cell),
                $"{first.name}: regrown cell {cell} must be registered as a building blocker again.");
            Assert.IsFalse(grid.Walkable(cell),
                $"{first.name}: regrown cell {cell} must block movement again.");
        }

        try { File.Delete(path); } catch (IOException) { }
        try { File.Delete(path + ".tmp"); } catch (IOException) { }
    }
}
