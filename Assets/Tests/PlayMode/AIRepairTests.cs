using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Phase A Task 5: the AI repairs its own key buildings from its own bank.
///
/// The pass is driven directly through reflection (the AI is created inactive so
/// its own Update never ticks between frames), matching AIProductionBudgetTests.
/// The shared price formula is <see cref="Building.RepairCost"/>; only the AI's
/// private ledger may change, never the player's <see cref="Economy"/>.
/// </summary>
public class AIRepairTests : RegressionFixture
{
    const string CastlePath = "Assets/Prefabs/Buildings/Knights/Castle_Red.prefab";
    const string RedHousePath = "Assets/Prefabs/Buildings/Knights/House_Red.prefab";
    const string BlueHousePath = "Assets/Prefabs/Buildings/Knights/House_Blue.prefab";
    const string RedTowerPath = "Assets/Prefabs/Buildings/Knights/Tower_Red.prefab";
    const string TreePath = "Assets/Prefabs/Resources/Tree1.prefab";

    static readonly MethodInfo RepairBuildingsMethod = typeof(AIController).GetMethod(
        "RepairBuildings", BindingFlags.Instance | BindingFlags.NonPublic);

    [UnityTest]
    public IEnumerator RepairBuildings_DamagedProductionBuilding_RestoresAndPaysExactly()
    {
        PaintRect(-12, -8, 40, 20);
        var ai = CreateAi(500, 300, 200);
        Economy.gold = 1000;
        Economy.wood = 1000;
        Economy.meat = 1000;
        yield return null;                         // let Start() deposit the opening bank
        AssertBank(ai, 500, 300, 200, "must start with the seeded opening bank.");

        var house = Spawn(RedHousePath, new Vector3Int(6, 0, 0), "House_Red", Team.Red);
        house.TakeDamage(100);
        Assert.AreEqual(200, house.health, "The control house must be 100 health short.");
        Assert.IsNotNull(house.GetComponent<ProductionBuilding>(),
            "The red house control must be a production building.");

        ResourceCost cost = house.RepairCost;
        Assert.AreEqual(10, cost.gold, "100 missing health costs 10 gold.");
        Assert.AreEqual(10, cost.wood, "100 missing health costs 10 wood.");
        Assert.AreEqual(10, cost.meat, "100 missing health costs 10 meat.");

        InvokeRepair(ai);

        Assert.AreEqual(house.maxHealth, house.health,
            "The AI repair pass must restore the building to full health.");
        Assert.AreEqual(0, house.MissingHealth,
            "A repaired building must have no missing health left.");
        AssertBank(ai, 500 - cost.gold, 300 - cost.wood, 200 - cost.meat,
            "must drop by exactly RepairCost in each kind after an AI repair.");
        Assert.AreEqual(1000, Economy.gold,
            "The AI must never spend the player's gold.");
        Assert.AreEqual(1000, Economy.wood,
            "The AI must never spend the player's wood.");
        Assert.AreEqual(1000, Economy.meat,
            "The AI must never spend the player's meat.");
    }

    [UnityTest]
    public IEnumerator RepairBuildings_CommandCentreOutranksHouse()
    {
        PaintRect(-12, -8, 40, 20);
        var ai = CreateAi(200, 90, 80);            // exactly one repair is payable
        yield return null;

        var castle = Spawn(CastlePath, new Vector3Int(6, 0, 0), "Castle_Red", Team.Red);
        var house = Spawn(RedHousePath, new Vector3Int(16, 0, 0), "House_Red", Team.Red);

        castle.TakeDamage(360);                    // 1200 -> 840: exactly 70%
        house.TakeDamage(100);                     // 300 -> 200
        Assert.AreEqual(840, castle.health, "The control command centre must be at 70%.");
        Assert.AreEqual(200, house.health, "The control house must be damaged below 70%.");

        // Castle costs 36 of each; the house costs 10. The wood ledger permits one.
        Assert.AreEqual(36, castle.RepairCost.wood, "The control command centre cost is 36 wood.");
        Assert.AreEqual(10, house.RepairCost.wood, "The control house cost is 10 wood.");

        InvokeRepair(ai);

        Assert.AreEqual(castle.maxHealth, castle.health,
            "The command centre has the highest priority and must be repaired first.");
        Assert.AreEqual(200, house.health,
            "The house must be left untouched when only one repair is affordable.");
        AssertBank(ai, 200 - 36, 90 - 36, 80 - 36,
            "must be charged exactly for the command centre repair.");
    }

