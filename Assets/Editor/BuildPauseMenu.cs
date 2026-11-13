using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Builds the in-game ESC menu and attaches the save restore bootstrap.</summary>
public static class BuildPauseMenu
{
    const string ControllerName = "Pause Menu";

    [MenuItem("Tools/UI/生成暂停菜单")]
    public static void Build()
    {
        var canvas = GameObject.Find("HUD Canvas");
        if (canvas == null) throw new System.InvalidOperationException("HUD Canvas missing");
        var gameState = GameObject.Find("GameState");
        if (gameState == null) throw new System.InvalidOperationException("GameState missing");

        var old = canvas.transform.Find(ControllerName);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        if (gameState.GetComponent<SaveGameRuntime>() == null)
            gameState.AddComponent<SaveGameRuntime>();

        var panelSprite = Sprite("Assets/Art/UI/Banners/Carved_9Slides.png");
        var buttonSprite = Sprite("Assets/Art/UI/Buttons/Button_Blue_9Slides.png");
        var buttonPressed = Sprite("Assets/Art/UI/Buttons/Button_Blue_9Slides_Pressed.png");
        var buttonHover = Sprite("Assets/Art/UI/Buttons/Button_Hover_9Slides.png");
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        var controller = new GameObject(ControllerName, typeof(RectTransform));
        var controllerRect = controller.GetComponent<RectTransform>();
        controllerRect.SetParent(canvas.transform, false);
        Stretch(controllerRect);
        controller.transform.SetAsLastSibling();
        var menu = controller.AddComponent<PauseMenu>();

        var overlay = Panel(controller.transform, "Panel", null, new Color(0.02f, 0.03f, 0.05f, 0.72f));
        Stretch(overlay);
        var overlayImage = overlay.GetComponent<Image>();
        overlayImage.raycastTarget = true;
        menu.panel = overlay.gameObject;

        var box = Panel(overlay, "Box", panelSprite, new Color(1f, 1f, 1f, 1f));
        Place(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 720f));

        var title = Label(box, "Title", "游 戏 暂 停", 46, TextAnchor.MiddleCenter, new Color(1f, 0.96f, 0.86f));
        Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -52f), new Vector2(500f, 70f));

        menu.resumeButton = MenuButton(box, "Resume", "继续游戏", -148f, buttonSprite, buttonPressed, buttonHover, font);
        menu.saveButton = MenuButton(box, "Save", "保存进度", -230f, buttonSprite, buttonPressed, buttonHover, font);
        menu.loadButton = MenuButton(box, "Load", "读取进度", -312f, buttonSprite, buttonPressed, buttonHover, font);
        menu.restartButton = MenuButton(box, "Restart", "重新开始", -394f, buttonSprite, buttonPressed, buttonHover, font);
        menu.quitButton = MenuButton(box, "Quit", "退出游戏", -476f, buttonSprite, buttonPressed, buttonHover, font);

        menu.saveTimeText = Label(box, "SaveTime", "尚无存档", 19, TextAnchor.MiddleCenter,
                                  new Color(0.95f, 0.92f, 0.82f));
        Place(menu.saveTimeText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
              new Vector2(0f, 76f), new Vector2(500f, 30f));

        menu.statusText = Label(box, "Status", "", 20, TextAnchor.MiddleCenter,
                                new Color(0.65f, 1f, 0.65f));
        Place(menu.statusText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
              new Vector2(0f, 38f), new Vector2(500f, 34f));

        overlay.gameObject.SetActive(false);
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PauseMenu] 已在 SampleScene 生成暂停菜单与 SaveGameRuntime。");
    }

    static Button MenuButton(Transform parent, string name, string text, float y,
                             Sprite sprite, Sprite pressed, Sprite hover, Font font)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        Place(rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
              new Vector2(0f, y), new Vector2(380f, 66f));

        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        if (sprite != null && sprite.border != Vector4.zero) image.type = Image.Type.Sliced;
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        var state = button.spriteState;
        state.pressedSprite = pressed;
        state.highlightedSprite = hover;
        button.spriteState = state;

        var label = Label(rt, "Label", text, 28, TextAnchor.MiddleCenter, new Color(1f, 0.98f, 0.9f));
        Stretch(label.rectTransform);
        label.font = font;
        label.raycastTarget = false;
        return button;
    }

    static RectTransform Panel(Transform parent, string name, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        if (sprite != null && sprite.border != Vector4.zero) image.type = Image.Type.Sliced;
        return rt;
    }

    static Text Label(Transform parent, string name, string text, int size, TextAnchor anchor, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        var label = go.GetComponent<Text>();
        label.text = text;
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (label.font == null) label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        label.fontSize = size;
        label.alignment = anchor;
        label.color = color;
        label.raycastTarget = false;
        return label;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static Sprite Sprite(string path) =>
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
}
