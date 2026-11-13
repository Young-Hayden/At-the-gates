using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Idempotently adds settings and updated rules to a MainMenu scene.
/// Never rebuilds the existing board or its manually edited children.</summary>
public static class EnhanceMainMenu
{
    public const string UpdatedRules =
        "开局\n" +
        "　双方无基地，仅 600 金 / 300 木 / 200 肉。\n" +
        "\n" +
        "经济与基地\n" +
        "　指挥部：卸货点与胜负核心，只能建一座。\n" +
        "　民房造工人；金矿与树木由工人采集。\n" +
        "　废墟仍会阻挡通行，请绕行。\n" +
        "\n" +
        "出兵\n" +
        "　兵营 → 战士 / 枪兵；靶场 → 弓手\n" +
        "　修道院 → 僧侣（治疗，不参战）\n" +
        "\n" +
        "驻塔\n" +
        "　选中弓手右键我方箭塔，到塔底后驻防并射击。\n" +
        "　再次右键或塔被摧毁时撤出。\n" +
        "\n" +
        "操作\n" +
        "　左键点选/框选；右键移动、攻击、采集、驻塔\n" +
        "　选中士兵点「攻击」：点地面攻击移动，点敌集火\n" +
        "　H 驻守　点击建造栏放置　Esc 取消建造\n" +
        "　非建造时 Esc 打开菜单：保存 / 读取 / 重开 / 退出\n" +
        "　保存单槽，主菜单可“继续游戏”\n" +
        "\n" +
        "胜负\n" +
        "　摧毁对方指挥部即获胜。";

