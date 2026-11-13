using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// B09: one AI production tick must share a single worker budget across every
/// factory. Living workers plus already-queued worker orders count against
/// maxGatherers, so a second house cannot spend a slot the first just claimed.
/// Military products are deliberately outside that budget.
/// </summary>
public class AIProductionBudgetTests : RegressionFixture
{
    const string RedHousePath = "Assets/Prefabs/Buildings/Knights/House_Red.prefab";
    const string RedBarracksPath = "Assets/Prefabs/Buildings/Knights/Barracks_Red.prefab";
    const string RedPawnPath = "Assets/Prefabs/Units/Pawn_Red.prefab";
    const string RedWarriorPath = "Assets/Prefabs/Units/Warrior_Red.prefab";
    const string RedArcherPath = "Assets/Prefabs/Units/Archer_Red.prefab";

    static readonly MethodInfo ProduceMethod = typeof(AIController).GetMethod(
        "Produce", BindingFlags.Instance | BindingFlags.NonPublic);

    [UnityTest]
    public IEnumerator Produce_TwoHouses_ShareOneWorkerBudgetPerTick()
    {
        PaintRect(-12, -8, 40, 20);
        var ai = CreateAi(maxGatherers: 2, startGold: 1000);
        yield return null;                         // let Start() deposit the opening bank
        Assert.AreEqual(1000, ai.Gold, "The AI control must start with its opening bank.");

        CreateWorker(new Vector3Int(0, 0));
        var first = CreateFactory(RedHousePath, new Vector3Int(6, 0, 0));
        var second = CreateFactory(RedHousePath, new Vector3Int(12, 0, 0));

        InvokeProduce(ai);

        Assert.AreEqual(1, Queued(first, second),
            "One living worker leaves only one slot across both houses.");
        Assert.AreEqual(960, ai.Gold,
            "Exactly one 40-gold worker may be paid for in the first tick.");

        InvokeProduce(ai);

        Assert.AreEqual(1, Queued(first, second),
            "A second tick must not exceed the living plus queued worker budget.");
        Assert.AreEqual(960, ai.Gold,
            "The budget-blocked tick must not charge the AI again.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Produce_OverCapLivingWorkers_StopNewHiresButKeepThem()
    {
        PaintRect(-12, -8, 40, 20);
        var ai = CreateAi(maxGatherers: 2, startGold: 1000);
        yield return null;

        var doomed = CreateWorker(new Vector3Int(0, 0));
        var survivor = CreateWorker(new Vector3Int(2, 0, 0));
        var house = CreateFactory(RedHousePath, new Vector3Int(6, 0, 0));

        InvokeProduce(ai);

        Assert.AreEqual(0, house.Queue.Count,
            "Two living workers already fill a budget of two.");
        Assert.AreEqual(1000, ai.Gold,
            "A budget-blocked tick must not spend gold.");
        Assert.IsTrue(doomed.GetComponent<UnitActor>().IsAlive,
            "Existing workers over the cap must not be deleted.");
        Assert.IsTrue(survivor.GetComponent<UnitActor>().IsAlive,
            "Existing workers over the cap must not be deleted.");

        Object.Destroy(doomed.gameObject);
        yield return null;                         // OnDisable drops it from UnitActor.All

        InvokeProduce(ai);

        Assert.AreEqual(1, house.Queue.Count,
            "Removing one living worker must release one production slot.");
        Assert.AreEqual(960, ai.Gold,
            "The released slot should cost exactly one worker.");
    }

    [UnityTest]
    public IEnumerator Produce_ZeroWorkerBudget_StillQueuesMilitaryProduct()
    {
        PaintRect(-12, -8, 40, 20);
        var ai = CreateAi(maxGatherers: 0, startGold: 1000);
        yield return null;

        var barracks = CreateFactory(RedBarracksPath, new Vector3Int(6, 0, 0));

        InvokeProduce(ai);

        Assert.AreEqual(1, barracks.Queue.Count,
            "A zero worker budget must not block affordable military production.");
        Assert.AreEqual(920, ai.Gold,
            "The cheapest barracks product (80-gold warrior) should be paid for once.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Produce_ZeroWorkerBudget_MixedFactory_SkipsWorkerAndQueuesMilitary()
    {
        PaintRect(-12, -8, 40, 20);
        var ai = CreateAi(maxGatherers: 0, startGold: 1000);
        yield return null;

        var factory = CreateMixedFactory(new Vector3Int(6, 0, 0));

        InvokeProduce(ai);

        Assert.AreEqual(1, factory.Queue.Count,
            "A budget-blocked cheap worker must not skip an affordable military product.");
        CollectionAssert.AreEqual(new[] { 1 }, factory.Queue,
            "Only the 80-gold warrior, not the 40-gold worker, may be queued.");
        Assert.AreEqual(920, ai.Gold,
            "The mixed factory must charge exactly one military product.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Produce_LivingNonWorker_DoesNotConsumeWorkerBudget()
    {
        PaintRect(-12, -8, 40, 20);
        var ai = CreateAi(maxGatherers: 1, startGold: 1000);
        yield return null;

        var archerPrefab = LoadPrefab<GameObject>(RedArcherPath);
        var archer = Object.Instantiate(
            archerPrefab, Grid.CellCenter(new Vector3Int(0, 0)), Quaternion.identity);
        archer.GetComponent<UnitActor>().team = Team.Red;
        var house = CreateFactory(RedHousePath, new Vector3Int(6, 0, 0));

        InvokeProduce(ai);

        Assert.AreEqual(1, house.Queue.Count,
            "A living same-team archer is military and must leave the worker slot available.");
        Assert.AreEqual(960, ai.Gold,
            "The available worker slot should pay for exactly one 40-gold worker.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Produce_QueuedMilitary_DoesNotConsumeWorkerBudget()
    {
        PaintRect(-12, -8, 40, 20);
        var ai = CreateAi(maxGatherers: 2, startGold: 1000);
        yield return null;

        var barracks = CreateFactory(RedBarracksPath, new Vector3Int(6, 0, 0));
        var house = CreateFactory(RedHousePath, new Vector3Int(12, 0, 0));

        InvokeProduce(ai);

        Assert.AreEqual(1, house.Queue.Count,
            "The first of two worker slots should be used once.");
        Assert.AreEqual(1, barracks.Queue.Count,
            "Military production is independent of the worker budget.");
        Assert.AreEqual(880, ai.Gold,
            "One 40-gold worker and one 80-gold warrior should be paid for.");

        InvokeProduce(ai);

        Assert.AreEqual(2, house.Queue.Count,
            "The queued military order must leave a second worker slot for the next tick.");
        Assert.AreEqual(2, barracks.Queue.Count,
            "Military production keeps its own tempo.");
        Assert.AreEqual(760, ai.Gold,
            "The second tick must pay for one more worker and one more warrior.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Produce_WarriorCap_FallsThroughToLancerInsteadOfStalling()
    {
        PaintRect(-12, -8, 40, 20);
        var ai = CreateAi(maxGatherers: 0, startGold: 1000, startWood: 100);
        yield return null;

        for (int i = 0; i < UnitLimits.WarriorCap; i++)
            CreateUnit(RedWarriorPath, new Vector3Int(i, 0, 0));
        UnitLimits.Invalidate();

        // B1: eight warriors are 16 population, above the free base of 12. Two
        // completed Red houses raise the capacity to 24 and leave real headroom for
        // the lancer, without making this test about the population boundary.
        // CreateFactory never starts construction, so both houses count at once;
        // the CanQueue assertion below proves the capacity actually applies.
        CreateFactory(RedHousePath, new Vector3Int(0, 4, 0));
        CreateFactory(RedHousePath, new Vector3Int(8, 4, 0));
        Population.Invalidate();

        var barracks = CreateFactory(RedBarracksPath, new Vector3Int(12, 0, 0));
        Assert.GreaterOrEqual(barracks.products.Length, 2,
            "The barracks control requires warrior and lancer products.");
        Assert.AreEqual(UnitRole.Lancer, UnitLimits.RoleOf(barracks.products[1].unitPrefab),
            "The second barracks product must be the lancer for this control.");
        Assert.IsFalse(UnitLimits.CanProduce(Team.Red,
            LoadPrefab<GameObject>(RedWarriorPath), out _),
            "The living warrior roster is already at its cap.");
        Assert.IsTrue(UnitLimits.CanProduce(Team.Red,
            barracks.products[1].unitPrefab, out _),
            "The lancer must still be legal.");
        Assert.AreEqual(1000, ai.Gold, "The control requires the AI's opening bank.");
        Assert.AreEqual(100, ai.Wood, "The lancer also costs wood, so the AI needs enough.");
        Assert.IsTrue(barracks.CanQueue(1, false, out string lancerRefusal),
            "The factory must accept the lancer order: " + lancerRefusal);

        InvokeProduce(ai);

        CollectionAssert.AreEqual(new[] { 1 }, barracks.Queue,
            "A capped warrior must not starve the barracks; the AI must queue the lancer."
            + " Last refusal: " + (barracks.LastRefusalReason ?? "<none>")
            + "; AI gold: " + ai.Gold);
        Assert.AreEqual(1000 - barracks.products[1].cost.gold, ai.Gold,
            "Only the queued lancer may be charged; a refusal must not spend.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator CancelLast_RedFactoryOrder_RefundsAiInsteadOfPlayer()
    {
        PaintRect(-8, -6, 20, 12);
        var ai = CreateAi(maxGatherers: 0, startGold: 1000);
        yield return null; // AIController.Start initialises its private bank
        var factory = CreateMixedFactory(new Vector3Int(6, 0, 0));
        int playerGold = Economy.gold;
        var spend = typeof(AIController).GetMethod("TrySpend",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(spend);
        Assert.IsTrue((bool)spend.Invoke(ai, new object[] { new ResourceCost(80) }),
            "The AI order must be paid from the AI bank before cancellation.");
        Assert.IsTrue(factory.TryQueue(1, payCost: false));
        Assert.AreEqual(920, ai.Gold);

        factory.CancelLast();

        Assert.AreEqual(1000, ai.Gold, "Cancelling the AI order must refund its own bank.");
        Assert.AreEqual(playerGold, Economy.gold, "No part of an AI cancellation may be refunded to the player.");
        Assert.AreEqual(0, factory.Queue.Count);
    }

    // ---------------------------------------------------------------- helpers

    AIController CreateAi(int maxGatherers, int startGold, int startWood = 0)
    {
        var go = new GameObject("AI production budget");
        go.transform.position = Grid.CellCenter(new Vector3Int(0, 4, 0));
        var ai = go.AddComponent<AIController>();
        ai.team = Team.Red;
        ai.active = false;                         // no ambient build/produce/attack ticks
        ai.maxGatherers = maxGatherers;
        ai.startGold = startGold;
        ai.startWood = startWood;
        ai.startMeat = 0;
        ai.buildOrder = new GameObject[0];
        return ai;
    }

    UnitActor CreateWorker(Vector3Int cell)
    {
        var prefab = LoadPrefab<GameObject>(RedPawnPath);
        var go = Object.Instantiate(prefab, Grid.CellCenter(cell), Quaternion.identity);
        var actor = go.GetComponent<UnitActor>();
        Assert.IsNotNull(actor, RedPawnPath + " must carry a UnitActor.");
        actor.team = Team.Red;
        var gatherer = go.GetComponent<Gatherer>();
        Assert.IsNotNull(gatherer, RedPawnPath + " must carry a Gatherer.");
        gatherer.autoStart = false;
        return actor;
    }

    UnitActor CreateUnit(string path, Vector3Int cell)
    {
        var prefab = LoadPrefab<GameObject>(path);
        var go = Object.Instantiate(prefab, Grid.CellCenter(cell), Quaternion.identity);
        var actor = go.GetComponent<UnitActor>();
        Assert.IsNotNull(actor, path + " must carry a UnitActor.");
        actor.team = Team.Red;
        actor.showHealthBar = false;
        return actor;
    }

    ProductionBuilding CreateFactory(string path, Vector3Int cell)
    {
        var prefab = LoadPrefab<GameObject>(path);
        var go = Object.Instantiate(prefab, Grid.CellCenter(cell), Quaternion.identity);
        var building = go.GetComponent<Building>();
        Assert.IsNotNull(building, path + " must carry a Building.");
        building.team = Team.Red;
        building.Recompute();

        var factory = go.GetComponent<ProductionBuilding>();
        Assert.IsNotNull(factory, path + " must carry a ProductionBuilding.");
        return factory;
    }

    ProductionBuilding CreateMixedFactory(Vector3Int cell)
    {
        var go = new GameObject("Mixed worker and military factory");
        go.transform.position = Grid.CellCenter(cell);
        var building = go.AddComponent<Building>();
        building.team = Team.Red;
        building.footprint = new Vector2Int(2, 1);
        building.Recompute();

        var factory = go.AddComponent<ProductionBuilding>();
        factory.products = new[]
        {
            new ProductionBuilding.Product
            {
                label = "Worker",
                unitPrefab = LoadPrefab<GameObject>(RedPawnPath),
                cost = new ResourceCost(40),
                buildSeconds = 60f,
            },
            new ProductionBuilding.Product
            {
                label = "Warrior",
                unitPrefab = LoadPrefab<GameObject>(RedWarriorPath),
                cost = new ResourceCost(80),
                buildSeconds = 60f,
            },
        };
        return factory;
    }

    static void InvokeProduce(AIController ai)
    {
        Assert.IsNotNull(ProduceMethod, "AIController.Produce must remain available.");
        ProduceMethod.Invoke(ai, null);
    }

    static int Queued(params ProductionBuilding[] factories)
    {
        return factories.Sum(factory => factory.Queue.Count);
    }
}
