using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>Scene-level regressions for the targeted 2026-11-08 UI polish.</summary>
public class UiPolish20261108Tests
{
    [UnityTest]
    public IEnumerator SelectionTitles_UseChineseNames()
    {
        yield return LoadScene("Assets/Scenes/SampleScene.scene");
        var selection = Object.FindFirstObjectByType<SelectionManager>();
        var hud = Object.FindFirstObjectByType<HUD>();
        Assert.IsNotNull(selection);
        Assert.IsNotNull(hud);

        var warrior = UnitActor.All.FirstOrDefault(u => u != null && u.IsAlive &&
            u.team == Team.Blue && u.name.Contains("Warrior"));
        Assert.IsNotNull(warrior, "SampleScene must contain a blue warrior for the title check.");
        selection.selectedBuilding = null;
        selection.selected.Clear();
        selection.selected.Add(warrior.GetComponent<UnitMover>());
        yield return null;
        yield return null;
        Assert.AreEqual("战士", hud.selectionTitle.text);

        var housePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Buildings/Knights/House_Blue.prefab");
        Assert.IsNotNull(housePrefab);
        var house = Object.Instantiate(housePrefab, Vector3.zero, Quaternion.identity)
            .GetComponent<Building>();
        selection.selected.Clear();
        selection.selectedBuilding = house;
        yield return null;
        yield return null;
        Assert.AreEqual("民房", hud.selectionTitle.text);

        var unknown = new GameObject("Unknown custom unit");
        var actor = unknown.AddComponent<UnitActor>();
        actor.team = Team.Blue;
        actor.showHealthBar = false;
        actor.DisplayName = "自定义卫兵";
        selection.selectedBuilding = null;
        selection.selected.Clear();
        selection.selected.Add(actor.GetComponent<UnitMover>());
        yield return null;
        yield return null;
        Assert.AreEqual("自定义卫兵", hud.selectionTitle.text,
            "Unknown units must retain their existing display-name fallback.");
    }

    [UnityTest]
    public IEnumerator MainMenuRules_DoNotAdvertiseRemovedQuickBuild()
    {
        yield return LoadScene("Assets/Scenes/MainMenu.scene");
        var texts = Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Assert.IsFalse(texts.Any(t => t.text.Contains("1~6")),
            "The main menu must not advertise removed number-key building shortcuts.");
        Assert.IsTrue(texts.Any(t => t.text.Contains("点击建造栏")),
            "The replacement must tell the player to use the build bar.");
    }

    [UnityTest]
    public IEnumerator UnitPanel_CostNameAndButtonRects_DoNotOverlap_AndClearBottom()
    {
        yield return LoadScene("Assets/Scenes/SampleScene.scene");
        var hud = Object.FindFirstObjectByType<HUD>();
        Assert.IsNotNull(hud);
        for (int i = 0; i < 5; i++)
        {
            var cost = hud.unitCosts[i];
            var name = hud.unitNames[i];
            var button = hud.unitButtons[i].GetComponent<RectTransform>();
            Assert.IsNotNull(cost);
            Assert.IsNotNull(name);
            Assert.IsNotNull(button);

            var costRt = cost.rectTransform;
            var nameRt = name.rectTransform;
            Assert.GreaterOrEqual(costRt.anchoredPosition.y, 10f,
                "Cost " + i + " must clear the panel's bottom frame.");
            float costTop = costRt.anchoredPosition.y + costRt.rect.height;
            float nameBottom = nameRt.anchoredPosition.y;
            Assert.LessOrEqual(costTop, nameBottom,
                "Cost " + i + " must not overlap its unit name.");
            float nameTop = nameRt.anchoredPosition.y + nameRt.rect.height;
            float buttonBottom = button.anchoredPosition.y;
            Assert.LessOrEqual(nameTop, buttonBottom,
                "Name " + i + " must not overlap the portrait button.");
            Assert.LessOrEqual(cost.preferredHeight, costRt.rect.height + 0.01f,
                "Cost text " + i + " must fit vertically.");
            Assert.LessOrEqual(name.preferredHeight, nameRt.rect.height + 0.01f,
                "Name text " + i + " must fit vertically.");
        }
    }

    [UnityTest]
    public IEnumerator AttackLabel_KeepsRightInset()
    {
        yield return LoadScene("Assets/Scenes/SampleScene.scene");
        var hud = Object.FindFirstObjectByType<HUD>();
        Assert.IsNotNull(hud);
        Assert.IsNotNull(hud.attackButton);
        var button = hud.attackButton.GetComponent<RectTransform>();
        var label = button.Find("Label") as RectTransform;
        var icon = button.Find("Icon") as RectTransform;
        Assert.IsNotNull(label);
        Assert.IsNotNull(icon);

        var buttonCorners = new Vector3[4];
        var labelCorners = new Vector3[4];
        var iconCorners = new Vector3[4];
        button.GetWorldCorners(buttonCorners);
        label.GetWorldCorners(labelCorners);
        icon.GetWorldCorners(iconCorners);
        // The Canvas uses a 1920x1080 reference resolution. At 1280x720 its scale is 2/3,
        // so a six-pixel local inset measures four pixels in world/screen space. Transform
        // both actual corners back into button-local space to remove CanvasScaler from the
        // measurement while retaining the geometric assertion rather than trusting offsetMax.
        var localButtonTopRight = button.InverseTransformPoint(buttonCorners[2]);
        var localLabelTopRight = button.InverseTransformPoint(labelCorners[2]);
        Assert.GreaterOrEqual(localButtonTopRight.x - localLabelTopRight.x, 5.99f,
            "The attack label must retain a six-pixel right inset.");
        Assert.GreaterOrEqual(labelCorners[0].x, iconCorners[2].x - 0.01f,
            "The attack label must not overlap the icon.");
    }

    static IEnumerator LoadScene(string path)
    {
        Time.timeScale = 1f;
        EditorSceneManager.LoadSceneInPlayMode(path,
            new UnityEngine.SceneManagement.LoadSceneParameters(
                UnityEngine.SceneManagement.LoadSceneMode.Single));
        yield return null;
        yield return null;
        foreach (var ai in Object.FindObjectsByType<AIController>(FindObjectsSortMode.None))
            ai.active = false;
    }
}
