using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Phase B Task 5 — the balance numbers are asserted against the assets that ship, not
/// against the source text that was edited. A balance change that only lands in a C#
/// field comment (or that a prefab/scene override silently shadows) looks applied in
/// review but does nothing in the game, so every value here is read from the loaded
/// prefab, the loaded scene, or a freshly constructed component.
///
/// Covered: house cost 75 on both the player and AI house prefabs, the scene's EnemyAI
/// firstAttack 120 / maxGatherers 5, the Economy starting-gold default 600, the
/// gatherer's flee radius 6 with a 3 s resume, and the same two values on the worker
/// prefabs that actually spawn (a serialized prefab value overrides the C# default, so
/// asserting only the default would have been a green test over an inert change).
/// </summary>
public class BalanceValueTests
{
    const string ScenePath = "Assets/Scenes/SampleScene.scene";
    const string HouseBluePath = "Assets/Prefabs/Buildings/Knights/House_Blue.prefab";
    const string HouseRedPath = "Assets/Prefabs/Buildings/Knights/House_Red.prefab";
    const string PawnBluePath = "Assets/Prefabs/Units/Pawn_Blue.prefab";
    const string PawnRedPath = "Assets/Prefabs/Units/Pawn_Red.prefab";

    const float Tolerance = 0.001f;

    [Test]
    public void HousePrefabs_CostSeventyFiveGold()
    {
        var blue = AssetDatabase.LoadAssetAtPath<GameObject>(HouseBluePath);
        var red = AssetDatabase.LoadAssetAtPath<GameObject>(HouseRedPath);

        Assert.IsNotNull(blue, "Missing prefab " + HouseBluePath);
        Assert.IsNotNull(red, "Missing prefab " + HouseRedPath);

        var blueBuilding = blue.GetComponent<Building>();
        var redBuilding = red.GetComponent<Building>();
        Assert.IsNotNull(blueBuilding, HouseBluePath + " has no Building component.");
        Assert.IsNotNull(redBuilding, HouseRedPath + " has no Building component.");

        Assert.AreEqual(75, blueBuilding.cost.gold,
            "The player house must cost 75 gold (was 50).");
        Assert.AreEqual(75, redBuilding.cost.gold,
            "The AI house must cost 75 gold too; both sides were raised together.");
        Assert.AreEqual(0, blueBuilding.cost.wood, "The house balance change must not touch wood.");
        Assert.AreEqual(0, blueBuilding.cost.meat, "The house balance change must not touch meat.");
        Assert.AreEqual(0, redBuilding.cost.wood, "The house balance change must not touch wood.");
        Assert.AreEqual(0, redBuilding.cost.meat, "The house balance change must not touch meat.");
    }

    [UnityTest]
    public IEnumerator SceneEnemyAi_FirstAttack120AndFiveGatherers()
    {
        EditorSceneManager.LoadSceneInPlayMode(
            ScenePath,
            new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;

        var ai = Object.FindFirstObjectByType<AIController>();
        Assert.IsNotNull(ai, "SampleScene has no AIController; the EnemyAI object was removed.");
        Assert.AreEqual("EnemyAI", ai.gameObject.name,
            "The scene's AI controller is expected to be the EnemyAI object.");

        Assert.AreEqual(120f, ai.firstAttack, Tolerance,
            "EnemyAI.firstAttack must be 120 (was 90): the first wave is later so the opening is not rushed.");
        Assert.AreEqual(5, ai.maxGatherers,
            "EnemyAI.maxGatherers must be 5 (was 4).");

        // The other timings are deliberately unchanged; pin them so a stray edit elsewhere
        // cannot ride along with this balance pass unnoticed.
        Assert.AreEqual(45f, ai.attackEvery, Tolerance, "attackEvery must stay 45.");
        Assert.AreEqual(20f, ai.firstProduce, Tolerance, "firstProduce must stay 20.");
        Assert.AreEqual(12f, ai.produceEvery, Tolerance, "produceEvery must stay 12.");
    }

    [UnityTest]
    public IEnumerator Economy_StartingGoldDefaultIs600()
    {
        var go = new GameObject("EconomyDefaultProbe");
        var economy = go.AddComponent<Economy>();
        // Read before the end of the frame: Economy.Awake only claims the singleton, it
        // never rewrites the field, so this is the compiled field initializer.
        int gold = economy.gold;
        int wood = economy.wood;
        int meat = economy.meat;

        Object.Destroy(go);
        yield return null;

        Assert.AreEqual(600, gold, "Economy.gold must default to 600 (was 500).");
        Assert.AreEqual(300, wood, "The starting-gold change must not touch wood.");
        Assert.AreEqual(200, meat, "The starting-gold change must not touch meat.");
    }

    [UnityTest]
    public IEnumerator Gatherer_DefaultFleeRadiusIsSixWithThreeSecondResume()
    {
        var go = new GameObject("GathererDefaultProbe");
        var gatherer = go.AddComponent<Gatherer>();
        float radius = gatherer.fleeSafeRadius;
        float resume = gatherer.fleeResumeSeconds;

        Object.Destroy(go);
        yield return null;

        Assert.AreEqual(6f, radius, Tolerance,
            "Gatherer.fleeSafeRadius must default to 6 (was 10): workers stay at their post longer.");
        Assert.AreEqual(3f, resume, Tolerance,
            "Gatherer.fleeResumeSeconds must default to 3 (was 2).");
    }

    /// <summary>
    /// The shipped workers, not just the field default. Both pawn prefabs serialize
    /// fleeSafeRadius / fleeResumeSeconds, and a serialized value overrides the C#
    /// initializer at load — so changing only Gatherer.cs would have left every worker
    /// fleeing at 10 and resuming after 2. This asserts the asset the game actually
    /// spawns, and then that a spawned copy keeps the value.
    /// </summary>
    [UnityTest]
    public IEnumerator WorkerPrefabs_SpawnWithFleeRadiusSixAndThreeSecondResume()
    {
        foreach (var path in new[] { PawnBluePath, PawnRedPath })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, "Missing prefab " + path);
            var fromAsset = prefab.GetComponent<Gatherer>();
            Assert.IsNotNull(fromAsset, path + " has no Gatherer component.");

            Assert.AreEqual(6f, fromAsset.fleeSafeRadius, Tolerance,
                path + " must serialize fleeSafeRadius 6; a serialized prefab value overrides the C# default.");
            Assert.AreEqual(3f, fromAsset.fleeResumeSeconds, Tolerance,
                path + " must serialize fleeResumeSeconds 3.");

            var instance = Object.Instantiate(prefab);
            var spawned = instance.GetComponent<Gatherer>();
            Assert.IsNotNull(spawned, path + " spawned without a Gatherer component.");
            float radius = spawned.fleeSafeRadius;
            float resume = spawned.fleeResumeSeconds;
            Object.Destroy(instance);

            Assert.AreEqual(6f, radius, Tolerance,
                "A spawned worker from " + path + " must carry fleeSafeRadius 6.");
            Assert.AreEqual(3f, resume, Tolerance,
                "A spawned worker from " + path + " must carry fleeResumeSeconds 3.");
        }

        yield return null;
    }
}
