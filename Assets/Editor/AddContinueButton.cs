using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Adds the main-menu Continue button without rebuilding the whole scene.</summary>
public static class AddContinueButton
{
    const string ScenePath = "Assets/Scenes/MainMenu.scene";

    [MenuItem("Tools/UI/添加继续游戏按钮")]
    public static void Apply()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            MainMenu menu = null;
            foreach (var candidate in Object.FindObjectsByType<MainMenu>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (candidate.gameObject.scene == scene) { menu = candidate; break; }
            if (menu == null) throw new System.InvalidOperationException("MainMenu missing");
            if (menu.startButton == null || menu.quitButton == null)
                throw new System.InvalidOperationException("Main menu buttons missing");

            var parent = menu.startButton.transform.parent;
            var old = parent.Find("ContinueButton");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var blue = Sprite("Assets/Art/UI/Buttons/Button_Blue_9Slides.png");
            var down = Sprite("Assets/Art/UI/Buttons/Button_Blue_9Slides_Pressed.png");
            var over = Sprite("Assets/Art/UI/Buttons/Button_Hover_9Slides.png");
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            var continueButton = MakeButton(parent, "ContinueButton", "继续游戏", -110f, blue, down, over, font);
            var startRt = menu.startButton.GetComponent<RectTransform>();
            startRt.anchoredPosition = new Vector2(-10f, -38f);
            startRt.sizeDelta = new Vector2(400f, 70f);
            var quitRt = menu.quitButton.GetComponent<RectTransform>();
            quitRt.anchoredPosition = new Vector2(-10f, -182f);
            quitRt.sizeDelta = new Vector2(400f, 70f);

            menu.continueButton = continueButton;
            menu.continueScene = "SampleScene";
            EditorUtility.SetDirty(menu);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[MainMenu] 已添加继续游戏按钮。");
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    static Button MakeButton(Transform parent, string name, string label, float y,
                             Sprite bg, Sprite down, Sprite over, Font font)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(-10f, y);
        rt.sizeDelta = new Vector2(400f, 70f);

        var img = go.GetComponent<Image>();
        img.sprite = bg;
        img.type = bg != null && bg.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        img.color = Color.white;

        var button = go.GetComponent<Button>();
        button.targetGraphic = img;
        button.transition = Selectable.Transition.SpriteSwap;
        var state = button.spriteState;
        state.pressedSprite = down;
        state.highlightedSprite = over;
        button.spriteState = state;

        var textGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
        var textRt = textGO.GetComponent<RectTransform>();
        textRt.SetParent(rt, false);
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;
        var text = textGO.GetComponent<Text>();
        text.text = label;
        text.font = font;
        text.fontSize = 32;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color(1f, 0.97f, 0.88f);
        text.raycastTarget = false;
        return button;
    }

    static Sprite Sprite(string path) =>
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
}
