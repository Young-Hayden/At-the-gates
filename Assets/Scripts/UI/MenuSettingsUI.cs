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
        ConfigureFixedDisplay();
        GameSettings.ApplyDisplay();
        if (openButton != null) openButton.onClick.AddListener(Open);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (resetButton != null) resetButton.onClick.AddListener(ResetToDefaults);
        if (edgePanButton != null) edgePanButton.onClick.AddListener(ToggleEdgePan);

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

    void Update()
    {
        if (panel != null && panel.activeSelf && Input.GetKeyDown(KeyCode.Escape))
            Close();
    }

    /// <summary>The display is fixed at 2560×1440 fullscreen window: keep the first
    /// resolution button as a non-interactive readout, hide the rest, and lock the
    /// fullscreen button.</summary>
    void ConfigureFixedDisplay()
    {
        for (int i = 0; i < resolutionButtons.Length; i++)
        {
            var button = resolutionButtons[i];
            if (button == null) continue;
            if (i == 0)
            {
                button.gameObject.SetActive(true);
                button.interactable = false;
                var label = button.GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.text = GameSettings.FixedResolutionText;
                    label.color = selectedTint;
                }
            }
            else button.gameObject.SetActive(false);
        }

        if (fullscreenButton != null) fullscreenButton.interactable = false;
        if (fullscreenLabel != null)
            fullscreenLabel.text = "全屏：固定 " + GameSettings.FixedResolutionText;
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
        if (edgePanLabel != null) edgePanLabel.text = GameSettings.EdgePan ? "边缘平移：开" : "边缘平移：关";
        if (panSpeedSlider != null) panSpeedSlider.SetValueWithoutNotify(GameSettings.PanSpeed);
        if (panSpeedValue != null) panSpeedValue.text = Mathf.RoundToInt(GameSettings.PanSpeed).ToString();
    }
}
