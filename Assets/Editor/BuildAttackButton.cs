using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Adds the attack-command button to the selection panel without rebuilding
/// the HUD (same additive pattern as EnhanceDemolitionUI — BuildHUD wipes the whole
/// canvas, pause menu included, so it must not be re-run for a one-button change).
/// Safe to re-run: an existing button is rewired, not duplicated.</summary>
public static class BuildAttackButton
{
    [MenuItem("Tools/UI/添加攻击按钮")]
    public static void Apply()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.scene", OpenSceneMode.Additive);
        try
        {
            var hud = Object.FindFirstObjectByType<HUD>(FindObjectsInactive.Include);
            if (hud == null) throw new System.InvalidOperationException("HUD missing");
            if (hud.selectionPanel == null) throw new System.InvalidOperationException("selectionPanel missing");
            var panel = hud.selectionPanel.GetComponent<RectTransform>();

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            var rt = panel.Find("Attack") as RectTransform;
            Button button;
            if (rt == null)
            {
                var go = new GameObject("Attack", typeof(RectTransform), typeof(Image), typeof(Button));
                rt = go.GetComponent<RectTransform>();
                rt.SetParent(panel, false);

                var image = go.GetComponent<Image>();
                var sprite = Sprite("Assets/Art/UI/Buttons/Button_Blue_9Slides.png");
                image.sprite = sprite;
                image.type = sprite != null && sprite.border != Vector4.zero
                    ? Image.Type.Sliced : Image.Type.Simple;

                button = go.GetComponent<Button>();
                button.targetGraphic = image;
                button.transition = Selectable.Transition.SpriteSwap;
                var state = button.spriteState;
                state.pressedSprite = Sprite("Assets/Art/UI/Buttons/Button_Blue_9Slides_Pressed.png");
                state.highlightedSprite = Sprite("Assets/Art/UI/Buttons/Button_Hover_9Slides.png");
                button.spriteState = state;

                // crossed swords on the left, label fills the rest
                var iconGO = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                var iconRt = iconGO.GetComponent<RectTransform>();
                iconRt.SetParent(rt, false);
                iconRt.anchorMin = new Vector2(0f, 0.5f);
                iconRt.anchorMax = new Vector2(0f, 0.5f);
                iconRt.pivot = new Vector2(0f, 0.5f);
                iconRt.anchoredPosition = new Vector2(6f, 0f);
                iconRt.sizeDelta = new Vector2(24f, 24f);
                var icon = iconGO.GetComponent<Image>();
                icon.preserveAspect = true;
                icon.raycastTarget = false;

                var labelGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
                var labelRt = labelGO.GetComponent<RectTransform>();
                labelRt.SetParent(rt, false);
                labelRt.anchorMin = Vector2.zero;
                labelRt.anchorMax = Vector2.one;
                labelRt.offsetMin = new Vector2(30f, 0f);
                labelRt.offsetMax = new Vector2(-6f, 0f);
                var label = labelGO.GetComponent<Text>();
                label.text = "攻击";
                label.font = font;
                label.fontSize = 16;
                label.alignment = TextAnchor.MiddleCenter;
                label.color = new Color(1f, 0.97f, 0.88f);
                label.raycastTarget = false;

                var swap = go.AddComponent<IconSwap>();
                swap.icon = icon;
            }
            else
            {
                button = rt.GetComponent<Button>();
            }

            // right of the 拆除 slot (104..208), same bottom row
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(214f, 10f);
            rt.sizeDelta = new Vector2(96f, 34f);

            var swap2 = rt.GetComponent<IconSwap>();
            if (swap2 != null)
            {
                swap2.normal = Sprite("Assets/Art/UI/Icons/Regular_01.png");
                swap2.pressed = Sprite("Assets/Art/UI/Icons/Pressed_01.png");
                swap2.disabled = Sprite("Assets/Art/UI/Icons/Disable_01.png");
                if (swap2.icon != null) swap2.icon.sprite = swap2.normal;
            }

            hud.attackButton = button;
            rt.gameObject.SetActive(false);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[HUD] 攻击按钮已加入选中面板。");
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }

    static Sprite Sprite(string path) =>
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
}
