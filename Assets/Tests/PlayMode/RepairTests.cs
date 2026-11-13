using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Phase A Task 4: player-paid, instant repair of damaged buildings.
///
/// The repair command must be a single production entry point
/// (<see cref="SelectionManager.RepairSelected"/>) that re-checks ownership and
/// affordability itself — selecting an enemy building is already refused by
/// <see cref="SelectionManager"/>, but the command must not rely on that.
/// </summary>
public class RepairTests : RegressionFixture
{
    const string HousePath = "Assets/Prefabs/Buildings/Knights/House_Blue.prefab";
    const string TreePath = "Assets/Prefabs/Resources/Tree1.prefab";

    /// <summary>The same team source SelectionManager.Owned reads.</summary>
    static Team PlayerTeam =>
        GameState.Instance != null ? GameState.Instance.playerTeam : Team.Blue;

    Building SpawnHouse(Vector3Int cell, string name)
    {
        var prefab = LoadPrefab<GameObject>(HousePath);
        var go = Object.Instantiate(
            prefab, Grid.CellCenter(cell) - new Vector3(0f, 0.5f, 0f), Quaternion.identity);
        go.name = name;
        var building = go.GetComponent<Building>();
        building.Recompute();
        return building;
    }

    Building SpawnNeutralTree(Vector3Int cell, string name)
    {
        var prefab = LoadPrefab<GameObject>(TreePath);
        var go = Object.Instantiate(
            prefab, Grid.CellCenter(cell) - new Vector3(0f, 0.5f, 0f), Quaternion.identity);
        go.name = name;
        var building = go.GetComponent<Building>();
        building.Recompute();
        return building;
    }

    void SetBanks(int gold, int wood, int meat)
    {
        Economy.gold = gold;
        Economy.wood = wood;
        Economy.meat = meat;
    }

    static void AssertBanks(int gold, int wood, int meat, string because)
    {
        Assert.AreEqual(gold, Economy.Instance.gold, "gold " + because);
        Assert.AreEqual(wood, Economy.Instance.wood, "wood " + because);
        Assert.AreEqual(meat, Economy.Instance.meat, "meat " + because);
    }

    [Test]
    public void RepairSelected_RestoresHealthAndSpendsTheCostOnlyOnce()
    {
        PaintRect(-4, -4, 16, 12);
        var house = SpawnHouse(Vector3Int.zero, "Repair happy-path house");
        house.TakeDamage(100);
        SetBanks(1000, 1000, 1000);

        Assert.AreEqual(100, house.MissingHealth, "The control house must be 100 health short.");
        ResourceCost cost = house.RepairCost;
        Assert.AreEqual(10, cost.gold, "100 missing health costs 10 gold.");
        Assert.AreEqual(10, cost.wood, "100 missing health costs 10 wood.");
        Assert.AreEqual(10, cost.meat, "100 missing health costs 10 meat.");

        int goldAfter = Economy.gold - cost.gold;
        int woodAfter = Economy.wood - cost.wood;
        int meatAfter = Economy.meat - cost.meat;
        Selection.selectedBuilding = house;

        Selection.RepairSelected();

        Assert.AreEqual(house.maxHealth, house.health,
            "A paid repair must restore the building to full health.");
        Assert.AreEqual(0, house.MissingHealth,
            "A repaired building must have no missing health left.");
        AssertBanks(goldAfter, woodAfter, meatAfter,
            "after a successful repair must drop by exactly RepairCost.");

        // A second click on a full-health building must be a no-op, not a free donation.
        Selection.RepairSelected();

        Assert.AreEqual(house.maxHealth, house.health,
            "A second repair on a full-health building must not change health.");
        AssertBanks(goldAfter, woodAfter, meatAfter,
            "after a redundant repair must not be charged twice.");
        Assert.AreEqual(0, house.RepairCost.gold,
            "A full-health building must cost nothing to repair.");
    }

    [Test]
    public void RepairCost_ChargesPerStartedTenMissingHealth()
    {
        PaintRect(-4, -4, 16, 12);
        var house = SpawnHouse(Vector3Int.zero, "Repair cost-formula house");

        house.TakeDamage(25);
        Assert.AreEqual(25, house.MissingHealth, "Control damage 25.");
        Assert.AreEqual(3, house.RepairCost.gold, "25 missing health is 2.5 units, ceiled to 3 gold.");
        Assert.AreEqual(3, house.RepairCost.wood, "25 missing health is 2.5 units, ceiled to 3 wood.");
        Assert.AreEqual(3, house.RepairCost.meat, "25 missing health is 2.5 units, ceiled to 3 meat.");

        house.Repair(house.MissingHealth);
        house.TakeDamage(10);
        Assert.AreEqual(10, house.MissingHealth, "Control damage 10.");
        Assert.AreEqual(1, house.RepairCost.gold, "Exactly 10 missing health is one full unit.");
        Assert.AreEqual(1, house.RepairCost.wood, "Exactly 10 missing health is one full unit.");
        Assert.AreEqual(1, house.RepairCost.meat, "Exactly 10 missing health is one full unit.");

        // Ceil, not round: one point past the boundary starts a second unit.
        house.Repair(house.MissingHealth);
        house.TakeDamage(11);
        Assert.AreEqual(11, house.MissingHealth, "Control damage 11.");
        Assert.AreEqual(2, house.RepairCost.gold, "11 missing health is 1.1 units, ceiled to 2 gold.");
        Assert.AreEqual(2, house.RepairCost.wood, "11 missing health is 1.1 units, ceiled to 2 wood.");
        Assert.AreEqual(2, house.RepairCost.meat, "11 missing health is 1.1 units, ceiled to 2 meat.");
    }