    [MenuItem("Tools/UI/增强当前主菜单（设置与规则）")]
    public static void ApplyCurrent()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/MainMenu.scene")
            throw new System.InvalidOperationException("请先打开 MainMenu.scene");
        Apply(scene);
        EditorSceneManager.SaveScene(scene);
    }

    public static void Apply(Scene scene)
    {
        var canvas = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Menu Canvas");
        if (canvas == null) throw new System.InvalidOperationException("场景里找不到 Menu Canvas");
        var mainMenu = canvas.GetComponent<MainMenu>();
        if (mainMenu != null) mainMenu.gameScene = "ModeSelect";

        var rules = canvas.transform.Find("Board/Rules/Body");
        if (rules != null)
        {
            var text = rules.GetComponent<Text>();
            if (text != null) text.text = UpdatedRules;
        }

        var board = canvas.transform.Find("Board") as RectTransform;
        if (board == null) throw new System.InvalidOperationException("Menu Canvas 缺 Board");
        var openButton = board.Find("SettingsButton") as RectTransform;
        if (openButton == null)
        {
            openButton = MakeButton(board, "SettingsButton", "", new Vector2(64, 64), true);
        }
        openButton.anchorMin = openButton.anchorMax = new Vector2(1f, 1f);
        openButton.pivot = new Vector2(1f, 1f);
        openButton.anchoredPosition = new Vector2(-20f, -20f);
        openButton.sizeDelta = new Vector2(64f, 64f);

        var panel = canvas.transform.Find("SettingsPanel") as RectTransform;
        if (panel == null)
        {
            panel = MakePanel(canvas.transform, "SettingsPanel", null, new Color(0, 0, 0, 0.65f));
        }
        Stretch(panel);
        panel.GetComponent<Image>().raycastTarget = true;

        var window = panel.Find("Window") as RectTransform;
        if (window == null) window = MakePanel(panel, "Window", Sprite("Assets/Art/UI/Banners/Banner_Vertical.png"), Color.white);
        window.anchorMin = window.anchorMax = new Vector2(0.5f, 0.5f);
        window.pivot = new Vector2(0.5f, 0.5f);
        window.anchoredPosition = Vector2.zero;
        window.sizeDelta = new Vector2(640f, 760f);

        var title = MakeText(window, "Title", "设  置", 36, TextAnchor.MiddleCenter);
        Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -24), new Vector2(560, 52));

        MakeText(window, "ResolutionLabel", "分辨率", 22, TextAnchor.MiddleLeft);
        var resolutionLabel = window.Find("ResolutionLabel").GetComponent<Text>();
        Place(resolutionLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-220, -92), new Vector2(160, 42));

        string[] labels = { "1280×720", "1600×900", "1920×1080" };
        var resolutionButtons = new Button[3];
        for (int i = 0; i < 3; i++)
        {
            var rt = MakeButton(window, "Resolution" + i, labels[i], new Vector2(164, 58), false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(-20 + i * 172, -88);
            rt.sizeDelta = new Vector2(164, 58);
            resolutionButtons[i] = rt.GetComponent<Button>();
        }

        var fullscreen = MakeButton(window, "Fullscreen", "全屏：开", new Vector2(400, 62), false);
        PlaceFullscreenButton(fullscreen, -178);
        var edgePan = MakeButton(window, "EdgePan", "边缘平移：开", new Vector2(400, 62), false);
        PlaceFullscreenButton(edgePan, -254);

        var speedLabel = MakeText(window, "SpeedLabel", "镜头速度", 22, TextAnchor.MiddleLeft);
        Place(speedLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-190, -326), new Vector2(180, 42));
        var speedValue = MakeText(window, "SpeedValue", "18", 22, TextAnchor.MiddleRight);
        Place(speedValue.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(190, -326), new Vector2(180, 42));
        var slider = MakeSlider(window);

        var reset = MakeButton(window, "Reset", "恢复默认", new Vector2(210, 62), false);
        reset.anchorMin = reset.anchorMax = new Vector2(0.5f, 0f);
        reset.pivot = new Vector2(0.5f, 0f);
        reset.anchoredPosition = new Vector2(-120, 112);
        reset.sizeDelta = new Vector2(210, 62);
        var close = MakeButton(window, "Close", "返回", new Vector2(210, 62), false);
        close.anchorMin = close.anchorMax = new Vector2(0.5f, 0f);
        close.pivot = new Vector2(0.5f, 0f);
        close.anchoredPosition = new Vector2(120, 112);
        close.sizeDelta = new Vector2(210, 62);

        var settings = canvas.GetComponent<MenuSettingsUI>();
        if (settings == null) settings = canvas.AddComponent<MenuSettingsUI>();
        settings.openButton = openButton.GetComponent<Button>();
        settings.panel = panel.gameObject;
        settings.resolutionButtons = resolutionButtons;
        settings.fullscreenButton = fullscreen.GetComponent<Button>();
        settings.fullscreenLabel = fullscreen.GetComponentInChildren<Text>();
        settings.edgePanButton = edgePan.GetComponent<Button>();
        settings.edgePanLabel = edgePan.GetComponentInChildren<Text>();
        settings.panSpeedSlider = slider;
        settings.panSpeedValue = speedValue;
        settings.resetButton = reset.GetComponent<Button>();
        settings.closeButton = close.GetComponent<Button>();
        settings.backgroundGroup = board.GetComponent<CanvasGroup>();
        if (settings.backgroundGroup == null) settings.backgroundGroup = board.gameObject.AddComponent<CanvasGroup>();

        panel.gameObject.SetActive(false);
        var fade = canvas.transform.Find("Fade");
        if (fade != null) fade.SetAsLastSibling();
        EditorSceneManager.MarkSceneDirty(scene);
    }

    static void PlaceFullscreenButton(RectTransform rt, float y)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0, y);
        rt.sizeDelta = new Vector2(400, 62);
    }

    static Slider MakeSlider(Transform parent)
    {
        var rt = parent.Find("SpeedSlider") as RectTransform;
        if (rt == null)
        {
            var go = new GameObject("SpeedSlider", typeof(RectTransform), typeof(Image), typeof(Slider));
            rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
        }
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0, -382);
        rt.sizeDelta = new Vector2(400, 26);
        var bg = rt.GetComponent<Image>();
        bg.color = new Color(0.16f, 0.20f, 0.22f, 0.72f);
        bg.raycastTarget = true;

        var handleArea = rt.Find("Handle Slide Area") as RectTransform;
        if (handleArea == null)
        {
            var go = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea = go.GetComponent<RectTransform>();
            handleArea.SetParent(rt, false);
        }
        Stretch(handleArea);

        var handle = handleArea.Find("Handle") as RectTransform;
        if (handle == null)
        {
            handle = MakePanel(handleArea, "Handle", Sprite("Assets/Art/UI/Buttons/Button_Blue_9Slides.png"), Color.white);
        }
        handle.sizeDelta = new Vector2(28, 34);
        var slider = rt.GetComponent<Slider>();
        slider.handleRect = handle;
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.direction = Slider.Direction.LeftToRight;
        return slider;
    }

    static RectTransform MakeButton(Transform parent, string name, string label, Vector2 size, bool icon)
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
            button.transition = Selectable.Transition.SpriteSwap;
            var state = button.spriteState;
            state.pressedSprite = Sprite("Assets/Art/UI/Buttons/Button_Blue_9Slides_Pressed.png");
            state.highlightedSprite = Sprite("Assets/Art/UI/Buttons/Button_Hover_9Slides.png");
            button.spriteState = state;
            if (icon)
            {
                var child = MakePanel(rt, "Icon", Sprite("Assets/Art/UI/Icons/Regular_02.png"), Color.white);
                Stretch(child);
                child.GetComponent<Image>().raycastTarget = false;
                child.GetComponent<Image>().preserveAspect = true;
            }
            else if (!string.IsNullOrEmpty(label))
            {
                var text = MakeText(rt, "Label", label, 20, TextAnchor.MiddleCenter);
                Stretch(text.rectTransform);
            }
        }
        rt.sizeDelta = size;
        return rt;
    }

    static RectTransform MakePanel(Transform parent, string name, Sprite sprite, Color colour)
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

    static Text MakeText(Transform parent, string name, string content, int size, TextAnchor align)
    {
        Text text = null;
        foreach (Transform child in parent.Cast<Transform>().ToArray())
        {
            if (child.name != name) continue;
            if (text == null) text = child.GetComponent<Text>();
            else Object.DestroyImmediate(child.gameObject);
        }
        if (text == null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            text = go.GetComponent<Text>();
            text.rectTransform.SetParent(parent, false);
        }
        text.text = content;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (text.font == null) text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = size;
        text.alignment = align;
        text.color = new Color(0.24f, 0.15f, 0.08f);
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
