using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// Phase A Task 1: per-profession and total combat caps. Living units, tower
/// garrisons and already-paid queue orders all occupy a slot; over-cap survivors
/// stay on the field but stop new production of their profession. Production must
/// refuse without spending, and the HUD must explain the refusal in place.
/// </summary>
public class UnitLimitTests : RegressionFixture
{
    const string WarriorPath = "Assets/Prefabs/Units/Warrior_Blue.prefab";
    const string ArcherPath = "Assets/Prefabs/Units/Archer_Blue.prefab";
    const string PawnPath = "Assets/Prefabs/Units/Pawn_Blue.prefab";
    const string LancerPath = "Assets/Prefabs/Units/Lancer_Blue.prefab";
    const string MonkPath = "Assets/Prefabs/Units/Monk_Blue.prefab";
    const string BarracksPath = "Assets/Prefabs/Buildings/Knights/Barracks_Blue.prefab";
    const string ArcheryPath = "Assets/Prefabs/Buildings/Knights/Archery_Blue.prefab";
    const string BarracksRedPath = "Assets/Prefabs/Buildings/Knights/Barracks_Red.prefab";
    const string TowerPath = "Assets/Prefabs/Buildings/Knights/Tower_Blue.prefab";

    static readonly string[] CombatPaths = { WarriorPath, ArcherPath, LancerPath, MonkPath };
    static readonly string[] RedCombatPaths =
    {
        "Assets/Prefabs/Units/Warrior_Red.prefab",
        "Assets/Prefabs/Units/Archer_Red.prefab",
        "Assets/Prefabs/Units/Lancer_Red.prefab",
        "Assets/Prefabs/Units/Monk_Red.prefab",
    };

    [Test]
    public void RoleOf_KnownPrefabs_MapsEveryProfession()
    {
        Assert.AreEqual(UnitRole.Worker, UnitLimits.RoleOf(LoadPrefab<GameObject>(PawnPath)));
        Assert.AreEqual(UnitRole.Warrior, UnitLimits.RoleOf(LoadPrefab<GameObject>(WarriorPath)));
        Assert.AreEqual(UnitRole.Lancer, UnitLimits.RoleOf(LoadPrefab<GameObject>(LancerPath)));
        Assert.AreEqual(UnitRole.Archer, UnitLimits.RoleOf(LoadPrefab<GameObject>(ArcherPath)));
        Assert.AreEqual(UnitRole.Monk, UnitLimits.RoleOf(LoadPrefab<GameObject>(MonkPath)));
    }

    [UnityTest]
    public IEnumerator TotalCombatCap_RefusesFourteenthUnitWithoutSpending()
    {
        PaintRect(-8, -8, 32, 24);
        for (int i = 0; i < UnitLimits.CombatCap; i++)
            CreateCombatant(Team.Blue, new Vector3Int(i % 8, i / 8, 0));
        UnitLimits.Invalidate();
        yield return null;

        var barracks = InstantiateFactory(BarracksPath, new Vector3Int(12, 0, 0));
        int index = barracks.IndexOfProduct(LoadPrefab<GameObject>(WarriorPath));
        Assert.GreaterOrEqual(index, 0, "The blue barracks must still offer a warrior.");
        int gold = Economy.gold;

        Assert.IsFalse(barracks.TryQueue(index), "A full combat roster must refuse the order.");
        Assert.AreEqual(0, barracks.Queue.Count);
        Assert.AreEqual(gold, Economy.gold, "A cap refusal must not spend resources.");
        Assert.IsFalse(barracks.CanQueue(index, true, out string reason));
        StringAssert.Contains("上限", reason);
    }

    [UnityTest]
    public IEnumerator RoleCap_BlocksOnlyThatProfession_AndOverCapUnitsRemain()
    {
        PaintRect(-8, -8, 32, 24);
        for (int i = 0; i < UnitLimits.WarriorCap; i++)
            CreateNamedUnit(WarriorPath, Team.Blue, new Vector3Int(i, 0, 0));
        UnitLimits.Invalidate();
        yield return null;

        Assert.AreEqual(UnitLimits.WarriorCap, UnitLimits.Count(Team.Blue, UnitRole.Warrior));
        Assert.IsFalse(UnitLimits.CanProduce(Team.Blue,
            LoadPrefab<GameObject>(WarriorPath), out _));
        Assert.IsTrue(UnitLimits.CanProduce(Team.Blue,
            LoadPrefab<GameObject>(ArcherPath), out _));
        Assert.AreEqual(UnitLimits.WarriorCap, UnitLimits.Count(Team.Blue, UnitRole.Warrior),
            "Existing over-cap units must not be deleted.");
    }

