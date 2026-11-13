using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// Presentation-phase backlog fixes (2026-11-01). Two behaviours the HUD/UI reports promised but
/// the code did not deliver:
///
///  * the always-on unit shop stayed lit while the side was starving — the click reached
///    <see cref="ProductionBuilding.CanQueue"/>, was refused with "食物不足", and the player saw
///    nothing happen; and
///  * <see cref="Gatherer"/>'s floating "+20" reported the whole haul even when storage was full
///    and only part of it landed.
///
/// Both assertions below are of the "must be refused / must report the truth" kind, so each one has
/// a mutation proof recorded in the task report.
/// </summary>
public class BacklogFixTests : RegressionFixture
{
    const string WarriorPath = "Assets/Prefabs/Units/Warrior_Blue.prefab";
    const string BarracksPath = "Assets/Prefabs/Buildings/Knights/Barracks_Blue.prefab";

    /// <summary>A starving side must not present a clickable purchase that silently fails.</summary>
    [UnityTest]
    public IEnumerator UnitShop_ShowsFoodRefusal_WhileStarving()
    {
        Upkeep.Reset();
        PaintRect(-16, -8, 48, 16);
        var warriorPrefab = LoadPrefab<GameObject>(WarriorPath);
        var soldier = Object.Instantiate(warriorPrefab,
            Grid.CellCenter(new Vector3Int(-6, 0, 0)), Quaternion.identity).GetComponent<UnitActor>();
        Assert.IsNotNull(soldier, "Warrior_Blue must carry a UnitActor.");
        soldier.team = Team.Blue;
        soldier.showHealthBar = false;
        InstantiateFactory(BarracksPath, new Vector3Int(6, 0, 0));
        UnitLimits.Invalidate();
        Population.Invalidate();

        Economy.gold = 1000;
        Economy.meat = 200;
        yield return null;

        var button = MakeButton("Warrior offer");
        var cost = MakeText(button.transform, "Cost");
        var hud = new GameObject("Backlog HUD").AddComponent<HUD>();
        hud.unitPanel = new GameObject("Unit panel");
        hud.unitOffers = new[]
        {
            new HUD.UnitOffer
            {
                label = "战士",
                unitPrefab = warriorPrefab,
                cost = new ResourceCost(80),
            },
        };
        hud.unitButtons = new[] { button };
        hud.unitCosts = new[] { cost };
        hud.unitIcons = new[] { MakeImage(button.transform, "Icon") };
        hud.unitNames = new[] { MakeText(button.transform, "Name") };
        hud.unitBacks = new[] { button.GetComponent<Image>() };
        yield return null;

        Assert.IsTrue(button.interactable,
            "Control: the shop row must be clickable while the side is fed. Shown: " + cost.text);

        Economy.meat = 0;
        Upkeep.Reset();
        Upkeep.Tick(Team.Blue, 10f);
        Assert.IsTrue(Upkeep.IsStarving(Team.Blue), "Control: the side must really be starving.");
        yield return null;

        Assert.IsFalse(button.interactable,
            "A starving side must not show an order it will refuse one layer below.");
        StringAssert.Contains("食物不足", cost.text,
            "The shop must name the food refusal, exactly like the factory does.");
        Assert.AreEqual(hud.warnTint, cost.color,
            "The refusal must be painted in the warning colour, like the other refusals.");
    }