    [Test]
    public void RepairSelected_UnderConstructionBuilding_ChangesNothing()
    {
        PaintRect(-4, -4, 16, 12);
        var house = SpawnHouse(Vector3Int.zero, "Repair under-construction house");
        house.MarkBuiltDuringMatch();
        Assert.IsTrue(house.IsUnderConstruction, "The control house must still be under construction.");
        house.TakeDamage(50);
        int health = house.health;
        SetBanks(1000, 1000, 1000);

        Assert.IsFalse(house.CanRepair, "A half-built building must not be repairable.");
        Selection.selectedBuilding = house;

        Selection.RepairSelected();

        Assert.AreEqual(health, house.health, "A half-built building must not be repaired.");
        AssertBanks(1000, 1000, 1000, "must not change for an under-construction building.");
    }

    [Test]
    public void RepairSelected_WreckedBuilding_ChangesNothing()
    {
        PaintRect(-4, -4, 16, 12);
        var house = SpawnHouse(Vector3Int.zero, "Repair wrecked house");
        house.TakeDamage(house.maxHealth);
        Assert.IsFalse(house.IsAlive, "The control house must be wrecked.");
        SetBanks(1000, 1000, 1000);

        Assert.IsFalse(house.CanRepair, "Rubble must not be repairable.");
        Selection.selectedBuilding = house;

        Selection.RepairSelected();

        Assert.AreEqual(0, house.health, "Rubble must stay at zero health.");
        AssertBanks(1000, 1000, 1000, "must not change for a wrecked building.");
    }

    [Test]
    public void RepairSelected_NeutralBuilding_ChangesNothing()
    {
        PaintRect(-4, -4, 16, 12);
        var tree = SpawnNeutralTree(Vector3Int.zero, "Repair neutral tree");
        Assert.AreEqual(Team.Neutral, tree.team, "The control tree must be neutral.");
        tree.TakeDamage(50);
        int health = tree.health;
        SetBanks(1000, 1000, 1000);

        Assert.IsFalse(tree.CanRepair, "Neutral scenery must not be repairable.");
        Selection.selectedBuilding = tree;

        Selection.RepairSelected();

        Assert.AreEqual(health, tree.health, "Neutral scenery must not be repaired.");
        AssertBanks(1000, 1000, 1000, "must not change for neutral scenery.");
    }

    [Test]
    public void RepairSelected_NoBuildingSelected_ChangesNothing()
    {
        PaintRect(-4, -4, 16, 12);
        SetBanks(1000, 1000, 1000);
        Selection.selected.Clear();
        Selection.selectedBuilding = null;

        Selection.RepairSelected();

        AssertBanks(1000, 1000, 1000, "must not change without a selected building.");
    }

    [Test]
    public void RepairSelected_Unaffordable_ChangesNothing()
    {
        PaintRect(-4, -4, 16, 12);
        var house = SpawnHouse(Vector3Int.zero, "Repair unaffordable house");
        house.TakeDamage(100);
        int health = house.health;

        // Short by one gold: whichever kind the command tries to spend first, TrySpend fails.
        SetBanks(9, 1000, 1000);
        Assert.IsTrue(house.CanRepair, "The control building itself must be repairable.");
        Assert.IsFalse(Economy.CanAfford(house.RepairCost),
            "The control bank must be unable to afford the repair.");
        Selection.selectedBuilding = house;

        Selection.RepairSelected();

        Assert.AreEqual(health, house.health, "An unaffordable repair must not change health.");
        AssertBanks(9, 1000, 1000, "must not change when the repair cannot be paid for.");
    }

    [Test]
    public void RepairSelected_EnemyBuilding_ChangesNothing()
    {
        PaintRect(-4, -4, 16, 12);
        var house = SpawnHouse(Vector3Int.zero, "Repair enemy house");
        house.TakeDamage(100);
        int health = house.health;

        // Never hardcode Blue: the player team comes from GameState, so pick the other side.
        Team enemyTeam = PlayerTeam == Team.Blue ? Team.Red : Team.Blue;
        house.team = enemyTeam;
        SetBanks(1000, 1000, 1000);

        Assert.IsTrue(house.CanRepair,
            "The enemy building is otherwise repairable — only the ownership guard may reject it.");
        Selection.selectedBuilding = house;

        Selection.RepairSelected();

        Assert.AreEqual(health, house.health, "An enemy building must never be repaired.");
        AssertBanks(1000, 1000, 1000, "must not be spent on an enemy building.");
    }
}
