using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// C3 regression: the AI must put a free archer into a finished empty tower instead of
/// leaving the tower as a pure obstacle. The check is driven through the same private
/// tick the controller uses.
/// </summary>
public class AITowerGarrisonTests : RegressionFixture
{
    const string TowerPath = "Assets/Prefabs/Buildings/Knights/Tower_Red.prefab";
    const string ArcherPath = "Assets/Prefabs/Units/Archer_Red.prefab";

    [UnityTest]
    public IEnumerator Ai_GarrisonsFinishedEmptyTower_WithAFreeArcher()
    {
        PaintRect(-10, -4, 24, 8);
        var towerGo = Object.Instantiate(LoadPrefab<GameObject>(TowerPath),
            Grid.CellCenter(new Vector3Int(0, 0)) - new Vector3(0f, 0.5f, 0f),
            Quaternion.identity);
        towerGo.name = "AI empty tower";
        towerGo.GetComponent<Building>().Recompute();

        var archerGo = Object.Instantiate(LoadPrefab<GameObject>(ArcherPath),
            Grid.CellCenter(new Vector3Int(-4, 0)), Quaternion.identity);
        archerGo.name = "AI free archer";

        var ai = new GameObject("AI garrison probe").AddComponent<AIController>();
        ai.team = Team.Red;
        ai.active = false;
        yield return null;

        var tower = towerGo.GetComponent<TowerGarrison>();
        Assert.IsNotNull(tower);
        Assert.IsFalse(tower.HasArcher, "The control tower must start empty.");

        var tick = typeof(AIController).GetMethod(
            "GarrisonTowers", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(tick, "AIController.GarrisonTowers must remain the AI garrison tick.");
        tick.Invoke(ai, null);

        float deadline = Time.time + 5f;
        while (!tower.HasArcher && Time.time < deadline) yield return null;

        Assert.IsTrue(tower.HasArcher, "A finished empty tower must be manned by a free AI archer.");
        Assert.AreSame(archerGo.GetComponent<UnitActor>(), tower.Archer);
        Assert.IsFalse(archerGo.GetComponent<UnitActor>().enabled,
            "The garrisoned archer must be out of play like a player-garrisoned one.");
    }
}
