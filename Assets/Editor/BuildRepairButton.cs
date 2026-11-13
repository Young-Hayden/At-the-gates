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

/// <summary>Adds the repair-command button to the selection panel without rebuilding
/// the HUD (BuildHUD wipes the whole canvas, pause menu included, so it must not be
/// re-run for a one-button change).
///
/// Unlike the older additive builders, this one never opens a second copy of
/// SampleScene: if the scene is already loaded it operates on that instance in place.
/// Safe to re-run: an existing button is rewired, not duplicated, and a run that
/// changes nothing skips the save entirely (every save re-rolls the animated water
/// tiles' m_AnimationSpeed, so a no-op save would churn the file for nothing).</summary>
public static class BuildRepairButton
{
    const string ScenePath = "Assets/Scenes/SampleScene.scene";
    const string PhaseDirectory = ".superpowers/sdd/phase-a";
    const string BackupPath = PhaseDirectory + "/task-4-scene-before.scene";
    const string ToolLogPath = PhaseDirectory + "/task-4b-tool.log";

    static readonly StringBuilder Log = new StringBuilder();

    [MenuItem("Tools/UI/添加维修按钮")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[HUD] 维修按钮：请先退出 PlayMode。");
            return;
        }

        Log.Length = 0;
        var scene = default(Scene);
        var previousActive = default(Scene);
        bool openedByTool = false;

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