    [UnityTest]
    public IEnumerator OverCapSurvivors_RemainAndBlockOnlyTheirProfession()
    {
        PaintRect(-8, -8, 32, 24);
        var survivors = new UnitActor[UnitLimits.WarriorCap + 2];
        for (int i = 0; i < survivors.Length; i++)
            survivors[i] = CreateNamedUnit(WarriorPath, Team.Blue, new Vector3Int(i, 0, 0));
        UnitLimits.Invalidate();
        yield return null;

        Assert.AreEqual(survivors.Length, UnitLimits.Count(Team.Blue, UnitRole.Warrior),
            "Every survivor over the cap must still be counted, not deleted.");
        foreach (var survivor in survivors)
            Assert.IsTrue(survivor.IsAlive,
                "Going over the cap must never kill an existing unit.");

        Assert.IsFalse(UnitLimits.CanProduce(Team.Blue,
            LoadPrefab<GameObject>(WarriorPath), out string reason));
        StringAssert.Contains("上限", reason);
        Assert.IsTrue(UnitLimits.CanProduce(Team.Blue,
            LoadPrefab<GameObject>(ArcherPath), out _),
            "An over-cap warrior roster must not block another profession.");
    }

    [UnityTest]
    public IEnumerator WorkerCap_SixthWorkerAllowed_SeventhRefused()
    {
        PaintRect(-8, -8, 32, 24);
        for (int i = 0; i < UnitLimits.WorkerCap - 1; i++)
            CreateNamedUnit(PawnPath, Team.Blue, new Vector3Int(i, 0, 0));
        UnitLimits.Invalidate();
        yield return null;

        Assert.AreEqual(UnitLimits.WorkerCap - 1, UnitLimits.Count(Team.Blue, UnitRole.Worker));
        Assert.IsTrue(UnitLimits.CanProduce(Team.Blue, LoadPrefab<GameObject>(PawnPath), out _),
            "The sixth worker must still be allowed.");

        CreateNamedUnit(PawnPath, Team.Blue, new Vector3Int(UnitLimits.WorkerCap - 1, 0, 0));
        UnitLimits.Invalidate();
        yield return null;

        Assert.AreEqual(UnitLimits.WorkerCap, UnitLimits.Count(Team.Blue, UnitRole.Worker));
        Assert.IsFalse(UnitLimits.CanProduce(Team.Blue,
            LoadPrefab<GameObject>(PawnPath), out string reason));
        StringAssert.Contains("上限", reason);
    }

    [UnityTest]
    public IEnumerator LancerAndMonkCaps_BlockOnlyTheirOwnProfession()
    {
        PaintRect(-8, -8, 32, 24);
        for (int i = 0; i < UnitLimits.LancerCap; i++)
            CreateNamedUnit(LancerPath, Team.Blue, new Vector3Int(i, 0, 0));
        for (int i = 0; i < UnitLimits.MonkCap; i++)
            CreateNamedUnit(MonkPath, Team.Blue, new Vector3Int(i, 2, 0));
        UnitLimits.Invalidate();
        yield return null;

        Assert.AreEqual(UnitLimits.LancerCap, UnitLimits.Count(Team.Blue, UnitRole.Lancer));
        Assert.AreEqual(UnitLimits.MonkCap, UnitLimits.Count(Team.Blue, UnitRole.Monk));
        Assert.AreEqual(UnitLimits.LancerCap + UnitLimits.MonkCap,
            UnitLimits.TotalCombat(Team.Blue));

        Assert.IsFalse(UnitLimits.CanProduce(Team.Blue,
            LoadPrefab<GameObject>(LancerPath), out string lancerReason));
        StringAssert.Contains("上限", lancerReason);
        Assert.IsFalse(UnitLimits.CanProduce(Team.Blue,
            LoadPrefab<GameObject>(MonkPath), out string monkReason));
        StringAssert.Contains("上限", monkReason);
        Assert.IsTrue(UnitLimits.CanProduce(Team.Blue,
            LoadPrefab<GameObject>(WarriorPath), out _),
            "Warriors stay legal while lancer and monk are individually capped.");
    }

