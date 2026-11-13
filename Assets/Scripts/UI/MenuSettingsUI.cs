using UnityEngine;
using UnityEngine.UI;

/// <summary>Owns the main-menu settings panel and all of its widgets. The builder
/// only connects references; values always come from GameSettings.</summary>
public class MenuSettingsUI : MonoBehaviour
{
    public Button openButton;
    public GameObject panel;
    public Button[] resolutionButtons = new Button[3];
    public Button fullscreenButton;
    public Text fullscreenLabel;
    public Button edgePanButton;
    public Text edgePanLabel;
    public Slider panSpeedSlider;
    public Text panSpeedValue;
    public Button resetButton;
    public Button closeButton;
    [Tooltip("Background controls to disable while the modal is open, including keyboard/controller navigation.")]
    public CanvasGroup backgroundGroup;

    public Color selectedTint = new Color(0.72f, 0.92f, 1f, 1f);
    public Color normalTint = Color.white;

    void Awake()
    {
        GameSettings.ApplyDisplay();
        if (openButton != null) openButton.onClick.AddListener(Open);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (resetButton != null) resetButton.onClick.AddListener(ResetToDefaults);
        if (fullscreenButton != null) fullscreenButton.onClick.AddListener(ToggleFullscreen);
        if (edgePanButton != null) edgePanButton.onClick.AddListener(ToggleEdgePan);

        for (int i = 0; i < resolutionButtons.Length; i++)
        {
            int index = i;
            if (resolutionButtons[i] != null)
                resolutionButtons[i].onClick.AddListener(() => SetResolution(index));
        }

        if (panSpeedSlider != null)
        {
            panSpeedSlider.minValue = 6f;
            panSpeedSlider.maxValue = 30f;
            panSpeedSlider.wholeNumbers = true;
            panSpeedSlider.onValueChanged.AddListener(SetPanSpeed);
        }

        Refresh();
        if (panel != null) panel.SetActive(false);
    }

    public void Open()
    {
        Refresh();
        if (panel != null) panel.SetActive(true);
        if (backgroundGroup != null)
        {
            backgroundGroup.interactable = false;
            backgroundGroup.blocksRaycasts = false;
        }
        if (closeButton != null && UnityEngine.EventSystems.EventSystem.current != null)
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(closeButton.gameObject);
    }

    public void Close()
    {
        if (backgroundGroup != null)
        {
            backgroundGroup.interactable = true;
            backgroundGroup.blocksRaycasts = true;
        }
        if (panel != null) panel.SetActive(false);
        if (openButton != null && UnityEngine.EventSystems.EventSystem.current != null)
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(openButton.gameObject);
    }

    public void SetResolution(int index)
    {
        GameSettings.ResolutionIndex = index;
        GameSettings.ApplyDisplay();
        Refresh();
    }

    public void ToggleFullscreen()
    {
        GameSettings.Fullscreen = !GameSettings.Fullscreen;
        GameSettings.ApplyDisplay();
        Refresh();
    }

    public void ToggleEdgePan()
    {
        GameSettings.EdgePan = !GameSettings.EdgePan;
        Refresh();
    }

    public void SetPanSpeed(float value)
    {
        GameSettings.PanSpeed = value;
        Refresh();
    }

    public void ResetToDefaults()
    {
        GameSettings.ResetToDefaults();
        GameSettings.ApplyDisplay();
        Refresh();
    }

    void Refresh()
    {
        for (int i = 0; i < resolutionButtons.Length; i++)
            if (resolutionButtons[i] != null && resolutionButtons[i].image != null)
                resolutionButtons[i].image.color = i == GameSettings.ResolutionIndex ? selectedTint : normalTint;

        if (fullscreenLabel != null) fullscreenLabel.text = GameSettings.Fullscreen ? "全屏：开" : "全屏：关";
        if (edgePanLabel != null) edgePanLabel.text = GameSettings.EdgePan ? "边缘平移：开" : "边缘平移：关";
        if (panSpeedSlider != null) panSpeedSlider.SetValueWithoutNotify(GameSettings.PanSpeed);
        if (panSpeedValue != null) panSpeedValue.text = Mathf.RoundToInt(GameSettings.PanSpeed).ToString();
    }
}