    [UnityTest]
    public IEnumerator RepairBuildings_TowerIsNeverRepaired()
    {
        PaintRect(-12, -8, 40, 20);
        var ai = CreateAi(500, 300, 200);
        yield return null;

        var tower = Spawn(RedTowerPath, new Vector3Int(6, 0, 0), "Tower_Red", Team.Red);
        tower.TakeDamage(200);                     // 500 -> 300: 60%, below the threshold
        Assert.AreEqual(300, tower.health, "The control tower must be damaged to 60%.");
        Assert.IsNull(tower.GetComponent<ProductionBuilding>(),
            "The tower control must not be a production building.");

        InvokeRepair(ai);

        Assert.AreEqual(300, tower.health,
            "Towers are priority 99 and must never be auto-repaired.");
        AssertBank(ai, 500, 300, 200,
            "must not be charged for a tower that is not repaired.");
    }

    [UnityTest]
    public IEnumerator RepairBuildings_AboveSeventyPercent_IsLeftAlone()
    {
        PaintRect(-12, -8, 40, 20);
        var ai = CreateAi(500, 300, 200);
        yield return null;

        var house = Spawn(RedHousePath, new Vector3Int(6, 0, 0), "House_Red", Team.Red);
        house.TakeDamage(60);                      // 300 -> 240: 80%
        Assert.AreEqual(240, house.health, "The control house must be at 80% health.");

        InvokeRepair(ai);

        Assert.AreEqual(240, house.health,
            "A building above 70% health must not be repaired.");
        AssertBank(ai, 500, 300, 200,
            "must not be charged for a building above the repair threshold.");
    }

    [UnityTest]
    public IEnumerator RepairBuildings_ExactlySeventyPercent_IsEligible()
    {
        PaintRect(-12, -8, 40, 20);
        var ai = CreateAi(500, 300, 200);
        yield return null;

        var house = Spawn(RedHousePath, new Vector3Int(6, 0, 0), "House_Red", Team.Red);
        house.TakeDamage(90);                      // 300 -> 210: exactly 70%
        Assert.AreEqual(210, house.health, "The control house must be at exactly 70% health.");

        InvokeRepair(ai);

        Assert.AreEqual(house.maxHealth, house.health,
            "Exactly 70% health is at the threshold and must be repaired.");
        AssertBank(ai, 491, 291, 191,
            "must be charged exactly 9 of each for 90 missing health.");
    }

    [UnityTest]
    public IEnumerator RepairBuildings_WoodReserveFloor_BlocksRepair()
    {
        PaintRect(-12, -8, 40, 20);
        var ai = CreateAi(500, 60, 200);           // gold is ample; wood reserve fails
        yield return null;

        var castle = Spawn(CastlePath, new Vector3Int(6, 0, 0), "Castle_Red", Team.Red);
        castle.TakeDamage(360);
        Assert.AreEqual(840, castle.health, "The control command centre must be eligible.");

        InvokeRepair(ai);

        Assert.AreEqual(840, castle.health,
            "The AI must keep 50 wood in reserve instead of repairing.");
        AssertBank(ai, 500, 60, 200,
            "must not be charged when the wood reserve floor would be broken.");
    }

    [UnityTest]
    public IEnumerator RepairBuildings_MeatReserveFloor_BlocksRepair()
    {
        PaintRect(-12, -8, 40, 20);
        var ai = CreateAi(500, 300, 55);           // gold and wood ample; meat reserve fails
        yield return null;

        var castle = Spawn(CastlePath, new Vector3Int(6, 0, 0), "Castle_Red", Team.Red);
        castle.TakeDamage(360);
        Assert.AreEqual(840, castle.health, "The control command centre must be eligible.");

        InvokeRepair(ai);

        Assert.AreEqual(840, castle.health,
            "The AI must keep 25 meat in reserve instead of repairing.");
        AssertBank(ai, 500, 300, 55,
            "must not be charged when the meat reserve floor would be broken.");
    }

    [UnityTest]
    public IEnumerator RepairBuildings_PlayerBuilding_IsLeftAlone()
    {
        PaintRect(-12, -8, 40, 20);
        var ai = CreateAi(500, 300, 200);
        yield return null;

        var blueHouse = Spawn(BlueHousePath, new Vector3Int(6, 0, 0), "House_Blue", Team.Blue);
        blueHouse.TakeDamage(100);
        Assert.AreEqual(200, blueHouse.health, "The control blue house must be damaged to 66%.");
        Assert.AreEqual(Team.Blue, blueHouse.team, "The control building must belong to the player.");

        InvokeRepair(ai);

        Assert.AreEqual(200, blueHouse.health,
            "The AI must never repair a player-owned building.");
        AssertBank(ai, 500, 300, 200,
            "must not be charged for a player-owned building.");
    }