    /// <summary>The haul report must be the amount the bank actually accepted.</summary>
    [UnityTest]
    public IEnumerator Credit_ReturnsOnlyWhatTheBankAccepted()
    {
        var credit = typeof(Gatherer).GetMethod("Credit", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(credit, "Gatherer.Credit must still exist; the test targets it by name.");

        Economy.gold = 100;
        int credited = (int)credit.Invoke(null, new object[] { Team.Blue, ResourceKind.Gold, 20 });
        Assert.AreEqual(20, credited, "A bank with room takes the whole load.");
        Assert.AreEqual(120, Economy.gold, "And the bank really received it.");

        Economy.gold = ResourceLimits.GoldCap - 5;
        credited = (int)credit.Invoke(null, new object[] { Team.Blue, ResourceKind.Gold, 20 });
        Assert.AreEqual(5, credited,
            "A nearly full bank gives back only what fit; the floating text used to claim the lot.");
        Assert.AreEqual(ResourceLimits.GoldCap, Economy.gold, "Storage stops exactly at its cap.");

        Economy.gold = ResourceLimits.GoldCap;
        credited = (int)credit.Invoke(null, new object[] { Team.Blue, ResourceKind.Gold, 20 });
        Assert.AreEqual(0, credited, "A full bank accepts nothing, so nothing may be reported.");
        yield return null;
    }

    const string ArcherPath = "Assets/Prefabs/Units/Archer_Blue.prefab";
    const string TowerPath = "Assets/Prefabs/Buildings/Knights/Tower_Blue.prefab";
    const string CastlePath = "Assets/Prefabs/Buildings/Knights/Castle_Blue.prefab";

    /// <summary>An unknown-named fighter must spend from the same 14-slot allowance as the
    /// named professions; before this fix it was refused only when the total was already
    /// full and never counted toward it.</summary>
    [UnityTest]
    public IEnumerator UnknownNamedCombatant_CountsTowardTheSharedCombatCap()
    {
        PaintRect(-8, -8, 32, 24);
        for (int i = 0; i < UnitLimits.CombatCap - 1; i++)
            CreateNamedUnit(WarriorPath, Team.Blue, new Vector3Int(i % 8, i / 8, 0));
        var odd = CreateNamedUnit(WarriorPath, Team.Blue, new Vector3Int(12, 0, 0));
        odd.gameObject.name = "P4_Champion";          // carries no known profession name
        UnitLimits.Invalidate();
        yield return null;

        Assert.AreEqual(UnitRole.OtherCombatant, UnitLimits.RoleOf(odd.gameObject),
            "An unknown-named fighter must not be filed with the non-combatants.");
        Assert.AreEqual(UnitLimits.CombatCap, UnitLimits.TotalCombat(Team.Blue),
            "The unknown-named fighter occupies one of the 14 combat slots.");
        Assert.IsFalse(UnitLimits.CanProduce(Team.Blue, odd.gameObject, out string reason),
            "A full roster must refuse even an unknown-named fighter.");
        StringAssert.Contains("上限", reason);
        Assert.AreEqual(1, UnitLimits.Count(Team.Blue, UnitRole.OtherCombatant),
            "It is counted in its own bucket, not silently dropped.");
    }

    /// <summary>The same fighter has to occupy population, or it would be free to field.</summary>
    [UnityTest]
    public IEnumerator UnknownNamedCombatant_OccupiesPopulation()
    {
        PaintRect(-8, -8, 32, 24);
        UnitLimits.Invalidate();
        Population.Invalidate();
        int before = Population.Used(Team.Blue);

        var odd = CreateNamedUnit(WarriorPath, Team.Blue, new Vector3Int(2, 0, 0));
        odd.gameObject.name = "P4_Champion";
        UnitLimits.Invalidate();
        Population.Invalidate();
        yield return null;

        Assert.AreEqual(before + 1, Population.Used(Team.Blue),
            "An unknown-named fighter must still occupy population.");
    }

    /// <summary>Armour grants head-room to every combatant. A garrisoned archer's UnitActor is
    /// disabled and absent from <see cref="UnitActor.All"/>, so it used to leave the tower
    /// already wounded (110/127) instead of topped up.</summary>
    [UnityTest]
    public IEnumerator ArmourHeadroom_ReachesAGarrisonedArcher()
    {
        PaintRect(-10, -10, 24, 20);
        Technology.Reset();
        Economy.gold = 2000;
        Economy.wood = 500;
        InstantiateBuilding(CastlePath, new Vector3Int(0, 0, 0), Team.Blue);
        var archer = CreateNamedUnit(ArcherPath, Team.Blue, new Vector3Int(-4, 0, 0));
        var tower = InstantiateBuilding(TowerPath, new Vector3Int(4, 0, 0), Team.Blue);
        yield return null;

        var garrison = tower.GetComponent<TowerGarrison>();
        Assert.IsNotNull(garrison, "Tower_Blue must carry a TowerGarrison.");
        Assert.IsTrue(garrison.Garrison(archer), "The tower must accept an archer.");
        yield return null;
        Assert.IsFalse(UnitActor.All.Contains(archer),
            "Control: a garrisoned archer is absent from UnitActor.All, which is why it was missed.");

        int maxBefore = archer.maxHealth;
        Assert.IsTrue(Technology.BeginResearch(Team.Blue, Technology.Tech.Armour, out string reason),
            "A completed command centre must accept Armour. " + reason);
        Technology.Tick(999f);
        Assert.IsTrue(Technology.Has(Team.Blue, Technology.Tech.Armour),
            "Control: Armour must actually be researched.");

        Assert.Greater(archer.EffectiveMaxHealth, maxBefore,
            "Control: Armour raises the archer's maximum.");
        Assert.AreEqual(archer.EffectiveMaxHealth, archer.health,
            "A garrisoned archer must receive the same head-room as the rest of the army.");
    }

    // ------------------------------------------------------------------ helpers

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

    ProductionBuilding InstantiateFactory(string path, Vector3Int cell)
    {
        var go = InstantiateBuilding(path, cell, Team.Blue);
        var factory = go.GetComponent<ProductionBuilding>();
        Assert.IsNotNull(factory, path + " must carry a ProductionBuilding.");
        return factory;
    }

    static Button MakeButton(string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        return go.GetComponent<Button>();
    }

    static Image MakeImage(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        return go.GetComponent<Image>();
    }

    static Text MakeText(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        return go.GetComponent<Text>();
    }
}
