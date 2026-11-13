using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Presentation Task P2: refines the HUD in place.
///
/// Never runs <c>Tools/重建 HUD</c> — that destroys the attack button, the repair button,
/// the technology strip, the game-over exit and the minimap move. Everything here is
/// incremental and idempotent: existing objects are rewired and restyled, never duplicated,
/// and a run that changes nothing skips the save entirely (every save re-rolls the water
/// tiles' m_AnimationSpeed, which is hundreds of lines of pure churn).
///
/// Panels touched: resource bar (labels + dark backing + a fourth population cell), build
/// bar (two-line costs, 1-6 hotkey badges, dimmed when unaffordable), unit panel
/// (horizontal title, framed portraits, dimmed unavailable slots), technology strip
/// (dimmed unavailable buttons), minimap (frame backing, thicker view rectangle) and the
/// status line (dark backing strip).
///
/// The tool refuses to save if any button label is not centred or does not fit its button,
/// or if a newly placed rect leaves its parent.
/// </summary>
public static class PolishHUD
{
    const string ScenePath = "Assets/Scenes/SampleScene.scene";
    const string PresentationDirectory = ".superpowers/sdd/presentation";
    const string BackupPath = PresentationDirectory + "/task-p2-scene-before.scene";
    const string ToolLogPath = PresentationDirectory + "/task-p2-tool.log";

    // ---- resource bar ----
    static readonly Vector2 ResourceBarSize = new Vector2(496f, 68f);
    const float CellWidth = 120f;
    const float CellStart = 8f;
    const float BarInset = 8f;

    // ---- build bar ----
    static readonly Vector2 BuildBarSize = new Vector2(450f, 96f);
    const float BuildButtonSize = 64f;
    const float BuildButtonSpacing = 70f;      // 64 + 6 px gap
    const float BuildButtonStartX = 18f;
    const float BuildButtonY = 16f;
    const float BuildCostHeight = 24f;
    const float HotkeySize = 16f;

    // ---- unit panel ----
    static readonly Vector2 UnitPanelSize = new Vector2(720f, 120f);
    const float UnitButtonSize = 56f;
    const float UnitButtonSpacing = 106f;      // 76 + 30 px gap
    const float UnitButtonStartX = 110f;       // centred: 5 * 76 + 4 * 30 = 500 -> (720-500)/2
    const float UnitButtonY = 41f;
    const float UnitNameY = 26f;
    const float UnitCostY = 10f;

    // ---- status line ----
    static readonly Vector2 StatusBackingSize = new Vector2(960f, 44f);
    static readonly Vector2 StatusBackingPosition = new Vector2(0f, 150f);

    static readonly Color BackingTint = new Color(0f, 0f, 0f, 0.58f);
    static readonly Color PortraitBackTint = new Color(0.05f, 0.04f, 0.03f, 0.50f);
    static readonly Color DimTint = new Color(0.02f, 0.02f, 0.02f, 0.45f);
    static readonly Color CaptionTint = new Color(0.95f, 0.90f, 0.78f, 1f);
    static readonly Color ValueTint = new Color(1f, 0.96f, 0.86f, 1f);
    static readonly Color CostTint = new Color(1f, 0.96f, 0.86f, 1f);

    static readonly string[] ResourceCaptions = { "金", "木", "肉" };
    static readonly string[] TechButtons = { "Tech_Forge", "Tech_Armour", "Tech_Gather" };

    static readonly StringBuilder Log = new StringBuilder();

    [MenuItem("Tools/UI/精修 HUD")]
    public static void ApplyMenu()
    {
        Apply(ScenePath, BackupPath, ToolLogPath);
    }

