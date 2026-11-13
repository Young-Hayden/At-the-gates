using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// AI composition (phase 1). The AI used to buy the cheapest affordable product in every
/// factory, so the barracks only ever produced warriors (80 gold) and never a lancer (110).
/// It now picks the profession furthest below its share of the army, weighted per role.
///
/// These tests drive <see cref="AIController.ChooseBest"/> directly with a candidate list, so
/// the scoring is checked without waiting for the AI's production timer.
/// </summary>
public class AiCompositionTests : RegressionFixture
{
    const string BarracksPath = "Assets/Prefabs/Buildings/Knights/Barracks_Blue.prefab";
    const string WarriorPath = "Assets/Prefabs/Units/Warrior_Blue.prefab";
    const string LancerPath = "Assets/Prefabs/Units/Lancer_Blue.prefab";

    [UnityTest]
    public IEnumerator ChooseBest_PicksTheRoleFurthestBelowItsWeight()
    {
        PaintRect(-8, -8, 32, 24);
        var barracks = InstantiateFactory(BarracksPath, new Vector3Int(4, 0, 0));
        var ai = MakeAi(Team.Blue);
        ai.warriorWeight = 1f;
        ai.lancerWeight = 3f;

        // One warrior, no lancer: with weight 3 the lancer is far below its share even
        // though it is the pricier product. This is exactly the case the old
        // "cheapest wins" rule got wrong.
        CreateSoldier(WarriorPath, Team.Blue, new Vector3Int(-4, 0, 0));
        UnitLimits.Invalidate();
        yield return null;

        var candidates = OfferableProducts(barracks);
        Assert.GreaterOrEqual(candidates.Count, 2,
            "Barracks_Blue must offer a warrior and a lancer for this check.");

        int pick = ai.ChooseBest(barracks, candidates);
        Assert.GreaterOrEqual(pick, 0, "A legal candidate must always be chosen.");
        Assert.AreEqual(UnitRole.Lancer, UnitLimits.RoleOf(barracks.products[pick].unitPrefab),
            "The under-represented role must win even though it costs more.");
    }

    [UnityTest]
    public IEnumerator ChooseBest_TiesBreakTowardTheCheaperProduct()
    {
        PaintRect(-8, -8, 32, 24);
        var barracks = InstantiateFactory(BarracksPath, new Vector3Int(4, 0, 0));
        var ai = MakeAi(Team.Blue);
        ai.warriorWeight = 2f;
        ai.lancerWeight = 2f;

        UnitLimits.Invalidate();
        yield return null;   // empty roster: both deficits are identical

        var candidates = OfferableProducts(barracks);
        int pick = ai.ChooseBest(barracks, candidates);
        Assert.AreEqual(UnitRole.Warrior, UnitLimits.RoleOf(barracks.products[pick].unitPrefab),
            "On a tie the cheaper product wins so a thin bank still produces something.");
    }

    [UnityTest]
    public IEnumerator ChooseBest_NeverReturnsMinusOne_WhenEveryRoleIsOverTarget()
    {
        PaintRect(-12, -12, 40, 30);
        var barracks = InstantiateFactory(BarracksPath, new Vector3Int(12, 0, 0));
        var ai = MakeAi(Team.Blue);
        ai.warriorWeight = 1f;
        ai.lancerWeight = 1f;

        // Deliberately an army far bigger than the weights imply, so every deficit is
        // negative; the factory must still pick something rather than idle.
        for (int i = 0; i < 6; i++) CreateSoldier(WarriorPath, Team.Blue, new Vector3Int(-10 + i, 0, 0));
        for (int i = 0; i < 4; i++) CreateSoldier(LancerPath, Team.Blue, new Vector3Int(-10 + i, 2, 0));
        UnitLimits.Invalidate();
        yield return null;

        var candidates = OfferableProducts(barracks);
        int pick = ai.ChooseBest(barracks, candidates);
        Assert.GreaterOrEqual(pick, 0,
            "An over-target roster must still produce: the least-negative deficit wins.");
    }

    /// <summary>
    /// The live game caught this one. The first version of the scoring used
    /// <c>weight x armySize</c> without normalising by the total weight, so a heavier role
    /// always out-scored a lighter one: the barracks produced seven warriors to one lancer.
    /// Targets are shares of the army, so a lancer that is behind catches up even while
    /// warriors are numerous.
    /// </summary>
    [UnityTest]
    public IEnumerator ChooseBest_NormalisesByTotalWeight_SoALightRoleCatchesUp()
    {
        PaintRect(-12, -12, 40, 30);
        var barracks = InstantiateFactory(BarracksPath, new Vector3Int(12, 0, 0));
        var ai = MakeAi(Team.Blue);
        ai.warriorWeight = 3f;
        ai.lancerWeight = 2f;

        for (int i = 0; i < 7; i++) CreateSoldier(WarriorPath, Team.Blue, new Vector3Int(-10 + i, 0, 0));
        CreateSoldier(LancerPath, Team.Blue, new Vector3Int(-10, 2, 0));
        UnitLimits.Invalidate();
        yield return null;

        var candidates = OfferableProducts(barracks);
        int pick = ai.ChooseBest(barracks, candidates);
        Assert.AreEqual(UnitRole.Lancer, UnitLimits.RoleOf(barracks.products[pick].unitPrefab),
            "Seven warriors against one lancer: the lancer is far below its share and must come next.");
    }

    [UnityTest]
    public IEnumerator ChooseBest_IgnoresProductsWithoutAPrefab()
    {
        PaintRect(-8, -8, 32, 24);
        var barracks = InstantiateFactory(BarracksPath, new Vector3Int(4, 0, 0));
        var ai = MakeAi(Team.Blue);
        yield return null;

        var candidates = new List<int>();
        for (int i = 0; i < barracks.products.Length; i++) candidates.Add(i);
        barracks.products[0].unitPrefab = null;      // simulate a misconfigured product

        int pick = ai.ChooseBest(barracks, candidates);
        Assert.AreNotEqual(0, pick, "A product without a prefab must never be selected.");
        Assert.GreaterOrEqual(pick, 0, "The remaining products must still be usable.");
    }

    // ------------------------------------------------------------------ helpers

    AIController MakeAi(Team team)
    {
        var ai = new GameObject("AI composition probe").AddComponent<AIController>();
        ai.active = false;          // never let the probe act on its own
        ai.team = team;
        return ai;
    }

    UnitActor CreateSoldier(string path, Team team, Vector3Int cell)
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

    ProductionBuilding InstantiateFactory(string path, Vector3Int cell)
    {
        var prefab = LoadPrefab<GameObject>(path);
        var go = Object.Instantiate(prefab, Grid.CellCenter(cell), Quaternion.identity);
        var building = go.GetComponent<Building>();
        Assert.IsNotNull(building, path + " must carry a Building.");
        building.team = Team.Blue;
        building.Recompute();
        var factory = go.GetComponent<ProductionBuilding>();
        Assert.IsNotNull(factory, path + " must carry a ProductionBuilding.");
        return factory;
    }

    static List<int> OfferableProducts(ProductionBuilding factory)
    {
        var list = new List<int>();
        for (int i = 0; i < factory.products.Length; i++)
            if (factory.products[i].unitPrefab != null) list.Add(i);
        return list;
    }
}
