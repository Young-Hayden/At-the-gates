using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Scene flow for the mode-selection screen. Only skirmish against the AI
/// and the back button are active; campaign and versus remain explicit placeholders.</summary>
public class ModeSelectMenu : MonoBehaviour
{
    public Button humanButton;
    public Button campaignButton;
    public Button versusButton;
    public Button backButton;
    public string gameScene = "SampleScene";
    public string mainMenuScene = "MainMenu";

    void Awake()
    {
        GameSettings.ApplyDisplay();
    }

    void Start()
    {
        if (humanButton != null) humanButton.onClick.AddListener(StartHuman);
        if (backButton != null) backButton.onClick.AddListener(BackToMain);
        if (campaignButton != null) campaignButton.interactable = false;
        if (versusButton != null) versusButton.interactable = false;
    }

    public void StartHuman()
    {
        LoadingScreen.Load(gameScene);
    }

    public void BackToMain()
    {
        LoadingScreen.Load(mainMenuScene);
    }
}
