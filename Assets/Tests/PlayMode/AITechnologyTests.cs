using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Phase B Task 6: the AI researches the same three lines the player does, in a fixed
/// order (Forge -> Armour -> Gathering), one at a time, at a completed command centre,
/// paid up front from its own bank and only while it is not starving.
///
/// The AI must reuse <see cref="Technology.BeginResearch(Team, Technology.Tech,
/// System.Func{ResourceCost, bool}, out string)"/> — the bank-agnostic overload Task 3
/// left open on purpose — so this file proves the seam is really being used: the AI's
/// ledger moves, the player's <see cref="Economy"/> never does, and the per-team mask
/// advances in exactly the order the rules fix.
///
/// The AI is created with <see cref="AIController.active"/> false and the private
/// <c>Research</c> tick is driven directly, so an ambient timer cannot act between
/// assertions.
/// </summary>
public class AITechnologyTests : RegressionFixture
{
    const string RedCastlePath = "Assets/Prefabs/Buildings/Knights/Castle_Red.prefab";
    const string BlueCastlePath = "Assets/Prefabs/Buildings/Knights/Castle_Blue.prefab";
    const string RedWarriorPath = "Assets/Prefabs/Units/Warrior_Red.prefab";
    const string RedArcherPath = "Assets/Prefabs/Units/Archer_Red.prefab";

