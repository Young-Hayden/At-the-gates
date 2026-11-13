using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Adds the quit-confirmation dialog (save & quit / quit / cancel) to the
/// existing pause menu in SampleScene, and wires it to the PauseMenu fields.
/// Safe to re-run: an existing dialog is rebuilt in place.</summary>
public static class BuildPauseConfirm
{
    const string DialogName = "QuitConfirm";

    [MenuItem("Tools/UI/生成退出确认弹窗")]
    public static void Apply()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.scene", OpenSceneMode.Additive);
        try
        {
            var menu = Object.FindFirstObjectByType<PauseMenu>(FindObjectsInactive.Include);
            if (menu == null) throw new System.InvalidOperationException("PauseMenu missing");
            if (menu.panel == null) throw new System.InvalidOperationException("PauseMenu.panel missing");

            var old = menu.panel.transform.Find(DialogName);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var panelSprite = Sprite("Assets/Art/UI/Banners/Carved_9Slides.png");
            var buttonSprite = Sprite("Assets/Art/UI/Buttons/Button_Blue_9Slides.png");
            var buttonPressed = Sprite("Assets/Art/UI/Buttons/Button_Blue_9Slides_Pressed.png");
            var buttonHover = Sprite("Assets/Art/UI/Buttons/Button_Hover_9Slides.png");
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            // full-screen dim above the pause box, so the dialog is clearly modal
            var overlay = Panel(menu.panel.transform, DialogName, null, new Color(0f, 0f, 0f, 0.55f));
            Stretch(overlay);
            overlay.GetComponent<Image>().raycastTarget = true;
            overlay.SetAsLastSibling();

            var box = Panel(overlay, "Box", panelSprite, Color.white);
            Place(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 400f));

            var title = Label(box, "Title", "退出到主菜单？", 32, TextAnchor.MiddleCenter,
                              new Color(1f, 0.96f, 0.86f), font);
            Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                  new Vector2(0f, -40f), new Vector2(460f, 48f));

            var hint = Label(box, "Hint", "未保存的进度将丢失", 20, TextAnchor.MiddleCenter,
                             new Color(0.92f, 0.9f, 0.8f), font);
            Place(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                  new Vector2(0f, -92f), new Vector2(460f, 30f));

            menu.saveQuitButton = DialogButton(box, "SaveQuit", "保存并退出", -150f,
                                               buttonSprite, buttonPressed, buttonHover, font);
            menu.quitNowButton = DialogButton(box, "QuitNow", "直接退出", -216f,
                                              buttonSprite, buttonPressed, buttonHover, font);
            menu.cancelQuitButton = DialogButton(box, "Cancel", "取消", -282f,
                                                 buttonSprite, buttonPressed, buttonHover, font);

            menu.confirmStatusText = Label(box, "Status", "", 18, TextAnchor.MiddleCenter,
                                           menu.errorColor, font);
            Place(menu.confirmStatusText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                  new Vector2(0f, 22f), new Vector2(480f, 30f));

            menu.confirmPanel = overlay.gameObject;
            overlay.gameObject.SetActive(false);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[PauseMenu] 退出确认弹窗已生成并接线。");
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }

    static Button DialogButton(Transform parent, string name, string text, float y,
                               Sprite sprite, Sprite pressed, Sprite hover, Font font)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        Place(rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(320f, 56f));

        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        if (sprite != null && sprite.border != Vector4.zero) image.type = Image.Type.Sliced;
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        var state = button.spriteState;
        state.pressedSprite = pressed;
        state.highlightedSprite = hover;
        button.spriteState = state;

        var label = Label(rt, "Label", text, 24, TextAnchor.MiddleCenter, new Color(1f, 0.98f, 0.9f), font);
        Stretch(label.rectTransform);
        label.raycastTarget = false;
        return button;
    }

    static RectTransform Panel(Transform parent, string name, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        if (sprite != null && sprite.border != Vector4.zero) image.type = Image.Type.Sliced;
        return rt;
    }

    static Text Label(Transform parent, string name, string text, int size,
                      TextAnchor anchor, Color color, Font font)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        var label = go.GetComponent<Text>();
        label.text = text;
        label.font = font;
        label.fontSize = size;
        label.alignment = anchor;
        label.color = color;
        label.raycastTarget = false;
        return label;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static Sprite Sprite(string path) =>
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
}
