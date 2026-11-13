using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Builds the approved three-card mode-selection scene without touching
/// MainMenu or SampleScene. Safe to re-run: existing build entries are replaced, not duplicated.</summary>
public static class BuildModeSelect
{
    const string ScenePath = "Assets/Scenes/ModeSelect.scene";
    const string MainMenuPath = "Assets/Scenes/MainMenu.scene";
    const string GamePath = "Assets/Scenes/SampleScene.scene";

    [MenuItem("Tools/UI/生成三模式选择场景")]
    public static void Build()
    {
        for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
        {
            var open = SceneManager.GetSceneAt(i);
            if (open.path == ScenePath) EditorSceneManager.CloseScene(open, true);
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var camGO = new GameObject("Menu Camera", typeof(Camera));
        var cam = camGO.GetComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.13f, 0.20f, 0.29f);
        camGO.tag = "MainCamera";
        SceneManager.MoveGameObjectToScene(camGO, scene);

        var canvasGO = new GameObject("Menu Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        SceneManager.MoveGameObjectToScene(canvasGO, scene);
        SceneManager.MoveGameObjectToScene(new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule)), scene);

        Stretch(Panel("Backdrop", canvasGO.transform, null, new Color(0.11f, 0.19f, 0.26f, 1f)));
        var board = Panel("Board", canvasGO.transform, Sprite("Assets/Art/UI/Banners/Carved_9Slides.png"), new Color(0.88f, 0.81f, 0.69f));
        Place(board, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(1400, 700));

        var titleBar = Panel("TitleBar", board, Sprite("Assets/Art/UI/Banners/Carved_3Slides.png"), Color.white);
        Place(titleBar, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -30), new Vector2(900, 150));
        var title = Text2(titleBar, "Title", "选  择  模  式", 52, TextAnchor.MiddleCenter, new Color(1f, 0.98f, 0.92f));
        Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 12), new Vector2(780, 76));

        var menu = canvasGO.AddComponent<ModeSelectMenu>();
        menu.humanButton = Card(board, "Human", "人机模式", "当前完整 RTS 对局\n立即可玩", "进入游戏 →", "Assets/Art/UI/Ribbons/Ribbon_Blue_3Slides.png", new Vector2(-440, -205), false);
        menu.campaignButton = Card(board, "Campaign", "战役模式", "章节与特殊目标", "后期开放", "Assets/Art/UI/Ribbons/Ribbon_Yellow_3Slides.png", new Vector2(0, -205), true);
        menu.versusButton = Card(board, "Versus", "对战模式", "玩家之间的对抗", "后期开放", "Assets/Art/UI/Ribbons/Ribbon_Red_3Slides.png", new Vector2(440, -205), true);
        menu.backButton = Button(canvasGO.transform, "Back", "返回主菜单", new Vector2(0, 32));
        menu.gameScene = "SampleScene";
        menu.mainMenuScene = "MainMenu";

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorSceneManager.CloseScene(scene, true);

        var list = EditorBuildSettings.scenes
            .Where(s => s.path != ScenePath && s.path != MainMenuPath && s.path != GamePath).ToList();
        list.Insert(0, new EditorBuildSettingsScene(GamePath, true));
        list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        list.Insert(0, new EditorBuildSettingsScene(MainMenuPath, true));
        EditorBuildSettings.scenes = list.ToArray();
        Debug.Log("[模式选择] 已生成三模式场景并更新 Build Settings");
    }

    static Button Card(Transform parent, string name, string title, string body, string status,
                       string ribbonPath, Vector2 position, bool locked)
    {
        var rt = parent.Find(name) as RectTransform;
        if (rt == null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
        }
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = position;
        rt.sizeDelta = new Vector2(400, 440);

        var sprite = Sprite("Assets/Art/UI/Banners/Banner_Vertical.png");
        var image = rt.GetComponent<Image>();
        image.sprite = sprite;
        image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        image.color = locked ? new Color(0.74f, 0.72f, 0.66f) : Color.white;
        var button = rt.GetComponent<Button>();
        button.targetGraphic = image;
        button.interactable = !locked;

        var ribbon = Panel("Ribbon", rt, Sprite(ribbonPath), Color.white);
        Place(ribbon, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -22), new Vector2(350, 92));
        ribbon.GetComponent<Image>().raycastTarget = false;
        var titleText = Text2(ribbon, "Title", title, 30, TextAnchor.MiddleCenter, new Color(1f, 0.97f, 0.88f));
        Place(titleText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(320, 54));

        if (locked)
        {
            var lockIcon = Panel("Lock", rt, Sprite("Assets/Art/UI/Icons/Regular_10.png"), Color.white);
            Place(lockIcon, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 12), new Vector2(90, 90));
            lockIcon.GetComponent<Image>().raycastTarget = false;
        }
        var bodyText = Text2(rt, "Body", body, 22, TextAnchor.MiddleCenter, new Color(0.27f, 0.18f, 0.10f));
        Place(bodyText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -44), new Vector2(330, 110));
        var statusText = Text2(rt, "Status", status, 20, TextAnchor.MiddleCenter, new Color(0.34f, 0.23f, 0.13f));
        Place(statusText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 100), new Vector2(330, 46));
        return button;
    }

    static Button Button(Transform parent, string name, string label, Vector2 position)
    {
        var rt = parent.Find(name) as RectTransform;
        if (rt == null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            var sprite = Sprite("Assets/Art/UI/Buttons/Button_Blue_9Slides.png");
            image.sprite = sprite;
            image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            var text = Text2(rt, "Label", label, 22, TextAnchor.MiddleCenter, new Color(1f, 0.97f, 0.88f));
            Stretch(text.rectTransform);
        }
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = position;
        rt.sizeDelta = new Vector2(230, 62);
        return rt.GetComponent<Button>();
    }

    static RectTransform Panel(string name, Transform parent, Sprite sprite, Color colour)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.color = colour;
        if (sprite != null && sprite.border != Vector4.zero) image.type = Image.Type.Sliced;
        return rt;
    }

    static Text Text2(Transform parent, string name, string content, int size, TextAnchor align, Color colour)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        var text = go.GetComponent<Text>();
        text.rectTransform.SetParent(parent, false);
        text.text = content;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (text.font == null) text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = size;
        text.alignment = align;
        text.color = colour;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static Sprite Sprite(string path) =>
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
}