    [UnityTest]
    public IEnumerator RepairBuildings_NeutralBuilding_IsLeftAlone()
    {
        PaintRect(-12, -8, 40, 20);
        var ai = CreateAi(500, 300, 200);
        yield return null;

        var tree = Spawn(TreePath, new Vector3Int(6, 0, 0), "Tree1_Neutral", Team.Neutral);
        tree.TakeDamage(50);                       // 150 -> 100: 66%
        Assert.AreEqual(100, tree.health, "The control tree must be damaged to 66%.");
        Assert.AreEqual(Team.Neutral, tree.team, "The control tree must be neutral.");

        InvokeRepair(ai);

        Assert.AreEqual(100, tree.health,
            "The AI must never repair neutral scenery.");
        AssertBank(ai, 500, 300, 200,
            "must not be charged for neutral scenery.");
    }

    [UnityTest]
    public IEnumerator RepairBuildings_UnderConstruction_IsSkipped()
    {
        PaintRect(-12, -8, 40, 20);
        var ai = CreateAi(500, 300, 200);
        yield return null;

        var house = Spawn(RedHousePath, new Vector3Int(6, 0, 0), "House_Red", Team.Red);
        house.MarkBuiltDuringMatch();
        Assert.IsTrue(house.IsUnderConstruction, "The control house must still be building.");
        house.TakeDamage(100);
        Assert.AreEqual(200, house.health, "The control house must be damaged to 66%.");

        InvokeRepair(ai);

        Assert.AreEqual(200, house.health,
            "A building still under construction must be skipped.");
        AssertBank(ai, 500, 300, 200,
            "must not be charged for a half-built building.");
    }

    [UnityTest]
    public IEnumerator RepairBuildings_Wrecked_IsSkipped()
    {
        PaintRect(-12, -8, 40, 20);
        var ai = CreateAi(500, 300, 200);
        yield return null;

        var house = Spawn(RedHousePath, new Vector3Int(6, 0, 0), "House_Red", Team.Red);

        // Zero the field directly instead of calling TakeDamage. TakeDamage at this
        // depth runs Building.Wreck(), which unregisters the building from Building.All,
        // so the wrecked house would never even be a candidate and this test would pass
        // with the whole repair body deleted. Writing the public field keeps the object
        // registered, making the !IsAlive guard the only thing that can skip it.
        house.health = 0;

        Assert.AreEqual(0, house.health, "The control house must have no health left.");
        Assert.IsFalse(house.IsAlive, "The control house must not be alive.");
        Assert.IsTrue(Building.All.Contains(house),
            "The wrecked house must still be a visible candidate in Building.All; "
            + "if it had been unregistered the assertions below would prove nothing.");
        Assert.IsNotNull(house.GetComponent<ProductionBuilding>(),
            "The wrecked house must otherwise look repairable (a production building), "
            + "so only the IsAlive guard can explain it being skipped.");
        Assert.AreEqual(30, house.RepairCost.gold,
            "Fully missing health must otherwise be payable (30 of each), "
            + "so an unguarded pass would visibly charge the bank.");

        InvokeRepair(ai);

        Assert.AreEqual(0, house.health,
            "A wrecked building must never be resurrected by the AI repair pass.");
        AssertBank(ai, 500, 300, 200,
            "must not be charged for a wrecked building.");
    }

    // ---------------------------------------------------------------- helpers

    AIController CreateAi(int gold, int wood, int meat)
    {
        var go = new GameObject("AI repair");
        go.transform.position = Grid.CellCenter(new Vector3Int(0, 0, 0));
        var ai = go.AddComponent<AIController>();
        ai.team = Team.Red;
        ai.active = false;                         // no ambient build/produce/attack ticks
        ai.startGold = gold;
        ai.startWood = wood;
        ai.startMeat = meat;
        ai.buildOrder = new GameObject[0];
        return ai;
    }

    Building Spawn(string path, Vector3Int cell, string name, Team team)
    {
        var prefab = LoadPrefab<GameObject>(path);
        var go = Object.Instantiate(
            prefab, Grid.CellCenter(cell) - new Vector3(0f, 0.5f, 0f), Quaternion.identity);
        go.name = name;
        var building = go.GetComponent<Building>();
        Assert.IsNotNull(building, path + " must carry a Building.");
        building.team = team;
        building.Recompute();
        return building;
    }

    static void InvokeRepair(AIController ai)
    {
        Assert.IsNotNull(RepairBuildingsMethod,
            "AIController.RepairBuildings must remain available.");
        RepairBuildingsMethod.Invoke(ai, null);
    }

    static void AssertBank(AIController ai, int gold, int wood, int meat, string because)
    {
        Assert.AreEqual(gold, ai.Gold, "gold " + because);
        Assert.AreEqual(wood, ai.Wood, "wood " + because);
        Assert.AreEqual(meat, ai.Meat, "meat " + because);
    }
}
