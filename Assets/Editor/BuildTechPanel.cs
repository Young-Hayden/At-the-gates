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

/// <summary>Adds the technology strip to the selection panel without rebuilding the HUD
/// (Tools/重建 HUD wipes the whole canvas, pause menu included, so it must not be re-run
/// for a one-panel change).
///
/// Same in-place pattern as <see cref="BuildRepairButton"/>: SampleScene is already the
/// active scene, so it is never opened a second time additively. Safe to re-run — an
/// existing strip is rewired, never duplicated, and a run that changes nothing skips the
/// save entirely (every save re-rolls the animated water tiles' m_AnimationSpeed, so a
/// no-op save would churn the file for nothing).
///
/// The selection panel is 320x190 and completely packed: Title 150..180, Body 48..144 and
/// the command row at 10..44 (panel space, origin bottom-left) leave a 4px gap. There is
/// no band a 40px button row can occupy, so the panel is grown downward by 76px (its
/// top-right pivot keeps the top edge fixed) and the strip takes the new band at
/// y 50..118. Every existing child keeps its own rect: Title and Body are anchored to the
/// top, the command row to the bottom, so none of them is moved or resized.
///
/// Also applies this task's two scene-side balance values (EnemyAI maxGatherers 4 -> 5,
/// firstAttack 90 -> 120) so the whole task makes exactly one scene write.</summary>
public static class BuildTechPanel
{
    const string ScenePath = "Assets/Scenes/SampleScene.scene";
    const string PhaseDirectory = ".superpowers/sdd/phase-b";
    const string BackupPath = PhaseDirectory + "/task-5-scene-before.scene";
    const string ToolLogPath = PhaseDirectory + "/task-5-tool.log";

    const string StripName = "TechStrip";
    const string ProgressName = "Progress";
    const string LabelName = "Label";
    const string CostName = "Cost";

    // One entry per Technology.Tech value: Forge = 0, Armour = 1, Gathering = 2.
    static readonly string[] ButtonNames = { "Tech_Forge", "Tech_Armour", "Tech_Gather" };
    static readonly string[] ButtonCaptions = { "锻造", "铠甲", "采集" };
    static readonly string[] ButtonCosts = { "250金100木", "250金100木", "200金" };

    static readonly string[] ExistingNames = { "Title", "Body", "Demolish", "Attack", "Repair" };

    const float PanelHeightBefore = 190f;
    const float PanelHeightAfter = 266f;
    const float PanelWidth = 320f;

    // Strip and children in panel space (bottom-left origin, the frame the brief quotes).
    static readonly Rect StripRect = Rect.MinMaxRect(14f, 50f, 306f, 118f);
    const float ButtonWidth = 92f;
    const float ButtonHeight = 40f;
    const float ButtonGap = 8f;
    const float ButtonRowY = 22f;          // inside the strip: buttons sit at the top
    static readonly Vector2 ProgressSize = new Vector2(292f, 18f);

    static readonly StringBuilder Log = new StringBuilder();

    [MenuItem("Tools/UI/添加科技条")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[HUD] 科技条：请先退出 PlayMode。");
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
                if (loaded.path == ScenePath && loaded.isLoaded) { scene = loaded; break; }
            }

            if (!scene.IsValid() || !scene.isLoaded)
            {
                previousActive = SceneManager.GetActiveScene();
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                openedByTool = true;
                Line("SampleScene was not loaded; opened additively for this run.");
            }
            else
            {
                Line("SampleScene was already open; operating on the loaded scene.");
            }

            // New GameObjects are created in the active scene. If SampleScene happened to
            // be loaded but not active, make it active for the run and put it back after.
            if (SceneManager.GetActiveScene() != scene)
            {
                previousActive = SceneManager.GetActiveScene();
                SceneManager.SetActiveScene(scene);
                switchedActive = true;
                Line("SampleScene was not the active scene; switched for this run.");
            }

