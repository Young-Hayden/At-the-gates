using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// Phase B Task 1: house-driven population. A side fields at most 12 population
/// for free and +6 per completed House (absolute cap 48). Consumption uses the
/// same roster as <see cref="UnitLimits"/>: living units, tower garrisons and paid
/// queue orders. Over-cap survivors are never deleted; only new production is
/// refused, with 人口已满 shown in the same two HUD surfaces as the Phase A caps.
/// </summary>
public class PopulationTests : RegressionFixture
{
    const string PawnPath = "Assets/Prefabs/Units/Pawn_Blue.prefab";
    const string WarriorPath = "Assets/Prefabs/Units/Warrior_Blue.prefab";
    const string ArcherPath = "Assets/Prefabs/Units/Archer_Blue.prefab";
    const string LancerPath = "Assets/Prefabs/Units/Lancer_Blue.prefab";
    const string MonkPath = "Assets/Prefabs/Units/Monk_Blue.prefab";

    const string RedPawnPath = "Assets/Prefabs/Units/Pawn_Red.prefab";
    const string RedWarriorPath = "Assets/Prefabs/Units/Warrior_Red.prefab";

    const string HousePath = "Assets/Prefabs/Buildings/Knights/House_Blue.prefab";
    const string RedHousePath = "Assets/Prefabs/Buildings/Knights/House_Red.prefab";
    const string BarracksPath = "Assets/Prefabs/Buildings/Knights/Barracks_Blue.prefab";
    const string BarracksRedPath = "Assets/Prefabs/Buildings/Knights/Barracks_Red.prefab";
    const string ArcheryPath = "Assets/Prefabs/Buildings/Knights/Archery_Blue.prefab";
    const string TowerPath = "Assets/Prefabs/Buildings/Knights/Tower_Blue.prefab";

    // ------------------------------------------------------------ pure tables

    [Test]
    public void Constants_AreTheApprovedNumbers()
    {
        Assert.AreEqual(12, Population.BaseCapacity, "Every side starts with 12 population.");
        Assert.AreEqual(6, Population.HouseBonus, "Each completed house adds 6.");
        Assert.AreEqual(48, Population.MaxCapacity, "Absolute cap is 48.");
        Assert.AreEqual(1, Population.PopOf(UnitRole.Worker));
        Assert.AreEqual(1, Population.PopOf(UnitRole.Archer));
        Assert.AreEqual(1, Population.PopOf(UnitRole.Monk));
        Assert.AreEqual(2, Population.PopOf(UnitRole.Warrior));
        Assert.AreEqual(2, Population.PopOf(UnitRole.Lancer));
        Assert.AreEqual(1, Population.PopOf(UnitRole.Other));
    }

    [Test]
    public void Cost_WarriorAndLancerCostTwo_WorkerArcherMonkCostOne()
    {
        Assert.AreEqual(2, Population.Cost(LoadPrefab<GameObject>(WarriorPath)));
        Assert.AreEqual(2, Population.Cost(LoadPrefab<GameObject>(LancerPath)));
        Assert.AreEqual(1, Population.Cost(LoadPrefab<GameObject>(ArcherPath)));
        Assert.AreEqual(1, Population.Cost(LoadPrefab<GameObject>(MonkPath)));
        Assert.AreEqual(1, Population.Cost(LoadPrefab<GameObject>(PawnPath)));
    }

    // ------------------------------------------------------------- capacity

    [UnityTest]
    public IEnumerator Capacity_BaseTwelve_ThenPlusSixPerCompletedHouse()
    {
        PaintRect(-16, -8, 48, 16);
        Population.Invalidate();
        yield return null;

        Assert.AreEqual(12, Population.Capacity(Team.Blue),
            "With no buildings a side fields 12 population.");

        CreateHouse(HousePath, new Vector3Int(-12, 0, 0), Team.Blue);
        Population.Invalidate();
        Assert.AreEqual(18, Population.Capacity(Team.Blue),
            "One completed house raises the capacity to 18.");

        CreateHouse(HousePath, new Vector3Int(-4, 0, 0), Team.Blue);
        Population.Invalidate();
        Assert.AreEqual(24, Population.Capacity(Team.Blue),
            "Two completed houses raise the capacity to 24.");

        Assert.AreEqual(12, Population.Capacity(Team.Red),
            "A blue house must not raise the red capacity.");
    }

