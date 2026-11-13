using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Main menu wiring. Lives on the menu Canvas; the buttons themselves are built as real
/// scene objects by Assets/Editor/BuildMainMenu.cs.
/// </summary>
public class MainMenu : MonoBehaviour
{
    [Header("按钮")]
    public Button startButton;
    public Button continueButton;
    public Button quitButton;

    [Header("要加载的场景")]
    public string gameScene = "SampleScene";
    public string continueScene = "SampleScene";

    [Header("淡出")]
    public CanvasGroup fadeGroup;
    public float fadeSeconds = 0.45f;

    float fadeT;
    bool leaving;
    string nextScene;

    void Awake()
    {
        // Start() decides from the save file; hide first so a stale scene copy never
        // flashes the empty Continue shell for one frame.
        if (continueButton != null) continueButton.gameObject.SetActive(false);
    }

    void Start()
    {
        if (startButton != null)
        {
            startButton.onClick.RemoveAllListeners();
            startButton.onClick.AddListener(StartGame);
        }
        if (continueButton != null)
        {
            continueButton.onClick.RemoveAllListeners();
            continueButton.onClick.AddListener(ContinueGame);
        }
        if (quitButton != null)
        {
            quitButton.onClick.RemoveAllListeners();
            quitButton.onClick.AddListener(QuitGame);
        }
        if (fadeGroup != null) fadeGroup.alpha = 0f;
        RefreshContinueButton();
    }

    /// <summary>Only offer continue when the slot exists and passes full validation.</summary>
    public void RefreshContinueButton()
    {
        ApplyContinueLayout(SaveGameService.HasValidSave());
    }

    /// <summary>Show/hide Continue and reflow the two surrounding buttons. Public so
    /// the layout state can be tested without depending on a real user save file.</summary>
    public void ApplyContinueLayout(bool hasSave)
    {
        if (continueButton != null)
        {
            continueButton.gameObject.SetActive(hasSave);
            continueButton.interactable = hasSave;
        }

        var startRt = startButton != null ? startButton.GetComponent<RectTransform>() : null;
        var quitRt = quitButton != null ? quitButton.GetComponent<RectTransform>() : null;
        var resumeRt = continueButton != null ? continueButton.GetComponent<RectTransform>() : null;

        if (hasSave)
        {
            if (startRt != null)
            {
                startRt.anchoredPosition = new Vector2(-10f, -38f);
                startRt.sizeDelta = new Vector2(400f, 70f);
            }
            if (resumeRt != null)
            {
                resumeRt.anchoredPosition = new Vector2(-10f, -110f);
                resumeRt.sizeDelta = new Vector2(400f, 70f);
            }
            if (quitRt != null)
            {
                quitRt.anchoredPosition = new Vector2(-10f, -182f);
                quitRt.sizeDelta = new Vector2(400f, 70f);
            }
        }
        else
        {
            if (startRt != null)
            {
                startRt.anchoredPosition = new Vector2(-10f, -54f);
                startRt.sizeDelta = new Vector2(400f, 82f);
            }
            if (quitRt != null)
            {
                quitRt.anchoredPosition = new Vector2(-10f, -180f);
                quitRt.sizeDelta = new Vector2(400f, 82f);
            }
        }
    }

    void Update()
    {
        if (!leaving) return;
        fadeT += Time.unscaledDeltaTime;
        if (fadeGroup != null) fadeGroup.alpha = Mathf.Clamp01(fadeT / Mathf.Max(0.01f, fadeSeconds));
        if (fadeT >= fadeSeconds)
        {
            leaving = false;    // one-shot: the loading screen owns the transition from here
            LoadingScreen.Load(nextScene);   // false = already loading; leaving is reset either way
        }
    }

    public void StartGame()
    {
        if (leaving) return;
        SaveGameService.CancelPendingLoad();
        nextScene = gameScene;
        BeginLeave();
    }

    public void ContinueGame()
    {
        if (leaving) return;
        if (!SaveGameService.TryRead(out var data, out _))
        {
            RefreshContinueButton();
            return;
        }
        SaveGameService.RequestLoad(data);
        nextScene = continueScene;
        BeginLeave();
    }

    void BeginLeave()
    {
        if (fadeGroup == null) { LoadingScreen.Load(nextScene); return; }
        leaving = true;
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
