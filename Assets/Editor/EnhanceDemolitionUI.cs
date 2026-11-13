using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Adds the UI demolition entry without rebuilding the HUD.</summary>
public static class EnhanceDemolitionUI
{
    [MenuItem("Tools/UI/添加建筑拆除按钮")]
    public static void Apply()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.scene", OpenSceneMode.Additive);
        try
        {
            var hud = Object.FindFirstObjectByType<HUD>(FindObjectsInactive.Include);
            if (hud == null) throw new System.InvalidOperationException("HUD missing");
            var panel = hud.selectionPanel.GetComponent<RectTransform>();
            panel.sizeDelta = new Vector2(320, 190);
            var minimap = GameObject.Find("MinimapPanel").GetComponent<RectTransform>();
            // -306, not -218: the selection panel is 266 px tall once the technology strip is
            // in, and the minimap sits below it (BuildTechPanel and BuildMinimap use the same
            // value; the layout guard test catches any drift).
            minimap.anchoredPosition = new Vector2(-16, -306);

            var rt = panel.Find("Demolish") as RectTransform;
            if (rt == null)
            {
                var go = new GameObject("Demolish", typeof(RectTransform), typeof(Image), typeof(Button));
                rt = go.GetComponent<RectTransform>();
                rt.SetParent(panel, false);
                var image = go.GetComponent<Image>();
                var sprite = Sprite("Assets/Art/UI/Buttons/Button_Blue_9Slides.png");
                image.sprite = sprite;
                image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
                var button = go.GetComponent<Button>();
                button.targetGraphic = image;
                var state = button.spriteState;
                state.pressedSprite = Sprite("Assets/Art/UI/Buttons/Button_Blue_9Slides_Pressed.png");
                state.highlightedSprite = Sprite("Assets/Art/UI/Buttons/Button_Hover_9Slides.png");
                button.spriteState = state;

                var labelGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
                var label = labelGO.GetComponent<Text>();
                labelGO.GetComponent<RectTransform>().SetParent(rt, false);
                label.text = "拆除";
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (label.font == null) label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                label.fontSize = 16;
                label.alignment = TextAnchor.MiddleCenter;
                label.color = new Color(1f, 0.97f, 0.88f);
                label.raycastTarget = false;
                Stretch(label.rectTransform);
            }
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(104, 10);
            rt.sizeDelta = new Vector2(104, 34);
            var existingLabel = rt.GetComponentInChildren<Text>();
            if (existingLabel != null)
            {
                existingLabel.alignment = TextAnchor.MiddleCenter;
                Stretch(existingLabel.rectTransform);
            }
            hud.demolishButton = rt.GetComponent<Button>();
            rt.gameObject.SetActive(false);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
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
