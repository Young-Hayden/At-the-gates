using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Phase A stockpile regressions: every bank addition is clamped at the per-kind
/// cap, automatic gathering (player and AI) skips a full kind, a completed delivery
/// of a now-full kind drops the job and sends the worker elsewhere, and neither the
/// save round trip nor a direct restore can raise a bank above its cap.
/// </summary>
public class ResourceLimitTests : RegressionFixture
{
    // ------------------------------------------------------------ pure limits

    [Test]
    public void ResourceLimits_ExposeThePhaseACapsAndClampHelpers()
    {
        Assert.AreEqual(2000, ResourceLimits.GoldCap);
        Assert.AreEqual(1200, ResourceLimits.WoodCap);
        Assert.AreEqual(1000, ResourceLimits.MeatCap);

        Assert.AreEqual(ResourceLimits.GoldCap, ResourceLimits.Cap(ResourceKind.Gold));
        Assert.AreEqual(ResourceLimits.WoodCap, ResourceLimits.Cap(ResourceKind.Wood));
        Assert.AreEqual(ResourceLimits.MeatCap, ResourceLimits.Cap(ResourceKind.Meat));

        Assert.AreEqual(RefCap(ResourceKind.Gold), ResourceLimits.ClampAdd(1990, 50, ResourceKind.Gold),
            "A clamped addition must stop at gold's cap.");
        Assert.AreEqual(0, ResourceLimits.ClampAdd(0, -50, ResourceKind.Wood),
            "A bank must never go below zero.");
        Assert.IsTrue(ResourceLimits.IsFull(ResourceLimits.MeatCap, ResourceKind.Meat));
        Assert.IsFalse(ResourceLimits.IsFull(ResourceLimits.MeatCap - 1, ResourceKind.Meat));
        Assert.AreEqual(ResourceLimits.MeatCap, ResourceLimits.Clamp(ResourceLimits.MeatCap + 1, ResourceKind.Meat));
        Assert.AreEqual(0, ResourceLimits.Clamp(-1, ResourceKind.Gold));
    }

    static int RefCap(ResourceKind kind) => ResourceLimits.Cap(kind);

    // ------------------------------------------------------------- bank clamps

    [Test]
    public void Add_ClampsAtEveryStockpileCap()
    {
        Economy.gold = ResourceLimits.GoldCap - 5;
        Economy.Add(ResourceKind.Gold, 50);
        Assert.AreEqual(ResourceLimits.GoldCap, Economy.gold,
            "Gold overflow must be discarded at the cap.");

        Economy.wood = ResourceLimits.WoodCap - 5;
        Economy.Add(ResourceKind.Wood, 50);
        Assert.AreEqual(ResourceLimits.WoodCap, Economy.wood,
            "Wood overflow must be discarded at the cap.");

        Economy.meat = ResourceLimits.MeatCap - 5;
        Economy.Add(ResourceKind.Meat, 50);
        Assert.AreEqual(ResourceLimits.MeatCap, Economy.meat,
            "Meat overflow must be discarded at the cap.");
    }

    [Test]
    public void Refund_ClampsEveryKind_AndAddNeverGoesNegative()
    {
        Economy.gold = ResourceLimits.GoldCap - 10;
        Economy.wood = ResourceLimits.WoodCap - 10;
        Economy.meat = ResourceLimits.MeatCap - 10;

        Economy.Refund(new ResourceCost(1000, 1000, 1000));

        Assert.AreEqual(ResourceLimits.GoldCap, Economy.gold);
        Assert.AreEqual(ResourceLimits.WoodCap, Economy.wood);
        Assert.AreEqual(ResourceLimits.MeatCap, Economy.meat);

        Economy.gold = 0;
        Economy.Add(ResourceKind.Gold, -100);
        Assert.AreEqual(0, Economy.gold, "A negative addition must not create a negative bank.");
    }

