using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Text;
using System.Linq;

/// <summary>
/// Builds the main menu into its own scene (Assets/Scenes/MainMenu.scene).
///
/// The scene is created additively and closed again, so whatever is open stays open.
/// Re-run with Tools > UI > 生成主菜单场景 after changing the title or button labels.
/// </summary>
public static class BuildMainMenu
{
    const string ScenePath = "Assets/Scenes/MainMenu.scene";
    const string GameScene = "SampleScene";
    const string ModeScene = "ModeSelect";
    const int RefW = 1920, RefH = 1080;

    /// <summary>The announcement shown on the start screen. Kept as one string so the
    /// rules a player reads are the same ones the scene was built from.</summary>
    const string RulesText = EnhanceMainMenu.UpdatedRules;

    [MenuItem("Tools/UI/生成主菜单场景")]
    public static void Build()
    {
        var sb = new StringBuilder();

        // You cannot save a new scene over one that is already open, so close it first —
        // otherwise re-running this while looking at the menu fails at SaveScene.
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

        // ---- canvas ----
        var canvasGO = new GameObject("Menu Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(RefW, RefH);
        scaler.matchWidthOrHeight = 0.5f;
        SceneManager.MoveGameObjectToScene(canvasGO, scene);

        var esGO = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        SceneManager.MoveGameObjectToScene(esGO, scene);

        var menu = canvasGO.AddComponent<MainMenu>();
        menu.gameScene = ModeScene;

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        // Every spritesheet under Menu/ is a "parts sheet": the nine slices are laid out
        // as separate tiles with transparent gaps between them, so no border value makes
        // Unity's 9-slicing assemble them. Carved_9Slides and Carved_3Slides are the two
        // that ship as one continuous image with the frame painted in, and those are the
        // only two that stretch properly.
        var titleBar = Sprite("Assets/Art/UI/Banners/Carved_3Slides.png");
        var carved = Sprite("Assets/Art/UI/Banners/Carved_9Slides.png");
        var blueBtn = Sprite("Assets/Art/UI/Buttons/Button_Blue_9Slides.png");
        var blueBtnDown = Sprite("Assets/Art/UI/Buttons/Button_Blue_9Slides_Pressed.png");
        var blueBtnOver = Sprite("Assets/Art/UI/Buttons/Button_Hover_9Slides.png");

        // ---- backdrop, then the board with a drop shadow so it reads as raised ----
        Stretch(Panel("Backdrop", canvasGO.transform, null, new Color(0.11f, 0.19f, 0.26f)));

        var shadow = Panel("BoardShadow", canvasGO.transform, null, new Color(0f, 0f, 0f, 0.38f));
        Place(shadow, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(1546f, 1006f));
        shadow.GetComponent<Image>().raycastTarget = false;

        var board = Panel("Board", canvasGO.transform, carved, new Color(0.88f, 0.81f, 0.69f));
        Place(board, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(1520f, 980f));
        board.GetComponent<Image>().raycastTarget = false;

        // ---- title on a carved banner ----
        var rb = Panel("TitleBar", board, titleBar, Color.white);
        Place(rb, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -38f), new Vector2(1180f, 260f));
        rb.GetComponent<Image>().raycastTarget = false;

        var title = Text2(rb, "Title", "兵临城下", 100, TextAnchor.MiddleCenter, new Color(1f, 0.98f, 0.92f));
        Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 26f), new Vector2(1000f, 136f));
        AddOutline(title, new Color(0.20f, 0.12f, 0.05f, 0.95f));

        var sub = Text2(rb, "Subtitle", "AT  THE  GATES", 32, TextAnchor.MiddleCenter, new Color(0.40f, 0.27f, 0.14f));
        Place(sub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -62f), new Vector2(1000f, 48f));

        // ---- gameplay announcement, left half ----
        var rules = Panel("Rules", board, carved, new Color(0.99f, 0.96f, 0.89f));
        Place(rules, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-372f, -155f), new Vector2(690f, 600f));
        rules.GetComponent<Image>().raycastTarget = false;

        var rulesHead = Text2(rules, "Head", "玩  法  说  明", 30, TextAnchor.MiddleCenter, new Color(0.45f, 0.28f, 0.14f));
        Place(rulesHead.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(630f, 44f));

        // legacy Text line height is about 1.15 x fontSize, so the line budget is
        // lines x fontSize x 1.15 x lineSpacing — 24 lines have to fit in 520px.
        var rulesBody = Text2(rules, "Body", RulesText, 18, TextAnchor.UpperLeft, new Color(0.24f, 0.15f, 0.08f));
        Place(rulesBody.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -64f), new Vector2(624f, 522f));
        rulesBody.lineSpacing = 1.02f;

        // ---- buttons, right half ----
        var holder = Panel("Menu", board, null, new Color(0f, 0f, 0f, 0f));
        Place(holder, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(350f, 15f), new Vector2(460f, 300f));

        menu.startButton = MenuButton(holder, "StartButton", "开始游戏", new Vector2(-10f, -54f), blueBtn, blueBtnDown, blueBtnOver, font);
        menu.quitButton = MenuButton(holder, "QuitButton", "退出游戏", new Vector2(-10f, -180f), blueBtn, blueBtnDown, blueBtnOver, font);

        var tip = Text2(holder, "Tip", "开局双方都没有基地，\n先造指挥部 —— 它就是你的家。", 22,
                        TextAnchor.UpperCenter, new Color(0.38f, 0.26f, 0.14f));
        Place(tip.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-10f, -184f), new Vector2(440f, 90f));

        // ---- a row of the troops you will be fielding ----
        var strip = Panel("Troops", board, null, new Color(0f, 0f, 0f, 0f));
        Place(strip, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(340f, -364f), new Vector2(440f, 120f));

        string[] troops = { "Pawn_Blue", "Warrior_Blue", "Archer_Blue", "Lancer_Blue", "Monk_Blue" };
        for (int i = 0; i < troops.Length; i++)
        {
            var icon = Panel("Troop" + i, strip, UnitIcon(troops[i]), Color.white);
            Place(icon, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(i * 86f + 15f, -14f), new Vector2(74f, 74f));
            var ii = icon.GetComponent<Image>();
            ii.preserveAspect = true;
            ii.raycastTarget = false;
        }
        var troopLabel = Text2(strip, "Label", "你 的 部 队", 20, TextAnchor.MiddleCenter, new Color(0.42f, 0.29f, 0.16f));
        Place(troopLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 2f), new Vector2(440f, 28f));

        var hint = Text2(canvasGO.transform, "Hint",
            "先造指挥部，再谈扩张　·　摧毁敌方指挥部即获胜", 22,
            TextAnchor.MiddleCenter, new Color(0.9f, 0.94f, 1f, 0.75f));
        Place(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(1400f, 40f));

        // ---- fade-to-black used when starting the game ----
        var fadeGO = new GameObject("Fade", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        var frt = fadeGO.GetComponent<RectTransform>();
        frt.SetParent(canvasGO.transform, false);
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
        var fi = fadeGO.GetComponent<Image>();
        fi.color = Color.black;
        fi.raycastTarget = false;
        var cg = fadeGO.GetComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.blocksRaycasts = false;
        menu.fadeGroup = cg;

        EnhanceMainMenu.Apply(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorSceneManager.CloseScene(scene, true);

        // ---- build settings: menu first, game second ----
        var list = EditorBuildSettings.scenes
            .Where(s => s.path != ScenePath && s.path != "Assets/Scenes/" + ModeScene + ".scene" && !s.path.EndsWith("/" + GameScene + ".scene"))
            .ToList();
        list.Insert(0, new EditorBuildSettingsScene("Assets/Scenes/" + GameScene + ".scene", true));
        list.Insert(0, new EditorBuildSettingsScene("Assets/Scenes/" + ModeScene + ".scene", true));
        list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();

        sb.AppendLine("[主菜单] 已生成 " + ScenePath);
        sb.AppendLine("  构建列表: " + string.Join(", ", list.Select(s => System.IO.Path.GetFileNameWithoutExtension(s.path))));
        Debug.Log(sb.ToString());
    }

    // ------------------------------------------------------------------ helpers

    static Sprite Sprite(string path) =>
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();

    /// <summary>The cropped unit portraits made by Tools > UI > 生成单位头像.</summary>
    static Sprite UnitIcon(string unit) =>
        AssetDatabase.LoadAllAssetsAtPath("Assets/Art/UI/UnitIcons/" + unit + ".png")
                     .OfType<Sprite>().FirstOrDefault();

    static RectTransform Panel(string name, Transform parent, Sprite sprite, Color tint)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = tint;
        if (sprite != null && sprite.border != Vector4.zero) img.type = Image.Type.Sliced;
        return rt;
    }

    static RectTransform Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return rt;
    }

    static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static void AddOutline(Graphic g, Color c)
    {
        var o = g.gameObject.AddComponent<Outline>();
        o.effectColor = c;
        o.effectDistance = new Vector2(3f, -3f);
        o.useGraphicAlpha = false;
    }

    static Text Text2(Transform parent, string name, string content, int size, TextAnchor align, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (t.font == null) t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        t.text = content;
        t.fontSize = size;
        t.alignment = align;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static Button MenuButton(Transform parent, string name, string label, Vector2 pos,
                             Sprite bg, Sprite down, Sprite over, Font font)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(400f, 82f);

        var img = go.GetComponent<Image>();
        img.sprite = bg;
        img.type = (bg != null && bg.border != Vector4.zero) ? Image.Type.Sliced : Image.Type.Simple;
        img.color = Color.white;

        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.transition = Selectable.Transition.SpriteSwap;
        var ss = btn.spriteState;
        ss.pressedSprite = down;
        ss.highlightedSprite = over;
        btn.spriteState = ss;

        var t = Text2(rt, "Label", label, 34, TextAnchor.MiddleCenter, new Color(1f, 0.97f, 0.88f));
        Stretch(t.rectTransform);
        return btn;
    }
}