            Run(scene, openedByTool);
        }
        catch (Exception e)
        {
            Line("ERROR: " + e);
            Debug.LogError("[HUD] 维修按钮失败：" + e.Message);
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

        var existingButton = panel.Find("Repair") as RectTransform;
        bool created = existingButton == null;
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
            else if (created)
            {
                // Stale partial-run backup while the button does not exist yet: the
                // brief requires the backup to equal the pre-save file.
                File.Copy(fullScenePath, fullBackupPath, true);
                Line("Backup existed but differed before the first change; refreshed: " + fullBackupPath);
            }
            else
                Line("Backup exists (original pre-change state); left untouched: " + fullBackupPath);
        }
        Line("MD5 before: " + md5Before);

        // ---- button root ----
        RectTransform rt;
        GameObject go;
        if (created)
        {
            go = new GameObject("Repair", typeof(RectTransform), typeof(Image), typeof(Button));
            rt = go.GetComponent<RectTransform>();
            rt.SetParent(panel, false);
            Line("Created Repair button.");
        }
        else
        {
            rt = existingButton;
            go = rt.gameObject;
            if (go.GetComponent<Image>() == null) go.AddComponent<Image>();
            if (go.GetComponent<Button>() == null) go.AddComponent<Button>();
            Line("Repair already in place; rewiring.");
        }

        // Empty left slot of the bottom row: x 14..98, 6px clear of 拆除 (104..208).
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(14f, 10f);
        rt.sizeDelta = new Vector2(84f, 34f);

        var image = go.GetComponent<Image>();
        var sprite = Sprite("Assets/Art/UI/Buttons/Button_Blue_9Slides.png");
        image.sprite = sprite;
        image.type = sprite != null && sprite.border != Vector4.zero
            ? Image.Type.Sliced : Image.Type.Simple;

        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.SpriteSwap;
        var state = button.spriteState;
        state.pressedSprite = Sprite("Assets/Art/UI/Buttons/Button_Blue_9Slides_Pressed.png");
        state.highlightedSprite = Sprite("Assets/Art/UI/Buttons/Button_Hover_9Slides.png");
        button.spriteState = state;

        // ---- icon (wrench, left of the label) ----
        var iconRt = rt.Find("Icon") as RectTransform;
        if (iconRt == null)
        {
            var iconGO = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconRt = iconGO.GetComponent<RectTransform>();
            iconRt.SetParent(rt, false);
        }
        else if (iconRt.GetComponent<Image>() == null)
        {
            iconRt.gameObject.AddComponent<Image>();
        }
        iconRt.anchorMin = new Vector2(0f, 0.5f);
        iconRt.anchorMax = new Vector2(0f, 0.5f);
        iconRt.pivot = new Vector2(0f, 0.5f);
        iconRt.anchoredPosition = new Vector2(6f, 0f);
        iconRt.sizeDelta = new Vector2(24f, 24f);
        var icon = iconRt.GetComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        // ---- label ----
        var labelRt = rt.Find("Label") as RectTransform;
        if (labelRt == null)
        {
            var labelGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelRt = labelGO.GetComponent<RectTransform>();
            labelRt.SetParent(rt, false);
        }
        else if (labelRt.GetComponent<Text>() == null)
        {
            labelRt.gameObject.AddComponent<Text>();
        }
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(30f, 0f);
        labelRt.offsetMax = Vector2.zero;
        var label = labelRt.GetComponent<Text>();
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        label.text = "维修";
        label.font = font;
        label.fontSize = 16;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color(1f, 0.97f, 0.88f);
        label.raycastTarget = false;

        // ---- icon state swapping ----
        var swap = go.GetComponent<IconSwap>();
        if (swap == null) swap = go.AddComponent<IconSwap>();
        swap.icon = icon;
        swap.normal = Sprite("Assets/Art/UI/Icons/Regular_01.png");
        swap.pressed = Sprite("Assets/Art/UI/Icons/Pressed_01.png");
        swap.disabled = Sprite("Assets/Art/UI/Icons/Disable_01.png");
        if (icon != null && swap.normal != null) icon.sprite = swap.normal;

        // ---- wiring ----
        bool wiringChanged = hud.repairButton != button;
        hud.repairButton = button;
        bool activeChanged = go.activeSelf;
        if (activeChanged) go.SetActive(false);
        Line("hud.repairButton wired=" + wiringChanged + ", forced inactive=" + activeChanged);

        // ---- pre-save validation (refuse to persist a bad layout) ----
        int repairChildren = CountRepairChildren(panel);
        var demolish = panel.Find("Demolish") as RectTransform;
        if (demolish == null)
            throw new InvalidOperationException("Demolish slot missing; cannot prove the repair slot is free");

        Rect repairRect = PanelRect(rt, panel);
        Rect demolishRect = PanelRect(demolish, panel);
        Line("Repair rect (panel space): x=" + repairRect.xMin + ".." + repairRect.xMax
            + " y=" + repairRect.yMin + ".." + repairRect.yMax
            + " size=" + repairRect.width + "x" + repairRect.height);
        Line("Demolish rect (panel space): x=" + demolishRect.xMin + ".." + demolishRect.xMax
            + " y=" + demolishRect.yMin + ".." + demolishRect.yMax
            + " size=" + demolishRect.width + "x" + demolishRect.height);
        Line("Repair world corners: " + Corners(rt));
        Line("Demolish world corners: " + Corners(demolish));

        if (repairChildren != 1)
            throw new InvalidOperationException("Expected exactly one Repair under SelectionPanel, found " + repairChildren);
        if (repairRect.Overlaps(demolishRect))
            throw new InvalidOperationException("Repair rect overlaps Demolish rect; refusing to save");

        // ---- save only when something actually changed ----
        string md5After;
        if (created || wiringChanged || activeChanged || scene.isDirty)
        {
            if (!EditorSceneManager.MarkSceneDirty(scene) && !scene.isDirty)
                throw new InvalidOperationException("Could not mark SampleScene dirty");
            if (!EditorSceneManager.SaveScene(scene))
                throw new IOException("Could not save " + ScenePath);
            md5After = Md5(fullScenePath);
            Line("Saved " + ScenePath + " (scene dirty before save handled).");
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
        if (repairChildren != 1)
            problems.Add("Repair count under SelectionPanel=" + repairChildren + " (expected 1)");
        if (hud.repairButton == null) problems.Add("hud.repairButton is null");
        else if (hud.repairButton.gameObject != go)
            problems.Add("hud.repairButton points at " + hud.repairButton.gameObject.name);
        if (repairRect.Overlaps(demolishRect))
            problems.Add("Repair and Demolish rects intersect");
        if (!openedByTool && SceneManager.GetActiveScene().path != ScenePath)
            problems.Add("active scene=" + SceneManager.GetActiveScene().path);
        if (problems.Count > 0)
            throw new InvalidOperationException("Post-save assertions failed: " + string.Join("; ", problems.ToArray()));

        Line("Assertions OK: sceneCount=" + EditorSceneManager.sceneCount
            + ", repairChildren=" + repairChildren
            + ", hud.repairButton=" + hud.repairButton.gameObject.name
            + ", overlaps=" + repairRect.Overlaps(demolishRect)
            + ", activeScene=" + SceneManager.GetActiveScene().path);
        Line("Done.");
        Debug.Log("[HUD] 维修按钮已加入选中面板。");
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

    static int CountRepairChildren(RectTransform panel)
    {
        int count = 0;
        for (int i = 0; i < panel.childCount; i++)
            if (panel.GetChild(i).name == "Repair") count++;
        return count;
    }

    /// <summary>Child rect expressed in the panel's local space, from world corners, so
    /// anchors/pivots cannot skew the intersection test.</summary>
    static Rect PanelRect(RectTransform child, RectTransform panel)
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
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    static string Corners(RectTransform rt)
    {
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        var sb = new StringBuilder();
        for (int i = 0; i < corners.Length; i++)
        {
            if (i > 0) sb.Append(" | ");
            sb.Append(corners[i].x).Append(",").Append(corners[i].y);
        }
        return sb.ToString();
    }

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