    [UnityTest]
    public IEnumerator PlayerAndAiBanks_ClampIndependently()
    {
        Economy.gold = 0;
        Economy.wood = 0;
        Economy.meat = 0;

        var ai = CreateAi(Team.Red, active: false);
        yield return null;                                // Start seeds the AI's own bank

        ai.Add(ResourceKind.Gold, -10000);                // empty control bank
        ai.Add(ResourceKind.Gold, 100);
        Assert.AreEqual(100, ai.Get(ResourceKind.Gold), "The AI control bank should hold 100 gold.");

        Economy.Add(ResourceKind.Gold, ResourceLimits.GoldCap * 3);
        Assert.AreEqual(ResourceLimits.GoldCap, Economy.gold,
            "The player's gold must clamp at its cap.");
        Assert.AreEqual(100, ai.Get(ResourceKind.Gold),
            "The player's cap must not touch the AI's independent bank.");

        ai.Add(ResourceKind.Gold, ResourceLimits.GoldCap * 3);
        Assert.AreEqual(ResourceLimits.GoldCap, ai.Get(ResourceKind.Gold),
            "The AI's gold must clamp at the same cap.");
        Assert.IsTrue(ai.IsFull(ResourceKind.Gold));
        Assert.IsTrue(Gatherer.IsKindFull(Team.Blue, ResourceKind.Gold),
            "The player bank must still be full when the AI bank is checked.");

        ai.Add(ResourceKind.Wood, ResourceLimits.WoodCap * 3);
        ai.Add(ResourceKind.Meat, ResourceLimits.MeatCap * 3);
        Assert.AreEqual(ResourceLimits.WoodCap, ai.Get(ResourceKind.Wood));
        Assert.AreEqual(ResourceLimits.MeatCap, ai.Get(ResourceKind.Meat));
    }

    // ---------------------------------------------------- automatic gathering

    [UnityTest]
    public IEnumerator AutoAssign_SkipsFullKind_AndUsesUnfullKind()
    {
        PaintRect(-4, -4, 16, 12);
        Economy.gold = ResourceLimits.GoldCap;
        Economy.wood = 0;

        var worker = CreateWorker("Full-kind worker", new Vector3Int(0, 0, 0), Team.Blue, autoStart: true);
        var gold = CreateNode("Full gold", new Vector3Int(1, 0, 0), ResourceKind.Gold);
        var wood = CreateNode("Open wood", new Vector3Int(0, 3, 0), ResourceKind.Wood);
        yield return null;
        yield return null;

        Assert.AreSame(wood, worker.TargetNode,
            "Automatic work must skip the full gold mine and pick the open wood.");
        Assert.AreNotSame(gold, worker.TargetNode);
    }

    [UnityTest]
    public IEnumerator PostDelivery_FullKind_ClearsJobAndSwitchesKind()
    {
        PaintRect(-6, -6, 24, 18);
        Economy.gold = 0;
        Economy.wood = 0;
        CreateDropoff(new Vector3Int(4, 0, 0), Team.Blue);

        var worker = CreateWorker("Diversion worker", new Vector3Int(0, 0, 0), Team.Blue, autoStart: true);
        worker.GetComponent<UnitMover>().speed = 20f;
        worker.carryCapacity = 20;

        var gold = CreateNode("Diversion gold", new Vector3Int(1, 0, 0), ResourceKind.Gold);
        var wood = CreateNode("Diversion wood", new Vector3Int(0, 4, 0), ResourceKind.Wood);
        // Keep the mine alive for the whole switch: if it depleted, the worker would
        // leave for the ordinary "node ran dry" reason instead of the full-kind rule.
        gold.amount = gold.maxAmount = 100000;
        wood.amount = wood.maxAmount = 100000;

        yield return WaitUntil(() => worker.Carrying > 0, 3f);
        Assert.Greater(worker.Carrying, 0, "The control worker should harvest gold first.");
        Assert.AreSame(gold, worker.TargetNode, "The nearest (gold) node should be the automatic target.");

        // The load is already in hand; the bank becomes full before it is delivered.
        Economy.gold = ResourceLimits.GoldCap;

        yield return WaitUntil(() => worker.TargetNode == wood, 6f);

        Assert.AreEqual(ResourceLimits.GoldCap, Economy.gold,
            "The delivered overflow must be discarded at the cap.");
        Assert.IsFalse(gold.Depleted,
            "The gold mine must still have ore when the worker is diverted, so depletion cannot explain the switch.");
        Assert.AreSame(wood, worker.TargetNode,
            "After delivering a now-full kind an automatic worker must switch to an unfull kind.");
        Assert.AreNotSame(gold, worker.TargetNode);
    }

