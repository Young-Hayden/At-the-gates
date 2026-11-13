using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Builds Assets/Resources/LoadingScreen.prefab — the dark backdrop, carved
/// panel, progress bar and the three idle Blue units that decorate every scene
/// transition. Safe to re-run: the prefab is rebuilt from scratch each time.</summary>
public static class BuildLoadingScreen
{
    const string PrefabPath = "Assets/Resources/LoadingScreen.prefab";

    [MenuItem("Tools/UI/生成加载界面预制体")]
    public static void Build()
    {
        var panelSprite = Sprite("Assets/Art/UI/Banners/Carved_9Slides.png");
        var barBase = Sprite("Assets/Art/UI/Bars/BigBar_Base.png");
        var barFill = Sprite("Assets/Art/UI/Bars/BigBar_Fill.png");
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        var root = new GameObject("LoadingScreen",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        try
        {
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // above the HUD (100) and anything else that might linger during the switch
            canvas.sortingOrder = 500;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var screen = root.AddComponent<LoadingScreen>();

            // backdrop — raycastTarget on purpose: clicks during the load go nowhere
            var bg = Panel(root.transform, "Background", null, new Color(0.05f, 0.07f, 0.11f, 0.97f));
            Stretch(bg);
            bg.GetComponent<Image>().raycastTarget = true;

            var box = Panel(root.transform, "Box", panelSprite, Color.white);
            Place(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(860f, 460f));

            var title = Label(box, "Title", "加 载 中 …", 44, TextAnchor.MiddleCenter, new Color(1f, 0.96f, 0.86f), font);
            Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -44f), new Vector2(600f, 64f));

            // three idle Blue units across the middle — Warrior, Archer, Lancer
            AddUnit(box, "Warrior", "Assets/Art/Units/Blue/Warrior/Warrior_Idle.png", new Vector2(-240f, 20f), 0f);
            AddUnit(box, "Archer", "Assets/Art/Units/Blue/Archer/Archer_Idle.png", new Vector2(0f, 20f), 2f);
            AddUnit(box, "Lancer", "Assets/Art/Units/Blue/Lancer/Lancer_Idle.png", new Vector2(240f, 20f), 4f);

            // progress bar near the bottom of the box
            var barBaseRt = Panel(box, "ProgressBar", barBase, Color.white);
            Place(barBaseRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 84f), new Vector2(560f, 36f));
            barBaseRt.GetComponent<Image>().raycastTarget = false;

            // dark track inside the frame: the base sprite's red scale marks otherwise
            // read as "already full" no matter what the fill is doing
            var trackGO = new GameObject("Track", typeof(RectTransform), typeof(Image));
            var trackRt = trackGO.GetComponent<RectTransform>();
            trackRt.SetParent(barBaseRt, false);
            trackRt.anchorMin = Vector2.zero;
            trackRt.anchorMax = Vector2.one;
            trackRt.offsetMin = new Vector2(8f, 8f);
            trackRt.offsetMax = new Vector2(-8f, -8f);
            var trackImg = trackGO.GetComponent<Image>();
            trackImg.color = new Color(0.12f, 0.09f, 0.08f, 0.9f);
            trackImg.raycastTarget = false;

            var fillGO = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            var fillRt = fillGO.GetComponent<RectTransform>();
            fillRt.SetParent(trackRt, false);
            Stretch(fillRt);
            var fillImg = fillGO.GetComponent<Image>();
            fillImg.sprite = barFill;
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImg.fillAmount = 0f;
            fillImg.color = new Color(1f, 0.85f, 0.3f);   // gold progress on the dark track
            fillImg.raycastTarget = false;
            screen.progressFill = fillImg;

            var tip = Label(box, "Tip", "正在集结部队…", 22, TextAnchor.MiddleCenter,
                            new Color(0.92f, 0.9f, 0.82f), font);
            Place(tip.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(600f, 30f));
            screen.tipText = tip;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[LoadingScreen] 预制体已生成：" + PrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static void AddUnit(Transform parent, string name, string sheetPath, Vector2 pos, float phase)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(150f, 150f);
        var img = go.GetComponent<Image>();
        img.preserveAspect = true;
        img.raycastTarget = false;

        var frames = LoadFrames(sheetPath);
        var anim = go.AddComponent<LoadingUnitAnim>();
        anim.image = img;
        anim.frames = frames.ToArray();
        anim.fps = 6f;
        anim.phaseOffset = phase;
        if (frames.Count > 0) img.sprite = frames[0];
        else Debug.LogWarning("[LoadingScreen] " + name + " 没有找到待机帧：" + sheetPath);
    }

    /// <summary>Frames of an idle strip. Sliced sheets hand over their sprites directly;
    /// a sheet that was never sliced falls back to its sibling Name_0.png … files
    /// (the Lancer ships both ways). Ordered by trailing digits, not alphabetically —
    /// plain string order puts _10 before _2.</summary>
    static List<Sprite> LoadFrames(string sheetPath)
    {
        var sprites = AssetDatabase.LoadAllAssetsAtPath(sheetPath).OfType<Sprite>().ToList();
        if (sprites.Count >= 2) return sprites.OrderBy(TrailingNumber).ToList();

        string dir = System.IO.Path.GetDirectoryName(sheetPath).Replace('\\', '/');
        string baseName = System.IO.Path.GetFileNameWithoutExtension(sheetPath);
        var frames = new List<Sprite>();
        for (int i = 0; i < 60; i++)
        {
            var s = Sprite(dir + "/" + baseName + "_" + i + ".png");
            if (s == null) break;
            frames.Add(s);
        }
        if (frames.Count > 0) return frames;
        return sprites; // whatever the sheet gave us, even a single frame
    }

    static int TrailingNumber(Sprite s)
    {
        var name = s.name;
        int i = name.Length;
        while (i > 0 && char.IsDigit(name[i - 1])) i--;
        return i < name.Length ? int.Parse(name.Substring(i)) : 0;
    }

    static RectTransform Panel(Transform parent, string name, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        if (sprite != null && sprite.border != Vector4.zero) img.type = Image.Type.Sliced;
        return rt;
    }

    static Text Label(Transform parent, string name, string text, int size,
                      TextAnchor anchor, Color color, Font font)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        var label = go.GetComponent<Text>();
        label.text = text;
        label.font = font;
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