    [UnityTest]
    public IEnumerator TowerGarrison_CountsArcherOnce_AndRemovesItFromActorAll()
    {
        PaintRect(-8, -8, 32, 24);
        var tower = InstantiateBuilding(TowerPath, new Vector3Int(4, 0, 0), Team.Blue);
        var archer = CreateNamedUnit(ArcherPath, Team.Blue, new Vector3Int(2, 0, 0));
        yield return null;

        var garrison = tower.GetComponent<TowerGarrison>();
        Assert.IsNotNull(garrison, TowerPath + " must carry a TowerGarrison.");
        Assert.IsTrue(garrison.Garrison(archer), "The tower must accept the archer.");
        UnitLimits.Invalidate();

        Assert.AreEqual(1, UnitLimits.Count(Team.Blue, UnitRole.Archer),
            "A garrisoned archer must occupy exactly one archer slot.");
        Assert.IsFalse(UnitActor.All.Contains(archer),
            "The disabled garrisoned archer must leave UnitActor.All, so it cannot be double counted.");

        archer.health = 0;
        UnitLimits.Invalidate();
        Assert.AreEqual(0, UnitLimits.Count(Team.Blue, UnitRole.Archer),
            "A dead garrisoned archer must not occupy a slot.");
    }

    [UnityTest]
    public IEnumerator BlueQueue_DoesNotConsumeRedCount()
    {
        PaintRect(-8, -8, 32, 24);
        var blue = InstantiateFactory(ArcheryPath, new Vector3Int(2, 0, 0), Team.Blue);
        int index = blue.IndexOfProduct(LoadPrefab<GameObject>(ArcherPath));
        Assert.GreaterOrEqual(index, 0, "The blue archery range must offer an archer.");
        Assert.IsTrue(blue.TryQueue(index, payCost: false));
        UnitLimits.Invalidate();
        yield return null;

        Assert.AreEqual(1, UnitLimits.Count(Team.Blue, UnitRole.Archer));
        Assert.AreEqual(0, UnitLimits.Count(Team.Red, UnitRole.Archer),
            "Blue's queued archer must not consume Red's allowance.");
        Assert.IsTrue(UnitLimits.CanProduce(Team.Red,
            LoadPrefab<GameObject>(ArcherPath), out _));
    }

    [UnityTest]
    public IEnumerator QueuedUnits_CountOnceBeforeSpawn_AcrossFactories()
    {
        PaintRect(-8, -8, 32, 24);
        var first = InstantiateFactory(ArcheryPath, new Vector3Int(2, 0, 0));
        var second = InstantiateFactory(ArcheryPath, new Vector3Int(10, 0, 0));
        var archerPrefab = LoadPrefab<GameObject>(ArcherPath);
        int firstIndex = first.IndexOfProduct(archerPrefab);
        int secondIndex = second.IndexOfProduct(archerPrefab);
        Assert.GreaterOrEqual(firstIndex, 0, "The first archery range must offer an archer.");
        Assert.GreaterOrEqual(secondIndex, 0, "The second archery range must offer an archer.");

        UnitLimits.Invalidate();
        yield return null;
        Assert.AreEqual(0, UnitLimits.Count(Team.Blue, UnitRole.Archer));

        Assert.IsTrue(first.TryQueue(firstIndex, payCost: false));
        Assert.AreEqual(1, UnitLimits.Count(Team.Blue, UnitRole.Archer),
            "A paid queue order counts as one archer before it spawns.");
        Assert.AreEqual(1, UnitLimits.TotalCombat(Team.Blue));

        Assert.IsTrue(second.TryQueue(secondIndex, payCost: false));
        Assert.AreEqual(2, UnitLimits.Count(Team.Blue, UnitRole.Archer),
            "Orders in different factories must both count exactly once.");
        Assert.AreEqual(2, UnitLimits.TotalCombat(Team.Blue));

        yield return null;                                  // still building, nothing spawned
        Assert.AreEqual(2, first.Queue.Count + second.Queue.Count);
        Assert.AreEqual(2, UnitLimits.Count(Team.Blue, UnitRole.Archer),
            "A later frame must not double count the same queue or drop it.");
    }

