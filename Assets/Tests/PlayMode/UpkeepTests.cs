using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// Phase B Task 4: meat upkeep. Every ten seconds each side pays one meat for every
/// living combat unit, out of its own bank. Workers, monks and buildings cost
/// nothing. A side that cannot pay the full amount is Starving until it can: no
/// health loss, the bank never goes negative, production is refused with 食物不足
/// (after the Phase A caps and population, so the most specific reason wins) and
/// monks stop healing. The flag clears on the next tick and immediately after a
/// meat delivery. Nothing about upkeep is saved — the timer and the flag are derived.
///
/// Time is advanced through the public <see cref="Upkeep.Tick(Team, float)"/> seam
/// with an explicit delta; no test waits ten real seconds.
/// </summary>
public class UpkeepTests : RegressionFixture
{
    const string WarriorPath = "Assets/Prefabs/Units/Warrior_Blue.prefab";
    const string ArcherPath = "Assets/Prefabs/Units/Archer_Blue.prefab";
    const string LancerPath = "Assets/Prefabs/Units/Lancer_Blue.prefab";
    const string MonkPath = "Assets/Prefabs/Units/Monk_Blue.prefab";
    const string PawnPath = "Assets/Prefabs/Units/Pawn_Blue.prefab";
    const string RedWarriorPath = "Assets/Prefabs/Units/Warrior_Red.prefab";
    const string RedPawnPath = "Assets/Prefabs/Units/Pawn_Red.prefab";
    const string BarracksPath = "Assets/Prefabs/Buildings/Knights/Barracks_Blue.prefab";
    const string BarracksRedPath = "Assets/Prefabs/Buildings/Knights/Barracks_Red.prefab";
    const string CastleRedPath = "Assets/Prefabs/Buildings/Knights/Castle_Red.prefab";
    const string TowerPath = "Assets/Prefabs/Buildings/Knights/Tower_Blue.prefab";
    const string HousePath = "Assets/Prefabs/Buildings/Knights/House_Blue.prefab";
    const string SheepPath = "Assets/Prefabs/Resources/HappySheep_Idle.prefab";
    const string TreePath = "Assets/Prefabs/Resources/Tree1.prefab";

    static readonly MethodInfo AssignGatherersMethod = typeof(AIController).GetMethod(
        "AssignGatherers", BindingFlags.Instance | BindingFlags.NonPublic);

    // ------------------------------------------------------------ pure tables

    [Test]
    public void Constants_AreTheApprovedNumbers()
    {
        Upkeep.Reset();
        Assert.AreEqual(10f, Upkeep.PeriodSeconds, "Upkeep fires every ten seconds.");
        Assert.AreEqual(1, Upkeep.MeatPerUnit, "One meat per living combat unit.");
        Assert.AreEqual(0, Upkeep.AmountDue(Team.Blue), "No army, nothing owed.");
        Assert.AreEqual(0, Upkeep.AmountDue(Team.Red));
        Assert.IsFalse(Upkeep.IsStarving(Team.Blue));
        Assert.IsFalse(Upkeep.IsStarving(Team.Red));
    }

    // ------------------------------------------------------------ unit count