    [UnityTest]
    public IEnumerator ManualAssignment_ToFullKind_KeepsJob_AndDeliveryIsTruncated()
    {
        PaintRect(-6, -6, 20, 14);
        Economy.gold = ResourceLimits.GoldCap;
        CreateDropoff(new Vector3Int(4, 0, 0), Team.Blue);

        var worker = CreateWorker("Manual full worker", new Vector3Int(0, 0, 0), Team.Blue, autoStart: false);
        worker.GetComponent<UnitMover>().speed = 20f;
        worker.carryCapacity = 20;
        var gold = CreateNode("Manual full gold", new Vector3Int(1, 0, 0), ResourceKind.Gold);
        gold.amount = gold.maxAmount = 100000;

        worker.Assign(gold);
        yield return WaitUntil(() => worker.Carrying > 0, 3f);
        Assert.Greater(worker.Carrying, 0, "A manual order may still harvest a full kind.");

        yield return WaitUntil(() => worker.Carrying == 0, 6f);

        Assert.AreEqual(ResourceLimits.GoldCap, Economy.gold,
            "A manual delivery into a full bank must be truncated, not overflow.");
        Assert.AreSame(gold, worker.TargetNode,
            "A manually assigned full-kind job must not be dropped automatically.");
    }

    // ------------------------------------------------------------- AI dispatch

    [UnityTest]
    public IEnumerator AiDispatch_SkipsFullKind_AndUsesUnfullKind()
    {
        PaintRect(-4, -4, 24, 18);
        CreateDropoff(new Vector3Int(2, 0, 0), Team.Red);

        var ai = CreateAi(Team.Red, active: false);
        yield return null;                                  // Start seeds the AI bank
        ai.Add(ResourceKind.Gold, ResourceLimits.GoldCap * 3);
        Assert.IsTrue(ai.IsFull(ResourceKind.Gold), "The AI control bank should be full of gold.");

        var worker = CreateWorker("AI worker", new Vector3Int(0, 0, 0), Team.Red, autoStart: false);
        worker.GetComponent<UnitMover>().speed = 20f;
        var gold = CreateNode("AI full gold", new Vector3Int(1, 0, 0), ResourceKind.Gold);
        var wood = CreateNode("AI open wood", new Vector3Int(0, 4, 0), ResourceKind.Wood);

        ai.active = true;
        yield return WaitUntil(() => worker.TargetNode != null, 5f);

        Assert.AreSame(wood, worker.TargetNode,
            "The AI must dispatch its idle worker to an unfull kind.");
        Assert.AreNotSame(gold, worker.TargetNode);
    }

    // ---------------------------------------------------------------- save/load