    [UnityTest]
    public IEnumerator QueuedUnits_InDisabledUnderConstructionFactory_AreCountedOnce()
    {
        PaintRect(-8, -8, 32, 24);
        var factory = InstantiateFactory(ArcheryPath, new Vector3Int(12, 0, 0));
        var building = factory.GetComponent<Building>();
        building.MarkBuiltDuringMatch();                  // disables ProductionBuilding
        yield return null;

        Assert.IsTrue(building.IsUnderConstruction, "The fixture factory must be building.");
        Assert.IsFalse(factory.enabled, "An under-construction factory is disabled.");
        Assert.GreaterOrEqual(factory.IndexOfProduct(LoadPrefab<GameObject>(ArcherPath)), 0);

        factory.RestoreSaveState(new ProductionBuilding.SaveState
        {
            queue = new List<int> { factory.IndexOfProduct(LoadPrefab<GameObject>(ArcherPath)) },
            remaining = 5f,
        });
        UnitLimits.Invalidate();
        Assert.AreEqual(1, UnitLimits.Count(Team.Blue, UnitRole.Archer),
            "A paid order in a disabled, under-construction factory must count once.");
        Assert.AreEqual(1, UnitLimits.TotalCombat(Team.Blue));

        yield return null;
        Assert.AreEqual(1, UnitLimits.Count(Team.Blue, UnitRole.Archer),
            "The disabled factory's order must remain counted on later frames.");
    }

    [UnityTest]
    public IEnumerator Caps_ArePerTeam_AndRefuseAiOwnedFactoryWithoutSpending()
    {
        PaintRect(-8, -8, 32, 24);
        for (int i = 0; i < UnitLimits.CombatCap; i++)
            CreateCombatant(Team.Red, RedCombatPaths, new Vector3Int(i % 8, i / 8, 0));
        UnitLimits.Invalidate();
        yield return null;

        Assert.IsFalse(UnitLimits.CanProduce(Team.Red,
            LoadPrefab<GameObject>(RedCombatPaths[0]), out string redReason));
        StringAssert.Contains("上限", redReason);
        Assert.IsTrue(UnitLimits.CanProduce(Team.Blue,
            LoadPrefab<GameObject>(WarriorPath), out _),
            "A full Red roster must not consume the Blue allowance.");

        var barracks = InstantiateFactory(BarracksRedPath, new Vector3Int(12, 0, 0), Team.Red);
        int index = barracks.IndexOfProduct(LoadPrefab<GameObject>(RedCombatPaths[0]));
        Assert.GreaterOrEqual(index, 0, "The red barracks must offer a red warrior.");
        Assert.IsFalse(barracks.TryQueue(index, payCost: false),
            "The AI production path reaches the same CanQueue and must obey the Red cap.");
        Assert.AreEqual(0, barracks.Queue.Count);
    }

    [UnityTest]
    public IEnumerator UnitPanel_ShowsCapRefusal_WithoutRebuilding()
    {
        PaintRect(-8, -8, 32, 24);
        InstantiateFactory(ArcheryPath, new Vector3Int(12, 0, 0));
        for (int i = 0; i < UnitLimits.ArcherCap; i++)
            CreateNamedUnit(ArcherPath, Team.Blue, new Vector3Int(i, 0, 0));
        UnitLimits.Invalidate();

        var archerCost = new ResourceCost(65);
        Economy.gold = 1000;
        Assert.IsTrue(Economy.CanAfford(archerCost),
            "The cap control needs affordable gold, or it could pass by being broke.");

        var button = MakeButton("Archer offer");
        var back = button.GetComponent<Image>();
        var icon = MakeImage(button.transform, "Icon");
        var label = MakeText(button.transform, "Name");
        var cost = MakeText(button.transform, "Cost");

        var hud = new GameObject("Unit cap HUD").AddComponent<HUD>();
        hud.unitPanel = new GameObject("Unit panel");
        hud.unitOffers = new[]
        {
            new HUD.UnitOffer
            {
                label = "弓手",
                unitPrefab = LoadPrefab<GameObject>(ArcherPath),
                cost = archerCost,
            },
        };
        hud.unitButtons = new[] { button };
        hud.unitIcons = new[] { icon };
        hud.unitBacks = new[] { back };
        hud.unitNames = new[] { label };
        hud.unitCosts = new[] { cost };

        yield return null;

        Assert.IsFalse(button.interactable, "A profession at its cap must be greyed out.");
        StringAssert.Contains("上限", cost.text, "The unit shop must name the cap refusal.");
        Assert.AreEqual(hud.warnTint, cost.color);
        Assert.AreEqual("弓手", label.text, "The panel must stay bound without a rebuild.");
    }

