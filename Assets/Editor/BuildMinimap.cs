using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Adds only the minimap panel to the existing HUD. Never rebuilds the canvas.</summary>
public static class BuildMinimap
{
    [MenuItem("Tools/第二阶段/添加或更新小地图")]
    public static void Apply()
    {
        var canvas = GameObject.Find("HUD Canvas");
        if (canvas == null) throw new System.InvalidOperationException("找不到 HUD Canvas");
        var root = canvas.transform.Find("Root");
        if (root == null) throw new System.InvalidOperationException("找不到 HUD Canvas/Root");

        var panel = root.Find("MinimapPanel") as RectTransform;
        if (panel == null)
        {
            var go = new GameObject("MinimapPanel", typeof(RectTransform), typeof(Image));
            panel = go.GetComponent<RectTransform>();
            panel.SetParent(root, false);
        }
        panel.anchorMin = panel.anchorMax = new Vector2(1f, 1f);
        panel.pivot = new Vector2(1f, 1f);
        panel.anchoredPosition = new Vector2(-16f, -306f);
        // -306, not -218: the selection panel grew to 266 px for the technology strip, and the
        // minimap has to sit below it. BuildTechPanel re-applies the same offset, so every tool
        // that touches the minimap agrees; SelectionPanel_DoesNotOverlapMinimap_AndBothFitCanvas
        // fails loudly if this drifts again.
        panel.sizeDelta = new Vector2(340f, 204f);

        var panelSprite = AssetDatabase.LoadAllAssetsAtPath("Assets/Art/UI/Banners/Carved_9Slides.png")
            .OfType<Sprite>().FirstOrDefault();
        var panelImage = panel.GetComponent<Image>();
        panelImage.sprite = panelSprite;
        panelImage.type = panelSprite != null && panelSprite.border != Vector4.zero
            ? Image.Type.Sliced : Image.Type.Simple;
        panelImage.color = Color.white;
        panelImage.raycastTarget = true;

        var view = panel.Find("View") as RectTransform;
        if (view == null)
        {
            var go = new GameObject("View", typeof(RectTransform), typeof(RawImage), typeof(Minimap));
            view = go.GetComponent<RectTransform>();
            view.SetParent(panel, false);
        }
        view.anchorMin = view.anchorMax = new Vector2(0.5f, 0.5f);
        view.pivot = new Vector2(0.5f, 0.5f);
        view.anchoredPosition = Vector2.zero;
        view.sizeDelta = new Vector2(320f, 180f);
        var raw = view.GetComponent<RawImage>();
        raw.color = Color.white;
        raw.raycastTarget = true;

        var viewport = view.Find("Viewport") as RectTransform;
        if (viewport == null)
        {
            var go = new GameObject("Viewport", typeof(RectTransform));
            viewport = go.GetComponent<RectTransform>();
            viewport.SetParent(view, false);
        }
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = viewport.offsetMax = Vector2.zero;

        var tint = new Color(1f, 0.9f, 0.25f, 0.95f);
        MakeStrip(viewport, "Top", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 2), tint);
        MakeStrip(viewport, "Bottom", Vector2.zero, new Vector2(1, 0), new Vector2(0, 2), tint);
        MakeStrip(viewport, "Left", Vector2.zero, new Vector2(0, 1), new Vector2(2, 0), tint);
        MakeStrip(viewport, "Right", new Vector2(1, 0), Vector2.one, new Vector2(2, 0), tint);

        var minimap = view.GetComponent<Minimap>();
        minimap.mainCamera = Camera.main;
        minimap.rtsCamera = Camera.main != null ? Camera.main.GetComponent<RTSCamera>() : null;
        minimap.viewport = viewport;
        minimap.textureWidth = 900;
        minimap.textureHeight = 500;

        // GameOver must remain the topmost HUD element, including on repeated runs.
        var gameOver = root.Find("GameOver");
        if (gameOver != null) gameOver.SetAsLastSibling();

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[小地图] 已添加/更新 MinimapPanel");
    }

    static void MakeStrip(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                          Vector2 size, Color colour)
    {
        var rt = parent.Find(name) as RectTransform;
        if (rt == null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
        }
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
        var image = rt.GetComponent<Image>();
        image.color = colour;
        image.raycastTarget = false;
    }
}
