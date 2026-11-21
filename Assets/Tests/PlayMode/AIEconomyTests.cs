using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// AI economy flow: a starving side must pull one worker onto meat, and the completed
/// build order must replace destroyed/repeated houses and production buildings.
/// </summary>
public class AIEconomyTests : RegressionFixture
{
    const string CastlePath = "Assets/Prefabs/Buildings/Knights/Castle_Red.prefab";
    const string HousePath = "Assets/Prefabs/Buildings/Knights/House_Red.prefab";
    const string PawnPath = "Assets/Prefabs/Units/Pawn_Red.prefab";

    [UnityTest]
    public IEnumerator StarvingAi_PullsOneWorkerOntoMeat()
    {
        PaintRect(-8, -4, 24, 12);
        var castle = SpawnBuilding(CastlePath, new Vector3Int(0, 0), "AI drop-off");
        castle.GetComponent<Building>().Recompute();

        var ai = new GameObject("AI meat probe").AddComponent<AIController>();
        ai.team = Team.Red;
        ai.active = false;
        ai.homeCell = Vector2Int.zero;
        ai.maxGatherers = 5;
        yield return null;
        ai.Add(ResourceKind.Meat, -ai.Meat);

        var worker = Object.Instantiate(LoadPrefab<GameObject>(PawnPath),
            Grid.CellCenter(new Vector3Int(2, 0)), Quaternion.identity);
        worker.name = "AI red worker";
        var gatherer = worker.GetComponent<Gatherer>();
        var wood = SpawnNode("AI wood", new Vector3Int(4, 0), ResourceKind.Wood);
        var meat = SpawnNode("AI meat", new Vector3Int(5, 0), ResourceKind.Meat);
        gatherer.Assign(wood);
        Assert.AreSame(wood, gatherer.TargetNode);

        var upkeep = new GameObject("Red upkeep warrior").AddComponent<UnitActor>();
        upkeep.team = Team.Red;
        upkeep.isCombatant = true;
        upkeep.showHealthBar = false;
        upkeep.maxHealth = 100;
        upkeep.health = 100;
        Upkeep.Reset();
        Upkeep.Tick(Team.Red, Upkeep.PeriodSeconds + 0.1f);
        Assert.IsTrue(Upkeep.IsStarving(Team.Red),
            "Control: the drained AI bank plus one combatant must starve.");

        var assign = typeof(AIController).GetMethod(
            "AssignGatherers", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(assign);
        assign.Invoke(ai, null);

        Assert.AreSame(meat, gatherer.TargetNode,
            "A starving AI must pull a working wood/gold gatherer onto meat.");
    }

    [UnityTest]
    public IEnumerator CompletedBuildOrder_RebuildsMissingHouse()
    {
        PaintRect(-10, -6, 24, 14);
        var ai = new GameObject("AI rebuild probe").AddComponent<AIController>();
        ai.team = Team.Red;
        ai.active = false;
        ai.homeCell = Vector2Int.zero;
        yield return null;

        var housePrefab = LoadPrefab<GameObject>(HousePath);
        ai.buildOrder = new[] { housePrefab, housePrefab };
        var nextBuild = typeof(AIController).GetField(
            "nextBuild", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(nextBuild);
        nextBuild.SetValue(ai, 2);

        var existing = SpawnBuilding(HousePath, new Vector3Int(0, 0), "Existing red house");
        existing.GetComponent<Building>().Recompute();
        yield return null;

        var build = typeof(AIController).GetMethod(
            "Build", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(build);
        build.Invoke(ai, null);
        yield return null;

        var identity = housePrefab.GetComponent<SaveIdentity>();
        string id = identity != null ? identity.PrefabId : null;
        int houses = Building.All.Count(b =>
        {
            if (b == null || !b.IsAlive || b.team != Team.Red) return false;
            var bi = b.GetComponent<SaveIdentity>();
            return id != null ? bi != null && bi.PrefabId == id : b.name.StartsWith(housePrefab.name);
        });

        Assert.AreEqual(2, houses,
            "A completed AI build order with two houses must rebuild the missing one.");
    }

    GameObject SpawnBuilding(string path, Vector3Int cell, string name)
    {
        var go = Object.Instantiate(LoadPrefab<GameObject>(path),
            Grid.CellCenter(cell) - new Vector3(0f, 0.5f, 0f), Quaternion.identity);
        go.name = name;
        return go;
    }

    ResourceNode SpawnNode(string name, Vector3Int cell, ResourceKind kind)
    {
        var go = new GameObject(name);
        go.transform.position = Grid.CellCenter(cell);
        var node = go.AddComponent<ResourceNode>();
        node.kind = kind;
        node.amount = 100;
        node.maxAmount = 100;
        node.perTrip = 20;
        return node;
    }
}
