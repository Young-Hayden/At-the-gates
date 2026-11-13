using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Adds a 「回主菜单」 button to the game-over panel, next to 「再来一局」,
/// without rebuilding the HUD (additive pattern — BuildHUD wipes the whole canvas,
/// pause menu included, so it must not be re-run for a one-button change).
/// Safe to re-run: an existing button is rewired, not duplicated.</summary>
public static class BuildGameOverExit
{
    [MenuItem("Tools/UI/结算面板加回主菜单按钮")]
    public static void Apply()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.scene", OpenSceneMode.Additive);
        try
        {
            var hud = Object.FindFirstObjectByType<HUD>(FindObjectsInactive.Include);
            if (hud == null) throw new System.InvalidOperationException("HUD missing");
            if (hud.gameOverPanel == null) throw new System.InvalidOperationException("GameOver panel missing");
            var box = hud.gameOverPanel.transform.Find("Box") as RectTransform;
            if (box == null) throw new System.InvalidOperationException("GameOver/Box missing");

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            var buttonSprite = Sprite("Assets/Art/UI/Buttons/Button_Blue_9Slides.png");
            var buttonPressed = Sprite("Assets/Art/UI/Buttons/Button_Blue_9Slides_Pressed.png");
            var buttonHover = Sprite("Assets/Art/UI/Buttons/Button_Hover_9Slides.png");

            // 「再来一局」 shifts left to make room; same size, same style
            if (hud.restartButton != null)
            {
                var rrt = hud.restartButton.GetComponent<RectTransform>();
                rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0f);
                rrt.pivot = new Vector2(0.5f, 0f);
                rrt.anchoredPosition = new Vector2(-112f, 24f);
                rrt.sizeDelta = new Vector2(200f, 70f);
            }

            var rt = box.Find("BackToMenu") as RectTransform;
            Button button;
            if (rt == null)
            {
                var go = new GameObject("BackToMenu", typeof(RectTransform), typeof(Image), typeof(Button));
                rt = go.GetComponent<RectTransform>();
                rt.SetParent(box, false);

                var image = go.GetComponent<Image>();
                image.sprite = buttonSprite;
                if (buttonSprite != null && buttonSprite.border != Vector4.zero)
                    image.type = Image.Type.Sliced;

                button = go.GetComponent<Button>();
                button.targetGraphic = image;
                button.transition = Selectable.Transition.SpriteSwap;
                var state = button.spriteState;
                state.pressedSprite = buttonPressed;
                state.highlightedSprite = buttonHover;
                button.spriteState = state;

                var labelGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
                var labelRt = labelGO.GetComponent<RectTransform>();
                labelRt.SetParent(rt, false);
                labelRt.anchorMin = Vector2.zero;
                labelRt.anchorMax = Vector2.one;
                labelRt.offsetMin = Vector2.zero;
                labelRt.offsetMax = Vector2.zero;
                var label = labelGO.GetComponent<Text>();
                label.text = "回主菜单";
                label.font = font;
                label.fontSize = 26;
                label.alignment = TextAnchor.MiddleCenter;
                label.color = new Color(1f, 0.98f, 0.9f);
                label.raycastTarget = false;
            }
            else
            {
                button = rt.GetComponent<Button>();
            }

            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(112f, 24f);
            rt.sizeDelta = new Vector2(200f, 70f);

            hud.backToMenuButton = button;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[HUD] 结算面板已加「回主菜单」按钮。");
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }

    static Sprite Sprite(string path) =>
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
}