    [UnityTest]
    public IEnumerator Capacity_IgnoresUnderConstructionWreckedAndGhostHouses()
    {
        PaintRect(-16, -8, 48, 16);
        Population.Invalidate();
        yield return null;

        var completed = CreateHouse(HousePath, new Vector3Int(-12, 0, 0), Team.Blue);
        var building = completed.GetComponent<Building>();
        var underConstruction = CreateHouse(HousePath, new Vector3Int(-4, 0, 0), Team.Blue);
        underConstruction.GetComponent<Building>().MarkBuiltDuringMatch();
        var ghostHouse = CreateHouse(HousePath, new Vector3Int(4, 0, 0), Team.Blue);
        ghostHouse.GetComponent<Building>().MakeGhost();
        yield return null;

        Assert.IsTrue(underConstruction.GetComponent<Building>().IsUnderConstruction,
            "The fixture house must really be under construction.");
        Population.Invalidate();
        Assert.AreEqual(18, Population.Capacity(Team.Blue),
            "Only the one completed house may count; half-built and ghost houses add nothing.");

        building.TakeDamage(100000);
        yield return null;
        Assert.IsFalse(building.IsAlive, "The control house must be wrecked.");
        Population.Invalidate();
        Assert.AreEqual(12, Population.Capacity(Team.Blue),
            "A wrecked house must stop contributing its 6 population.");
    }

    [UnityTest]
    public IEnumerator Capacity_ClampsAtFortyEight()
    {
        PaintRect(-24, -8, 64, 16);
        Population.Invalidate();
        yield return null;

        for (int i = 0; i < 6; i++)
            CreateHouse(HousePath, new Vector3Int(-20 + i * 7, 0, 0), Team.Blue);
        Population.Invalidate();
        Assert.AreEqual(48, Population.Capacity(Team.Blue),
            "Six houses reach the absolute cap: 12 + 6 * 6 = 48.");

        CreateHouse(HousePath, new Vector3Int(22, 0, 0), Team.Blue);
        Population.Invalidate();
        Assert.AreEqual(48, Population.Capacity(Team.Blue),
            "A seventh house must not raise capacity past 48.");
        yield return null;
    }

    // ----------------------------------------------------------------- used

    [UnityTest]
    public IEnumerator Used_CountsQueuedOrdersExactlyOnce_AcrossFactories()
    {
        PaintRect(-16, -8, 48, 16);
        Economy.gold = 1000;
        var archerPrefab = LoadPrefab<GameObject>(ArcherPath);
        var first = InstantiateFactory(ArcheryPath, new Vector3Int(-4, 0, 0));
        var second = InstantiateFactory(ArcheryPath, new Vector3Int(6, 0, 0));
        int firstIndex = first.IndexOfProduct(archerPrefab);
        int secondIndex = second.IndexOfProduct(archerPrefab);
        Assert.GreaterOrEqual(firstIndex, 0, "The first archery range must offer an archer.");
        Assert.GreaterOrEqual(secondIndex, 0, "The second archery range must offer an archer.");

        Population.Invalidate();
        yield return null;
        Assert.AreEqual(0, Population.Used(Team.Blue));

        Assert.IsTrue(first.TryQueue(firstIndex), "The first archer must fit under the base capacity.");
        Assert.AreEqual(1, Population.Used(Team.Blue),
            "A paid queue order consumes exactly one population before it spawns.");

        Assert.IsTrue(second.TryQueue(secondIndex), "The second archer must still fit.");
        Assert.AreEqual(2, Population.Used(Team.Blue),
            "Orders in different factories must each count exactly once.");

        yield return null;
        Assert.AreEqual(2, first.Queue.Count + second.Queue.Count,
            "Both orders must still be building, not spawned.");
        Assert.AreEqual(2, Population.Used(Team.Blue),
            "A later frame must neither drop nor double count the queued population.");
        Assert.AreEqual(2, UnitLimits.Count(Team.Blue, UnitRole.Archer),
            "Population must agree with the roster UnitLimits already owns.");
    }

    [UnityTest]
    public IEnumerator Used_CountsGarrisonedArcherOnce()
    {
        PaintRect(-16, -8, 48, 16);
        var tower = InstantiateBuilding(TowerPath, new Vector3Int(0, 0, 0), Team.Blue);
        var archer = CreateNamedUnit(ArcherPath, Team.Blue, new Vector3Int(-4, 0, 0));
        yield return null;

        var garrison = tower.GetComponent<TowerGarrison>();
        Assert.IsNotNull(garrison, TowerPath + " must carry a TowerGarrison.");
        Assert.IsTrue(garrison.Garrison(archer), "The tower must accept the archer.");
        Population.Invalidate();

        Assert.AreEqual(1, Population.Used(Team.Blue),
            "A garrisoned archer costs exactly one population.");
        Assert.AreEqual(1, UnitLimits.Count(Team.Blue, UnitRole.Archer),
            "Population must use the same garrison counting as UnitLimits.");

        archer.health = 0;
        Population.Invalidate();
        Assert.AreEqual(0, Population.Used(Team.Blue),
            "A dead garrisoned archer must not consume population.");
    }

    // -------------------------------------------------------------- refusal