            Run(scene, openedByTool);
        }
        catch (Exception e)
        {
            Line("ERROR: " + e);
            Debug.LogError("[HUD] 科技条失败：" + e.Message);
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
            WriteToolLog();
        }
    }

    static void Run(Scene scene, bool openedByTool)
    {
        var hud = FindHud(scene);
        if (hud == null) throw new InvalidOperationException("HUD missing in " + ScenePath);
        if (hud.selectionPanel == null)
            throw new InvalidOperationException("HUD.selectionPanel missing");
        var panel = hud.selectionPanel.GetComponent<RectTransform>();
        if (panel == null)
            throw new InvalidOperationException("selectionPanel has no RectTransform");

        // ---- byte backup + pre-save MD5 ----
        string fullScenePath = ProjectPath(ScenePath);
        string fullBackupPath = ProjectPath(BackupPath);
        string md5Before = Md5(fullScenePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullBackupPath));

        bool modified = false;
        if (!File.Exists(fullBackupPath))
        {
            File.Copy(fullScenePath, fullBackupPath, false);
            Line("Backup copied: " + fullBackupPath);
        }
        else
        {
            string backupMd5 = Md5(fullBackupPath);
            if (backupMd5 == md5Before)
                Line("Backup exists and matches the current scene: " + fullBackupPath);
            else
                Line("Backup exists (original pre-change state); left untouched: " + fullBackupPath);
        }
        Line("MD5 before: " + md5Before);

        // ---- scene-side balance values (so the task makes one save) ----
        bool balanceChanged = ApplyEnemyAiValues(scene);
        modified |= balanceChanged;

        // The taller selection panel needed the minimap moved down. That offset only lived
        // in the scene, so re-running 添加或更新小地图 (-218) and then this tool put the
        // minimap back under the panel. Encoded here so the chain ends in the right place.
        bool minimapChanged = ApplyMinimapPosition(scene);
        modified |= minimapChanged;

        // ---- panel growth ----
        Vector2 panelSize = panel.sizeDelta;
        bool resized = false;
        if (Mathf.Abs(panelSize.x - PanelWidth) > 0.01f)
            throw new InvalidOperationException("SelectionPanel width is " + panelSize.x + ", expected " + PanelWidth);
        if (Mathf.Abs(panelSize.y - PanelHeightBefore) > 0.01f)
        {
            if (Mathf.Abs(panelSize.y - PanelHeightAfter) > 0.01f)
                throw new InvalidOperationException("SelectionPanel height is " + panelSize.y
                    + ", expected " + PanelHeightBefore + " (before) or " + PanelHeightAfter + " (already applied)");
            Line("Panel already grown to " + PanelHeightAfter + ".");
        }
        else
        {
            panel.sizeDelta = new Vector2(PanelWidth, PanelHeightAfter);
            resized = true;
            modified = true;
            Line("Grew SelectionPanel height " + PanelHeightBefore + " -> " + PanelHeightAfter
                + " (pivot is the top-right corner, so the top edge stays put).");
        }

        // ---- strip root ----
        var existingStrip = panel.Find(StripName) as RectTransform;
        bool created = existingStrip == null;
        RectTransform strip;
        if (created)
        {
            var go = new GameObject(StripName, typeof(RectTransform));
            strip = go.GetComponent<RectTransform>();
            strip.SetParent(panel, false);
            Line("Created " + StripName + ".");
        }
        else
        {
            strip = existingStrip;
            Line(StripName + " already in place; rewiring.");
        }

        strip.anchorMin = Vector2.zero;
        strip.anchorMax = Vector2.zero;
        strip.pivot = Vector2.zero;
        strip.anchoredPosition = new Vector2(StripRect.xMin, StripRect.yMin);
        strip.sizeDelta = new Vector2(StripRect.width, StripRect.height);

        // ---- three tech buttons ----
        var buttons = new Button[ButtonNames.Length];
        var costs = new Text[ButtonNames.Length];
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        for (int i = 0; i < ButtonNames.Length; i++)
        {
            var buttonRt = strip.Find(ButtonNames[i]) as RectTransform;
            GameObject buttonGo;
            if (buttonRt == null)
            {
                buttonGo = new GameObject(ButtonNames[i], typeof(RectTransform), typeof(Image), typeof(Button));
                buttonRt = buttonGo.GetComponent<RectTransform>();
                buttonRt.SetParent(strip, false);
                Line("Created button " + ButtonNames[i] + ".");
            }
            else
            {
                buttonGo = buttonRt.gameObject;
                if (buttonGo.GetComponent<Image>() == null) buttonGo.AddComponent<Image>();
                if (buttonGo.GetComponent<Button>() == null) buttonGo.AddComponent<Button>();
            }

            buttonRt.anchorMin = Vector2.zero;
            buttonRt.anchorMax = Vector2.zero;
            buttonRt.pivot = Vector2.zero;
            buttonRt.anchoredPosition = new Vector2(i * (ButtonWidth + ButtonGap), ButtonRowY);
            buttonRt.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);

            var image = buttonGo.GetComponent<Image>();
            var sprite = Sprite("Assets/Art/UI/Buttons/Button_Blue_9Slides.png");
            image.sprite = sprite;
            image.type = sprite != null && sprite.border != Vector4.zero
                ? Image.Type.Sliced : Image.Type.Simple;
            image.color = Color.white;

            var button = buttonGo.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.SpriteSwap;
            var state = button.spriteState;
            state.pressedSprite = Sprite("Assets/Art/UI/Buttons/Button_Blue_9Slides_Pressed.png");
            state.highlightedSprite = Sprite("Assets/Art/UI/Buttons/Button_Hover_9Slides.png");
            button.spriteState = state;
            button.interactable = true;

            // name on the upper two lines, price under it
            var labelRt = EnsureChild(buttonRt, LabelName);
            labelRt.anchorMin = new Vector2(0f, 0.42f);
            labelRt.anchorMax = new Vector2(1f, 1f);
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            var label = EnsureText(labelRt, font);
            label.text = ButtonCaptions[i];
            label.fontSize = 15;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(1f, 0.97f, 0.88f);

            var costRt = EnsureChild(buttonRt, CostName);
            costRt.anchorMin = new Vector2(0f, 0f);
            costRt.anchorMax = new Vector2(1f, 0.45f);
            costRt.offsetMin = Vector2.zero;
            costRt.offsetMax = Vector2.zero;
            var cost = EnsureText(costRt, font);
            cost.text = ButtonCosts[i];
            cost.fontSize = 12;
            cost.alignment = TextAnchor.MiddleCenter;
            cost.color = new Color(1f, 0.97f, 0.85f);

            buttons[i] = button;
            costs[i] = cost;
        }

        // ---- shared progress label ----
        var progressRt = EnsureChild(strip, ProgressName);
        progressRt.anchorMin = Vector2.zero;
        progressRt.anchorMax = Vector2.zero;
        progressRt.pivot = Vector2.zero;
        progressRt.anchoredPosition = Vector2.zero;
        progressRt.sizeDelta = ProgressSize;
        var progress = EnsureText(progressRt, font);
        progress.text = "";
        progress.fontSize = 13;
        progress.alignment = TextAnchor.MiddleCenter;
        progress.color = new Color(1f, 0.92f, 0.7f);

        // ---- wiring ----
        bool wiringChanged = hud.techStrip != strip.gameObject
            || hud.techButtons == null || hud.techButtons.Length != buttons.Length
            || hud.techCosts == null || hud.techCosts.Length != costs.Length
            || hud.techProgress != progress;
        if (!wiringChanged)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                if (hud.techButtons[i] != buttons[i] || hud.techCosts[i] != costs[i])
                {
                    wiringChanged = true;
                    break;
                }
            }
        }
        hud.techStrip = strip.gameObject;
        hud.techButtons = buttons;
        hud.techCosts = costs;
        hud.techProgress = progress;
        modified |= wiringChanged;
        Line("hud.techStrip wired=" + wiringChanged);

        bool activeChanged = strip.gameObject.activeSelf;
        if (activeChanged) strip.gameObject.SetActive(false);
        modified |= activeChanged;

        // ---- pre-save validation (refuse to persist a bad layout) ----
        ValidateLayout(panel, strip, buttons, progressRt);

        // ---- save only when something actually changed ----
        string md5After;
        if (created || resized || wiringChanged || activeChanged || balanceChanged || minimapChanged || scene.isDirty)
        {
            if (!EditorSceneManager.MarkSceneDirty(scene) && !scene.isDirty)
                throw new InvalidOperationException("Could not mark SampleScene dirty");
            if (!EditorSceneManager.SaveScene(scene))
                throw new IOException("Could not save " + ScenePath);
            md5After = Md5(fullScenePath);
            Line("Saved " + ScenePath + ".");
        }
        else
        {
            md5After = Md5(fullScenePath);
            Line("No serialized change; save skipped (avoids the 400 m_AnimationSpeed re-roll).");
        }
        Line("MD5 after: " + md5After);

        // ---- hard assertions after the save ----
        var problems = new List<string>();
        if (!openedByTool && EditorSceneManager.sceneCount != 1)
            problems.Add("sceneCount=" + EditorSceneManager.sceneCount + " (expected 1)");
        if (CountChildren(panel, StripName) != 1)
            problems.Add("TechStrip count under SelectionPanel=" + CountChildren(panel, StripName) + " (expected 1)");
        if (hud.techStrip == null) problems.Add("hud.techStrip is null");
        else if (hud.techStrip != strip.gameObject) problems.Add("hud.techStrip points at " + hud.techStrip.name);
        if (hud.techButtons == null || hud.techButtons.Length != buttons.Length)
            problems.Add("hud.techButtons length=" + (hud.techButtons == null ? "null" : hud.techButtons.Length.ToString()));
        if (hud.techProgress == null) problems.Add("hud.techProgress is null");
        if (!openedByTool && SceneManager.GetActiveScene().path != ScenePath)
            problems.Add("active scene=" + SceneManager.GetActiveScene().path);

        var layoutProblems = new List<string>();
        ValidateLayout(panel, strip, buttons, progressRt, layoutProblems);
        problems.AddRange(layoutProblems);

        if (problems.Count > 0)
            throw new InvalidOperationException("Post-save assertions failed: " + string.Join("; ", problems.ToArray()));

        Line("Assertions OK: sceneCount=" + EditorSceneManager.sceneCount
            + ", techStrips=" + CountChildren(panel, StripName)
            + ", buttons=" + buttons.Length
            + ", activeScene=" + SceneManager.GetActiveScene().path);
        Line("Done.");
        Debug.Log("[HUD] 科技条已加入选中面板。");
    }

    /// <summary>Where the taller selection panel needs the minimap to sit.</summary>
    const float MinimapY = -306f;

    /// <summary>Puts the minimap where the 266 px selection panel requires. Returns true when
    /// it had to move. This offset used to live only in the scene, so re-running
    /// 添加或更新小地图 (-218) followed by this tool restored the overlapping layout.</summary>
    static bool ApplyMinimapPosition(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var rt in root.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt.name != "MinimapPanel") continue;
                var want = new Vector2(-16f, MinimapY);
                if ((rt.anchoredPosition - want).sqrMagnitude < 0.0001f) return false;
                Line("MinimapPanel " + rt.anchoredPosition.ToString("F0") + " -> " + want.ToString("F0"));
                rt.anchoredPosition = want;
                EditorUtility.SetDirty(rt);
                return true;
            }
        }
        Line("MinimapPanel not found; SelectionPanel_DoesNotOverlapMinimap_AndBothFitCanvas will catch it.");
        return false;
    }

    /// <summary>Applies the Task 5 AI balance values. Returns true when a value moved.</summary>
    static bool ApplyEnemyAiValues(Scene scene)    {
        bool changed = false;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var ai in root.GetComponentsInChildren<AIController>(true))
            {
                if (Mathf.Abs(ai.maxGatherers - 5) > 0.001f)
                {
                    Line("EnemyAI maxGatherers " + ai.maxGatherers + " -> 5");
                    ai.maxGatherers = 5;
                    EditorUtility.SetDirty(ai);
                    changed = true;
                }
                if (Mathf.Abs(ai.firstAttack - 120f) > 0.001f)
                {
                    Line("EnemyAI firstAttack " + ai.firstAttack + " -> 120");
                    ai.firstAttack = 120f;
                    EditorUtility.SetDirty(ai);
                    changed = true;
                }
            }
        }
        if (!changed) Line("EnemyAI values already 5 / 120.");
        return changed;
    }

    static void ValidateLayout(RectTransform panel, RectTransform strip, Button[] buttons,
                               RectTransform progress, List<string> problems = null)
    {
        var local = problems != null ? problems : new List<string>();

        Rect panelRect = new Rect(0f, 0f, panel.rect.width, panel.rect.height);
        Rect stripRect = PanelSpace(strip, panel);
        var newRects = new List<KeyValuePair<string, Rect>>();
        newRects.Add(new KeyValuePair<string, Rect>(StripName, stripRect));
        for (int i = 0; i < buttons.Length; i++)
            newRects.Add(new KeyValuePair<string, Rect>(ButtonNames[i], PanelSpace(buttons[i].transform as RectTransform, panel)));
        newRects.Add(new KeyValuePair<string, Rect>(ProgressName, PanelSpace(progress, panel)));

        foreach (var pair in newRects)
        {
            Rect r = pair.Value;
            Line(pair.Key + " rect (panel space): x=" + F(r.xMin) + ".." + F(r.xMax)
                + " y=" + F(r.yMin) + ".." + F(r.yMax) + " size=" + F(r.width) + "x" + F(r.height));
        }

        foreach (var pair in newRects)
        {
            if (!panelRect.Contains(pair.Value.min) || !panelRect.Contains(pair.Value.max))
                local.Add(pair.Key + " is not inside the panel [" + F(panelRect.xMin) + ".." + F(panelRect.xMax)
                    + " x " + F(panelRect.yMin) + ".." + F(panelRect.yMax) + "]");
        }

        for (int i = 0; i < ExistingNames.Length; i++)
        {
            var existing = panel.Find(ExistingNames[i]) as RectTransform;
            if (existing == null)
                throw new InvalidOperationException("SelectionPanel has no '" + ExistingNames[i] + "' child; "
                    + "cannot prove the new strip is clear of it");
            Rect existingRect = PanelSpace(existing, panel);
            Line(ExistingNames[i] + " rect (panel space): x=" + F(existingRect.xMin) + ".." + F(existingRect.xMax)
                + " y=" + F(existingRect.yMin) + ".." + F(existingRect.yMax));
            foreach (var pair in newRects)
            {
                if (pair.Value.Overlaps(existingRect))
                    local.Add(pair.Key + " intersects " + ExistingNames[i]);
            }
        }

        int stripChildren = strip.childCount;
        if (stripChildren != ButtonNames.Length + 1)
            local.Add(StripName + " has " + stripChildren + " children (expected " + (ButtonNames.Length + 1) + ")");
        for (int i = 0; i < ButtonNames.Length; i++)
        {
            if (CountChildren(strip, ButtonNames[i]) != 1)
                local.Add("button count '" + ButtonNames[i] + "'=" + CountChildren(strip, ButtonNames[i]) + " (expected 1)");
        }

        if (problems == null && local.Count > 0)
            throw new InvalidOperationException("Layout validation failed: " + string.Join("; ", local.ToArray()));
    }

    /// <summary>Child rect expressed in the panel's local space, from world corners so
    /// anchors/pivots cannot skew the test, then shifted from the panel's top-right pivot
    /// to the bottom-left origin the brief's measurements use.</summary>
    static Rect PanelSpace(RectTransform child, RectTransform panel)
    {
        var corners = new Vector3[4];
        child.GetWorldCorners(corners);
        Vector2 min = panel.InverseTransformPoint(corners[0]);
        Vector2 max = min;
        for (int i = 1; i < corners.Length; i++)
        {
            Vector2 p = panel.InverseTransformPoint(corners[i]);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
        return Rect.MinMaxRect(min.x + panel.rect.width, min.y + panel.rect.height,
                               max.x + panel.rect.width, max.y + panel.rect.height);
    }

    static RectTransform EnsureChild(RectTransform parent, string name)
    {
        var existing = parent.Find(name) as RectTransform;
        if (existing != null) return existing;
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    static Text EnsureText(RectTransform rt, Font font)
    {
        var text = rt.GetComponent<Text>();
        if (text == null) text = rt.gameObject.AddComponent<Text>();
        if (font != null) text.font = font;
        return text;
    }

    static HUD FindHud(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var hud = root.GetComponentInChildren<HUD>(true);
            if (hud != null) return hud;
        }
        return null;
    }

    static int CountChildren(Transform parent, string name)
    {
        int count = 0;
        for (int i = 0; i < parent.childCount; i++)
            if (parent.GetChild(i).name == name) count++;
        return count;
    }

    static string F(float value) => value.ToString("0.##");

    static Sprite Sprite(string path) =>
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();

    static string ProjectPath(string relative) =>
        Path.GetFullPath(Path.Combine(Path.Combine(Application.dataPath, ".."), relative));

    static string Md5(string path)
    {
        using (var md5 = MD5.Create())
        using (var stream = File.OpenRead(path))
            return BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }

    static void Line(string text) => Log.AppendLine(text);

    static void WriteToolLog()
    {
        try
        {
            string path = ProjectPath(ToolLogPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, Log.ToString(), new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            Debug.LogWarning("[HUD] Could not write " + ToolLogPath + ": " + e.Message);
        }
    }
}