    [UnityTest]
    public IEnumerator SaveLoad_OverCapBanks_AreClamped()
    {
        Economy.gold = 100;
        Economy.wood = 100;
        Economy.meat = 100;

        var ai = CreateAi(Team.Red, active: false);
        yield return null;
        ai.RestoreSaveState(new AIController.SaveState
        {
            team = Team.Red,
            active = false,
            gold = ResourceLimits.GoldCap + 700,
            wood = ResourceLimits.WoodCap + 700,
            meat = ResourceLimits.MeatCap + 700,
        });
        Assert.AreEqual(ResourceLimits.GoldCap, ai.Get(ResourceKind.Gold),
            "A direct AI restore must clamp gold to the cap.");
        Assert.AreEqual(ResourceLimits.WoodCap, ai.Get(ResourceKind.Wood),
            "A direct AI restore must clamp wood to the cap.");
        Assert.AreEqual(ResourceLimits.MeatCap, ai.Get(ResourceKind.Meat),
            "A direct AI restore must clamp meat to the cap.");

        var snapshot = SaveGameService.CaptureSnapshot(out string captureError);
        Assert.IsNotNull(snapshot, "Capture failed: " + captureError);
        Assert.AreEqual(1, snapshot.ai.Count, "The control AI should be captured.");

        // Simulate an edited/legacy save whose banks exceed the Phase A caps.
        snapshot.economy.gold = ResourceLimits.GoldCap + 999;
        snapshot.economy.wood = ResourceLimits.WoodCap + 999;
        snapshot.economy.meat = ResourceLimits.MeatCap + 999;
        snapshot.ai[0].gold = ResourceLimits.GoldCap + 999;
        snapshot.ai[0].wood = ResourceLimits.WoodCap + 999;
        snapshot.ai[0].meat = ResourceLimits.MeatCap + 999;

        string path = Path.Combine(Application.temporaryCachePath, "resource_limit_caps.json");
        Assert.IsTrue(SaveGameService.SaveToPath(snapshot, path, out string writeError),
            "SaveToPath failed: " + writeError);
        Assert.IsTrue(SaveGameService.TryReadFromPath(path, out var loaded, out string readError),
            "TryReadFromPath failed: " + readError);

        Economy.gold = 0;
        Economy.wood = 0;
        Economy.meat = 0;

        bool restored = false;
        string restoreError = null;
        yield return SaveGameService.RestoreSnapshot(loaded, (ok, error) =>
        {
            restored = ok;
            restoreError = error;
        });
        Assert.IsTrue(restored, "Restore failed: " + restoreError);

        Assert.AreEqual(ResourceLimits.GoldCap, Economy.gold, "A loaded player gold bank must clamp.");
        Assert.AreEqual(ResourceLimits.WoodCap, Economy.wood, "A loaded player wood bank must clamp.");
        Assert.AreEqual(ResourceLimits.MeatCap, Economy.meat, "A loaded player meat bank must clamp.");
        Assert.AreEqual(ResourceLimits.GoldCap, ai.Get(ResourceKind.Gold), "A loaded AI gold bank must clamp.");
        Assert.AreEqual(ResourceLimits.WoodCap, ai.Get(ResourceKind.Wood), "A loaded AI wood bank must clamp.");
        Assert.AreEqual(ResourceLimits.MeatCap, ai.Get(ResourceKind.Meat), "A loaded AI meat bank must clamp.");
    }

    // ---------------------------------------------------------------- helpers

    AIController CreateAi(Team team, bool active)
    {
        var go = new GameObject("Test AI " + team);
        var ai = go.AddComponent<AIController>();
        ai.team = team;
        ai.active = active;
        ai.homeCell = new Vector2Int(0, 0);
        ai.maxGatherers = 4;
        ai.gatherCheckSeconds = 0.05f;
        return ai;
    }

    Gatherer CreateWorker(string name, Vector3Int cell, Team team, bool autoStart)
    {
        var go = new GameObject(name);
        go.transform.position = Grid.CellCenter(cell);
        var actor = go.AddComponent<UnitActor>();
        actor.team = team;
        actor.showHealthBar = false;
        actor.maxHealth = 70;
        actor.health = 70;

        var gatherer = go.AddComponent<Gatherer>();
        gatherer.autoStart = autoStart;
        gatherer.autoStartRange = 50f;
        gatherer.retaskSeconds = 0.1f;
        return gatherer;
    }

    static ResourceNode CreateNode(string name, Vector3Int cell, ResourceKind kind)
    {
        var go = new GameObject(name);
        go.transform.position = MapGrid.Instance.CellCenter(cell);
        var node = go.AddComponent<ResourceNode>();
        node.kind = kind;
        node.amount = 100;
        node.maxAmount = 100;
        node.perTrip = 5;
        node.harvestSeconds = 0.05f;
        return node;
    }

    static Building CreateDropoff(Vector3Int cell, Team team)
    {
        var go = new GameObject("Test drop-off " + team);
        go.transform.position = MapGrid.Instance.CellCenter(cell) - new Vector3(0f, 0.5f, 0f);
        go.AddComponent<BoxCollider2D>();
        var building = go.AddComponent<Building>();
        building.footprint = Vector2Int.one;
        building.depth = 1;
        building.team = team;
        building.isDropoff = true;
        building.constructionSeconds = 0f;
        building.Recompute();
        return building;
    }

    static IEnumerator WaitUntil(Func<bool> condition, float timeout)
    {
        float deadline = Time.time + timeout;
        while (!condition() && Time.time < deadline)
            yield return null;
    }
}
