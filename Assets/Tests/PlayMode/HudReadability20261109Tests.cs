using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class HudReadability20261109Tests
{
    static readonly Color MainTint = new Color(1f, 0.96f, 0.86f, 1f);

    [UnityTest]
    public IEnumerator TargetPanels_UseDarkReadableBackings()
    {
        yield return LoadSample();
        var hud = Object.FindFirstObjectByType<HUD>();
        AssertBacking(hud.goldText.transform.parent.gameObject, "ResourceBar", 8f);
        AssertBacking(hud.buildButtons[0].transform.parent.gameObject, "BuildBar", 6f);
        AssertBacking(hud.unitPanel, "UnitPanel", 6f);
        AssertBacking(hud.selectionPanel, "SelectionPanel", 6f);
        AssertBacking(hud.productionPanel, "ProductionPanel", 6f);
    }

    [UnityTest]
    public IEnumerator UnitLabels_UseReadableSizes_AndStillFit()
    {
        yield return LoadSample();
        var hud = Object.FindFirstObjectByType<HUD>();
        for (int i = 0; i < 5; i++)
        {
            var name = hud.unitNames[i];
            var cost = hud.unitCosts[i];
            Assert.AreEqual(14, name.fontSize, "UnitName " + i);
            Assert.AreEqual(13, cost.fontSize, "UnitCost " + i);
            Assert.LessOrEqual(name.preferredWidth, name.rectTransform.rect.width + 0.01f);
            Assert.LessOrEqual(name.preferredHeight, name.rectTransform.rect.height + 0.01f);
            Assert.LessOrEqual(cost.preferredWidth, cost.rectTransform.rect.width + 0.01f);
            Assert.LessOrEqual(cost.preferredHeight, cost.rectTransform.rect.height + 0.01f);
        }
    }

    [UnityTest]
    public IEnumerator ProductionTitle_UsesChineseBuildingName()
    {
        yield return LoadSample();
        var hud = Object.FindFirstObjectByType<HUD>();
        var selection = Object.FindFirstObjectByType<SelectionManager>();
        SpawnBuilding("Assets/Prefabs/Buildings/Knights/Barracks_Blue.prefab", new Vector3(-18, 8, 0));
        var barracks = GameObject.Find("Barracks_Blue").GetComponent<Building>();
        selection.selected.Clear();
        selection.selectedBuilding = barracks;
        yield return null;
        yield return null;
        StringAssert.StartsWith("兵营", hud.productionTitle.text);
    }

    [UnityTest]
    public IEnumerator NormalUnitCost_KeepsMainTint_AfterRuntimeRefresh()
    {
        yield return LoadSample();
        var hud = Object.FindFirstObjectByType<HUD>();
        SpawnBuilding("Assets/Prefabs/Buildings/Knights/Castle_Blue.prefab", new Vector3(-15, 10, 0));
        SpawnBuilding("Assets/Prefabs/Buildings/Knights/Barracks_Blue.prefab", new Vector3(-18, 8, 0));
        // Let HUD's 0.3-second factory cache observe the spawned Barracks.
        yield return new WaitForSeconds(0.35f);
        yield return null;
        yield return null;
        yield return null;
        Assert.AreEqual(hud.unitOffers[0].cost.ToString(), hud.unitCosts[0].text);
        AssertColor(MainTint, hud.unitCosts[0].color);
    }

    static void AssertBacking(GameObject panel, string name, float expectedInset)
    {
        Assert.IsNotNull(panel, name);
        var backing = panel.transform.Find("Backing") as RectTransform;
        Assert.IsNotNull(backing, name + "/Backing");
        var image = backing.GetComponent<Image>();
        Assert.IsNotNull(image);
        Assert.AreEqual(0.58f, image.color.a, 0.001f, name + " backing alpha");
        Assert.AreEqual(new Vector2(expectedInset, expectedInset), backing.offsetMin, name + " inset min");
        Assert.AreEqual(new Vector2(-expectedInset, -expectedInset), backing.offsetMax, name + " inset max");
        Assert.AreEqual(0, backing.GetSiblingIndex(), name + " backing must be behind content");
        Assert.IsFalse(image.raycastTarget, name + " backing must not steal clicks");
    }

    static Building SpawnBuilding(string path, Vector3 position)
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.IsNotNull(prefab, path);
        var go = Object.Instantiate(prefab, position, Quaternion.identity);
        go.name = prefab.name;
        var building = go.GetComponent<Building>();
        building.Recompute();
        return building;
    }

    static void AssertColor(Color expected, Color actual)
    {
        Assert.AreEqual(expected.r, actual.r, 0.001f);
        Assert.AreEqual(expected.g, actual.g, 0.001f);
        Assert.AreEqual(expected.b, actual.b, 0.001f);
        Assert.AreEqual(expected.a, actual.a, 0.001f);
    }

    static IEnumerator LoadSample()
    {
        Time.timeScale = 1f;
        EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/SampleScene.scene",
            new UnityEngine.SceneManagement.LoadSceneParameters(
                UnityEngine.SceneManagement.LoadSceneMode.Single));
        yield return null;
        yield return null;
        foreach (var ai in Object.FindObjectsByType<AIController>(FindObjectsSortMode.None))
            ai.enabled = false;
    }
}