    [UnityTest]
    public IEnumerator OverCap_SurvivorsRemain_AndOnlyNewProductionIsRefused()
    {
        PaintRect(-16, -8, 48, 16);
        Economy.gold = 1000;

        // 6 workers (6) + 4 warriors (8) = 14 population, two over the base 12.
        for (int i = 0; i < 6; i++)
            CreateNamedUnit(PawnPath, Team.Blue, new Vector3Int(-14 + i, 0, 0));
        for (int i = 0; i < 4; i++)
            CreateNamedUnit(WarriorPath, Team.Blue, new Vector3Int(-14 + i, 2, 0));
        Population.Invalidate();
        yield return null;

        Assert.AreEqual(14, Population.Used(Team.Blue),
            "Every existing unit must be counted, including those over the capacity.");
        Assert.AreEqual(12, Population.Capacity(Team.Blue));

        var warriorPrefab = LoadPrefab<GameObject>(WarriorPath);
        Assert.IsTrue(UnitLimits.CanProduce(Team.Blue, warriorPrefab, out _),
            "Control: the Phase A warrior and combat caps are not the reason.");

        var barracks = InstantiateFactory(BarracksPath, new Vector3Int(4, 0, 0));
        int index = barracks.IndexOfProduct(warriorPrefab);
        Assert.GreaterOrEqual(index, 0, "The barracks must offer a warrior.");

        int gold = Economy.gold;
        Assert.IsFalse(barracks.CanQueue(index, true, out string reason),
            "A side above its population capacity must refuse new production.");
        StringAssert.Contains("人口已满", reason,
            "The refusal must use the approved player-facing reason.");
        Assert.IsFalse(barracks.TryQueue(index));
        Assert.AreEqual(0, barracks.Queue.Count);
        Assert.AreEqual(gold, Economy.gold,
            "A population refusal must happen before any resources are spent.");

        foreach (var actor in Object.FindObjectsByType<UnitActor>(FindObjectsSortMode.None))
            Assert.IsTrue(actor.IsAlive,
                "Going over the population capacity must never kill an existing unit.");

        CreateHouse(HousePath, new Vector3Int(-8, 4, 0), Team.Blue);
        Population.Invalidate();
        Assert.AreEqual(18, Population.Capacity(Team.Blue),
            "A completed house must raise the capacity above the existing 14.");

        Assert.IsTrue(barracks.TryQueue(index),
            "Once a house lifts the capacity, the same order must be accepted.");
        Assert.AreEqual(16, Population.Used(Team.Blue),
            "The newly queued warrior must consume two population.");
        Assert.AreEqual(gold - barracks.products[index].cost.gold, Economy.gold,
            "The accepted order charges exactly its price, once.");
    }

    [UnityTest]
    public IEnumerator PlayerAndAi_ShareTheSamePopulationRules()
    {
        PaintRect(-16, -8, 48, 16);

        for (int i = 0; i < 6; i++)
            CreateNamedUnit(RedPawnPath, Team.Red, new Vector3Int(-14 + i, 0, 0));
        for (int i = 0; i < 4; i++)
            CreateNamedUnit(RedWarriorPath, Team.Red, new Vector3Int(-14 + i, 2, 0));
        Population.Invalidate();
        yield return null;

        Assert.AreEqual(12, Population.Capacity(Team.Red));
        Assert.AreEqual(14, Population.Used(Team.Red));

        var redWarrior = LoadPrefab<GameObject>(RedWarriorPath);
        Assert.IsFalse(Population.CanFit(Team.Red, redWarrior, out string redReason));
        StringAssert.Contains("人口已满", redReason);

        Assert.IsTrue(Population.CanFit(Team.Blue, LoadPrefab<GameObject>(WarriorPath), out _),
            "A full red side must not consume the blue allowance.");
        Assert.AreEqual(0, Population.Used(Team.Blue));

        var redBarracks = InstantiateFactory(BarracksRedPath, new Vector3Int(6, 0, 0), Team.Red);
        int index = redBarracks.IndexOfProduct(redWarrior);
        Assert.GreaterOrEqual(index, 0, "The red barracks must offer a red warrior.");
        Assert.IsFalse(redBarracks.TryQueue(index, payCost: false),
            "The AI production path reaches the same CanQueue and must obey population.");

        CreateHouse(RedHousePath, new Vector3Int(-8, 4, 0), Team.Red);
        Population.Invalidate();
        Assert.AreEqual(18, Population.Capacity(Team.Red),
            "Red houses raise red capacity by exactly the same rule.");
        Assert.IsTrue(redBarracks.TryQueue(index, payCost: false));
        yield return null;
    }

    // ------------------------------------------------------------------ HUD

