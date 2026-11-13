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
/// Presentation Task P3: brings the main menu, the pause menu and the loading screen up to the
/// HUD's standard without restyling them.
///
/// What was actually wrong (judged from PlayMode screenshots, `Screenshots/p3_*_before.png`):
///  * the pause title and the loading title are cream text on cream parchment — the single
///    biggest readability problem in the whole UI; the main menu solved it long ago with a dark
///    outline, so the same recipe is applied here;
///  * the pause "尚无存档" line and the loading "正在集结部队…" line were cream on parchment too,
///    and are darkened to the menu's brown instead;
///  * every main-menu button label sits 5 px above its button's centre (the rect is 80 px tall
///    inside a 70 px button, anchored 5 px high).
///
/// Everything is incremental and idempotent: objects are found by name, no object is created
/// except the Outline components, and a run that changes nothing skips the save.
/// </summary>
public static class PolishMenus
{
    const string Dir = ".superpowers/sdd/presentation";
    const string MainMenuPath = "Assets/Scenes/MainMenu.scene";
    const string GameScenePath = "Assets/Scenes/SampleScene.scene";
    const string LoadingPrefabPath = "Assets/Resources/LoadingScreen.prefab";

    const string MainMenuBackup = Dir + "/p3-mainmenu-before.scene";
    const string PauseBackup = Dir + "/p3-sample-before-pausepolish.scene";
    const string LoadingBackup = Dir + "/p3-loading-before.prefab";

    const string MainMenuLog = Dir + "/p3-mainmenu-tool.log";
    const string PauseLog = Dir + "/p3-pause-tool.log";
    const string LoadingLog = Dir + "/p3-loading-tool.log";

    static readonly Color OutlineColor = new Color(0.20f, 0.12f, 0.05f, 0.95f);
    static readonly Color ParchmentText = new Color(0.38f, 0.26f, 0.14f, 1f);
    static readonly float LabelTolerance = 0.5f;

    static readonly StringBuilder Log = new StringBuilder();

    // ------------------------------------------------------------------ entries

    [MenuItem("Tools/UI/精修菜单/主菜单")]
    public static void PolishMainMenu() => ApplyMainMenu(MainMenuPath, MainMenuBackup, MainMenuLog);

    [MenuItem("Tools/UI/精修菜单/暂停菜单")]
    public static void PolishPauseMenu() => ApplyPauseMenu(GameScenePath, PauseBackup, PauseLog);

    [MenuItem("Tools/UI/精修菜单/加载界面")]
    public static void PolishLoadingScreen() => ApplyLoading(LoadingPrefabPath, LoadingBackup, LoadingLog);

    // ------------------------------------------------------------- main menu

