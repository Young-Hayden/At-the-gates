using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Full-match save/load regressions. These drive the same capture and restore
/// routines used by the ESC menu and the main-menu continue button.
/// </summary>
public class SaveLoadTests : RegressionFixture
{
    const string TowerPath = "Assets/Prefabs/Buildings/Knights/Tower_Blue.prefab";
    const string ArcherPath = "Assets/Prefabs/Units/Archer_Blue.prefab";
    const string ArcheryPath = "Assets/Prefabs/Buildings/Knights/Archery_Blue.prefab";
    const string HousePath = "Assets/Prefabs/Buildings/Knights/House_Blue.prefab";
    const string PawnPath = "Assets/Prefabs/Units/Pawn_Blue.prefab";
    const string TreePath = "Assets/Prefabs/Resources/Tree1.prefab";

    [UnityTest]
    public IEnumerator FullSnapshot_RoundTripsCoreMatchState()
    {
        PaintRect(-24, -8, 48, 20);
        Economy.gold = 321;
        Economy.wood = 123;
        Economy.meat = 77;

        var worker = Instantiate(PawnPath, new Vector3Int(-10, 0, 0), "Saved worker");
        yield return null;
        var gatherer = worker.GetComponent<Gatherer>();
        Assert.IsNotNull(gatherer, "The worker control must carry a Gatherer.");
        gatherer.RestoreSaveState(new Gatherer.SaveState
        {
            carrying = 7,
            carryingKind = ResourceKind.Wood,
            manualIdle = false,
        });

        var tower = Instantiate(TowerPath, new Vector3Int(0, 0, 0), "Saved tower");
        var archer = Instantiate(ArcherPath, new Vector3Int(2, 0, 0), "Saved garrison archer");
        yield return null;
        var archerActor = archer.GetComponent<UnitActor>();
        archerActor.health = 41;
        var towerGarrison = tower.GetComponent<TowerGarrison>();
        Assert.IsTrue(towerGarrison.Garrison(archerActor),
            "The control archer must be garrisoned before saving.");

        var archery = Instantiate(ArcheryPath, new Vector3Int(6, 0, 0), "Saved archery");
        yield return null;
        var production = archery.GetComponent<ProductionBuilding>();
        Assert.IsTrue(production.TryQueue(0, payCost: false),
            "The archery control must accept a production order.");

        var house = Instantiate(HousePath, new Vector3Int(12, 0, 0), "Saved construction house");
        house.GetComponent<Building>().MarkBuiltDuringMatch();
        yield return null;

        var rubbleHouse = Instantiate(HousePath, new Vector3Int(-16, 4, 0), "Saved rubble house");
        yield return null;
        var rubbleBuilding = rubbleHouse.GetComponent<Building>();
        var rubbleCells = new List<Vector3Int>(rubbleBuilding.Cells);
        rubbleBuilding.TakeDamage(100000);
        yield return null;
        Assert.IsFalse(rubbleBuilding.IsAlive, "The control house must become rubble.");
        Assert.IsNotNull(rubbleBuilding.destroyedSprite,
            "The rubble control requires a persistent destroyed sprite.");

        var tree = Instantiate(TreePath, new Vector3Int(-18, -2, 0), "Saved tree");
        yield return null;
        var node = tree.GetComponent<ResourceNode>();
        node.RestoreSaveState(new ResourceNode.SaveState
        {
            amount = 23,
            maxAmount = 80,
            regrowing = false,
            regrowTimer = 0f,
        });

        var camera = Camera.main;
        camera.transform.position = new Vector3(3.5f, 4.5f, -10f);
        camera.orthographicSize = 12.5f;

        var snapshot = SaveGameService.CaptureSnapshot(out string captureError);
        Assert.IsNotNull(snapshot, "Capture failed: " + captureError);
        Assert.AreEqual(321, snapshot.economy.gold, "The snapshot must capture gold.");
        Assert.AreEqual(123, snapshot.economy.wood, "The snapshot must capture wood.");

        string path = Path.Combine(Application.temporaryCachePath, "at_the_gates_roundtrip.json");
        Assert.IsTrue(SaveGameService.SaveToPath(snapshot, path, out string writeError),
            "SaveToPath failed: " + writeError);
        Assert.IsTrue(SaveGameService.TryReadFromPath(path, out var loaded, out string readError),
            "TryReadFromPath failed: " + readError);

        // Wipe the scene state the same way a scene reload does.
        foreach (var u in UnityEngine.Object.FindObjectsByType<UnitActor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (u != null && u.gameObject.scene.IsValid()) UnityEngine.Object.Destroy(u.gameObject);
        foreach (var b in UnityEngine.Object.FindObjectsByType<Building>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (b != null && b.gameObject.scene.IsValid()) UnityEngine.Object.Destroy(b.gameObject);
        yield return null;
        Economy.gold = 0; Economy.wood = 0; Economy.meat = 0;
        camera.transform.position = Vector3.zero;
        camera.orthographicSize = 5f;

        bool restored = false;
        string restoreError = null;
        yield return SaveGameService.RestoreSnapshot(loaded, (ok, error) =>
        {
            restored = ok;
            restoreError = error;
        });
        Assert.IsTrue(restored, "Restore failed: " + restoreError);

        Assert.AreEqual(321, Economy.gold, "Gold must survive the round trip.");
        Assert.AreEqual(123, Economy.wood, "Wood must survive the round trip.");
        Assert.AreEqual(77, Economy.meat, "Meat must survive the round trip.");

        var restoredWorker = GameObject.Find("Saved worker");
        Assert.IsNotNull(restoredWorker, "The saved worker must be recreated.");
        Assert.AreEqual(7, restoredWorker.GetComponent<Gatherer>().Carrying,
            "The worker's carried wood must survive the round trip.");

        var restoredTower = GameObject.Find("Saved tower");
        Assert.IsNotNull(restoredTower, "The saved tower must be recreated.");
        var restoredGarrison = restoredTower.GetComponent<TowerGarrison>();
        Assert.IsTrue(restoredGarrison.HasArcher, "The tower must restore its garrison.");
        Assert.AreEqual(41, restoredGarrison.Archer.health,
            "The garrisoned archer's health must survive the round trip.");

        var restoredArchery = GameObject.Find("Saved archery");
        Assert.IsNotNull(restoredArchery, "The saved production building must be recreated.");
        Assert.AreEqual(1, restoredArchery.GetComponent<ProductionBuilding>().Queue.Count,
            "The production queue must survive the round trip.");

        var restoredConstruction = GameObject.Find("Saved construction house");
        Assert.IsNotNull(restoredConstruction, "The constructing house must be recreated.");
        Assert.IsTrue(restoredConstruction.GetComponent<Building>().IsUnderConstruction,
            "A building saved mid-construction must still be under construction.");

        var restoredRubble = GameObject.Find("Saved rubble house");
        Assert.IsNotNull(restoredRubble, "Saved rubble must be recreated.");
        var restoredRubbleBuilding = restoredRubble.GetComponent<Building>();
        Assert.IsFalse(restoredRubbleBuilding.IsAlive, "Saved rubble must remain destroyed.");
        foreach (var cell in rubbleCells)
            Assert.IsFalse(Grid.Walkable(cell),
                "Restored rubble must keep blocking its footprint cell " + cell + ".");

        var restoredTree = GameObject.Find("Saved tree");
        Assert.IsNotNull(restoredTree, "The saved resource node must be recreated.");
        Assert.AreEqual(23, restoredTree.GetComponent<ResourceNode>().amount,
            "The resource node's remaining amount must survive the round trip.");

        Assert.AreEqual(new Vector3(3.5f, 4.5f, -10f), camera.transform.position,
            "The camera position must be restored.");
        Assert.AreEqual(12.5f, camera.orthographicSize, 0.01f,
            "The camera zoom must be restored.");
    }

    [UnityTest]
    public IEnumerator LegacyUnderConstructionGarrison_RoundTripsAndFiresOnlyAfterCompletion()
    {
        PaintRect(-8, -8, 24, 24);
        var tower = Instantiate(TowerPath, new Vector3Int(0, 0, 0), "Legacy construction tower");
        tower.GetComponent<Building>().MarkBuiltDuringMatch();
        var archer = Instantiate(ArcherPath, new Vector3Int(2, 0, 0), "Legacy archer");
        yield return null;

        var building = tower.GetComponent<Building>();
        Assert.IsTrue(building.IsUnderConstruction);
        var actor = archer.GetComponent<UnitActor>();
        actor.health = 41;
        Assert.IsTrue(tower.GetComponent<TowerGarrison>().RestoreGarrison(actor));

        var snapshot = SaveGameService.CaptureSnapshot(out string captureError);
        Assert.IsNotNull(snapshot, captureError);
        string path = Path.Combine(Application.temporaryCachePath, "legacy_construction_garrison.json");
        Assert.IsTrue(SaveGameService.SaveToPath(snapshot, path, out string writeError), writeError);
        Assert.IsTrue(SaveGameService.TryReadFromPath(path, out var loaded, out string readError), readError);

        foreach (var u in UnityEngine.Object.FindObjectsByType<UnitActor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (u != null && u.gameObject.scene.IsValid()) UnityEngine.Object.Destroy(u.gameObject);
        foreach (var b in UnityEngine.Object.FindObjectsByType<Building>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (b != null && b.gameObject.scene.IsValid()) UnityEngine.Object.Destroy(b.gameObject);
        yield return null;

        bool restored = false;
        string restoreError = null;
        yield return SaveGameService.RestoreSnapshot(loaded, (ok, error) =>
        {
            restored = ok;
            restoreError = error;
        });
        Assert.IsTrue(restored, restoreError);

        var restoredTower = GameObject.Find("Legacy construction tower");
        Assert.IsNotNull(restoredTower);
        var restoredBuilding = restoredTower.GetComponent<Building>();
        var garrison = restoredTower.GetComponent<TowerGarrison>();
        Assert.IsTrue(restoredBuilding.IsUnderConstruction);
        Assert.IsTrue(garrison.HasArcher);
        Assert.AreEqual(41, garrison.Archer.health);

        var update = typeof(TowerGarrison).GetMethod(
            "Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(update, "The test must drive the same private TowerGarrison.Update entry point.");

        var enemyGO = new GameObject("Restored tower target");
        enemyGO.transform.position = restoredTower.transform.position + Vector3.left * 2f;
        var enemy = enemyGO.AddComponent<UnitActor>();
        enemy.team = Team.Red;
        enemy.showHealthBar = false;
        enemy.maxHealth = 100;
        enemy.health = 100;
        enemy.isCombatant = true;
        update.Invoke(garrison, null);
        Assert.IsNull(UnityEngine.Object.FindFirstObjectByType<Projectile>(),
            "A restored half-built tower must not fire at an in-range enemy.");

        typeof(Building).GetMethod("FinishConstruction",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Invoke(restoredBuilding, null);
        update.Invoke(garrison, null);
        Assert.IsNotNull(UnityEngine.Object.FindFirstObjectByType<Projectile>(),
            "The restored garrison must fire after construction completes.");
    }

    [Test]
    public void MalformedSave_ReturnsFalseWithoutThrowing()
    {
        string path = Path.Combine(Application.temporaryCachePath, "at_the_gates_bad_save.json");
        File.WriteAllText(path, "{ definitely not json");
        Assert.IsFalse(SaveGameService.TryReadFromPath(path, out _, out string error));
        Assert.IsFalse(string.IsNullOrEmpty(error), "A malformed save must report a reason.");
    }

    [Test]
    public void UnsupportedVersion_ReturnsFalseWithoutThrowing()
    {
        string path = Path.Combine(Application.temporaryCachePath, "at_the_gates_version_save.json");
        File.WriteAllText(path, "{\"version\":999,\"sceneName\":\"SampleScene\"}");
        Assert.IsFalse(SaveGameService.TryReadFromPath(path, out _, out string error));
        StringAssert.Contains("版本", error);
    }

    [Test]
    public void NullContainers_AreRejectedBeforeWriting()
    {
        PaintRect(-2, -2, 5, 5);
        foreach (string field in new[] { "buildings", "units", "ai", "bushes" })
        {
            var data = SaveGameService.CaptureSnapshot(out string captureError);
            Assert.IsNotNull(data, captureError);
            switch (field)
            {
                case "buildings": data.buildings = null; break;
                case "units": data.units = null; break;
                case "ai": data.ai = null; break;
                default: data.bushes = null; break;
            }
            string path = Path.Combine(Application.temporaryCachePath, "null_container_" + field + ".json");
            // Exercise the in-memory API boundary directly. JsonUtility normalises
            // JSON null lists to empty arrays, so a text round-trip cannot cover it.
            Assert.IsFalse(SaveGameService.SaveToPath(data, path, out string error), field + " must be rejected");
            Assert.IsFalse(string.IsNullOrEmpty(error), field + " must report a validation error");
        }
    }

    [UnityTest]
    public IEnumerator RestoreNullContainer_ReturnsFalseAndPreservesTimeScale()
    {
        PaintRect(-2, -2, 5, 5);
        var data = SaveGameService.CaptureSnapshot(out string captureError);
        Assert.IsNotNull(data, captureError);
        data.buildings = null;
        Time.timeScale = 1f;
        bool called = false;
        bool success = true;
        string restoreError = null;
        yield return SaveGameService.RestoreSnapshot(data, (ok, reason) =>
        {
            called = true;
            success = ok;
            restoreError = reason;
        });
        Assert.IsTrue(called, "Invalid structure must complete with a failure instead of aborting the coroutine.");
        Assert.IsFalse(success);
        Assert.IsFalse(string.IsNullOrEmpty(restoreError));
        Assert.AreEqual(1f, Time.timeScale, "A rejected restore must not leave match time frozen.");
    }

    [Test]
    public void RewritingExistingSave_KeepsPreviousFileAsBackup()
    {
        PaintRect(-2, -2, 5, 5);
        var data = SaveGameService.CaptureSnapshot(out string captureError);
        Assert.IsNotNull(data, captureError);
        string path = Path.Combine(Application.temporaryCachePath, "atomic_save_backup.json");
        data.economy.gold = 111;
        Assert.IsTrue(SaveGameService.SaveToPath(data, path, out string firstError), firstError);
        data.economy.gold = 222;
        Assert.IsTrue(SaveGameService.SaveToPath(data, path, out string secondError), secondError);

        Assert.IsTrue(SaveGameService.TryReadFromPath(path, out var current, out string currentError), currentError);
        Assert.AreEqual(222, current.economy.gold, "The formal file must contain the new save.");
        Assert.IsTrue(SaveGameService.TryReadFromPath(path + ".bak", out var backup, out string backupError), backupError);
        Assert.AreEqual(111, backup.economy.gold, "The previous valid save must survive as a readable backup.");
    }

    GameObject Instantiate(string prefabPath, Vector3Int cell, string name)
    {
        var prefab = LoadPrefab<GameObject>(prefabPath);
        var go = UnityEngine.Object.Instantiate(
            prefab, Grid.CellCenter(cell) - new Vector3(0f, 0.5f, 0f), Quaternion.identity);
        go.name = name;
        var building = go.GetComponent<Building>();
        if (building != null) building.Recompute();
        return go;
    }
}
