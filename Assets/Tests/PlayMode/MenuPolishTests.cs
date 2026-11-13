using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// Presentation Task P3 — the menu refinement shipped by <see cref="PolishMenus"/>.
///
/// Three separate bugs motivated these assertions, all found by looking at PlayMode screenshots:
/// cream titles sitting on cream parchment (pause menu and loading screen), a cream tip line on
/// the same parchment, and every main-menu button label floating 5 px above its button's centre.
///
/// They also pin the parts a rebuild would silently undo: `Tools/UI/生成主菜单场景` recreates the
/// main menu from scratch, and `Tools/UI/生成暂停菜单` would drop the outline.
/// </summary>
public class MenuPolishTests : RegressionFixture
{
    const string ScenePath = "Assets/Scenes/MainMenu.scene";
    const string GameScenePath = "Assets/Scenes/SampleScene.scene";
    const string LoadingPrefab = "LoadingScreen";

    static readonly Color ParchmentText = new Color(0.38f, 0.26f, 0.14f, 1f);
    const float Tolerance = 0.5f;

    [UnityTest]
    public IEnumerator MainMenu_EveryButtonLabel_IsCentredAndFits()
    {
        EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;
        Canvas.ForceUpdateCanvases();

        var canvas = Object.FindFirstObjectByType<Canvas>();
        Assert.IsNotNull(canvas, "The main menu scene has no Canvas.");

        int checkedLabels = 0;
        foreach (var button in canvas.GetComponentsInChildren<Button>(true))
        {
            var label = button.transform.Find("Label") as RectTransform;
            if (label == null) continue;
            var text = label.GetComponent<Text>();
            if (text == null) continue;
            checkedLabels++;

            Assert.AreEqual(TextAnchor.MiddleCenter, text.alignment,
                "Label \"" + text.text + "\" on " + button.name + " must be centred.");
            Assert.LessOrEqual(text.preferredWidth, label.rect.width + Tolerance,
                "Label \"" + text.text + "\" on " + button.name + " is wider than its button.");
            Assert.LessOrEqual(text.preferredHeight, label.rect.height + Tolerance,
                "Label \"" + text.text + "\" on " + button.name + " is taller than its button.");

            Vector2 offset = CentreOffset(label, button.GetComponent<RectTransform>());
            Assert.LessOrEqual(offset.magnitude, 1f,
                "Label \"" + text.text + "\" on " + button.name + " is off centre by "
                + offset.ToString("F1") + " px; the shipped menu centres every label.");
        }
        Assert.GreaterOrEqual(checkedLabels, 10,
            "Only " + checkedLabels + " labelled buttons were found; the main menu lost its buttons.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator PauseMenu_TitlesAreOutlined_AndQuietLinesAreDark()
    {
        EditorSceneManager.LoadSceneInPlayMode(GameScenePath, new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;

        var canvas = Object.FindFirstObjectByType<Canvas>();
        Assert.IsNotNull(canvas, "SampleScene has no Canvas.");
        var pause = canvas.transform.Find("Pause Menu");
        Assert.IsNotNull(pause, "HUD Canvas/Pause Menu is missing.");

        AssertOutlined(pause, "Panel/Box/Title", "pause title");
        AssertOutlined(pause, "Panel/QuitConfirm/Box/Title", "quit-confirm title");

        var saveTime = pause.Find("Panel/Box/SaveTime");
        Assert.IsNotNull(saveTime, "The pause menu has no SaveTime line.");
        Assert.AreEqual(ParchmentText, saveTime.GetComponent<Text>().color,
            "The \"no save yet\" line must use the dark parchment brown, not cream on cream.");

        var hint = pause.Find("Panel/QuitConfirm/Box/Hint");
        Assert.IsNotNull(hint, "The quit-confirm dialog has no Hint line.");
        Assert.AreEqual(ParchmentText, hint.GetComponent<Text>().color,
            "The quit-confirm hint must use the dark parchment brown.");
        yield return null;
    }

    [UnityTest]
    public IEnumerator LoadingScreen_TitleIsOutlined_AndTipIsDark()
    {
        var prefab = Resources.Load<GameObject>(LoadingPrefab);
        Assert.IsNotNull(prefab, "Resources/LoadingScreen.prefab is missing.");
        var instance = Object.Instantiate(prefab);
        try
        {
            var title = instance.transform.Find("Box/Title");
            Assert.IsNotNull(title, "The loading screen has no Box/Title.");
            Assert.IsNotNull(title.GetComponent<Outline>(),
                "The loading title must carry the dark outline; cream on cream is unreadable.");

            var tip = instance.transform.Find("Box/Tip");
            Assert.IsNotNull(tip, "The loading screen has no Box/Tip.");
            Assert.AreEqual(ParchmentText, tip.GetComponent<Text>().color,
                "The loading tip must use the dark parchment brown.");
            yield return null;
        }
        finally
        {
            Object.Destroy(instance);
        }
    }

    // ------------------------------------------------------------------ helpers

    static void AssertOutlined(Transform root, string path, string what)
    {
        var target = root.Find(path);
        Assert.IsNotNull(target, "Missing " + path + " (" + what + ").");
        Assert.IsNotNull(target.GetComponent<Outline>(),
            "The " + what + " must carry the dark outline the main-menu title uses.");
    }

    static Vector2 CentreOffset(RectTransform label, RectTransform button)
    {
        Vector3 labelCentre = WorldCentre(label);
        Vector3 buttonCentre = button.TransformPoint(button.rect.center);
        return (Vector2)button.InverseTransformVector(labelCentre - buttonCentre);
    }

    static Vector2 WorldCentre(RectTransform rt)
    {
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        return (corners[0] + corners[2]) * 0.5f;
    }
}