    [UnityTest]
    public IEnumerator ProductionPanel_ShowsCapRefusal_InCostText()
    {
        PaintRect(-8, -8, 32, 24);
        var barracks = InstantiateFactory(BarracksPath, new Vector3Int(12, 0, 0));
        for (int i = 0; i < UnitLimits.CombatCap; i++)
            CreateCombatant(Team.Blue, new Vector3Int(i % 8, i / 8, 0));
        UnitLimits.Invalidate();

        Economy.gold = 1000;
        Assert.GreaterOrEqual(barracks.products.Length, 1);
        Assert.IsTrue(Economy.CanAfford(barracks.products[0].cost),
            "The cap control needs affordable gold, or it could pass by being broke.");

        var button = MakeButton("Produce0");
        var back = button.GetComponent<Image>();
        var icon = MakeImage(button.transform, "Icon");
        var cost = MakeText(button.transform, "Cost");

        Selection.selectedBuilding = barracks.GetComponent<Building>();

        var hud = new GameObject("Production cap HUD").AddComponent<HUD>();
        hud.productionPanel = new GameObject("Production panel");
        hud.produceButtons = new[] { button };
        hud.produceIcons = new[] { icon };
        hud.produceCosts = new[] { cost };
        hud.produceBacks = new[] { back };

        yield return null;

        Assert.IsFalse(button.interactable, "A capped factory must grey out the product.");
        StringAssert.Contains("上限", cost.text, "The production button must explain the cap.");
        Assert.AreEqual(hud.warnTint, cost.color);
    }

    [UnityTest]
    public IEnumerator RosterCache_SurvivesFrames_AndDeathInvalidatesImmediately()
    {
        PaintRect(-8, -8, 16, 16);
        UnitLimits.Invalidate();
        var unit = CreateNamedUnit(WarriorPath, Team.Blue, new Vector3Int(0, 0, 0));
        Assert.AreEqual(1, UnitLimits.Count(Team.Blue, UnitRole.Warrior));

        var field = typeof(UnitLimits).GetField("cache",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(field, "The cache test must inspect the real roster cache.");
        var cache = (Dictionary<Team, Dictionary<UnitRole, int>>)field.GetValue(null);
        var first = cache[Team.Blue];

        yield return null;
        Assert.AreEqual(1, UnitLimits.Count(Team.Blue, UnitRole.Warrior));
        var second = cache[Team.Blue];
        Assert.AreSame(first, second, "An unchanged roster must not be rebuilt every frame.");

        unit.TakeDamage(unit.maxHealth + 1000);
        Assert.IsFalse(unit.IsAlive, "The test unit must really die.");
        Assert.AreEqual(0, UnitLimits.Count(Team.Blue, UnitRole.Warrior),
            "Death must invalidate the roster in the same frame, even with a cross-frame cache.");
        var third = cache[Team.Blue];
        Assert.AreNotSame(second, third, "The death must rebuild the invalidated dictionary.");
    }

    // ---------------------------------------------------------------- helpers

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

    UnitActor CreateCombatant(Team team, Vector3Int cell)
    {
        return CreateCombatant(team, CombatPaths, cell);
    }

    UnitActor CreateCombatant(Team team, string[] paths, Vector3Int cell)
    {
        // Cycle all four professions so no single role cap is hit before the
        // shared combat cap (14 across 4 roles: 4/4/3/3).
        string path = paths[Mathf.Abs(cell.x + cell.y * 8) % paths.Length];
        return CreateNamedUnit(path, team, cell);
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