    /// <summary>
    /// Applies the refinement to <paramref name="scenePath"/>. A null backup path skips the
    /// byte backup — only used when running against a throwaway copy of the scene.
    /// </summary>
    public static void Apply(string scenePath, string backupPath, string logPath)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[PolishHUD] 请先退出 PlayMode。");
            return;
        }

        Log.Length = 0;
        var scene = default(Scene);
        var previousActive = default(Scene);
        bool openedByTool = false;
        bool switchedActive = false;

        try
        {
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var loaded = EditorSceneManager.GetSceneAt(i);
                if (loaded.path == scenePath && loaded.isLoaded) { scene = loaded; break; }
            }

            if (!scene.IsValid() || !scene.isLoaded)
            {
                previousActive = SceneManager.GetActiveScene();
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                openedByTool = true;
                Line("Scene was not loaded; opened additively for this run.");
            }
            else
            {
                Line("Scene was already open; operating on the loaded scene.");
            }

            if (SceneManager.GetActiveScene() != scene)
            {
                previousActive = SceneManager.GetActiveScene();
                SceneManager.SetActiveScene(scene);
                switchedActive = true;
                Line("Scene was not active; switched for this run.");
            }

            Run(scene, scenePath, backupPath);
        }
        catch (Exception e)
        {
            Line("ERROR: " + e);
            Debug.LogError("[PolishHUD] 精修失败：" + e.Message);
            throw;
        }
        finally
        {
            if (openedByTool && scene.IsValid() && scene.isLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previousActive.IsValid() && previousActive.isLoaded)
                    SceneManager.SetActiveScene(previousActive);
            }
            else if (switchedActive && previousActive.IsValid() && previousActive.isLoaded)
            {
                SceneManager.SetActiveScene(previousActive);
            }
            if (!string.IsNullOrEmpty(logPath)) WriteToolLog(logPath);
        }
    }

    static void Run(Scene scene, string scenePath, string backupPath)
    {
        var hud = UnityEngine.Object.FindFirstObjectByType<HUD>();
        if (hud == null) throw new InvalidOperationException("HUD missing in " + scenePath);
        var canvas = hud.GetComponentInParent<Canvas>();
        if (canvas == null) throw new InvalidOperationException("HUD is not under a Canvas.");
        var canvasRt = canvas.GetComponent<RectTransform>();
        var root = canvasRt.Find("Root") as RectTransform;
        if (root == null) throw new InvalidOperationException("HUD Canvas/Root missing.");

        string md5Before = null;
        if (!string.IsNullOrEmpty(backupPath))
        {
            string fullScenePath = ProjectPath(scenePath);
            string fullBackupPath = ProjectPath(backupPath);
            md5Before = Md5(fullScenePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullBackupPath));
            if (!File.Exists(fullBackupPath))
            {
                File.Copy(fullScenePath, fullBackupPath, false);
                Line("Backup copied: " + fullBackupPath);
            }
            else
            {
                Line("Backup already exists (original pre-change state); left untouched: " + fullBackupPath);
            }
            Line("MD5 before: " + md5Before);
        }

        bool modified = false;
        modified |= Step("resource bar", PolishResourceBar(hud, root));
        modified |= Step("build bar", PolishBuildBar(hud, root));
        modified |= Step("unit panel", PolishUnitPanel(hud, root));
        modified |= Step("tech strip", PolishTechStrip(hud));
        modified |= Step("production panel", PolishProductionPanel(hud));
        modified |= Step("selection panel", PolishSelectionPanel(hud));
        modified |= Step("minimap", PolishMinimap(root));
        modified |= Step("status line", PolishStatus(hud, root));
        modified |= Step("button labels", NormalizeButtonLabels(canvasRt));

        ValidateLayout(canvasRt, root);

        if (!modified)
        {
            Line("No changes needed — scene not saved.");
            return;
        }

        EditorUtility.SetDirty(hud);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Line("Scene saved.");
        if (md5Before != null) Line("MD5 after: " + Md5(ProjectPath(scenePath)));
    }

    // ------------------------------------------------------------------ panels

    static bool PolishResourceBar(HUD hud, RectTransform root)
    {
        var bar = FindRect(root, "ResourceBar");
        bool changed = SetRect(bar, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                               new Vector2(16f, -16f), ResourceBarSize);

        changed |= EnsureBacking(bar, "Backing", new Vector2(BarInset, BarInset),
                                 new Vector2(-BarInset, -BarInset), BackingTint);

        var captions = new Text[3];
        var values = new Text[3];
        for (int i = 0; i < 3; i++)
        {
            float x = CellStart + i * CellWidth;
            var icon = FindRect(bar, "Icon" + i);
            changed |= SetRect(icon, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                               new Vector2(x + 6f, -21f), new Vector2(26f, 26f));

            values[i] = EnsureTextAt(bar, "Value" + i, new Vector2(x + 36f, -12f), new Vector2(62f, 32f),
                                     24, TextAnchor.MiddleRight, ValueTint, ref changed);
            values[i].horizontalOverflow = HorizontalWrapMode.Overflow;
            values[i].verticalOverflow = VerticalWrapMode.Overflow;

            captions[i] = EnsureTextAt(bar, "Caption" + i, new Vector2(x + 100f, -18f), new Vector2(18f, 24f),
                                       14, TextAnchor.MiddleLeft, CaptionTint, ref changed);
            if (captions[i].text != ResourceCaptions[i])
            {
                captions[i].text = ResourceCaptions[i];
                changed = true;
            }
        }

        hud.goldText = values[0];
        hud.woodText = values[1];
        hud.meatText = values[2];

        // ---- fourth cell: population ----
        float px = CellStart + 3f * CellWidth;
        var popCaption = EnsureTextAt(bar, "CaptionPopulation", new Vector2(px + 6f, -18f),
                                      new Vector2(40f, 24f), 14, TextAnchor.MiddleLeft, CaptionTint, ref changed);
        if (popCaption.text != "人口") { popCaption.text = "人口"; changed = true; }

        var popValue = EnsureTextAt(bar, "ValuePopulation", new Vector2(px + 44f, -12f),
                                    new Vector2(72f, 32f), 22, TextAnchor.MiddleRight, ValueTint, ref changed);
        popValue.horizontalOverflow = HorizontalWrapMode.Overflow;
        if (hud.populationText != popValue) { hud.populationText = popValue; changed = true; }

        Line("Resource bar: 4 cells (金/木/肉/人口), dark backing " + (changed ? "restyled" : "already ok"));
        return changed;
    }

    static bool PolishBuildBar(HUD hud, RectTransform root)
    {
        var bar = FindRect(root, "BuildBar");
        bool changed = SetRect(bar, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                               new Vector2(16f, 16f), BuildBarSize);
        changed |= EnsureBacking(bar, "Backing", new Vector2(6f, 6f), new Vector2(-6f, -6f), BackingTint);

        int count = hud.buildButtons != null ? hud.buildButtons.Length : 0;
        var costTexts = new Text[count];
        for (int i = 0; i < count; i++)
        {
            var button = hud.buildButtons[i] != null ? hud.buildButtons[i].transform as RectTransform : null;
            if (button == null) continue;

            changed |= SetRect(button, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                               new Vector2(BuildButtonStartX + i * BuildButtonSpacing, BuildButtonY),
                               new Vector2(BuildButtonSize, BuildButtonSize));

            var icon = button.Find("Icon") as RectTransform;
            if (icon != null)
            {
                changed |= SetStretch(icon, new Vector2(5f, BuildCostHeight + 9f), new Vector2(-5f, -4f));
            }

            var cost = EnsureChild(button, "Cost");
            changed |= SetRect(cost, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                               new Vector2(0f, 8f), new Vector2(-4f, BuildCostHeight));
            var costText = EnsureText(cost, 10, TextAnchor.MiddleCenter, CostTint, ref changed);
            costText.lineSpacing = 0.8f;
            string want = BuildCostLabel(hud, i);
            if (costText.text != want) { costText.text = want; changed = true; }
            costTexts[i] = costText;

            var badge = EnsureChild(button, "Hotkey");
            changed |= SetRect(badge, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                               new Vector2(-4f, -4f), new Vector2(HotkeySize, HotkeySize));
            var badgeImage = EnsureImage(badge, new Color(0f, 0f, 0f, 0.62f), ref changed);
            badgeImage.raycastTarget = false;
            // One Graphic per GameObject: the digit must live on a child of the badge image.
            var badgeLabel = EnsureChild(badge, "Text");
            changed |= SetStretch(badgeLabel, Vector2.zero, Vector2.zero);
            var badgeText = EnsureText(badgeLabel, 10, TextAnchor.MiddleCenter, new Color(1f, 0.93f, 0.72f), ref changed);
            string key = (i + 1).ToString();
            if (badgeText.text != key) { badgeText.text = key; changed = true; }
            badge.SetAsLastSibling();
        }

        hud.buildCostTexts = costTexts;
        // The scene serialised the old default; set it explicitly so the dimmed price stays
        // legible on the greyed button instead of depending on the C# default.
        var dimTint = new Color(0.74f, 0.70f, 0.62f, 1f);
        if (hud.costDimTint != dimTint) { hud.costDimTint = dimTint; changed = true; }
        Line("Build bar: " + count + " buttons, two-line costs and 1-" + count + " hotkey badges.");
        return changed;
    }

    static string BuildCostLabel(HUD hud, int index)
    {
        var prefab = null as GameObject;
        if (hud != null && hud.buildPrefabs != null && index < hud.buildPrefabs.Length)
            prefab = hud.buildPrefabs[index];
        var building = prefab != null ? prefab.GetComponent<Building>() : null;
        if (building == null) return "";
        var cost = building.cost;
        return "金 " + cost.gold + "\n木 " + cost.wood;
    }

    static bool PolishUnitPanel(HUD hud, RectTransform root)
    {
        var panel = FindRect(root, "UnitPanel");
        bool changed = SetRect(panel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                               new Vector2(0f, 16f), UnitPanelSize);
        changed |= EnsureBacking(panel, "Backing", new Vector2(6f, 6f), new Vector2(-6f, -6f), BackingTint);

        var title = EnsureChild(panel, "Title");
        changed |= SetRect(title, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                           new Vector2(14f, -6f), new Vector2(80f, 22f));
        var titleText = EnsureText(title, 16, TextAnchor.MiddleLeft, ValueTint, ref changed);
        if (titleText.text != "兵种") { titleText.text = "兵种"; changed = true; }
        titleText.lineSpacing = 1f;

        int count = hud.unitButtons != null ? hud.unitButtons.Length : 0;
        var costs = new Text[count];
        var dims = new Image[count];
        for (int i = 0; i < count; i++)
        {
            var button = hud.unitButtons[i] != null ? hud.unitButtons[i].transform as RectTransform : null;
            if (button == null) { continue; }
            float x = UnitButtonStartX + i * UnitButtonSpacing;

            changed |= SetRect(button, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                               new Vector2(x, UnitButtonY), new Vector2(UnitButtonSize, UnitButtonSize));

            // dark portrait backing inside the blue frame
            var portrait = EnsureChild(button, "PortraitBack");
            changed |= SetStretch(portrait, new Vector2(5f, 5f), new Vector2(-5f, -5f));
            var portraitImage = EnsureImage(portrait, PortraitBackTint, ref changed);
            portraitImage.raycastTarget = false;
            portrait.SetAsFirstSibling();

            var icon = button.Find("Icon") as RectTransform;
            if (icon != null)
            {
                icon.SetSiblingIndex(Mathf.Clamp(portrait.GetSiblingIndex() + 1, 0, button.childCount - 1));
                changed |= SetStretch(icon, new Vector2(7f, 7f), new Vector2(-7f, -7f));
            }

            // dim overlay, above the portrait, below nothing else
            var dim = EnsureChild(button, "Dim");
            changed |= SetStretch(dim, Vector2.zero, Vector2.zero);
            var dimImage = EnsureImage(dim, DimTint, ref changed);
            dimImage.raycastTarget = false;
            dim.SetAsLastSibling();
            dims[i] = dimImage;

            // name under the portrait
            var name = EnsureChild(panel, "UnitName" + i);
            changed |= SetRect(name, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                               new Vector2(x, UnitNameY), new Vector2(76f, 15f));
            var nameText = EnsureText(name, 14, TextAnchor.MiddleCenter, ValueTint, ref changed);
            nameText.horizontalOverflow = HorizontalWrapMode.Wrap;
            if (hud.unitNames != null && i < hud.unitNames.Length && hud.unitNames[i] != null
                && hud.unitNames[i] != nameText)
            {
                // The scene's label lives under the panel already; keep the wired object.
                hud.unitNames[i] = nameText;
                changed = true;
            }

            // cost under the name — moved out of the button so the name has room
            var cost = button.Find("Cost") as RectTransform;
            if (cost != null) cost.SetParent(panel, false);
            var panelCost = panel.Find("UnitCost" + i) as RectTransform;
            if (panelCost == null) panelCost = cost;
            if (panelCost == null) panelCost = EnsureChild(panel, "UnitCost" + i);
            if (panelCost.name != "UnitCost" + i) { panelCost.name = "UnitCost" + i; changed = true; }
            changed |= SetRect(panelCost, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                               new Vector2(x, UnitCostY), new Vector2(76f, 15f));
            var costText = EnsureText(panelCost, 13, TextAnchor.MiddleCenter, CostTint, ref changed);
            costs[i] = costText;

            // production progress bar inside the smaller button
            var fill = button.Find("Fill") as RectTransform;
            if (fill != null)
                changed |= SetRect(fill, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                                   new Vector2(6f, 4f), new Vector2(UnitButtonSize - 12f, 6f));
        }

        hud.unitCosts = costs;
        hud.unitDims = dims;
        Line("Unit panel: horizontal title, framed portraits, name + cost stacked, dim overlays.");
        return changed;
    }

    /// <summary>The technology buttons split their face into a name band and a price band.
    /// The nine-sliced frame's art is top-aligned inside the rect, so the original 40 px
    /// button drew the price under the frame; the button is 48 px tall and both bands are
    /// pushed up into the visible face.</summary>
    static bool PolishTechStrip(HUD hud)
    {
        if (hud.techButtons == null || hud.techButtons.Length == 0) return false;
        bool changed = false;
        var dims = new Image[hud.techButtons.Length];
        for (int i = 0; i < hud.techButtons.Length; i++)
        {
            var button = hud.techButtons[i] != null ? hud.techButtons[i].transform as RectTransform : null;
            if (button == null) continue;

            changed |= SetRect(button, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                               new Vector2(i * 100f, 18f), new Vector2(92f, 48f));

            var label = button.Find("Label") as RectTransform;
            if (label != null)
                changed |= SetBand(label, 0.56f, 1f);

            var cost = button.Find("Cost") as RectTransform;
            if (cost != null)
                changed |= SetBand(cost, 0.24f, 0.56f);

            var dim = EnsureChild(button, "Dim");
            changed |= SetStretch(dim, Vector2.zero, Vector2.zero);
            var dimImage = EnsureImage(dim, DimTint, ref changed);
            dimImage.raycastTarget = false;
            dim.SetAsLastSibling();
            dims[i] = dimImage;
        }
        hud.techDims = dims;
        Line("Tech strip: " + hud.techButtons.Length + " buttons at 92x48 with raised label/price bands and dim overlays.");
        return changed;
    }

    /// <summary>Full-width horizontal band between two vertical fractions of the parent.</summary>
    static bool SetBand(RectTransform rt, float minY, float maxY)
    {
        var aMin = new Vector2(0f, minY);
        var aMax = new Vector2(1f, maxY);
        bool changed = rt.anchorMin != aMin || rt.anchorMax != aMax
                    || rt.offsetMin != Vector2.zero || rt.offsetMax != Vector2.zero;
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return changed;
    }

    /// <summary>The production panel's cost line doubles as its refusal reason
    /// ("战士已达上限" and friends), so it runs at the smallest legible size that still
    /// fits a six-character refusal inside a 64 px button.</summary>
    static bool PolishProductionPanel(HUD hud)
    {
        if (hud.productionPanel == null) return false;
        var panel = hud.productionPanel.transform as RectTransform;
        bool changed = EnsureBacking(panel, "Backing", new Vector2(6f, 6f), new Vector2(-6f, -6f), BackingTint);
        if (hud.productionTitle != null)
            EnsureText(hud.productionTitle.rectTransform, 20, TextAnchor.MiddleLeft, ValueTint, ref changed);
        if (hud.produceCosts != null)
            foreach (var text in hud.produceCosts)
                if (text != null)
                    EnsureText(text.rectTransform, 10, TextAnchor.MiddleCenter, text.color, ref changed);
        Line("Production panel: inset readable backing and fitted cost/refusal text.");
        return changed;
    }

    /// <summary>The selection body reports at most five lines (health, construction or
    /// repair, footprint, dropoff, command centre) in a 96 px box. At 18 pt with 1.15 line
    /// spacing that is 119 px, so the last line ran under the technology strip. 16 pt at
    /// 1.04 fits five lines in 96 px.</summary>
    static bool PolishSelectionPanel(HUD hud)
    {
        if (hud.selectionPanel == null) return false;
        var panel = hud.selectionPanel.transform as RectTransform;
        bool changed = EnsureBacking(panel, "Backing", new Vector2(6f, 6f), new Vector2(-6f, -6f), BackingTint);
        if (hud.selectionTitle != null)
            EnsureText(hud.selectionTitle.rectTransform, 22, TextAnchor.MiddleLeft, ValueTint, ref changed);
        var body = hud.selectionBody;
        if (body == null) return changed;
        EnsureText(body.rectTransform, 16, TextAnchor.UpperLeft, CaptionTint, ref changed);
        if (!Mathf.Approximately(body.lineSpacing, 1.04f)) { body.lineSpacing = 1.04f; changed = true; }
        Line("Selection panel: inset readable backing; body 16 pt / 1.04 spacing.");
        return changed;
    }

    static bool PolishMinimap(RectTransform root)
    {
        var panel = FindRect(root, "MinimapPanel");
        bool changed = EnsureBacking(panel, "Backing", new Vector2(10f, 12f), new Vector2(-10f, -12f), BackingTint);

        var view = panel.Find("View");
        if (view != null)
        {
            // The four edges live under View/Viewport, not directly under View.
            var viewport = view.Find("Viewport");
            var host = viewport != null ? viewport : view;
            string[] edges = { "Top", "Bottom", "Left", "Right" };
            int thickened = 0;
            foreach (var edge in edges)
            {
                var rt = host.Find(edge) as RectTransform;
                if (rt == null) continue;
                Vector2 size = rt.sizeDelta;
                if (edge == "Top" || edge == "Bottom") size.y = 4f;
                else size.x = 4f;
                if (rt.sizeDelta != size) { rt.sizeDelta = size; changed = true; thickened++; }
            }
            Line("Minimap view rectangle: " + thickened + " edge(s) set to 4 px.");
        }
        Line("Minimap: backing added.");
        return changed;
    }

    static bool PolishStatus(HUD hud, RectTransform root)
    {
        var status = root.Find("Status") as RectTransform;
        if (status == null) return false;

        bool changed = false;
        var backing = EnsureChild(root, "StatusBacking");
        changed |= SetRect(backing, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                           StatusBackingPosition, StatusBackingSize);
        var image = EnsureImage(backing, new Color(0f, 0f, 0f, 0.35f), ref changed);
        image.raycastTarget = false;
        // The strip only exists while there is something to read; the scene ships it off.
        if (image.enabled) { image.enabled = false; changed = true; }
        if (hud.statusBacking != image) { hud.statusBacking = image; changed = true; }
        if (backing.GetSiblingIndex() >= status.GetSiblingIndex())
        {
            backing.SetSiblingIndex(status.GetSiblingIndex());
            changed = true;
        }
        Line("Status line: dark backing strip (hidden while empty).");
        return changed;
    }

    /// <summary>Every button label must be centred on both axes and fit inside its button.</summary>
    static bool NormalizeButtonLabels(RectTransform canvasRt)
    {
        bool changed = false;
        var buttons = canvasRt.GetComponentsInChildren<Button>(true);
        foreach (var button in buttons)
        {
            var label = button.transform.Find("Label") as RectTransform;
            if (label == null) continue;
            var text = label.GetComponent<Text>();
            if (text == null) continue;

            if (text.alignment != TextAnchor.MiddleCenter)
            {
                text.alignment = TextAnchor.MiddleCenter;
                changed = true;
            }

            Vector2 offset = LabelCentreOffset(label, button.transform as RectTransform);
            // The technology buttons deliberately split their face into a name band and a
            // price band, so their label is centred inside its own band, not the button.
            // Centring it on the whole button would slide it over the price.
            bool splitFace = label.anchorMin.y != label.anchorMax.y;
            if (!splitFace && offset.sqrMagnitude > 0.25f)
            {
                changed |= SetStretch(label, Vector2.zero, Vector2.zero);
                Line("Centred label of " + button.name + " (was off by " + offset.ToString("F1") + ")");
                offset = LabelCentreOffset(label, button.transform as RectTransform);
                if (offset.sqrMagnitude > 0.25f)
                    Line("  still off-centre by " + offset.ToString("F1") + " — an icon occupies the row.");
            }

            int size = text.fontSize;
            while (text.preferredWidth > label.rect.width + 0.5f
                   || text.preferredHeight > label.rect.height + 0.5f)
            {
                if (size <= 10)
                {
                    throw new InvalidOperationException(
                        "Button label \"" + text.text + "\" on " + button.name + " does not fit its rect: "
                        + text.preferredWidth.ToString("F1") + "x" + text.preferredHeight.ToString("F1")
                        + " vs " + label.rect.width.ToString("F1") + "x" + label.rect.height.ToString("F1"));
                }
                size--;
                text.fontSize = size;
                changed = true;
            }
        }
        Line("Button labels checked: " + buttons.Length + " buttons.");
        return changed;
    }

    /// <summary>Distance between the label's centre and the button's centre, in button space.</summary>
    static Vector2 LabelCentreOffset(RectTransform label, RectTransform button)
    {
        if (button == null) return Vector2.zero;
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

    // ------------------------------------------------------------- validation

    /// <summary>
    /// Refuses to persist a layout where a touched rect left its parent, two siblings of a
    /// touched panel overlap, or the status backing covers the unit shop.
    /// </summary>
    static void ValidateLayout(RectTransform canvasRt, RectTransform root)
    {
        var problems = new List<string>();

        foreach (var pair in new[]
        {
            new KeyValuePair<string, string>("ResourceBar", "Backing"),
            new KeyValuePair<string, string>("BuildBar", "Backing"),
            new KeyValuePair<string, string>("UnitPanel", "Backing"),
            new KeyValuePair<string, string>("MinimapPanel", "Backing"),
        })
        {
            var parent = root.Find(pair.Key) as RectTransform;
            var child = parent != null ? parent.Find(pair.Value) as RectTransform : null;
            if (parent == null || child == null) { problems.Add("missing " + pair.Key + "/" + pair.Value); continue; }
            Rect c = CanvasRect(child, canvasRt), p = CanvasRect(parent, canvasRt);
            if (!Inside(c, p)) problems.Add(pair.Key + " backing leaves its panel: " + R(c) + " vs " + R(p));
        }

        var panel = root.Find("UnitPanel") as RectTransform;
        if (panel != null)
        {
            var rects = new List<KeyValuePair<string, Rect>>();
            foreach (var child in panel.Cast<Transform>())
            {
                var rt = child as RectTransform;
                if (rt == null || !rt.gameObject.activeSelf) continue;
                if (rt.name == "Backing") continue;
                rects.Add(new KeyValuePair<string, Rect>(rt.name, CanvasRect(rt, canvasRt)));
            }
            for (int i = 0; i < rects.Count; i++)
            {
                for (int k = i + 1; k < rects.Count; k++)
                {
                    if (rects[i].Value.Overlaps(rects[k].Value))
                        problems.Add("UnitPanel siblings overlap: " + rects[i].Key + " " + R(rects[i].Value)
                                     + " / " + rects[k].Key + " " + R(rects[k].Value));
                }
            }
        }

        var status = root.Find("StatusBacking") as RectTransform;
        var unitPanel = root.Find("UnitPanel") as RectTransform;
        if (status != null && unitPanel != null)
        {
            Rect s = CanvasRect(status, canvasRt), u = CanvasRect(unitPanel, canvasRt);
            if (s.Overlaps(u)) problems.Add("StatusBacking overlaps UnitPanel");
        }

        var minimap = root.Find("MinimapPanel") as RectTransform;
        var selection = root.Find("SelectionPanel") as RectTransform;
        if (minimap != null && selection != null)
        {
            Rect m = CanvasRect(minimap, canvasRt), sel = CanvasRect(selection, canvasRt);
            if (m.Overlaps(sel)) problems.Add("MinimapPanel overlaps SelectionPanel");
        }

        // Cost readouts are not button labels, but they spill the same way: check the
        // static ones the tool itself authors.
        var costs = new List<Text>();
        foreach (var button in canvasRt.GetComponentsInChildren<Button>(true))
        {
            var cost = button.transform.Find("Cost");
            if (cost != null) costs.Add(cost.GetComponent<Text>());
        }
        var panelForCosts = root.Find("UnitPanel") as RectTransform;
        if (panelForCosts != null)
        {
            foreach (var child in panelForCosts.Cast<Transform>())
            {
                if (!child.name.StartsWith("UnitCost")) continue;
                var t = child.GetComponent<Text>();
                if (t != null) costs.Add(t);
            }
        }
        float tightest = float.MaxValue;
        foreach (var cost in costs)
        {
            if (cost == null || string.IsNullOrEmpty(cost.text)) continue;
            var rt = cost.rectTransform;
            float marginX = rt.rect.width - cost.preferredWidth;
            float marginY = rt.rect.height - cost.preferredHeight;
            tightest = Mathf.Min(tightest, Mathf.Min(marginX, marginY));
            if (marginX < -0.5f || marginY < -0.5f)
                problems.Add("Cost text \"" + cost.text + "\" on " + cost.transform.parent.name
                             + " does not fit: " + cost.preferredWidth.ToString("F1") + "x"
                             + cost.preferredHeight.ToString("F1") + " in "
                             + rt.rect.width.ToString("F1") + "x" + rt.rect.height.ToString("F1"));
        }
        if (tightest < float.MaxValue)
            Line("Cost readouts checked: " + costs.Count + ", tightest margin " + tightest.ToString("F1") + " px.");

        if (problems.Count > 0)
        {
            foreach (var p in problems) Line("LAYOUT: " + p);
            throw new InvalidOperationException("Layout validation failed with " + problems.Count
                + " problem(s); the scene was NOT saved.");
        }
        Line("Layout validated.");
    }

    static bool Inside(Rect child, Rect parent)
    {
        return child.xMin >= parent.xMin - 0.5f && child.xMax <= parent.xMax + 0.5f
            && child.yMin >= parent.yMin - 0.5f && child.yMax <= parent.yMax + 0.5f;
    }

    static Rect CanvasRect(RectTransform child, RectTransform canvas)
    {
        var corners = new Vector3[4];
        child.GetWorldCorners(corners);
        Vector2 min = canvas.InverseTransformPoint(corners[0]);
        Vector2 max = min;
        for (int i = 1; i < corners.Length; i++)
        {
            Vector2 point = canvas.InverseTransformPoint(corners[i]);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    static string R(Rect r)
    {
        return "(" + r.xMin.ToString("F0") + "," + r.yMin.ToString("F0") + ")-("
             + r.xMax.ToString("F0") + "," + r.yMax.ToString("F0") + ")";
    }

    // ---------------------------------------------------------------- helpers

    static RectTransform FindRect(Transform parent, string name)
    {
        var rt = parent.Find(name) as RectTransform;
        if (rt == null) throw new InvalidOperationException("Missing " + name + " under " + parent.name);
        return rt;
    }

    static RectTransform EnsureChild(Transform parent, string name)
    {
        var existing = parent.Find(name) as RectTransform;
        if (existing != null) return existing;
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    static Text EnsureText(RectTransform rt, int fontSize, TextAnchor anchor, Color color, ref bool changed)
    {
        var text = rt.GetComponent<Text>();
        if (text == null)
        {
            var other = rt.GetComponent<Graphic>();
            if (other != null)
                throw new InvalidOperationException(rt.name + " already carries a " + other.GetType().Name
                    + "; a Text needs a child object of its own.");
            text = rt.gameObject.AddComponent<Text>();
            changed = true;
        }
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (text.font != font) { Line("    font differs on " + rt.name); text.font = font; changed = true; }
        if (text.fontSize != fontSize) { Line("    fontSize " + text.fontSize + " -> " + fontSize + " on " + rt.name); text.fontSize = fontSize; changed = true; }
        if (text.alignment != anchor) { Line("    alignment " + text.alignment + " on " + rt.name); text.alignment = anchor; changed = true; }
        if (text.color != color) { Line("    colour " + text.color.ToString("F3") + " -> " + color.ToString("F3") + " on " + rt.name); text.color = color; changed = true; }
        if (text.raycastTarget) { Line("    raycastTarget on " + rt.name); text.raycastTarget = false; changed = true; }
        return text;
    }

    static Text EnsureTextAt(Transform parent, string name, Vector2 pos, Vector2 size,
                             int fontSize, TextAnchor anchor, Color color, ref bool changed)
    {
        var rt = EnsureChild(parent, name);
        changed |= SetRect(rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), pos, size);
        return EnsureText(rt, fontSize, anchor, color, ref changed);
    }

    static Image EnsureImage(RectTransform rt, Color color, ref bool changed)
    {
        var image = rt.GetComponent<Image>();
        if (image == null) { image = rt.gameObject.AddComponent<Image>(); changed = true; }
        if (image.sprite != null) { image.sprite = null; changed = true; }
        if (image.type != Image.Type.Simple) { image.type = Image.Type.Simple; changed = true; }
        if (image.color != color) { image.color = color; changed = true; }
        return image;
    }

    /// <summary>A full-width dark strip behind a panel's content, inset by the given margins.</summary>
    static bool EnsureBacking(RectTransform parent, string name, Vector2 min, Vector2 max, Color color)
    {
        bool changed = false;
        var rt = EnsureChild(parent, name);
        changed |= SetStretch(rt, min, max);
        var image = EnsureImage(rt, color, ref changed);
        image.raycastTarget = false;
        if (rt.GetSiblingIndex() != 0)
        {
            rt.SetSiblingIndex(0);
            changed = true;
        }
        return changed;
    }

    static bool SetRect(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 pivot,
                        Vector2 pos, Vector2 size)
    {
        bool changed = rt.anchorMin != aMin || rt.anchorMax != aMax || rt.pivot != pivot
                    || rt.anchoredPosition != pos || rt.sizeDelta != size;
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return changed;
    }

    static bool SetStretch(RectTransform rt, Vector2 offsetMin, Vector2 offsetMax)
    {
        bool changed = rt.anchorMin != Vector2.zero || rt.anchorMax != Vector2.one
                    || rt.pivot != new Vector2(0.5f, 0.5f)
                    || rt.offsetMin != offsetMin || rt.offsetMax != offsetMax;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        return changed;
    }

    static string ProjectPath(string relative)
    {
        return Path.GetFullPath(Path.Combine(Path.Combine(Application.dataPath, ".."), relative));
    }

    static string Md5(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var md5 = MD5.Create())
        {
            var hash = md5.ComputeHash(stream);
            var sb = new StringBuilder();
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    static void Line(string message)
    {
        Log.AppendLine(message);
    }

    /// <summary>Records whether a panel actually changed; a clean second run must report
    /// false for every panel and skip the save entirely.</summary>
    static bool Step(string name, bool changed)
    {
        if (changed) Line("changed: " + name);
        return changed;
    }

    static void WriteToolLog(string relativePath)
    {
        try
        {
            string full = ProjectPath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, Log.ToString());
        }
        catch (Exception e)
        {
            Debug.LogWarning("[PolishHUD] Could not write the tool log: " + e.Message);
        }
    }
}

