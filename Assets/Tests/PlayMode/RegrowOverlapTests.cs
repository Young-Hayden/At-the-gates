using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// C1 regression: a depleted tree must not regrow its blocking footprint into a
/// building placed over its stump. It should stay depleted while occupied and resume
/// normally once the building is gone.
/// </summary>
public class RegrowOverlapTests : RegressionFixture
{
    const string TreePath = "Assets/Prefabs/Resources/Tree1.prefab";
    const string HousePath = "Assets/Prefabs/Buildings/Knights/House_Blue.prefab";

    [UnityTest]
    public IEnumerator RegrowingTree_DefersWhileBuildingOccupiesCell_AndResumesAfterDemolition()
    {
        PaintRect(-4, -4, 12, 12);
        var cell = new Vector3Int(0, 0, 0);
        var tree = Object.Instantiate(LoadPrefab<GameObject>(TreePath),
            Grid.CellCenter(cell) - new Vector3(0f, 0.5f, 0f), Quaternion.identity);
        tree.name = "Regrow tree";
        var node = tree.GetComponent<ResourceNode>();
        var treeBuilding = tree.GetComponent<Building>();
        node.regrowSeconds = 0.2f;
        node.amount = 80;
        node.maxAmount = 80;
        yield return null;

        Assert.AreEqual(80, node.Harvest(80));
        Assert.IsTrue(node.IsRegrowing, "The harvested tree must enter its regrow state.");
        Assert.IsFalse(treeBuilding.blocksMovement, "A stump must stop blocking until it regrows.");

        var house = Object.Instantiate(LoadPrefab<GameObject>(HousePath),
            Grid.CellCenter(cell) - new Vector3(0f, 0.5f, 0f), Quaternion.identity);
        house.name = "Building over stump";
        house.GetComponent<Building>().Recompute();
        yield return new WaitForSeconds(1.2f);

        Assert.AreEqual(0, node.amount,
            "A tree must not regrow into a building placed over its stump.");
        Assert.IsTrue(node.IsRegrowing,
            "The deferred tree must keep its regrow state while the cell is occupied.");

        Object.Destroy(house);
        yield return null;
        yield return new WaitForSeconds(1.2f);

        Assert.AreEqual(80, node.amount,
            "Once the cell is free again, the tree must finish regrowing.");
        Assert.IsFalse(node.IsRegrowing);
        Assert.IsTrue(treeBuilding.blocksMovement,
            "A regrown tree must restore its blocking footprint.");
    }
}
