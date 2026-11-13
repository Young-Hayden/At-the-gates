using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Regression coverage for the restart path: reloading the isolated regression
/// scene in Play Mode must leave no old registries, no old dynamic blockers, no
/// placement ghost, and must repoint the scene singletons at the new scene.
///
/// The reload is deliberately performed in the middle of a running PlayMode test.
/// The test runner's own objects live in DontDestroyOnLoad, so the coroutine
/// survives the Single-mode load; every wait below is bounded with real time so a
/// stalled load fails the test instead of hanging the runner.
/// </summary>
public class SceneLifecycleTests : RegressionFixture
{
    const string HousePath = "Assets/Prefabs/Buildings/Knights/House_Blue.prefab";
    const string CastlePath = "Assets/Prefabs/Buildings/Knights/Castle_Blue.prefab";

    [UnityTest]
    public IEnumerator Restart_ReloadingScene_ClearsOldStaticsBlockersAndGhost()
    {
        // Build a scene that mirrors a live match: terrain, units on both sides,
        // a real building with a registered footprint, a resource node, a bush,
        // an AI, an extra dynamic blocker, and an active placement ghost.
        PaintRect(-8, -4, 20, 12);
        var paintedCell = new Vector3Int(0, 0, 0);
        Assert.IsTrue(Grid.Walkable(paintedCell),
            "The painted regression floor should be walkable before the reload.");

        var blue = CreateActor("Lifecycle blue unit", new Vector3Int(-2, 0, 0), Team.Blue);
        var red = CreateActor("Lifecycle red unit", new Vector3Int(-1, 0, 0), Team.Red);
        var node = CreateNode("Lifecycle resource", new Vector3Int(-4, 0, 0));
        var bush = CreateBush("Lifecycle bush", new Vector3Int(-5, 0, 0));

        var aiObject = new GameObject("Lifecycle AI");
        aiObject.transform.position = Grid.CellCenter(new Vector3Int(-6, 0, 0));
        var ai = aiObject.AddComponent<AIController>();
        ai.team = Team.Red;
        ai.active = false;      // no build/produce ticks during this short scenario

        var housePrefab = LoadPrefab<GameObject>(HousePath);
        var house = UnityEngine.Object.Instantiate(housePrefab, Grid.CellCenter(new Vector3Int(4, 2, 0)) - new Vector3(0f, 0.5f, 0f), Quaternion.identity);
        house.name = "Lifecycle real house";
        var houseBuilding = house.GetComponent<Building>();
        Assert.IsNotNull(houseBuilding, "The lifecycle house prefab must expose a Building.");
        houseBuilding.Recompute();
        var houseCells = new List<Vector3Int>(houseBuilding.Cells);
        Assert.IsNotEmpty(houseCells, "The lifecycle house must reserve a real footprint.");

        var directBlockerCell = new Vector3Int(8, 8, 0);
        Grid.AddBuildingFootprint(new[] { directBlockerCell });
        Assert.IsTrue(Grid.IsBlockedByBuilding(directBlockerCell),
            "The control blocker should be registered before the reload.");

        // Active ghost, moved away from the house so its cells are independent.
        var castlePrefab = LoadPrefab<GameObject>(CastlePath);
        Placer.Begin(castlePrefab);
        Assert.IsTrue(Placer.IsPlacing, "The placement ghost control must be active.");
        var ghostField = typeof(BuildingPlacer).GetField(
            "ghostBuilding", BindingFlags.Instance | BindingFlags.NonPublic);
        var snap = typeof(BuildingPlacer).GetMethod(
            "SnapGhostTo", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(ghostField, "BuildingPlacer.ghostBuilding must remain available.");
        Assert.IsNotNull(snap, "BuildingPlacer.SnapGhostTo must remain available.");
        snap.Invoke(Placer, new object[] { Grid.CellCenter(new Vector3Int(12, 6, 0)) });
        var ghost = (Building)ghostField.GetValue(Placer);
        Assert.IsNotNull(ghost, "The placement ghost control must exist.");

        // Building.Cells is computed when the ghost is created; SnapGhostTo only
        // rewrites BuildingPlacer.ghostCells. Read the placer's actual snapped cells
        // so the leak check below cannot become vacuous.
        var ghostCellsField = typeof(BuildingPlacer).GetField(
            "ghostCells", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(ghostCellsField, "BuildingPlacer.ghostCells must remain available.");
        var ghostCells = (List<Vector3Int>)ghostCellsField.GetValue(Placer);
        Assert.IsNotNull(ghostCells, "BuildingPlacer.ghostCells must contain a list.");
        Assert.IsNotEmpty(ghostCells, "The snapped ghost must occupy at least one cell.");

        // Snapshot every static registry and singleton that the reload must replace.
        var oldUnits = UnitActor.All.ToArray();
        var oldBuildings = Building.All.ToArray();
        var oldNodes = ResourceNode.All.ToArray();
        var oldBushes = ClearableBush.All.ToArray();
        var oldAis = AIController.All.ToArray();

        Assert.IsTrue(Array.IndexOf(oldUnits, blue) >= 0 && Array.IndexOf(oldUnits, red) >= 0,
            "Both units must be registered in UnitActor.All.");
        Assert.IsTrue(Array.IndexOf(oldBuildings, houseBuilding) >= 0,
            "The real house must be registered in Building.All.");
        Assert.IsTrue(Array.IndexOf(oldNodes, node) >= 0,
            "The resource node must be registered in ResourceNode.All.");
        Assert.IsTrue(Array.IndexOf(oldBushes, bush) >= 0,
            "The bush must be registered in ClearableBush.All.");
        Assert.IsTrue(Array.IndexOf(oldAis, ai) >= 0,
            "The AI must be registered in AIController.All.");

        Scene oldScene = SceneManager.GetActiveScene();
        MapGrid oldGrid = Grid;
        Economy oldEconomy = Economy.Instance;
        GameState oldGameState = GameState.Instance;
        Assert.IsTrue(oldScene.IsValid(), "The pre-reload scene handle must be valid.");
        Assert.IsTrue(oldScene.isLoaded, "The pre-reload scene must be loaded before the restart.");
        Assert.AreEqual(oldScene, Grid.gameObject.scene,
            "The old MapGrid must live in the old active scene.");
        Assert.IsNotNull(oldEconomy, "The old scene must own an Economy instance.");
        Assert.IsNotNull(oldGameState, "The old scene must own a GameState instance.");

        // Verify the old grid's blocker state BEFORE the reload: the real footprint
        // and the control blocker must be registered, while the placement ghost must
        // not reserve its preview cells. The post-reload check then means something.
        Assert.IsTrue(oldGrid.IsBlockedByBuilding(directBlockerCell),
            "The control blocker must be registered on the old grid before the reload.");
        Assert.IsTrue(oldGrid.IsBlockedByBuilding(houseCells[0]),
            "The real house footprint must be registered on the old grid before the reload.");
        var ghostBlockedBeforeReload = new List<string>();
        foreach (var cell in ghostCells)
            if (oldGrid.IsBlockedByBuilding(cell))
                ghostBlockedBeforeReload.Add(cell.ToString());
        Assert.IsEmpty(ghostBlockedBeforeReload,
            "A placement ghost must not register its preview cells as blockers: [" +
            string.Join("; ", ghostBlockedBeforeReload) + "].");

        // Restart sequence, mirroring GameState.RestartLevel(): a finished match
        // has left time paused, and the restart restores time before loading.
        Time.timeScale = 0f;
        yield return null;
        Assert.AreEqual(0f, Time.timeScale, "The paused-game control must hold time at zero.");
        Time.timeScale = 1f;

        var reload = EditorSceneManager.LoadSceneInPlayMode(
            RegressionScenePath,
            new LoadSceneParameters(LoadSceneMode.Single));
        Assert.IsTrue(reload.IsValid(),
            "EditorSceneManager.LoadSceneInPlayMode must return a valid scene handle.");

        // Bounded wait (real time, immune to timeScale) for the load to finish and
        // for every old object to be destroyed. The reload replaces the scene and
        // the old objects go away at the end of the load operation.
        const float ReloadTimeout = 15f;
        float deadline = Time.realtimeSinceStartup + ReloadTimeout;
        bool settled = false;
        while (Time.realtimeSinceStartup < deadline)
        {
            settled =
                reload.isLoaded &&
                oldGrid == null && oldEconomy == null && oldGameState == null &&
                blue == null && red == null && houseBuilding == null &&
                node == null && bush == null && ai == null && ghost == null &&
                UnitActor.All.Count == 0 && Building.All.Count == 0 &&
                ResourceNode.All.Count == 0 && ClearableBush.All.Count == 0 &&
                AIController.All.Count == 0 &&
                Economy.Instance != null &&
                Economy.Instance != oldEconomy &&
                MapGrid.Instance != null &&
                MapGrid.Instance != oldGrid;
            if (settled) break;
            yield return null;
        }

        Assert.IsTrue(settled,
            "The scene reload did not settle within " + ReloadTimeout + "s. " +
            "Scene loaded=" + reload.isLoaded + ", " +
            "Old grid alive=" + (oldGrid != null) +
            ", old economy alive=" + (oldEconomy != null) +
            ", old game state alive=" + (oldGameState != null) +
            ", ghost alive=" + (ghost != null) + ".");

        var newGrid = MapGrid.Instance;
        Assert.IsNotNull(newGrid, "The reloaded scene must own a MapGrid.");
        Assert.AreNotSame(oldGrid, newGrid, "MapGrid.Instance must point at the new scene.");
        Assert.AreEqual(reload, newGrid.gameObject.scene,
            "MapGrid.Instance must live in the reloaded scene.");

        // 1. Static registries contain no old instances and no leftovers at all.
        AssertNoOldReferences("UnitActor.All", UnitActor.All, oldUnits);
        AssertNoOldReferences("Building.All", Building.All, oldBuildings);
        AssertNoOldReferences("ResourceNode.All", ResourceNode.All, oldNodes);
        AssertNoOldReferences("ClearableBush.All", ClearableBush.All, oldBushes);
        AssertNoOldReferences("AIController.All", AIController.All, oldAis);
        Assert.AreEqual(0, UnitActor.All.Count, "The reloaded scene must start with no units.");
        Assert.AreEqual(0, Building.All.Count, "The reloaded scene must start with no buildings.");
        Assert.AreEqual(0, ResourceNode.All.Count, "The reloaded scene must start with no resource nodes.");
        Assert.AreEqual(0, ClearableBush.All.Count, "The reloaded scene must start with no bushes.");
        Assert.AreEqual(0, AIController.All.Count, "The reloaded scene must start with no AI controllers.");

        // 2. Old dynamic blockers, building footprints, and ghost cells are gone.
        var checkedCells = new List<Vector3Int> { directBlockerCell };
        checkedCells.AddRange(houseCells);
        checkedCells.AddRange(ghostCells);
        var stillBlocked = new List<string>();
        foreach (var cell in checkedCells)
            if (newGrid.IsBlockedByBuilding(cell))
                stillBlocked.Add(cell.ToString());
        Assert.IsEmpty(stillBlocked,
            "Old footprints leaked into the reloaded scene's MapGrid: [" +
            string.Join("; ", stillBlocked) + "].");
        Assert.IsFalse(newGrid.Walkable(paintedCell),
            "Runtime-painted terrain must not survive the reload.");
        Assert.IsFalse(newGrid.TerrainWalkable(paintedCell),
            "The reloaded scene's tilemaps must be back to their saved, empty state.");

        // 3. The old ghost object is gone from the new scene.
        var ghostNames = new List<string>();
        foreach (var b in UnityEngine.Object.FindObjectsByType<Building>(FindObjectsSortMode.None))
            if (b.name.StartsWith("__Ghost_")) ghostNames.Add(b.name);
        Assert.IsEmpty(ghostNames,
            "A placement ghost survived the reload: [" + string.Join("; ", ghostNames) + "].");

        // 4. Scene singletons refer to the new scene, not the old one.
        Assert.IsNotNull(Economy.Instance, "The reloaded scene must provide Economy.Instance.");
        Assert.AreNotSame(oldEconomy, Economy.Instance, "Economy.Instance must be replaced on reload.");
        Assert.AreEqual(reload, Economy.Instance.gameObject.scene,
            "Economy.Instance must live in the reloaded scene.");

        Assert.IsNotNull(GameState.Instance, "The reloaded scene must provide GameState.Instance.");
        Assert.AreNotSame(oldGameState, GameState.Instance, "GameState.Instance must be replaced on reload.");
        Assert.AreEqual(reload, GameState.Instance.gameObject.scene,
            "GameState.Instance must live in the reloaded scene.");

        Assert.IsNotNull(BuildingPlacer.Instance, "The reloaded scene must provide BuildingPlacer.Instance.");
        Assert.AreEqual(reload, BuildingPlacer.Instance.gameObject.scene,
            "BuildingPlacer.Instance must live in the reloaded scene.");
        Assert.IsFalse(BuildingPlacer.Instance.IsPlacing,
            "The reloaded scene must not resume the previous placement ghost.");

        Assert.IsFalse(oldScene.isLoaded,
            "The previous scene must be unloaded by the Single-mode reload.");
        Assert.IsTrue(reload.IsValid(),
            "The reloaded scene handle must remain valid after the load.");
        Assert.IsFalse(oldScene == reload,
            "The reload must produce a new scene identity, not reuse the old one.");
        Assert.AreEqual(reload, SceneManager.GetActiveScene(),
            "The reloaded scene must become the active scene.");

        // 5. Time is back at normal speed for the restarted match.
        Assert.AreEqual(1f, Time.timeScale, "The restart sequence must restore Time.timeScale to 1.");
    }

    static void AssertNoOldReferences<T>(string registry, List<T> current, T[] old) where T : UnityEngine.Object
    {
        var leaked = new List<string>();
        foreach (var instance in old)
            if (instance != null && current.Contains(instance))
                leaked.Add(instance.name);
        Assert.IsEmpty(leaked,
            registry + " still contains objects from the old scene: [" +
            string.Join("; ", leaked) + "].");
    }

    UnitActor CreateActor(string name, Vector3Int cell, Team team)
    {
        var go = new GameObject(name);
        go.transform.position = Grid.CellCenter(cell);
        go.AddComponent<UnitMover>();
        var actor = go.AddComponent<UnitActor>();
        actor.team = team;
        actor.showHealthBar = false;
        return actor;
    }

    ResourceNode CreateNode(string name, Vector3Int cell)
    {
        var go = new GameObject(name);
        go.transform.position = Grid.CellCenter(cell);
        var node = go.AddComponent<ResourceNode>();
        node.kind = ResourceKind.Wood;
        node.amount = 100;
        node.maxAmount = 100;
        return node;
    }

    ClearableBush CreateBush(string name, Vector3Int cell)
    {
        var go = new GameObject(name);
        go.transform.position = Grid.CellCenter(cell);
        return go.AddComponent<ClearableBush>();
    }
}
