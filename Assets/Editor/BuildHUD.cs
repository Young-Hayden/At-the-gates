using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Text;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// Builds the HUD as real scene objects using the Tiny Swords UI art, so it can be
/// seen and edited in the editor instead of only existing at runtime.
/// Re-run with Tools > 重建 HUD after changing the buildable list.
/// </summary>
public static class BuildHUD
{
    const int RefW = 1920, RefH = 1080;

    static readonly string[] DefaultBuildList = {
        "Assets/Prefabs/Buildings/Knights/House_Blue.prefab",
        "Assets/Prefabs/Buildings/Knights/Barracks_Blue.prefab",
        "Assets/Prefabs/Buildings/Knights/Archery_Blue.prefab",
        "Assets/Prefabs/Buildings/Knights/Monastery_Blue.prefab",
        "Assets/Prefabs/Buildings/Knights/Tower_Blue.prefab",
        "Assets/Prefabs/Buildings/Knights/Castle_Blue.prefab",
    };

    [MenuItem("Tools/重建 HUD")]
    public static void Rebuild()
    {
        var sb = Build(true);
        Debug.Log(sb.ToString());
    }

    [MenuItem("Tools/重建 HUD（不写日志）")]
    public static void RebuildQuiet()
    {
        Build(false);
    }

    public static StringBuilder Build(bool verbose)
    {
        var sb = new StringBuilder();

        // ---- wipe any previous canvas ----
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).ToArray())
            if (c.gameObject.name == "HUD Canvas") Object.DestroyImmediate(c.gameObject);
        var oldHud = GameObject.Find("HUD");
        if (oldHud != null) Object.DestroyImmediate(oldHud);

        // ---- canvas + event system ----
        var canvasGO = new GameObject("HUD Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(RefW, RefH);
        scaler.matchWidthOrHeight = 0.5f;
        sb.AppendLine("HUD Canvas 已重建");

        if (EventSystem.current == null && Object.FindFirstObjectByType<EventSystem>() == null)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            sb.AppendLine("  新建 EventSystem");
        }

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        var panelSprite = Sprite("Assets/Art/UI/Banners/Carved_9Slides.png");
        var buttonSprite = Sprite("Assets/Art/UI/Buttons/Button_Blue_9Slides.png");
        var buttonPressed = Sprite("Assets/Art/UI/Buttons/Button_Blue_9Slides_Pressed.png");
        var buttonHover = Sprite("Assets/Art/UI/Buttons/Button_Hover_9Slides.png");
        var iconGold = Sprite("Assets/Art/Resources/Resource/G_Idle.png");
        var iconWood = Sprite("Assets/Art/Resources/Resource/W_Idle.png");
        var iconMeat = Sprite("Assets/Art/Resources/Resource/M_Idle.png");

        var hudGO = new GameObject("HUD");
        var hud = hudGO.AddComponent<HUD>();
        hudGO.transform.SetParent(canvasGO.transform, false);

        var root = NewRect("Root", canvasGO.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Stretch(root);

        // ---------------- resource bar (top-left) ----------------
        var resBar = Panel(root, "ResourceBar", new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -16),
                           new Vector2(372, 68), panelSprite);
        var icons = new[]{ iconGold, iconWood, iconMeat };
        var texts = new Text[3];
        for (int i = 0; i < 3; i++)
        {
            float x = 18 + i * 118;
            Icon(resBar, "Icon" + i, new Vector2(x, -16), new Vector2(36, 36), icons[i]);
            texts[i] = Label(resBar, "Value" + i, new Vector2(x + 42, -14), new Vector2(72, 40),
                             28, TextAnchor.MiddleLeft, new Color(1f, 0.96f, 0.86f));
            // four-digit stockpiles are normal once the economy runs; wrapping turns 1320
            // into "132" with a stray "0" underneath
            texts[i].horizontalOverflow = HorizontalWrapMode.Overflow;
        }
        hud.goldText = texts[0]; hud.woodText = texts[1]; hud.meatText = texts[2];

        // ---------------- build bar (bottom-left) ----------------
        var builds = DefaultBuildList
            .Select(p => AssetDatabase.LoadAssetAtPath<GameObject>(p))
            .ToArray();
        var buildBar = Panel(root, "BuildBar", new Vector2(0, 0), new Vector2(0, 0), new Vector2(16, 16),
                             new Vector2(builds.Length * 74 + 20, 96), panelSprite);

        hud.buildPrefabs = builds;
        hud.buildButtons = new Button[builds.Length];
        hud.buildButtonImages = new Image[builds.Length];
        for (int i = 0; i < builds.Length; i++)
        {
            float x = 18 + i * 74;
            var (btn, img) = IconButton(buildBar, "Build" + i, new Vector2(x, 16), new Vector2(64, 64),
                                        buttonSprite, buttonPressed,
                                        builds[i] != null ? PrefabIcon(builds[i]) : null,
                                        builds[i] != null ? CostLabel(builds[i]) : "", font,
                                        highlight: buttonHover);
            hud.buildButtons[i] = btn;
            hud.buildButtonImages[i] = img;
        }

        // ---------------- production panel (bottom-right) ----------------
        var prodPanel = Panel(root, "ProductionPanel", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-16, 16),
                              new Vector2(300, 128), panelSprite);
        hud.productionPanel = prodPanel.gameObject;
        hud.productionTitle = Label(prodPanel, "Title", new Vector2(14, -8), new Vector2(272, 28),
                                    20, TextAnchor.MiddleLeft, new Color(1f, 0.96f, 0.86f));
        const int Slots = 3;
        hud.produceButtons = new Button[Slots];
        hud.produceIcons = new Image[Slots];
        hud.produceCosts = new Text[Slots];
        hud.produceBacks = new Image[Slots];
        for (int i = 0; i < Slots; i++)
        {
            float x = 14 + i * 74;
            var (btn, img) = IconButton(prodPanel, "Produce" + i, new Vector2(x, 14), new Vector2(64, 64),
                                        buttonSprite, buttonPressed, null, "", font,
                                        alwaysCreateContent: true);
            hud.produceButtons[i] = btn;
            hud.produceBacks[i] = img;
            hud.produceIcons[i] = btn.GetComponentsInChildren<Image>().FirstOrDefault(x2 => x2.gameObject.name == "Icon");
            hud.produceCosts[i] = btn.GetComponentsInChildren<Text>().FirstOrDefault(t => t.gameObject.name == "Cost");
        }
        prodPanel.gameObject.SetActive(false);

        // ---------------- unit shop (always on, bottom-centre) ----------------
        // The catalogue is generated from the factories' own product tables, so a price or
        // icon changed on a building appears here automatically and cannot drift.
        FillUnitOffers(hud, sb);

        var unitBar = Panel(root, "UnitPanel", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0f, 16f),
                            new Vector2(720, 120), panelSprite);
        hud.unitPanel = unitBar.gameObject;

        var unitTitle = Label(unitBar, "Title", Vector2.zero, new Vector2(56, 46),
                              20, TextAnchor.MiddleCenter, new Color(1f, 0.96f, 0.86f));
        unitTitle.text = "兵\n种";
        unitTitle.lineSpacing = 0.9f;
        Place(unitTitle.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(16f, 37f), new Vector2(56f, 46f));

        const int ShopSlots = 6;
        hud.unitButtons = new Button[ShopSlots];
        hud.unitIcons = new Image[ShopSlots];
        hud.unitBacks = new Image[ShopSlots];
        hud.unitNames = new Text[ShopSlots];
        hud.unitCosts = new Text[ShopSlots];
        hud.unitFills = new RectTransform[ShopSlots];

        for (int i = 0; i < ShopSlots; i++)
        {
            float x = 84f + i * 106f;
            var offer = i < hud.unitOffers.Length ? hud.unitOffers[i] : null;
            var (btn, img) = IconButton(unitBar, "Unit" + i, new Vector2(x, 36f), new Vector2(76, 76),
                                        buttonSprite, buttonPressed,
                                        offer != null ? offer.icon : null,
                                        offer != null ? offer.cost.ToString() : "", font);
            hud.unitButtons[i] = btn;
            hud.unitBacks[i] = img;
            hud.unitIcons[i] = btn.GetComponentsInChildren<Image>().FirstOrDefault(z => z.gameObject.name == "Icon");
            hud.unitCosts[i] = btn.GetComponentsInChildren<Text>().FirstOrDefault(t => t.gameObject.name == "Cost");

            var nm = Label(unitBar, "UnitName" + i, Vector2.zero, new Vector2(76, 22),
                           15, TextAnchor.MiddleCenter, new Color(1f, 0.96f, 0.86f));
            Place(nm.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(x, 10f), new Vector2(76f, 22f));
            hud.unitNames[i] = nm;

            // progress of the unit this button is currently paying for, drawn inside it
            var fillGO = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            var frt = fillGO.GetComponent<RectTransform>();
            frt.SetParent(btn.transform, false);
            frt.anchorMin = frt.anchorMax = new Vector2(0f, 0f);
            frt.pivot = new Vector2(0f, 0f);
            frt.anchoredPosition = new Vector2(6f, 22f);
            frt.sizeDelta = new Vector2(64f, 6f);
            var fimg = fillGO.GetComponent<Image>();
            fimg.color = new Color(1f, 0.85f, 0.3f, 0.95f);
            fimg.raycastTarget = false;
            hud.unitFills[i] = frt;
            fillGO.SetActive(false);
        }

        // ---------------- selection panel (top-right) ----------------
        var selPanel = Panel(root, "SelectionPanel", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-16, -16),
                             new Vector2(320, 190), panelSprite);
        hud.selectionPanel = selPanel.gameObject;
        hud.selectionTitle = Label(selPanel, "Title", new Vector2(14, -10), new Vector2(292, 30),
                                   22, TextAnchor.MiddleLeft, new Color(1f, 0.96f, 0.86f));
        hud.selectionBody = Label(selPanel, "Body", new Vector2(14, -46), new Vector2(292, 96),
                                  18, TextAnchor.UpperLeft, new Color(0.92f, 0.92f, 0.88f));
        hud.selectionBody.lineSpacing = 1.15f;
        var (demolish, _) = IconButton(selPanel, "Demolish", new Vector2(104, 10), new Vector2(104, 34),
                                       buttonSprite, buttonPressed, null, "", font, "拆除", 16);
        hud.demolishButton = demolish;
        var demolishLabel = demolish.GetComponentInChildren<Text>();
        if (demolishLabel != null)
        {
            demolishLabel.rectTransform.anchorMin = Vector2.zero;
            demolishLabel.rectTransform.anchorMax = Vector2.one;
            demolishLabel.rectTransform.offsetMin = Vector2.zero;
            demolishLabel.rectTransform.offsetMax = Vector2.zero;
            demolishLabel.alignment = TextAnchor.MiddleCenter;
        }
        demolish.gameObject.SetActive(false);
        selPanel.gameObject.SetActive(false);

        // ---------------- status line (bottom-centre) ----------------
        // ---------------- status line (above the unit shop) ----------------
        hud.statusText = Label(root, "Status", new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 152),
                               940, 40, 24, TextAnchor.MiddleCenter, new Color(1f, 0.95f, 0.8f));
        var st = hud.statusText.rectTransform;
        st.anchorMin = st.anchorMax = new Vector2(0.5f, 0);
        st.pivot = new Vector2(0.5f, 0);
        st.anchoredPosition = new Vector2(0, 152);

        // ---------------- game over ----------------
        var over = Panel(root, "GameOver", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, null,
                         new Color(0f, 0f, 0f, 0.65f));
        Stretch(over);
        var overBox = Panel(over, "Box", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                            new Vector2(460, 260), panelSprite, new Color(1f, 1f, 1f, 1f));
        hud.gameOverText = Label(overBox, "Text", new Vector2(0, -60), new Vector2(460, 80),
                                 56, TextAnchor.MiddleCenter, Color.white);
        var rt = hud.gameOverText.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        var (rbtn, _) = IconButton(overBox, "Restart", new Vector2(0, -18), new Vector2(200, 70),
                                   buttonSprite, buttonPressed, null, "", font, "再来一局", 26);
        var rrt = rbtn.GetComponent<RectTransform>();
        rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0f);
        rrt.pivot = new Vector2(0.5f, 0f);
        rrt.anchoredPosition = new Vector2(0, 24);
        hud.restartButton = rbtn;
        over.gameObject.SetActive(false);
        hud.gameOverPanel = over.gameObject;

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        sb.AppendLine("  资源栏 / 建造栏 / 生产面板 / 选中面板 / 状态行 / 结算面板 已建好并保存");
        sb.AppendLine("  建造列表 " + builds.Length + " 项：" + string.Join(", ",
            builds.Select(b => b != null ? b.name : "NULL")));
        return sb;
    }

    // ------------------------------------------------------------------ helpers

    [MenuItem("Tools/首批修复/补齐工厂生产面板")]
    public static void RepairProductionPanel()
    {
        var hud = Object.FindFirstObjectByType<HUD>();
        if (hud == null) throw new System.InvalidOperationException("Open SampleScene before repairing its production panel.");
        if (hud.produceButtons == null || hud.produceIcons == null || hud.produceCosts == null)
            throw new System.InvalidOperationException("Production panel arrays must not be null.");
        int count = hud.produceButtons.Length;
        if (hud.produceIcons.Length != count || hud.produceCosts.Length != count)
            throw new System.InvalidOperationException(
                $"Production panel array length mismatch: buttons={count}, icons={hud.produceIcons.Length}, costs={hud.produceCosts.Length}.");
        for (int i = 0; i < count; i++)
            if (hud.produceButtons[i] == null || hud.produceButtons[i].GetComponent<RectTransform>() == null)
                throw new System.InvalidOperationException($"Production button {i} or its RectTransform is missing.");
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        for (int i = 0; i < count; i++)
        {
            var rt = hud.produceButtons[i].GetComponent<RectTransform>();
            hud.produceIcons[i] = EnsureIcon(rt, hud.produceIcons[i] != null ? hud.produceIcons[i].sprite : null);
            hud.produceCosts[i] = EnsureCost(rt, font, hud.produceCosts[i] != null ? hud.produceCosts[i].text : "");
        }
        EditorUtility.SetDirty(hud);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
    }

    static Sprite Sprite(string path)
    {
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
    }

    /// <summary>Labels default to top-left anchoring; this re-anchors one anywhere.</summary>
    static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    /// <summary>
    /// Builds the unit shop's catalogue out of the factories' own product tables.
    ///
    /// One entry per distinct unit, so a price or icon edited on a building shows up in
    /// the shop automatically instead of needing to be kept in sync by hand. The hint
    /// records which building unlocks it — a Castle can make workers, a Barracks cannot.
    /// </summary>
    static void FillUnitOffers(HUD hud, StringBuilder sb)
    {
        var list = new List<HUD.UnitOffer>();
        foreach (var bp in hud.buildPrefabs)
        {
            if (bp == null) continue;
            var pb = bp.GetComponent<ProductionBuilding>();
            if (pb == null) continue;
            foreach (var p in pb.products)
            {
                if (p.unitPrefab == null) continue;
                bool dup = false;
                for (int i = 0; i < list.Count; i++)
                    if (list[i].unitPrefab == p.unitPrefab) { dup = true; break; }
                if (dup) continue;

                list.Add(new HUD.UnitOffer
                {
                    label = p.label,
                    unitPrefab = p.unitPrefab,
                    icon = p.icon,
                    cost = p.cost,
                    requiresHint = "需" + BuildingName(bp.name),
                });
            }
        }
        hud.unitOffers = list.ToArray();
        sb.AppendLine("  兵种面板 " + list.Count + " 项：" +
            string.Join("、", list.ConvertAll(e => e.label + " " + e.cost + " ← " + e.requiresHint)));
    }

    static string BuildingName(string prefab)
    {
        if (prefab.StartsWith("Barracks")) return "兵营";
        if (prefab.StartsWith("Archery")) return "靶场";
        if (prefab.StartsWith("Monastery")) return "修道院";
        if (prefab.StartsWith("Castle")) return "指挥部";
        if (prefab.StartsWith("House")) return "民房";
        if (prefab.StartsWith("Tower")) return "箭塔";
        if (prefab.StartsWith("GoldMine")) return "金矿";
        return prefab;
    }

    static Sprite PrefabIcon(GameObject prefab)
    {
        var sr = prefab.GetComponentInChildren<SpriteRenderer>();
        return sr != null ? sr.sprite : null;
    }

    static string CostLabel(GameObject prefab)
    {
        var b = prefab.GetComponent<Building>();
        return b != null ? b.cost.ToString() : "";
    }

    static RectTransform NewRect(string name, Transform parent, Vector2 aMin, Vector2 aMax,
                                 Vector2 offset, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.sizeDelta = size;
        rt.anchoredPosition = offset;
        return rt;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    static RectTransform Panel(Transform parent, string name, Vector2 anchor, Vector2 pivot,
                               Vector2 pos, Vector2 size, Sprite sprite, Color? tint = null)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.type = (sprite != null && sprite.border != Vector4.zero) ? Image.Type.Sliced : Image.Type.Simple;
        img.color = tint ?? new Color(1f, 1f, 1f, 1f);
        return rt;
    }

    static void Icon(Transform parent, string name, Vector2 pos, Vector2 size, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        img.raycastTarget = false;
    }

    static Text Label(Transform parent, string name, Vector2 pos, Vector2 size,
                      int fontSize, TextAnchor align, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var t = go.GetComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (t.font == null) t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        t.fontSize = fontSize;
        t.color = color;
        t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static Text Label(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos,
                      float width, float height, int fontSize, TextAnchor align, Color color)
    {
        var t = Label(parent, name, pos, new Vector2(width, height), fontSize, align, color);
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        return t;
    }

    static Image EnsureIcon(RectTransform parent, Sprite sprite)
    {
        var existing = parent.Find("Icon")?.GetComponent<Image>();
        if (existing != null)
        {
            existing.sprite = sprite;
            existing.enabled = sprite != null;
            return existing;
        }

        var go = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(5, 17);
        rt.offsetMax = new Vector2(-5, -5);
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.enabled = sprite != null;
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }

    static Text EnsureCost(RectTransform parent, Font font, string value)
    {
        var existing = parent.Find("Cost")?.GetComponent<Text>();
        if (existing != null) { existing.text = value; return existing; }
        var text = Label(parent, "Cost", new Vector2(0, 0), new Vector2(0, -1),
            new Vector2(0, 2), parent.sizeDelta.x, 16, 11,
            TextAnchor.MiddleCenter, new Color(1f, 0.97f, 0.85f));
        if (font != null) text.font = font;
        text.text = value;
        return text;
    }

    static (Button, Image) IconButton(Transform parent, string name, Vector2 pos, Vector2 size,
                                      Sprite bg, Sprite pressed, Sprite icon, string cost,
                                      Font font, string label = null, int labelSize = 0,
                                      Sprite iconPressed = null, Sprite iconDisabled = null,
                                      Sprite highlight = null, bool alwaysCreateContent = false)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0, 0);
        rt.pivot = new Vector2(0, 0);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        var img = go.GetComponent<Image>();
        img.sprite = bg;
        img.type = (bg != null && bg.border != Vector4.zero) ? Image.Type.Sliced : Image.Type.Simple;
        img.color = Color.white;

        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.transition = Selectable.Transition.SpriteSwap;
        var ss = btn.spriteState;
        ss.pressedSprite = pressed;
        if (highlight != null) ss.highlightedSprite = highlight;
        btn.spriteState = ss;

        if (icon != null || alwaysCreateContent)
        {
            var iimg = EnsureIcon(rt, icon);

            if (iconPressed != null || iconDisabled != null)
            {
                var swap = go.AddComponent<IconSwap>();
                swap.icon = iimg;
                swap.normal = icon;
                swap.pressed = iconPressed;
                swap.disabled = iconDisabled;
            }
        }

        if (!string.IsNullOrEmpty(cost) || alwaysCreateContent)
        {
            EnsureCost(rt, font, cost);
        }
        if (!string.IsNullOrEmpty(label))
        {
            var t = Label(rt, "Label", new Vector2(0, 0), new Vector2(0, -1),
                          new Vector2(0, 6), size.x, 24, labelSize, TextAnchor.MiddleCenter, Color.white);
            t.text = label;
        }
        return (btn, img);
    }
}
