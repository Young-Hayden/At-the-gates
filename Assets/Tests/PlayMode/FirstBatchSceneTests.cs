using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class FirstBatchSceneTests : RegressionFixture
{
    [UnityTest]
    public IEnumerator ProductionPanel_HasBoundContent_AndRealFactoryCanQueue()
    {
        EditorSceneManager.LoadSceneInPlayMode(
            "Assets/Scenes/SampleScene.scene",
            new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;

        var hud = Object.FindFirstObjectByType<HUD>();
        Assert.IsNotNull(hud);
        Assert.AreEqual(3, hud.produceButtons.Length);
        for (int i = 0; i < 3; i++)
        {
            Assert.IsNotNull(hud.produceButtons[i], $"Produce{i} button");
            Assert.IsNotNull(hud.produceIcons[i], $"Produce{i} icon binding");
            Assert.IsNotNull(hud.produceCosts[i], $"Produce{i} cost binding");
        }

        var prefab = LoadPrefab<GameObject>("Assets/Prefabs/Buildings/Knights/Barracks_Blue.prefab");
        var factoryGO = Object.Instantiate(prefab, new Vector3(0f, 0f, 0f), Quaternion.identity);
        var building = factoryGO.GetComponent<Building>();
        var factory = factoryGO.GetComponent<ProductionBuilding>();
        Object.FindFirstObjectByType<SelectionManager>().selectedBuilding = building;
        yield return null;

        Assert.IsTrue(hud.productionPanel.activeSelf);
        Assert.IsTrue(hud.produceButtons[0].gameObject.activeSelf);
        Assert.IsNotNull(hud.produceIcons[0].sprite);
        Assert.IsNotEmpty(hud.produceCosts[0].text);
        hud.produceButtons[0].onClick.Invoke();
        Assert.AreEqual(1, factory.Queue.Count);
    }

    [UnityTest]
    public IEnumerator ProductionPanel_EmptyIcon_IsHiddenAndRecoversWhenSpriteArrives()
    {
        var factoryGO = new GameObject("Iconless factory", typeof(Building), typeof(ProductionBuilding));
        var factory = factoryGO.GetComponent<ProductionBuilding>();
        var productGO = new GameObject("Iconless product");
        factory.products = new[]
        {
            new ProductionBuilding.Product { unitPrefab = productGO, icon = null, cost = new ResourceCost(10) }
        };

        var panel = new GameObject("Production panel", typeof(RectTransform));
        var buttonGO = new GameObject("Produce0", typeof(RectTransform), typeof(Image), typeof(Button));
        var button = buttonGO.GetComponent<Button>();
        var iconGO = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGO.transform.SetParent(buttonGO.transform, false);
        var icon = iconGO.GetComponent<Image>();
        icon.enabled = true;
        var costGO = new GameObject("Cost", typeof(RectTransform), typeof(Text));
        costGO.transform.SetParent(buttonGO.transform, false);

        var hud = new GameObject("HUD under test").AddComponent<HUD>();
        hud.productionPanel = panel;
        hud.produceButtons = new[] { button };
        hud.produceIcons = new[] { icon };
        hud.produceCosts = new[] { costGO.GetComponent<Text>() };
        hud.produceBacks = new[] { buttonGO.GetComponent<Image>() };
        Selection.selectedBuilding = factoryGO.GetComponent<Building>();

        yield return null;
        Assert.IsFalse(icon.enabled, "A null product icon must not render an empty white Image.");

        var sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
        factory.products[0].icon = sprite;
        yield return null;
        Assert.IsTrue(icon.enabled, "The icon must become visible when a sprite is assigned.");
        Assert.AreSame(sprite, icon.sprite);

        factory.products[0].icon = null;
        yield return null;
        Assert.IsFalse(icon.enabled, "Removing the sprite must hide the Image again.");
    }

    /// <summary>
    /// The 1-6 quick-build shortcuts were a debug aid. `BuildingPlacer.quickBuild` shipped with
    /// six entries, so a player pressing a number key silently entered placement mode; the array
    /// is now empty, which makes `HandleQuickKeys` a no-op.
    ///
    /// This pins the shipped state, and it is demonstrably able to fail: the previous version of
    /// this test asserted six entries and went red the moment the array was cleared (that failure
    /// is what prompted this rewrite). Re-populating the array turns it red again.
    /// </summary>
    [UnityTest]
    public IEnumerator QuickBuild_DebugHotkeys_AreOffInTheShippedScene()
    {
        EditorSceneManager.LoadSceneInPlayMode(
            "Assets/Scenes/SampleScene.scene",
            new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;

        var placer = Object.FindFirstObjectByType<BuildingPlacer>();
        Assert.IsNotNull(placer, "SampleScene has no BuildingPlacer.");
        Assert.IsNotNull(placer.quickBuild, "quickBuild must be an empty array, not null.");
        Assert.AreEqual(0, placer.quickBuild.Length,
            "The 1-6 debug hotkeys must stay cleared; pressing a number key must not enter "
            + "placement mode in a shipped scene.");
    }
}