    [UnityTest]
    public IEnumerator ProductionPanel_ShowsPopulationRefusal_InCostText()
    {
        PaintRect(-16, -8, 48, 16);
        var barracks = InstantiateFactory(BarracksPath, new Vector3Int(4, 0, 0));
        for (int i = 0; i < 6; i++)
            CreateNamedUnit(PawnPath, Team.Blue, new Vector3Int(-14 + i, 0, 0));
        for (int i = 0; i < 3; i++)
            CreateNamedUnit(WarriorPath, Team.Blue, new Vector3Int(-14 + i, 2, 0));
        Population.Invalidate();

        var warriorPrefab = LoadPrefab<GameObject>(WarriorPath);
        int index = barracks.IndexOfProduct(warriorPrefab);
        Assert.GreaterOrEqual(index, 0, "The barracks must offer a warrior.");
        Economy.gold = 1000;
        Assert.IsTrue(UnitLimits.CanProduce(Team.Blue, warriorPrefab, out _),
            "Control: the Phase A caps must not be what greys out the button.");
        Assert.IsTrue(Economy.CanAfford(barracks.products[index].cost),
            "Control: the order must be affordable, or this could pass for the wrong reason.");
        Assert.AreEqual(12, Population.Used(Team.Blue),
            "Control: the side is exactly at its population capacity.");

        var buttons = new Button[barracks.products.Length];
        var icons = new Image[barracks.products.Length];
        var costs = new Text[barracks.products.Length];
        var backs = new Image[barracks.products.Length];
        for (int i = 0; i < barracks.products.Length; i++)
        {
            buttons[i] = MakeButton("Produce" + i);
            icons[i] = MakeImage(buttons[i].transform, "Icon");
            costs[i] = MakeText(buttons[i].transform, "Cost");
            backs[i] = buttons[i].GetComponent<Image>();
        }

        Selection.selectedBuilding = barracks.GetComponent<Building>();

        var hud = new GameObject("Production population HUD").AddComponent<HUD>();
        hud.productionPanel = new GameObject("Production panel");
        hud.produceButtons = buttons;
        hud.produceIcons = icons;
        hud.produceCosts = costs;
        hud.produceBacks = backs;

        yield return null;

        Assert.IsFalse(buttons[index].interactable,
            "A warrior that would exceed population must grey out its product.");
        StringAssert.Contains("人口已满", costs[index].text,
            "The production button must name the population refusal.");
        Assert.AreEqual(hud.warnTint, costs[index].color);
    }

    [UnityTest]
    public IEnumerator UnitPanel_ShowsPopulationRefusal_WithoutRebuilding()
    {
        PaintRect(-16, -8, 48, 16);
        InstantiateFactory(BarracksPath, new Vector3Int(4, 0, 0));
        for (int i = 0; i < 6; i++)
            CreateNamedUnit(PawnPath, Team.Blue, new Vector3Int(-14 + i, 0, 0));
        for (int i = 0; i < 3; i++)
            CreateNamedUnit(WarriorPath, Team.Blue, new Vector3Int(-14 + i, 2, 0));
        Population.Invalidate();

        var warriorPrefab = LoadPrefab<GameObject>(WarriorPath);
        var warriorCost = new ResourceCost(80);
        Economy.gold = 1000;
        Assert.IsTrue(UnitLimits.CanProduce(Team.Blue, warriorPrefab, out _),
            "Control: the Phase A caps must not be what greys out the shop row.");
        Assert.IsTrue(Economy.CanAfford(warriorCost),
            "Control: the shop row must be affordable, or this could pass for the wrong reason.");
        Assert.AreEqual(12, Population.Used(Team.Blue),
            "Control: the side is exactly at its population capacity.");

        var button = MakeButton("Warrior offer");
        var back = button.GetComponent<Image>();
        var icon = MakeImage(button.transform, "Icon");
        var label = MakeText(button.transform, "Name");
        var cost = MakeText(button.transform, "Cost");

        var hud = new GameObject("Unit population HUD").AddComponent<HUD>();
        hud.unitPanel = new GameObject("Unit panel");
        hud.unitOffers = new[]
        {
            new HUD.UnitOffer
            {
                label = "战士",
                unitPrefab = warriorPrefab,
                cost = warriorCost,
            },
        };
        hud.unitButtons = new[] { button };
        hud.unitIcons = new[] { icon };
        hud.unitBacks = new[] { back };
        hud.unitNames = new[] { label };
        hud.unitCosts = new[] { cost };

        yield return null;

        Assert.IsFalse(button.interactable, "A warrior over the capacity must be greyed out.");
        StringAssert.Contains("人口已满", cost.text,
            "The unit shop must name the population refusal.");
        Assert.AreEqual(hud.warnTint, cost.color);
        Assert.AreEqual("战士", label.text, "The panel must stay bound without a rebuild.");
    }

    // -------------------------------------------------------------- helpers

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

    GameObject CreateHouse(string path, Vector3Int cell, Team team)
    {
        var go = InstantiateBuilding(path, cell, team);
        Population.Invalidate();
        return go;
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
}
