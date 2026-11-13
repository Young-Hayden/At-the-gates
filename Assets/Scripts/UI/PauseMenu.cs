using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// In-game ESC menu. Owns pause time only while it is open; placement and the
/// game-over screen keep ESC for themselves.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    [Header("面板")]
    public GameObject panel;
    public Text statusText;
    public Text saveTimeText;

    [Header("按钮")]
    public Button resumeButton;
    public Button saveButton;
    public Button loadButton;
    public Button restartButton;
    public Button quitButton;

    [Header("退出确认弹窗")]
    public GameObject confirmPanel;
    public Button saveQuitButton;
    public Button quitNowButton;
    public Button cancelQuitButton;
    public Text confirmStatusText;
    public string mainMenuScene = "MainMenu";

    [Header("提示颜色")]
    public Color successColor = new Color(0.65f, 1f, 0.65f);
    public Color errorColor = new Color(1f, 0.55f, 0.5f);

    bool loading;

    public bool IsOpen => panel != null && panel.activeSelf;

    static PauseMenu instance;

    /// <summary>True while any pause menu is open. World input (selection, placement,
    /// HUD buttons) checks this so a frozen game cannot still take orders or spend
    /// resources — Update methods ignore timeScale, so the freeze alone never blocked them.</summary>
    public static bool IsAnyOpen => instance != null && instance.IsOpen;

    void Awake()
    {
        instance = this;
        if (panel != null) panel.SetActive(false);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void Start()
    {
        if (resumeButton != null) resumeButton.onClick.AddListener(() => SetOpen(false));
        if (saveButton != null) saveButton.onClick.AddListener(SaveGame);
        if (loadButton != null) loadButton.onClick.AddListener(LoadGame);
        if (restartButton != null) restartButton.onClick.AddListener(RestartGame);
        if (quitButton != null) quitButton.onClick.AddListener(QuitGame);
        if (saveQuitButton != null) saveQuitButton.onClick.AddListener(SaveAndQuit);
        if (quitNowButton != null) quitNowButton.onClick.AddListener(QuitToMain);
        if (cancelQuitButton != null) cancelQuitButton.onClick.AddListener(() => SetConfirm(false));
        if (confirmPanel != null) confirmPanel.SetActive(false);
        if (panel != null) panel.SetActive(false);
        if (GameState.Instance == null || !GameState.Instance.IsOver) Time.timeScale = 1f;
        RefreshSaveInfo();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) HandleEscape();
    }

    /// <summary>Returns true when ESC belonged to this menu or another modal owner.</summary>
    public bool HandleEscape()
    {
        // An open menu owns ESC ahead of everything else. World input is gated on
        // IsAnyOpen now, so if the armed-attack / placement checks ran first, ESC with
        // the menu open would be "owned" by a system that is refusing input — and the
        // key would do nothing at all.
        if (confirmPanel != null && confirmPanel.activeSelf)
        {
            SetConfirm(false);
            return true;
        }
        if (IsOpen)
        {
            SetOpen(false);
            return true;
        }

        if (BuildingPlacer.Instance != null &&
            (BuildingPlacer.Instance.IsPlacing ||
             BuildingPlacer.Instance.LastCancelFrame == Time.frameCount))
            return true;      // BuildingPlacer owns this ESC, even if its Update ran first

        // An armed attack order owns ESC too — and the frame it disarmed on, so one
        // keypress never both cancels the order and pops this menu open.
        var sm = Object.FindFirstObjectByType<SelectionManager>();
        if (sm != null && (sm.AttackArmed || sm.LastDisarmFrame == Time.frameCount))
            return true;

        if (GameState.Instance != null && GameState.Instance.IsOver)
            return true;      // result screen stays on top
        Toggle();
        return true;
    }

    public void Toggle() { SetOpen(!IsOpen); }

    public void SetOpen(bool open)
    {
        if (panel == null) return;
        if (open && GameState.Instance != null && GameState.Instance.IsOver) return;

        panel.SetActive(open);
        Time.timeScale = open ? 0f :
            (GameState.Instance != null && GameState.Instance.IsOver ? 0f : 1f);
        if (open)
        {
            SetConfirm(false);
            RefreshSaveInfo();
            SetStatus(string.Empty, successColor);
        }
    }

    void RefreshSaveInfo()
    {
        bool hasSave = SaveGameService.HasSave;
        if (loadButton != null) loadButton.interactable = hasSave && SaveGameService.HasValidSave();
        if (saveButton != null)
            saveButton.interactable = GameState.Instance == null || !GameState.Instance.IsOver;

        if (saveTimeText == null) return;
        if (!hasSave)
        {
            saveTimeText.text = "尚无存档";
            return;
        }
        try
        {
            saveTimeText.text = "上次保存：" +
                File.GetLastWriteTime(SaveGameService.SavePath).ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch
        {
            saveTimeText.text = "存在存档";
        }
    }

    public void SaveGame()
    {
        if (loading) return;
        bool ok = SaveGameService.Save(out string error);
        if (ok)
        {
            SetStatus("进度已保存。", successColor);
            RefreshSaveInfo();
        }
        else
        {
            SetStatus("保存失败：" + error, errorColor);
        }
    }

    public void LoadGame()
    {
        if (loading) return;
        if (!SaveGameService.TryRead(out var data, out string error))
        {
            SetStatus("读取失败：" + error, errorColor);
            return;
        }
        loading = true;
        Time.timeScale = 1f;
        SaveGameService.RequestLoad(data);
        loading = LoadingScreen.Load(SceneManager.GetActiveScene().name);
        if (!loading) SaveGameService.CancelPendingLoad();  // refused (busy) — don't let the request ambush a later scene
    }

    public void RestartGame()
    {
        if (loading) return;
        loading = true;
        Time.timeScale = 1f;
        loading = LoadingScreen.Load(SceneManager.GetActiveScene().name);
    }

    /// <summary>The quit button no longer kills the app — it asks whether the player
    /// wants to save first, then returns to the main menu.</summary>
    public void QuitGame()
    {
        if (loading) return;
        if (confirmPanel == null) { QuitToMain(); return; }
        SetConfirm(true);
    }

    void SetConfirm(bool on)
    {
        if (confirmPanel == null) return;
        confirmPanel.SetActive(on);
        if (!on) return;
        // A finished match cannot be saved, so only offer the two honest choices.
        bool canSave = GameState.Instance == null || !GameState.Instance.IsOver;
        if (saveQuitButton != null) saveQuitButton.interactable = canSave;
        if (confirmStatusText != null) confirmStatusText.text = string.Empty;
    }

    public void SaveAndQuit()
    {
        if (loading) return;
        if (SaveGameService.Save(out string error))
        {
            QuitToMain();
            return;
        }
        if (confirmStatusText != null)
        {
            confirmStatusText.text = "保存失败：" + error;
            confirmStatusText.color = errorColor;
        }
    }

    public void QuitToMain()
    {
        if (loading) return;
        loading = true;
        Time.timeScale = 1f;
        // A load queued by an earlier click must not ambush the next match start.
        SaveGameService.CancelPendingLoad();
        loading = LoadingScreen.Load(mainMenuScene);
    }

    void SetStatus(string message, Color color)
    {
        if (statusText == null) return;
        statusText.text = message;
        statusText.color = color;
    }
}
