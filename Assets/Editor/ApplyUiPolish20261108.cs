using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>One-off, additive-only edits for the approved 2026-11-08 UI polish.
/// Never rebuilds the HUD or creates scene objects. Safe to re-run.</summary>
public static class ApplyUiPolish20261108
{
    const string MainMenuPath = "Assets/Scenes/MainMenu.scene";
    const string SamplePath = "Assets/Scenes/SampleScene.scene";

    [MenuItem("Tools/UI/应用 2026-11-08 观感修补")]
    public static void Apply()
    {
        ApplyMainMenu();
        ApplySampleScene();
    }

    public static void ApplyMainMenu()
    {
        var scene = EditorSceneManager.OpenScene(MainMenuPath, OpenSceneMode.Additive);
        try
        {
            var texts = Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(t => t.gameObject.scene == scene).ToArray();
            bool changed = false;
            foreach (var text in texts)
            {
                if (!text.text.Contains("1~6 建造")) continue;
                text.text = text.text.Replace("1~6 建造", "点击建造栏放置");
                EditorUtility.SetDirty(text);
                changed = true;
            }
            if (!changed && !texts.Any(t => t.text.Contains("点击建造栏放置")))
                throw new System.InvalidOperationException("MainMenu rules contain neither the old nor the new build instruction.");
            if (changed)
            {
                if (!EditorSceneManager.SaveScene(scene))
                    throw new System.IO.IOException("Could not save " + MainMenuPath);
                Debug.Log("[UiPolish] MainMenu build instruction updated.");
            }
            else Debug.Log("[UiPolish] MainMenu already up to date.");
        }
        finally
        {
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    public static void ApplySampleScene()
    {
        var scene = EditorSceneManager.OpenScene(SamplePath, OpenSceneMode.Additive);
        try
        {
            var hud = Object.FindObjectsByType<HUD>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(h => h.gameObject.scene == scene);
            if (hud == null) throw new System.InvalidOperationException("SampleScene has no HUD.");
            bool changed = false;

            int count = Mathf.Min(6, hud.unitButtons.Length);
            for (int i = 0; i < count; i++)
            {
                if (hud.unitButtons[i] != null)
                    changed |= SetRect(hud.unitButtons[i].GetComponent<RectTransform>(), 41f, 56f);
                if (i < hud.unitNames.Length && hud.unitNames[i] != null)
                    changed |= SetRect(hud.unitNames[i].rectTransform, 26f, 15f);
                if (i < hud.unitCosts.Length && hud.unitCosts[i] != null)
                    changed |= SetRect(hud.unitCosts[i].rectTransform, 10f, 14f);
            }

            if (hud.attackButton != null)
            {
                var label = hud.attackButton.transform.Find("Label") as RectTransform;
                if (label == null) throw new System.InvalidOperationException("Attack button has no Label.");
                if (label.offsetMax != new Vector2(-6f, 0f))
                {
                    label.offsetMax = new Vector2(-6f, 0f);
                    EditorUtility.SetDirty(label);
                    changed = true;
                }
            }

            if (changed)
            {
                if (!EditorSceneManager.SaveScene(scene))
                    throw new System.IO.IOException("Could not save " + SamplePath);
                Debug.Log("[UiPolish] SampleScene unit-panel and attack-label layout updated.");
            }
            else Debug.Log("[UiPolish] SampleScene already up to date.");
        }
        finally
        {
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    static bool SetRect(RectTransform rect, float y, float height)
    {
        if (rect == null) return false;
        if (Mathf.Approximately(rect.anchoredPosition.y, y) &&
            Mathf.Approximately(rect.sizeDelta.y, height)) return false;
        rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, y);
        rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
        EditorUtility.SetDirty(rect);
        return true;
    }
}
