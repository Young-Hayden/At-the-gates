using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Flow/feedback regressions: blocked factory exits, full stockpile deliveries and
/// unreachable explicit attacks must all tell the player what happened instead of
/// looking like a silent freeze.
/// </summary>
public class ProductionAndFeedbackTests : RegressionFixture
{
    const string HousePath = "Assets/Prefabs/Buildings/Knights/House_Blue.prefab";
    const string CastlePath = "Assets/Prefabs/Buildings/Knights/Castle_Blue.prefab";
    const string PawnPath = "Assets/Prefabs/Units/Pawn_Blue.prefab";
    const string WarriorPath = "Assets/Prefabs/Units/Warrior_Blue.prefab";

    [UnityTest]
    public IEnumerator FactoryWithNoSpawnCell_ReportsSpawnBlocked()
    {
        PaintRect(-10, -6, 24, 14);
        var house = Object.Instantiate(LoadPrefab<GameObject>(HousePath),
            Grid.CellCenter(new Vector3Int(0, 0)) - new Vector3(0f, 0.5f, 0f),
            Quaternion.identity);
        house.name = "Blocked factory";
        house.GetComponent<Building>().Recompute();
        var production = house.GetComponent<ProductionBuilding>();
        Assert.IsNotNull(production);
        production.products[0].buildSeconds = 0.05f;
        Assert.IsTrue(production.TryQueue(0, payCost: false));

        var blocked = new List<Vector3Int>();
        for (int x = -8; x <= 8; x++)
        for (int y = -8; y <= 8; y++)
            blocked.Add(new Vector3Int(x, y, 0));
        Grid.AddBuildingFootprint(blocked);

        yield return new WaitForSeconds(1.2f);
        Assert.IsTrue(production.SpawnBlocked,
            "A ready queue with every spawn cell occupied must report SpawnBlocked.");
    }

    [UnityTest]
    public IEnumerator FullStockpileDelivery_ShowsFullWarehouseText()
    {
        PaintRect(-8, -4, 20, 12);
        var castle = Spawn(CastlePath, new Vector3Int(0, 0), "Full drop-off");
        castle.GetComponent<Building>().Recompute();
        var worker = Spawn(PawnPath, new Vector3Int(5, 0), "Full-delivery worker");
        Economy.wood = ResourceLimits.WoodCap;
        yield return null;

        worker.GetComponent<Gatherer>().RestoreSaveState(new Gatherer.SaveState
        {
            carrying = 5,
            carryingKind = ResourceKind.Wood,
            manualIdle = false,
        });

        bool sawFull = false;
        float deadline = Time.time + 6f;
        while (!sawFull && Time.time < deadline)
        {
            foreach (var text in Object.FindObjectsByType<TextMesh>(FindObjectsSortMode.None))
                if (text != null && text.text == "仓库已满") { sawFull = true; break; }
            yield return null;
        }

        Assert.IsTrue(sawFull,
            "A delivery the capped bank cannot accept must show the full-warehouse text.");
    }

    [UnityTest]
    public IEnumerator UnreachableExplicitAttack_ShowsBlockedMarker_ButKeepsTarget()
    {
        PaintRect(-4, -4, 8, 8);
        var warrior = Spawn(WarriorPath, new Vector3Int(0, 0), "Unreachable attacker");

        var enemyGo = new GameObject("Island target");
        enemyGo.transform.position = Grid.CellCenter(new Vector3Int(30, 0));
        var enemy = enemyGo.AddComponent<UnitActor>();
        enemy.team = Team.Red;
        enemy.showHealthBar = false;
        enemy.maxHealth = 100;
        enemy.health = 100;
        enemy.isCombatant = true;

        yield return null;

        Selection.selected.Clear();
        Selection.selectedBuilding = null;
        Selection.selected.Add(warrior.GetComponent<UnitMover>());

        var order = typeof(SelectionManager).GetMethod(
            "OrderAttack", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(order);
        bool accepted = (bool)order.Invoke(Selection, new object[] { enemy });

        Assert.IsTrue(accepted);
        Assert.AreSame(enemy, warrior.GetComponent<CombatAI>().Target,
            "The forced attack target must be retained even when unreachable.");
        Assert.IsNotNull(GameObject.Find("BlockedMarker"),
            "An unreachable explicit attack must show the red-X feedback.");
    }

    GameObject Spawn(string path, Vector3Int cell, string name)
    {
        var go = Object.Instantiate(LoadPrefab<GameObject>(path),
            Grid.CellCenter(cell) - new Vector3(0f, 0.5f, 0f), Quaternion.identity);
        go.name = name;
        UnitLimits.Invalidate();
        Population.Invalidate();
        return go;
    }
}
