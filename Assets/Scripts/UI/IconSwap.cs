using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Swaps the little icon inside a button through its three states.
///
/// Unity's Button only sprite-swaps its own graphic, so the background changes on press
/// while the icon on top would sit there unchanged. Tiny Swords ships every icon three
/// times over (Regular / Pressed / Disable) and this is what uses them.
///
/// The disabled state is polled rather than pushed: the HUD flips Button.interactable
/// whenever the player can or cannot afford something, and there is no callback for that.
/// </summary>
[RequireComponent(typeof(Button))]
public class IconSwap : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public Image icon;
    public Sprite normal;
    public Sprite pressed;
    public Sprite disabled;

    Button button;
    bool held;

    void Awake()
    {
        button = GetComponent<Button>();
        if (icon == null)
        {
            foreach (var i in GetComponentsInChildren<Image>())
                if (i.gameObject != gameObject) { icon = i; break; }
        }
    }

    void OnEnable()
    {
        held = false;
        Apply();
    }

    void Update()
    {
        Apply();
    }

    public void OnPointerDown(PointerEventData e) { held = true; Apply(); }
    public void OnPointerUp(PointerEventData e) { held = false; Apply(); }
    public void OnPointerExit(PointerEventData e) { held = false; Apply(); }

    void OnDisable() { held = false; }

    void Apply()
    {
        if (icon == null) return;
        bool usable = button == null || button.interactable;
        Sprite want = !usable && disabled != null ? disabled
                    : held && pressed != null ? pressed
                    : normal;
        if (want != null && icon.sprite != want) icon.sprite = want;
    }
}