    static readonly MethodInfo ResearchMethod = typeof(AIController).GetMethod(
        "Research", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo ProduceMethod = typeof(AIController).GetMethod(
        "Produce", BindingFlags.Instance | BindingFlags.NonPublic);

    // ------------------------------------------------------------ starts, pays

    [UnityTest]
    public IEnumerator Research_StartsForgeFirst_AndPaysOnceFromTheAisOwnBank()
    {
        PaintRect(-20, -10, 40, 20);
        Technology.Reset();
        Upkeep.Reset();
        Economy.gold = 1000;
        Economy.wood = 500;
        var ai = CreateAi(startGold: 1000, startWood: 500);
        yield return null;                    // Start seeds the AI bank
        CreateCastle(RedCastlePath, new Vector3Int(6, 0, 0));
        yield return null;

        Assert.IsTrue(Building.HasCompletedCommandCentre(Team.Red),
            "Control: the AI must own a completed command centre.");
        Assert.AreEqual(1000, ai.Gold, "Control: the AI starts with its own 1000 gold.");
        Assert.AreEqual(500, ai.Wood, "Control: the AI starts with its own 500 wood.");

        InvokeResearch(ai);

        Assert.AreEqual(Technology.Tech.Forge, Technology.ActiveTechOf(Team.Red),
            "The AI researches Forge first.");
        Assert.IsFalse(Technology.Has(Team.Red, Technology.Tech.Forge),
            "A started line is not a finished technology.");
        Assert.AreEqual(750, ai.Gold, "Forge costs 250 gold up front.");
        Assert.AreEqual(400, ai.Wood, "Forge costs 100 wood up front.");

        Assert.AreEqual(1000, Economy.gold,
            "The AI must pay from its own ledger, never the player's Economy.");
        Assert.AreEqual(500, Economy.wood,
            "The AI must pay from its own ledger, never the player's Economy.");

        // A second tick while the line runs must not charge a second time.
        InvokeResearch(ai);
        Assert.AreEqual(750, ai.Gold, "The AI must pay for one research exactly once.");
        Assert.AreEqual(400, ai.Wood, "The AI must pay for one research exactly once.");
        Assert.AreEqual(Technology.Tech.Forge, Technology.ActiveTechOf(Team.Red),
            "The running line must be the one it started with.");
    }

    // ------------------------------------------------------------- refusals

    [UnityTest]
    public IEnumerator Research_WithoutACompletedCommandCentre_StartsNothing()
    {
        PaintRect(-20, -10, 40, 20);
        Technology.Reset();
        Upkeep.Reset();
        var ai = CreateAi(startGold: 1000, startWood: 500);
        yield return null;

        // An enemy command centre is not the AI's own and must not unlock its research.
        CreateCastle(BlueCastlePath, new Vector3Int(-6, 0, 0));
        yield return null;
        Assert.IsFalse(Building.HasCompletedCommandCentre(Team.Red),
            "Control: the only command centre belongs to the other side.");

        InvokeResearch(ai);

        Assert.IsFalse(Technology.ActiveTechOf(Team.Red).HasValue,
            "Research without the AI's own completed command centre must not start.");
        Assert.AreEqual(1000, ai.Gold, "A refused research must not spend gold.");
        Assert.AreEqual(500, ai.Wood, "A refused research must not spend wood.");
    }

    [UnityTest]
    public IEnumerator Research_WhileALineIsRunning_DoesNotRestartOrChargeAgain()
    {
        PaintRect(-20, -10, 40, 20);
        Technology.Reset();
        Upkeep.Reset();
        var ai = CreateAi(startGold: 1000, startWood: 500);
        yield return null;
        CreateCastle(RedCastlePath, new Vector3Int(6, 0, 0));
        yield return null;

        InvokeResearch(ai);
        Assert.AreEqual(Technology.Tech.Forge, Technology.ActiveTechOf(Team.Red));
        Technology.Tick(10f);
        float remaining = Technology.RemainingOf(Team.Red);
        Assert.That(remaining, Is.InRange(34f, 35.5f), "Control: 35 s of the 45 s line are left.");

        InvokeResearch(ai);

        Assert.AreEqual(Technology.Tech.Forge, Technology.ActiveTechOf(Team.Red),
            "A second line must not replace the running one.");
        Assert.AreEqual(remaining, Technology.RemainingOf(Team.Red), 0.001f,
            "The running line must not be restarted by a refused second order.");
        Assert.AreEqual(750, ai.Gold, "The refused second order must not spend gold.");
        Assert.AreEqual(400, ai.Wood, "The refused second order must not spend wood.");
    }

    [UnityTest]
    public IEnumerator Research_WhenUnaffordable_LeavesTheBankAlone()
    {
        PaintRect(-20, -10, 40, 20);
        Technology.Reset();
        Upkeep.Reset();
        var ai = CreateAi(startGold: 100, startWood: 500);
        yield return null;
        CreateCastle(RedCastlePath, new Vector3Int(6, 0, 0));
        yield return null;

        InvokeResearch(ai);

        Assert.IsFalse(Technology.ActiveTechOf(Team.Red).HasValue,
            "Forge costs 250 gold; with 100 the AI must not start it.");
        Assert.AreEqual(100, ai.Gold, "An unaffordable order must not spend the little it has.");
        Assert.AreEqual(500, ai.Wood, "A refused order must not spend wood either.");

        // The other half of the same rule: enough gold but not enough wood.
        var rich = CreateAi(startGold: 1000, startWood: 50, name: "AI technology - short wood");
        yield return null;
        InvokeResearch(rich);

        Assert.IsFalse(Technology.ActiveTechOf(Team.Red).HasValue,
            "Forge costs 100 wood; with 50 the AI must not start it.");
        Assert.AreEqual(1000, rich.Gold, "A wood-short refusal must not spend gold.");
        Assert.AreEqual(50, rich.Wood, "A wood-short refusal must not spend wood.");
    }

    [UnityTest]
    public IEnumerator Research_WhileStarving_IsRefused_AndFeedingUnlocksIt()
    {
        PaintRect(-20, -10, 40, 20);
        Technology.Reset();
        Upkeep.Reset();
        var ai = CreateAi(startGold: 1000, startWood: 500, startMeat: 0);
        yield return null;
        CreateCastle(RedCastlePath, new Vector3Int(6, 0, 0));
        CreateUnit(RedWarriorPath, new Vector3Int(-6, 0, 0));
        yield return null;

        // One living combatant owes one meat; an empty bank cannot pay, so the side
        // starves exactly like the player's would.
        Upkeep.Tick(Team.Red, 10f);
        Assert.IsTrue(Upkeep.IsStarving(Team.Red), "Control: the AI must really be starving.");

        InvokeResearch(ai);

        Assert.IsFalse(Technology.ActiveTechOf(Team.Red).HasValue,
            "A starving AI must not start a research, even with the gold in hand.");
        Assert.AreEqual(1000, ai.Gold, "The starving refusal must not spend gold.");
        Assert.AreEqual(500, ai.Wood, "The starving refusal must not spend wood.");

        // Feeding the side is the only thing that changes the answer: the same call
        // immediately after a meat delivery must now start Forge.
        ai.Add(ResourceKind.Meat, 5);
        Assert.IsFalse(Upkeep.IsStarving(Team.Red), "Control: the delivery ends the famine.");

        InvokeResearch(ai);

        Assert.AreEqual(Technology.Tech.Forge, Technology.ActiveTechOf(Team.Red),
            "A fed AI must research again.");
        Assert.AreEqual(750, ai.Gold, "The unlocked research pays Forge once.");
        Assert.AreEqual(400, ai.Wood, "The unlocked research pays Forge once.");
    }

    // ---------------------------------------------------------- fixed order

    [UnityTest]
    public IEnumerator Research_RunsForgeThenArmourThenGathering_ThenStops()
    {
        PaintRect(-20, -10, 40, 20);
        Technology.Reset();
        Upkeep.Reset();
        var ai = CreateAi(startGold: 2000, startWood: 1000);
        yield return null;
        CreateCastle(RedCastlePath, new Vector3Int(6, 0, 0));
        yield return null;

        var order = new[]
        {
            Technology.Tech.Forge, Technology.Tech.Armour, Technology.Tech.Gathering,
        };
        var costs = new[] { new ResourceCost(250, 100), new ResourceCost(250, 100), new ResourceCost(200) };

        for (int i = 0; i < order.Length; i++)
        {
            InvokeResearch(ai);
            Assert.AreEqual(order[i], Technology.ActiveTechOf(Team.Red),
                "Research line " + i + " must be " + order[i] + ".");
            Technology.Tick(45f);
            Assert.IsTrue(Technology.Has(Team.Red, order[i]),
                order[i] + " must complete once its 45 s elapse.");
            Assert.IsFalse(Technology.ActiveTechOf(Team.Red).HasValue,
                "A completed line must free the slot.");
            Assert.AreEqual(2000 - costs.Take(i + 1).Sum(c => c.gold), ai.Gold,
                order[i] + " must be paid from the AI bank, once per line.");
            Assert.AreEqual(1000 - costs.Take(i + 1).Sum(c => c.wood), ai.Wood,
                order[i] + " must be paid from the AI bank, once per line.");
        }

        Assert.AreEqual(Technology.AllBits, Technology.Mask(Team.Red),
            "All three lines must be researched.");

        // Nothing further: no repeat purchase, no crash on an empty next line.
        InvokeResearch(ai);
        Assert.IsFalse(Technology.ActiveTechOf(Team.Red).HasValue,
            "After all three lines the AI must do nothing further.");
        Assert.AreEqual(1300, ai.Gold, "1300 gold must remain; the AI must not buy a fourth line.");
        Assert.AreEqual(800, ai.Wood, "800 wood must remain; the AI must not buy a fourth line.");
    }

    [UnityTest]
    public IEnumerator Research_IsPerTeam_ThePlayersRunningLineDoesNotBlockTheAi()
    {
        PaintRect(-20, -10, 40, 20);
        Technology.Reset();
        Upkeep.Reset();
        Economy.gold = 1000;
        Economy.wood = 500;
        var ai = CreateAi(startGold: 1000, startWood: 500);
        yield return null;
        CreateCastle(BlueCastlePath, new Vector3Int(-6, 0, 0));
        CreateCastle(RedCastlePath, new Vector3Int(6, 0, 0));
        yield return null;

        Assert.IsTrue(Technology.BeginResearch(Team.Blue, Technology.Tech.Armour, out string reason),
            "Control: the player's line must start. " + reason);
        int playerGold = Economy.gold;
        int playerWood = Economy.wood;

        InvokeResearch(ai);

        Assert.AreEqual(Technology.Tech.Forge, Technology.ActiveTechOf(Team.Red),
            "The player's running line must not block the AI's own research.");
        Assert.AreEqual(Technology.Tech.Armour, Technology.ActiveTechOf(Team.Blue),
            "The player's line must survive untouched.");
        Assert.AreEqual(750, ai.Gold, "The AI pays from its own bank.");
        Assert.AreEqual(playerGold, Economy.gold,
            "The AI research must not touch the player's bank.");
        Assert.AreEqual(playerWood, Economy.wood,
            "The AI research must not touch the player's bank.");
    }

    // ------------------------------------------------- population-aware pick

    [UnityTest]
    public IEnumerator Produce_PopulationBlockedProduct_FallsThroughToTheOneThatFits()
    {
        PaintRect(-20, -10, 40, 20);
        Upkeep.Reset();
        var ai = CreateAi(startGold: 1000, startWood: 0, startMeat: 0, maxGatherers: 4);
        yield return null;

        // 12 population free. Five warriors (2 each) plus one archer is 11, so the
        // cheap heavy product below is blocked by population alone while the pricey
        // light one fits exactly.
        for (int i = 0; i < 5; i++) CreateUnit(RedWarriorPath, new Vector3Int(i, 0, 0));
        CreateUnit(RedArcherPath, new Vector3Int(6, 0, 0));
        UnitLimits.Invalidate();
        Population.Invalidate();

        var warriorPrefab = LoadPrefab<GameObject>(RedWarriorPath);
        var archerPrefab = LoadPrefab<GameObject>(RedArcherPath);

        Assert.AreEqual(11, Population.Used(Team.Red), "Control: the roster occupies 11 population.");
        Assert.AreEqual(12, Population.Capacity(Team.Red), "Control: no houses means the base 12.");
        Assert.IsTrue(UnitLimits.CanProduce(Team.Red, warriorPrefab, out string capReason),
            "Control: the warrior is legal by the Phase A caps, only population blocks it. "
            + capReason);
        Assert.IsFalse(Population.CanFit(Team.Red, warriorPrefab, out string popReason),
            "Control: a sixth warrior would make 13 of 12.");
        StringAssert.Contains("人口已满", popReason);
        Assert.IsTrue(Population.CanFit(Team.Red, archerPrefab, out string archerReason),
            "Control: the archer fits exactly. " + archerReason);

        // The heavy unit is cheaper, so the old cheapest-first filter would pick it,
        // fail at TryQueue and idle the factory.
        var factory = CreateFactory(new Vector3Int(10, 0, 0), warriorPrefab, 50,
            archerPrefab, 80);

        InvokeProduce(ai);

        CollectionAssert.AreEqual(new[] { 1 }, factory.Queue,
            "A population-blocked cheap unit must not starve the factory; the AI must "
            + "queue the only product it can actually build. Last refusal: "
            + (factory.LastRefusalReason ?? "<none>"));
        Assert.AreEqual(920, ai.Gold,
            "Only the 80-gold archer may be charged; a population refusal must not spend.");
        yield return null;
    }

    // ---------------------------------------------------------------- save

    [UnityTest]
    public IEnumerator Research_ResumesFromTheAisOwnSavedFields_AndContinuesTheOrder()
    {
        PaintRect(-24, -12, 48, 24);
        Technology.Reset();
        Upkeep.Reset();
        var ai = CreateAi(startGold: 1000, startWood: 500);
        yield return null;
        CreateCastle(RedCastlePath, new Vector3Int(0, 0, 0));
        yield return null;

        InvokeResearch(ai);
        Assert.AreEqual(Technology.Tech.Forge, Technology.ActiveTechOf(Team.Red),
            "Control: Forge is running before the save.");
        Technology.Tick(20f);

        var snapshot = SaveGameService.CaptureSnapshot(out string captureError);
        Assert.IsNotNull(snapshot, "Capture failed: " + captureError);
        Assert.AreEqual(0, snapshot.gameState.redTechMask,
            "Control: Forge has not finished, so the mask carries nothing yet.");
        Assert.AreEqual(Technology.Bit(Technology.Tech.Forge), snapshot.gameState.redResearchTech,
            "The AI's running line must land in the AI team's own saved field.");
        Assert.That(snapshot.gameState.redResearchRemaining, Is.InRange(24f, 25.5f),
            "The AI's remaining research time must be saved.");
        Assert.AreEqual(0, snapshot.gameState.blueResearchTech,
            "Control: the player has nothing running, so their field stays empty.");

        string path = Path.Combine(Application.temporaryCachePath, "ai_technology_resume.json");
        Assert.IsTrue(SaveGameService.SaveToPath(snapshot, path, out string writeError), writeError);
        Assert.IsTrue(SaveGameService.TryReadFromPath(path, out var loaded, out string readError), readError);
        Assert.AreEqual(Technology.Bit(Technology.Tech.Forge), loaded.gameState.redResearchTech,
            "The AI's running line must survive serialisation.");

        // Simulate a fresh process: the live line must not be what the restore reads.
        Technology.Reset();
        Assert.IsFalse(Technology.ActiveTechOf(Team.Red).HasValue,
            "Control: the reset cleared the line before the load.");

        bool restored = false;
        string restoreError = null;
        yield return SaveGameService.RestoreSnapshot(loaded, (ok, error) =>
        {
            restored = ok;
            restoreError = error;
        });
        Assert.IsTrue(restored, "Restore failed: " + restoreError);

        Assert.AreEqual(Technology.Tech.Forge, Technology.ActiveTechOf(Team.Red),
            "A load must resume the AI's line exactly as it resumes the player's.");
        Assert.IsFalse(Technology.Has(Team.Red, Technology.Tech.Forge),
            "A half-finished line must not come back as already researched.");
        float remaining = Technology.RemainingOf(Team.Red);
        Assert.That(remaining, Is.InRange(20f, 25.5f),
            "The restored line must keep its saved remaining time, not restart at 45 s.");
        Assert.AreEqual(750, ai.Gold,
            "The saved AI bank already paid for Forge; loading must not charge again.");
        Assert.AreEqual(400, ai.Wood,
            "The saved AI bank already paid for Forge; loading must not charge again.");

        // The resumed AI must not restart the line it is in the middle of.
        InvokeResearch(ai);
        Assert.AreEqual(750, ai.Gold, "A running line must not be paid for twice.");
        Assert.AreEqual(Technology.Tech.Forge, Technology.ActiveTechOf(Team.Red),
            "A running line must not be replaced after a load.");

        Technology.Tick(remaining + 0.5f);
        Assert.IsTrue(Technology.Has(Team.Red, Technology.Tech.Forge),
            "The restored line must complete once its remaining time elapses.");

        InvokeResearch(ai);
        Assert.AreEqual(Technology.Tech.Armour, Technology.ActiveTechOf(Team.Red),
            "After resuming and finishing Forge the AI must continue with Armour.");
        Assert.AreEqual(500, ai.Gold, "Armour costs the AI its own 250 gold.");
        Assert.AreEqual(300, ai.Wood, "Armour costs the AI its own 100 wood.");
    }

    // ---------------------------------------------------------------- helpers

    AIController CreateAi(int startGold, int startWood, int startMeat = 0,
                          int maxGatherers = 4, string name = "AI technology")
    {
        var go = new GameObject(name);
        go.transform.position = Grid.CellCenter(new Vector3Int(0, 4, 0));
        var ai = go.AddComponent<AIController>();
        ai.team = Team.Red;
        ai.active = false;                    // no ambient build/produce/attack ticks
        ai.startGold = startGold;
        ai.startWood = startWood;
        ai.startMeat = startMeat;
        ai.maxGatherers = maxGatherers;
        ai.buildOrder = new GameObject[0];
        return ai;
    }

    Building CreateCastle(string path, Vector3Int cell, bool underConstruction = false)
    {
        var prefab = LoadPrefab<GameObject>(path);
        var go = Object.Instantiate(
            prefab, Grid.CellCenter(cell) - new Vector3(0f, 0.5f, 0f), Quaternion.identity);
        var building = go.GetComponent<Building>();
        Assert.IsNotNull(building, path + " must carry a Building.");
        building.team = path.Contains("Red") ? Team.Red : Team.Blue;
        building.Recompute();
        if (underConstruction) building.MarkBuiltDuringMatch();
        return building;
    }

    UnitActor CreateUnit(string path, Vector3Int cell)
    {
        var prefab = LoadPrefab<GameObject>(path);
        var go = Object.Instantiate(prefab, Grid.CellCenter(cell), Quaternion.identity);
        var actor = go.GetComponent<UnitActor>();
        Assert.IsNotNull(actor, path + " must carry a UnitActor.");
        actor.team = Team.Red;
        actor.showHealthBar = false;
        UnitLimits.Invalidate();
        Population.Invalidate();
        return actor;
    }

    /// <summary>A factory whose two products deliberately put the cheaper item over the
    /// population line so only a population-aware filter can keep it working.</summary>
    ProductionBuilding CreateFactory(Vector3Int cell, GameObject cheapHeavy, int cheapGold,
                                     GameObject pricierLight, int pricierGold)
    {
        var go = new GameObject("AI mixed factory");
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
                label = "Cheap heavy",
                unitPrefab = cheapHeavy,
                cost = new ResourceCost(cheapGold),
                buildSeconds = 60f,
            },
            new ProductionBuilding.Product
            {
                label = "Pricey light",
                unitPrefab = pricierLight,
                cost = new ResourceCost(pricierGold),
                buildSeconds = 60f,
            },
        };
        return factory;
    }

    static void InvokeResearch(AIController ai)
    {
        Assert.IsNotNull(ResearchMethod,
            "AIController.Research must remain available: the AI research tick.");
        ResearchMethod.Invoke(ai, null);
    }

    static void InvokeProduce(AIController ai)
    {
        Assert.IsNotNull(ProduceMethod, "AIController.Produce must remain available.");
        ProduceMethod.Invoke(ai, null);
    }
}