    [UnityTest]
    public IEnumerator UnitCount_CountsOnlyLivingCombatants()
    {
        Upkeep.Reset();
        PaintRect(-16, -8, 48, 16);
        var warrior = CreateNamedUnit(WarriorPath, Team.Blue, new Vector3Int(-6, 0, 0));
        CreateNamedUnit(WarriorPath, Team.Blue, new Vector3Int(-4, 0, 0));
        CreateNamedUnit(ArcherPath, Team.Blue, new Vector3Int(-2, 0, 0));
        CreateNamedUnit(LancerPath, Team.Blue, new Vector3Int(0, 0, 0));
        var monk = CreateNamedUnit(MonkPath, Team.Blue, new Vector3Int(2, 0, 0));
        CreateNamedUnit(PawnPath, Team.Blue, new Vector3Int(4, 0, 0));
        CreateNamedUnit(PawnPath, Team.Blue, new Vector3Int(6, 0, 0));
        yield return null;

        Assert.IsTrue(warrior.isCombatant, "The fixture warrior must be a combatant.");
        Assert.IsFalse(monk.isCombatant, "The fixture monk must be a non-combatant.");
        Assert.AreEqual(4, Upkeep.UnitCount(Team.Blue),
            "Warriors, lancers and archers eat; workers and monks do not.");
        Assert.AreEqual(4, Upkeep.AmountDue(Team.Blue),
            "The amount due is exactly the combatant count.");
        Assert.AreEqual(0, Upkeep.UnitCount(Team.Red),
            "One side's roster is never charged against the other's.");

        warrior.TakeDamage(100000);
        Assert.AreEqual(3, Upkeep.UnitCount(Team.Blue),
            "A dead combatant stops eating immediately.");
        monk.TakeDamage(100000);
        Assert.AreEqual(3, Upkeep.UnitCount(Team.Blue),
            "Killing a non-combatant changes nothing.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator GarrisonedArcher_StillCountsAsOneCombatant()
    {
        Upkeep.Reset();
        PaintRect(-16, -8, 48, 16);
        var tower = InstantiateBuilding(TowerPath, new Vector3Int(0, 0, 0), Team.Blue);
        var archer = CreateNamedUnit(ArcherPath, Team.Blue, new Vector3Int(-4, 0, 0));
        yield return null;

        var garrison = tower.GetComponent<TowerGarrison>();
        Assert.IsNotNull(garrison, TowerPath + " must carry a TowerGarrison.");
        Assert.IsTrue(garrison.Garrison(archer), "The tower must accept the archer.");

        Assert.AreEqual(1, Upkeep.UnitCount(Team.Blue),
            "An archer locked in a tower is still a living combatant and still eats.");

        archer.health = 0;                 // the garrison-counting precedent from PopulationTests
        Assert.AreEqual(0, Upkeep.UnitCount(Team.Blue),
            "A dead garrisoned archer must stop eating.");
    }

    // ---------------------------------------------------------------- charges

    [UnityTest]
    public IEnumerator ChargesOneMeatPerCombatant_EveryTenSeconds()
    {
        Upkeep.Reset();
        PaintRect(-16, -8, 48, 16);
        for (int i = 0; i < 3; i++)
            CreateNamedUnit(WarriorPath, Team.Blue, new Vector3Int(-6 + i * 2, 0, 0));
        CreateNamedUnit(MonkPath, Team.Blue, new Vector3Int(0, 2, 0));
        CreateNamedUnit(PawnPath, Team.Blue, new Vector3Int(2, 2, 0));
        yield return null;

        Assert.AreEqual(3, Upkeep.AmountDue(Team.Blue));

        Economy.meat = 100;
        Upkeep.Reset();
        Upkeep.Tick(Team.Blue, 9.9f);
        Assert.AreEqual(100, Economy.meat,
            "Nothing is due before the tenth second.");
        Upkeep.Tick(Team.Blue, 0.2f);
        Assert.AreEqual(97, Economy.meat,
            "Three combatants pay three meat at the ten-second mark.");
        Upkeep.Tick(Team.Blue, 9.9f);
        Assert.AreEqual(97, Economy.meat, "The clock then starts again.");
        Upkeep.Tick(Team.Blue, 0.2f);
        Assert.AreEqual(94, Economy.meat, "The second period charges again.");
    }

    [UnityTest]
    public IEnumerator WorkersMonksAndBuildings_AreFree()
    {
        Upkeep.Reset();
        PaintRect(-16, -8, 48, 16);
        for (int i = 0; i < 3; i++)
            CreateNamedUnit(PawnPath, Team.Blue, new Vector3Int(-6 + i, 0, 0));
        for (int i = 0; i < 2; i++)
            CreateNamedUnit(MonkPath, Team.Blue, new Vector3Int(0 + i, 2, 0));
        InstantiateBuilding(HousePath, new Vector3Int(6, 0, 0), Team.Blue);
        yield return null;

        Assert.AreEqual(0, Upkeep.AmountDue(Team.Blue),
            "Workers, monks and buildings owe nothing.");

        Economy.meat = 50;
        Upkeep.Reset();
        Upkeep.Tick(Team.Blue, 10f);
        Assert.AreEqual(50, Economy.meat,
            "A side with no combat units pays no upkeep.");
        Assert.IsFalse(Upkeep.IsStarving(Team.Blue));
    }

    [UnityTest]
    public IEnumerator ShortBank_PaysOnlyWhatItHas_AndNeverGoesNegative()
    {
        Upkeep.Reset();
        PaintRect(-16, -8, 48, 16);
        var first = CreateNamedUnit(WarriorPath, Team.Blue, new Vector3Int(-4, 0, 0));
        var second = CreateNamedUnit(WarriorPath, Team.Blue, new Vector3Int(-2, 0, 0));
        yield return null;

        Economy.meat = 1;
        Upkeep.Reset();
        Upkeep.Tick(Team.Blue, 10f);

        Assert.AreEqual(0, Economy.meat,
            "A short bank is charged what it has, never more.");
        Assert.IsTrue(Upkeep.IsStarving(Team.Blue),
            "An unpaid balance makes the side starving.");

        Upkeep.Tick(Team.Blue, 10f);
        Assert.AreEqual(0, Economy.meat,
            "The unpaid remainder is forgiven, not booked as debt.");
        Assert.IsTrue(Upkeep.IsStarving(Team.Blue));

        Assert.AreEqual(first.maxHealth, first.health, "Starvation never costs health.");
        Assert.AreEqual(second.maxHealth, second.health, "Starvation never costs health.");
    }

    // ------------------------------------------------------------- refusals

    [UnityTest]
    public IEnumerator Starving_RefusesProduction_WithFoodReason()
    {
        Upkeep.Reset();
        PaintRect(-16, -8, 48, 16);
        CreateNamedUnit(WarriorPath, Team.Blue, new Vector3Int(-6, 0, 0));
        Economy.gold = 1000;
        var barracks = InstantiateFactory(BarracksPath, new Vector3Int(6, 0, 0));
        int index = barracks.IndexOfProduct(LoadPrefab<GameObject>(WarriorPath));
        Assert.GreaterOrEqual(index, 0, "The barracks must offer a warrior.");
        yield return null;

        Assert.IsTrue(barracks.CanQueue(index, true, out string before),
            "Control: the same order is legal while the side is fed. " + before);

        Economy.meat = 0;
        Upkeep.Reset();
        Upkeep.Tick(Team.Blue, 10f);
        Assert.IsTrue(Upkeep.IsStarving(Team.Blue), "Control: the side must be starving.");

        int gold = Economy.gold;
        Assert.IsFalse(barracks.CanQueue(index, true, out string reason),
            "A starving side must refuse production.");
        StringAssert.Contains("食物不足", reason,
            "The refusal must name food, the approved player-facing reason for upkeep.");
        Assert.IsFalse(barracks.TryQueue(index));
        Assert.AreEqual(0, barracks.Queue.Count);
        Assert.AreEqual(gold, Economy.gold,
            "A food refusal must happen before any resources are spent.");
    }

    [UnityTest]
    public IEnumerator RefusalPrecedence_WarriorCap_BeatsStarvation()
    {
        Upkeep.Reset();
        PaintRect(-16, -8, 48, 16);
        for (int i = 0; i < UnitLimits.WarriorCap; i++)
            CreateNamedUnit(WarriorPath, Team.Blue, new Vector3Int(-14 + i, 0, 0));
        Economy.gold = 1000;
        var barracks = InstantiateFactory(BarracksPath, new Vector3Int(10, 0, 0));
        int index = barracks.IndexOfProduct(LoadPrefab<GameObject>(WarriorPath));
        Assert.GreaterOrEqual(index, 0, "The barracks must offer a warrior.");
        UnitLimits.Invalidate();
        Population.Invalidate();
        yield return null;

        Assert.AreEqual(UnitLimits.WarriorCap, Upkeep.AmountDue(Team.Blue),
            "Control: the whole roster is alive and eating.");

        Economy.meat = 0;
        Upkeep.Reset();
        Upkeep.Tick(Team.Blue, 10f);
        Assert.IsTrue(Upkeep.IsStarving(Team.Blue),
            "Control: the side must be starving as well as capped.");

        Assert.IsFalse(barracks.CanQueue(index, true, out string reason));
        StringAssert.Contains("战士已达上限", reason,
            "With two reasons available, the Phase A cap is the more specific one.");
        StringAssert.DoesNotContain("食物不足", reason,
            "The upkeep gate must sit after the caps and population in the refusal chain.");
    }

    // --------------------------------------------------------------- healing

    [UnityTest]
    public IEnumerator Starving_StopsMonkHealing_AndFeedingRestoresIt()
    {
        Upkeep.Reset();
        PaintRect(-16, -8, 48, 16);
        var monk = CreateNamedUnit(MonkPath, Team.Blue, new Vector3Int(0, 0, 0));
        var patient = CreateNamedUnit(WarriorPath, Team.Blue, new Vector3Int(1, 0, 0));
        var healer = monk.GetComponent<Healer>();
        Assert.IsNotNull(healer, MonkPath + " must carry a Healer.");
        healer.healCooldown = 0f;
        yield return null;

        patient.TakeDamage(40);
        int wounded = patient.health;
        yield return null;
        Assert.Greater(patient.health, wounded,
            "Control: a fed monk heals a wounded ally.");
        int healed = patient.health;

        Economy.meat = 0;
        Upkeep.Reset();
        Upkeep.Tick(Team.Blue, 10f);
        Assert.IsTrue(Upkeep.IsStarving(Team.Blue), "Control: the side must be starving.");

        yield return null;
        yield return null;
        Assert.AreEqual(healed, patient.health,
            "A starving side's monks do not heal.");

        Economy.Add(ResourceKind.Meat, 20);
        Assert.IsFalse(Upkeep.IsStarving(Team.Blue),
            "A meat delivery ends the famine at once.");
        yield return null;
        Assert.Greater(patient.health, healed,
            "Healing resumes as soon as the side is fed.");
    }

    // --------------------------------------------------------------- recovery

    [UnityTest]
    public IEnumerator Delivery_EndsStarvation_Immediately()
    {
        Upkeep.Reset();
        PaintRect(-16, -8, 48, 16);
        CreateNamedUnit(WarriorPath, Team.Blue, new Vector3Int(0, 0, 0));
        yield return null;

        Economy.meat = 0;
        Upkeep.Reset();
        Upkeep.Tick(Team.Blue, 10f);
        Assert.IsTrue(Upkeep.IsStarving(Team.Blue), "Control: the side must be starving.");

        // Gatherer.Credit delivers through Economy.Add; a worker hauling meat home
        // must be able to end a famine without waiting for the next ten-second tick.
        Economy.Add(ResourceKind.Meat, 20);
        Assert.AreEqual(20, Economy.meat);
        Assert.IsFalse(Upkeep.IsStarving(Team.Blue),
            "A successful meat delivery clears starvation immediately.");
    }

    [UnityTest]
    public IEnumerator NextTick_RecomputesStarvation_FromTheBank()
    {
        Upkeep.Reset();
        PaintRect(-16, -8, 48, 16);
        CreateNamedUnit(WarriorPath, Team.Blue, new Vector3Int(0, 0, 0));
        yield return null;

        Economy.meat = 0;
        Upkeep.Reset();
        Upkeep.Tick(Team.Blue, 10f);
        Assert.IsTrue(Upkeep.IsStarving(Team.Blue), "Control: the side must be starving.");

        Economy.meat = 50;                 // the save loader writes the bank directly too
        Assert.IsTrue(Upkeep.IsStarving(Team.Blue),
            "A direct bank write is not a delivery; only a tick recomputes the flag.");

        Upkeep.Tick(Team.Blue, 10f);
        Assert.IsFalse(Upkeep.IsStarving(Team.Blue),
            "With a positive bank the next tick clears starvation.");
        Assert.AreEqual(49, Economy.meat, "That tick charges the one combat unit.");
    }

    // ------------------------------------------------------------ both sides

    [UnityTest]
    public IEnumerator PlayerAndAi_AreChargedAndRefusedIdentically()
    {
        Upkeep.Reset();
        PaintRect(-24, -8, 48, 16);
        var ai = CreateAi();
        yield return null;                 // let Start deposit the AI's opening bank
        Assert.AreEqual(200, ai.Meat, "Control: the AI starts with the opening 200 meat.");

        CreateNamedUnit(WarriorPath, Team.Blue, new Vector3Int(-6, 0, 0));
        CreateNamedUnit(WarriorPath, Team.Blue, new Vector3Int(-4, 0, 0));
        CreateNamedUnit(RedWarriorPath, Team.Red, new Vector3Int(-2, 0, 0));
        CreateNamedUnit(RedWarriorPath, Team.Red, new Vector3Int(0, 0, 0));
        Economy.gold = 1000;
        var blueBarracks = InstantiateFactory(BarracksPath, new Vector3Int(4, 0, 0));
        var redBarracks = InstantiateFactory(BarracksRedPath, new Vector3Int(8, 0, 0), Team.Red);
        UnitLimits.Invalidate();
        Population.Invalidate();
        yield return null;

        int blueIndex = blueBarracks.IndexOfProduct(LoadPrefab<GameObject>(WarriorPath));
        int redIndex = redBarracks.IndexOfProduct(LoadPrefab<GameObject>(RedWarriorPath));
        Assert.GreaterOrEqual(blueIndex, 0, "The blue barracks must offer a warrior.");
        Assert.GreaterOrEqual(redIndex, 0, "The red barracks must offer a warrior.");

        Economy.meat = 1;
        ai.Add(ResourceKind.Meat, -(ai.Meat - 1));
        Assert.AreEqual(1, ai.Meat);

        Upkeep.Reset();
        Upkeep.Tick(Team.Blue, 10f);
        Upkeep.Tick(Team.Red, 10f);

        Assert.AreEqual(0, Economy.meat, "The player pays what it has for two warriors.");
        Assert.AreEqual(0, ai.Meat, "The AI pays the same from its own bank.");
        Assert.IsTrue(Upkeep.IsStarving(Team.Blue), "A short player bank starves.");
        Assert.IsTrue(Upkeep.IsStarving(Team.Red), "A short AI bank starves by the same rule.");
        Assert.AreEqual(2, Upkeep.AmountDue(Team.Blue));
        Assert.AreEqual(2, Upkeep.AmountDue(Team.Red));

        Assert.IsFalse(blueBarracks.CanQueue(blueIndex, true, out string blueReason));
        StringAssert.Contains("食物不足", blueReason);
        Assert.IsFalse(redBarracks.CanQueue(redIndex, true, out string redReason),
            "The AI's factory reaches the same refusal chain.");
        StringAssert.Contains("食物不足", redReason);
        Assert.IsFalse(redBarracks.TryQueue(redIndex, payCost: false),
            "The AI production path must obey upkeep too.");
    }

    // ------------------------------------------------------------------ save

    [UnityTest]
    public IEnumerator UpkeepState_IsNotSaved_AndALoadRestartsTheClock()
    {
        Upkeep.Reset();
        PaintRect(-16, -8, 48, 16);
        CreateNamedUnit(WarriorPath, Team.Blue, new Vector3Int(0, 0, 0));
        yield return null;

        Economy.meat = 0;
        Upkeep.Reset();
        Upkeep.Tick(Team.Blue, 10f);
        Assert.IsTrue(Upkeep.IsStarving(Team.Blue),
            "Control: the side must be starving before the save is taken.");

        var before = SaveGameService.CaptureSnapshot(out string firstError);
        Assert.IsNotNull(before, "CaptureSnapshot failed: " + firstError);
        Assert.IsNull(firstError, firstError);
        before.savedAtUtc = "";
        string json = JsonUtility.ToJson(before, true);
        StringAssert.DoesNotContain("upkeep", json.ToLowerInvariant(),
            "The save payload must not grow an upkeep field.");
        StringAssert.DoesNotContain("starving", json.ToLowerInvariant(),
            "Starvation is derived, never serialised.");

        Upkeep.Reset();                    // exactly what GameState.Awake does on the load scene
        Assert.IsFalse(Upkeep.IsStarving(Team.Blue),
            "Starvation is recomputed from the bank rather than restored.");

        var after = SaveGameService.CaptureSnapshot(out string secondError);
        Assert.IsNotNull(after, "CaptureSnapshot failed: " + secondError);
        after.savedAtUtc = "";
        Assert.AreEqual(json, JsonUtility.ToJson(after, true),
            "Same bank and roster: a save taken while starving must be identical to one taken after the reset.");

        Upkeep.Tick(Team.Blue, 9.9f);
        Assert.AreEqual(0, Economy.meat,
            "The load path restarts the ten-second clock; no charge before it elapses.");
        Assert.IsFalse(Upkeep.IsStarving(Team.Blue));

        Upkeep.Tick(Team.Blue, 0.2f);
        Assert.IsTrue(Upkeep.IsStarving(Team.Blue),
            "The first post-load tick derives starvation from the bank again.");
    }

    // ------------------------------------------------------------ AI awareness

    [UnityTest]
    public IEnumerator Ai_PrefersMeatOnlyWhileStarving()
    {
        Upkeep.Reset();
        PaintRect(-24, -8, 48, 16);
        var ai = CreateAi();
        yield return null;                 // Start seeds the AI bank

        InstantiateBuilding(CastleRedPath, new Vector3Int(8, 0, 0), Team.Red);
        var tree = SpawnNode(TreePath, new Vector3Int(-3, -2, 0));
        var sheep = SpawnNode(SheepPath, new Vector3Int(-8, 2, 0));
        yield return null;

        Assert.AreEqual(ResourceKind.Wood, tree.kind, "The control node must be wood.");
        Assert.AreEqual(ResourceKind.Meat, sheep.kind, "The control node must be meat.");

        var fedWorker = CreateWorker(new Vector3Int(0, 0, 0));
        InvokeAssignGatherers(ai);
        Assert.AreEqual(tree, fedWorker.GetComponent<Gatherer>().TargetNode,
            "A fed AI takes the nearest node, whatever its kind.");

        Object.Destroy(fedWorker.gameObject);
        yield return null;

        CreateNamedUnit(RedWarriorPath, Team.Red, new Vector3Int(0, 2, 0));
        ai.Add(ResourceKind.Meat, -ai.Meat);
        Upkeep.Reset();
        Upkeep.Tick(Team.Red, 10f);
        Assert.IsTrue(Upkeep.IsStarving(Team.Red), "Control: the AI must be starving.");

        var hungryWorker = CreateWorker(new Vector3Int(0, 0, 0));
        float treeDistance = (tree.transform.position - hungryWorker.transform.position).sqrMagnitude;
        float sheepDistance = (sheep.transform.position - hungryWorker.transform.position).sqrMagnitude;
        Assert.Less(treeDistance, sheepDistance,
            "Control: the meat node must really be the farther one.");

        InvokeAssignGatherers(ai);
        Assert.AreEqual(sheep, hungryWorker.GetComponent<Gatherer>().TargetNode,
            "A starving AI goes for meat even when a wood node is nearer.");
    }

    // ------------------------------------------------------------------- HUD

    [UnityTest]
    public IEnumerator Hud_MeatReadout_IsAmberBelowFifty_AndRedAtZero()
    {
        Upkeep.Reset();
        var label = MakeText(null, "Meat");
        var hud = new GameObject("Meat colour HUD").AddComponent<HUD>();
        hud.meatText = label;
        yield return null;                 // Start captures the authored colour

        Color normal = label.color;

        Economy.meat = 100;
        Economy.RaiseChanged();
        Assert.AreEqual("100", label.text);
        Assert.AreEqual(normal, label.color, "Above the threshold the readout is unchanged.");

        Economy.meat = 50;
        Economy.RaiseChanged();
        Assert.AreEqual(normal, label.color, "Fifty is the boundary and is still normal.");

        Economy.meat = 49;
        Economy.RaiseChanged();
        Assert.AreEqual(hud.meatLowTint, label.color, "Below fifty the meat readout turns amber.");

        Economy.meat = 0;
        Economy.RaiseChanged();
        Assert.AreEqual(hud.meatEmptyTint, label.color, "At zero the meat readout turns red.");
    }

    // --------------------------------------------------------------- helpers

    AIController CreateAi()
    {
        var go = new GameObject("Upkeep AI");
        go.transform.position = Grid.CellCenter(new Vector3Int(0, 4, 0));
        var ai = go.AddComponent<AIController>();
        ai.team = Team.Red;
        ai.active = false;                 // no ambient build/produce/attack ticks
        ai.startGold = 0;
        ai.startWood = 0;
        ai.startMeat = 200;
        ai.buildOrder = new GameObject[0];
        return ai;
    }

    static void InvokeAssignGatherers(AIController ai)
    {
        Assert.IsNotNull(AssignGatherersMethod,
            "AIController.AssignGatherers must remain available.");
        AssignGatherersMethod.Invoke(ai, null);
    }

    UnitActor CreateWorker(Vector3Int cell)
    {
        var actor = CreateNamedUnit(RedPawnPath, Team.Red, cell);
        var gatherer = actor.GetComponent<Gatherer>();
        Assert.IsNotNull(gatherer, RedPawnPath + " must carry a Gatherer.");
        gatherer.autoStart = false;        // the AI dispatch in the test does the ordering
        return actor;
    }

    UnitActor CreateNamedUnit(string path, Team team, Vector3Int cell)
    {
        var prefab = LoadPrefab<GameObject>(path);
        var go = Object.Instantiate(prefab, Grid.CellCenter(cell), Quaternion.identity);
        var actor = go.GetComponent<UnitActor>();
        Assert.IsNotNull(actor, path + " must carry a UnitActor.");
        actor.team = team;
        actor.showHealthBar = false;
        UnitLimits.Invalidate();
        Population.Invalidate();
        return actor;
    }

    ResourceNode SpawnNode(string path, Vector3Int cell)
    {
        var prefab = LoadPrefab<GameObject>(path);
        var go = Object.Instantiate(prefab, Grid.CellCenter(cell), Quaternion.identity);
        var node = go.GetComponentInChildren<ResourceNode>(true);
        Assert.IsNotNull(node, path + " must carry a ResourceNode.");
        return node;
    }

    GameObject InstantiateBuilding(string path, Vector3Int cell, Team team)
    {
        var prefab = LoadPrefab<GameObject>(path);
        var go = Object.Instantiate(prefab, Grid.CellCenter(cell), Quaternion.identity);
        var building = go.GetComponent<Building>();
        Assert.IsNotNull(building, path + " must carry a Building.");
        building.team = team;
        building.Recompute();
        UnitLimits.Invalidate();
        Population.Invalidate();
        return go;
    }

    ProductionBuilding InstantiateFactory(string path, Vector3Int cell, Team team = Team.Blue)
    {
        var go = InstantiateBuilding(path, cell, team);
        var factory = go.GetComponent<ProductionBuilding>();
        Assert.IsNotNull(factory, path + " must carry a ProductionBuilding.");
        return factory;
    }

    static Text MakeText(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        if (parent != null) go.transform.SetParent(parent, false);
        return go.GetComponent<Text>();
    }
}