    /// <summary>Main menu: centre every button label, keep it inside its button, and leave the
    /// rest of the board (title, rules, continue-button logic, settings window) untouched.</summary>
    public static void ApplyMainMenu(string scenePath, string backupPath, string logPath)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[PolishMenus] 请先退出 PlayMode。");
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
            }
            if (SceneManager.GetActiveScene() != scene)
            {
                previousActive = SceneManager.GetActiveScene();
                SceneManager.SetActiveScene(scene);
                switchedActive = true;
            }

            string md5Before = Backup(scenePath, backupPath);
            bool changed = CentreLabels(FindCanvas(scene), "main menu");

            if (!changed)
            {
                Line("No changes needed — scene not saved.");
            }
            else
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Line("Scene saved. MD5 after: " + Md5(ProjectPath(scenePath)));
            }
            if (md5Before != null) Line("MD5 before: " + md5Before);
        }
        catch (Exception e)
        {
            Line("ERROR: " + e);
            Debug.LogError("[PolishMenus] 主菜单精修失败：" + e.Message);
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
            if (!string.IsNullOrEmpty(logPath)) WriteLog(logPath);
        }
    }

    // ------------------------------------------------------------ pause menu

    /// <summary>Pause menu: a dark outline behind the two cream-on-parchment titles and a darker
    /// colour for the "no save yet" line. SampleScene is written once.</summary>
    public static void ApplyPauseMenu(string scenePath, string backupPath, string logPath)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[PolishMenus] 请先退出 PlayMode。");
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
            }
            if (SceneManager.GetActiveScene() != scene)
            {
                previousActive = SceneManager.GetActiveScene();
                SceneManager.SetActiveScene(scene);
                switchedActive = true;
            }

            string md5Before = Backup(scenePath, backupPath);
            var canvas = FindCanvas(scene);
            var pause = canvas.Find("Pause Menu");
            if (pause == null) throw new InvalidOperationException("HUD Canvas/Pause Menu missing.");

            bool changed = false;
            changed |= AddOutline(FindText(pause, "Panel/Box/Title"), 3f, ref changed, "pause title");
            changed |= Recolour(FindText(pause, "Panel/Box/SaveTime"), ParchmentText, ref changed, "pause save-time line");
            changed |= AddOutline(FindText(pause, "Panel/QuitConfirm/Box/Title"), 3f, ref changed, "quit-confirm title");
            changed |= Recolour(FindText(pause, "Panel/QuitConfirm/Box/Hint"), ParchmentText, ref changed, "quit-confirm hint");
            changed |= CentreLabels(pause, "pause menu");

            if (!changed)
            {
                Line("No changes needed — scene not saved.");
            }
            else
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Line("Scene saved. MD5 after: " + Md5(ProjectPath(scenePath)));
            }
            if (md5Before != null) Line("MD5 before: " + md5Before);
        }
        catch (Exception e)
        {
            Line("ERROR: " + e);
            Debug.LogError("[PolishMenus] 暂停菜单精修失败：" + e.Message);
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
            if (!string.IsNullOrEmpty(logPath)) WriteLog(logPath);
        }
    }

    // -------------------------------------------------------- loading screen

    /// <summary>Loading screen prefab: dark outline behind the cream title and a darker colour
    /// for the tip line. The animated units and the progress bar are untouched.</summary>
    public static void ApplyLoading(string prefabPath, string backupPath, string logPath)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[PolishMenus] 请先退出 PlayMode。");
            return;
        }

        Log.Length = 0;
        try
        {
            string md5Before = Backup(prefabPath, backupPath);
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            bool changed = false;
            try
            {
                changed |= AddOutline(FindText(root.transform, "Box/Title"), 2f, ref changed, "loading title");
                changed |= Recolour(FindText(root.transform, "Box/Tip"), ParchmentText, ref changed, "loading tip");
                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    Line("Prefab saved. MD5 after: " + Md5(ProjectPath(prefabPath)));
                }
                else
                {
                    Line("No changes needed — prefab not saved.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            if (md5Before != null) Line("MD5 before: " + md5Before);
        }
        catch (Exception e)
        {
            Line("ERROR: " + e);
            Debug.LogError("[PolishMenus] 加载界面精修失败：" + e.Message);
            throw;
        }
        finally
        {
            if (!string.IsNullOrEmpty(logPath)) WriteLog(logPath);
        }
    }

    // ---------------------------------------------------------------- helpers

    static Transform FindCanvas(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var canvas = root.GetComponent<Canvas>();
            if (canvas != null) return root.transform;
            var nested = root.GetComponentInChildren<Canvas>(true);
            if (nested != null) return nested.transform;
        }
        throw new InvalidOperationException("No Canvas in " + scene.path);
    }

    static Text FindText(Transform root, string path)
    {
        var t = root.Find(path);
        if (t == null) throw new InvalidOperationException("Missing " + path + " under " + root.name);
        var text = t.GetComponent<Text>();
        if (text == null) throw new InvalidOperationException(path + " has no Text component.");
        return text;
    }

    /// <summary>Adds (or fixes) the dark outline the main-menu title already uses.</summary>
    static bool AddOutline(Text text, float distance, ref bool changed, string label)
    {
        var outline = text.GetComponent<Outline>();
        if (outline == null)
        {
            outline = text.gameObject.AddComponent<Outline>();
            changed = true;
            Line("  outlined " + label);
        }
        if (outline.effectColor != OutlineColor) { outline.effectColor = OutlineColor; changed = true; }
        var want = new Vector2(distance, -distance);
        if (outline.effectDistance != want) { outline.effectDistance = want; changed = true; }
        if (outline.useGraphicAlpha) { outline.useGraphicAlpha = false; changed = true; }
        return changed;
    }

    static bool Recolour(Text text, Color colour, ref bool changed, string label)
    {
        if (text.color == colour) return changed;
        text.color = colour;
        changed = true;
        Line("  recoloured " + label + " to " + colour.ToString("F2"));
        return changed;
    }

    /// <summary>Every button label must be centred on both axes and fit inside its button.</summary>
    static bool CentreLabels(Transform root, string what)
    {
        bool changed = false;
        int checkedLabels = 0;
        var buttons = root.GetComponentsInChildren<Button>(true);
        foreach (var button in buttons)
        {
            var label = button.transform.Find("Label") as RectTransform;
            if (label == null) continue;
            var text = label.GetComponent<Text>();
            if (text == null) continue;
            checkedLabels++;

            bool moved = label.anchorMin != Vector2.zero || label.anchorMax != Vector2.one
                      || label.pivot != new Vector2(0.5f, 0.5f)
                      || label.offsetMin != Vector2.zero || label.offsetMax != Vector2.zero;
            if (moved)
            {
                label.anchorMin = Vector2.zero;
                label.anchorMax = Vector2.one;
                label.pivot = new Vector2(0.5f, 0.5f);
                label.offsetMin = Vector2.zero;
                label.offsetMax = Vector2.zero;
                changed = true;
                Line("  centred label of " + button.name);
            }
            if (text.alignment != TextAnchor.MiddleCenter)
            {
                text.alignment = TextAnchor.MiddleCenter;
                changed = true;
            }

            int size = text.fontSize;
            while (text.preferredWidth > label.rect.width + LabelTolerance
                   || text.preferredHeight > label.rect.height + LabelTolerance)
            {
                if (size <= 10)
                {
                    throw new InvalidOperationException(
                        "Label \"" + text.text + "\" on " + button.name + " does not fit: "
                        + text.preferredWidth.ToString("F1") + "x" + text.preferredHeight.ToString("F1")
                        + " in " + label.rect.width.ToString("F1") + "x" + label.rect.height.ToString("F1"));
                }
                size--;
                text.fontSize = size;
                changed = true;
            }
        }
        Line(what + ": " + checkedLabels + " button labels checked, " + buttons.Length + " buttons.");
        return changed;
    }

    static string Backup(string assetPath, string backupPath)
    {
        if (string.IsNullOrEmpty(backupPath)) return null;
        string full = ProjectPath(assetPath);
        string target = ProjectPath(backupPath);
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        if (!File.Exists(target))
        {
            File.Copy(full, target, false);
            Line("Backup copied: " + target);
        }
        else
        {
            Line("Backup already exists (pre-change state); left untouched: " + target);
        }
        return Md5(full);
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
            var sb = new StringBuilder();
            foreach (var b in md5.ComputeHash(stream)) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    static void Line(string message) { Log.AppendLine(message); }

    static void WriteLog(string relativePath)
    {
        try
        {
            string full = ProjectPath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, Log.ToString());
        }
        catch (Exception e)
        {
            Debug.LogWarning("[PolishMenus] Could not write the tool log: " + e.Message);
        }
    }
}
