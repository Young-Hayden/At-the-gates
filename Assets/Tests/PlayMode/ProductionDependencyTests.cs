using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Guards the building/unit production dependency table: archers come from the
/// archery range only. Barracks must keep their infantry products while no longer
/// offering the archer, and both archery ranges must remain the archer factory.
/// </summary>
public class ProductionDependencyTests
{
    const string BarracksBluePath = "Assets/Prefabs/Buildings/Knights/Barracks_Blue.prefab";
    const string BarracksRedPath = "Assets/Prefabs/Buildings/Knights/Barracks_Red.prefab";
    const string ArcheryBluePath = "Assets/Prefabs/Buildings/Knights/Archery_Blue.prefab";
    const string ArcheryRedPath = "Assets/Prefabs/Buildings/Knights/Archery_Red.prefab";

    const string ArcherBluePath = "Assets/Prefabs/Units/Archer_Blue.prefab";
    const string ArcherRedPath = "Assets/Prefabs/Units/Archer_Red.prefab";
    const string WarriorBluePath = "Assets/Prefabs/Units/Warrior_Blue.prefab";
    const string WarriorRedPath = "Assets/Prefabs/Units/Warrior_Red.prefab";
    const string LancerBluePath = "Assets/Prefabs/Units/Lancer_Blue.prefab";
    const string LancerRedPath = "Assets/Prefabs/Units/Lancer_Red.prefab";

    [Test]
    public void Barracks_DoesNotProduceArchers()
    {
        Assert.AreEqual(-1, LoadFactory(BarracksBluePath).IndexOfProduct(LoadUnit(ArcherBluePath)),
            "Blue barracks must no longer offer the blue archer.");
        Assert.AreEqual(-1, LoadFactory(BarracksRedPath).IndexOfProduct(LoadUnit(ArcherRedPath)),
            "Red barracks must no longer offer the red archer.");
    }

    [Test]
    public void Barracks_KeepWarriorAndLancerProducts()
    {
        var blue = LoadFactory(BarracksBluePath);
        Assert.GreaterOrEqual(blue.IndexOfProduct(LoadUnit(WarriorBluePath)), 0,
            "Blue barracks must keep producing warriors.");
        Assert.GreaterOrEqual(blue.IndexOfProduct(LoadUnit(LancerBluePath)), 0,
            "Blue barracks must keep producing lancers.");

        var red = LoadFactory(BarracksRedPath);
        Assert.GreaterOrEqual(red.IndexOfProduct(LoadUnit(WarriorRedPath)), 0,
            "Red barracks must keep producing warriors.");
        Assert.GreaterOrEqual(red.IndexOfProduct(LoadUnit(LancerRedPath)), 0,
            "Red barracks must keep producing lancers.");
    }

    [Test]
    public void Archery_RemainsTheArcherFactory()
    {
        Assert.GreaterOrEqual(LoadFactory(ArcheryBluePath).IndexOfProduct(LoadUnit(ArcherBluePath)), 0,
            "Blue archery range must still produce the blue archer.");
        Assert.GreaterOrEqual(LoadFactory(ArcheryRedPath).IndexOfProduct(LoadUnit(ArcherRedPath)), 0,
            "Red archery range must still produce the red archer.");
    }

    static ProductionBuilding LoadFactory(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.IsNotNull(prefab, "Missing production building prefab: " + path);
        var factory = prefab.GetComponent<ProductionBuilding>();
        Assert.IsNotNull(factory, "Missing ProductionBuilding on: " + path);
        return factory;
    }

    static GameObject LoadUnit(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.IsNotNull(prefab, "Missing unit prefab: " + path);
        return prefab;
    }
}
