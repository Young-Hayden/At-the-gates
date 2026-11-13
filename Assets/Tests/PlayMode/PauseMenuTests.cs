using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>Pause-menu state and input ownership regressions.</summary>
public class PauseMenuTests : RegressionFixture
{
    const string HousePath = "Assets/Prefabs/Buildings/Knights/House_Blue.prefab";

    [UnityTest]
    public IEnumerator SetOpen_PausesAndClosesWithoutLeavingTimeStopped()
    {
        var menu = CreateMenu();
        yield return null;

        menu.SetOpen(true);
        Assert.IsTrue(menu.IsOpen, "The pause panel must open.");
        Assert.AreEqual(0f, Time.timeScale, "Opening the pause menu must freeze game time.");

        menu.SetOpen(false);
        Assert.IsFalse(menu.IsOpen, "The pause panel must close.");
        Assert.AreEqual(1f, Time.timeScale, "Closing the pause menu must resume game time.");
    }

    [UnityTest]
    public IEnumerator Escape_WhilePlacingBuilding_DoesNotOpenPauseMenu()
    {
        var menu = CreateMenu();
        yield return null;

        var prefab = LoadPrefab<GameObject>(HousePath);
        Placer.Begin(prefab);
        Assert.IsTrue(Placer.IsPlacing, "The placement control must be active.");

        bool handled = menu.HandleEscape();

        Assert.IsTrue(handled, "ESC is consumed by the placement system.");
        Assert.IsFalse(menu.IsOpen, "ESC must not open the pause menu over a placement ghost.");
        Assert.AreEqual(1f, Time.timeScale, "Placement cancellation must not freeze time.");
        Placer.Cancel();
    }

    [UnityTest]
    public IEnumerator Escape_AfterCancelInSameFrame_DoesNotReopenPauseMenu()
    {
        var menu = CreateMenu();
        yield return null;

        var prefab = LoadPrefab<GameObject>(HousePath);
        Placer.Begin(prefab);
        Placer.Cancel();     // BuildingPlacer may run before PauseMenu in this frame
        Assert.IsFalse(Placer.IsPlacing, "The control placement must be cancelled.");

        menu.HandleEscape();

        Assert.IsFalse(menu.IsOpen,
            "The same ESC that cancelled placement must not also open the pause menu.");
        Assert.AreEqual(1f, Time.timeScale,
            "A cancelled placement must not leave the game paused.");
    }

    [UnityTest]
    public IEnumerator Escape_AfterMatchEnded_DoesNotOpenPauseMenu()
    {
        var menu = CreateMenu();
        yield return null;

        GameState.Instance.End(GameState.Outcome.Win);
        bool handled = menu.HandleEscape();

        Assert.IsTrue(handled, "The game-over screen owns ESC.");
        Assert.IsFalse(menu.IsOpen, "ESC must not cover the result screen.");
    }

    [UnityTest]
    public IEnumerator MainMenu_ContinueButton_OnlyExistsWhenSaveIsValid()
    {
        var load = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
            "Assets/Scenes/MainMenu.scene",
            new UnityEngine.SceneManagement.LoadSceneParameters(
                UnityEngine.SceneManagement.LoadSceneMode.Single));
        Assert.IsTrue(load.IsValid(), "The main menu scene must load for this regression.");

        float deadline = Time.realtimeSinceStartup + 5f;
        MainMenu menu = null;
        while (Time.realtimeSinceStartup < deadline)
        {
            menu = UnityEngine.Object.FindFirstObjectByType<MainMenu>();
            if (menu != null && load.isLoaded) break;
            yield return null;
        }
        Assert.IsNotNull(menu, "The main menu must expose MainMenu.");
        Assert.IsNotNull(menu.continueButton, "The main menu must expose a Continue button.");
        yield return null;

        var start = menu.startButton.GetComponent<RectTransform>();
        var quit = menu.quitButton.GetComponent<RectTransform>();
        var resume = menu.continueButton.GetComponent<RectTransform>();

        menu.ApplyContinueLayout(false);
        Assert.IsFalse(menu.continueButton.gameObject.activeSelf,
            "Without a save the Continue button must not occupy the menu.");
        Assert.AreEqual(-54f, start.anchoredPosition.y, 0.1f,
            "Without a save, Start must return to the original two-button layout.");
        Assert.AreEqual(-180f, quit.anchoredPosition.y, 0.1f,
            "Without a save, Quit must return to the original two-button layout.");
        Assert.AreEqual(82f, start.sizeDelta.y, 0.1f, "The original Start button height must return.");
        Assert.AreEqual(82f, quit.sizeDelta.y, 0.1f, "The original Quit button height must return.");

        menu.ApplyContinueLayout(true);
        Assert.IsTrue(menu.continueButton.gameObject.activeSelf,
            "With a save the Continue button must appear.");
        Assert.IsTrue(menu.continueButton.interactable,
            "A visible Continue button must be interactive.");
        Assert.AreEqual(-38f, start.anchoredPosition.y, 0.1f,
            "With a save, Start must leave room for Continue.");
        Assert.AreEqual(-110f, resume.anchoredPosition.y, 0.1f,
            "With a save, Continue sits between Start and Quit.");
        Assert.AreEqual(-182f, quit.anchoredPosition.y, 0.1f,
            "With a save, Quit must sit below Continue.");
        Assert.AreEqual(70f, start.sizeDelta.y, 0.1f, "The compact three-button height must apply.");
        Assert.AreEqual(70f, quit.sizeDelta.y, 0.1f, "The compact three-button height must apply.");
    }

    [UnityTest]
    public IEnumerator Close_AfterMatchEnded_KeepsTimeFrozen()
    {
        var menu = CreateMenu();
        yield return null;
        menu.SetOpen(true);
        GameState.Instance.End(GameState.Outcome.Lose);
        menu.SetOpen(false);
        Assert.IsFalse(menu.IsOpen);
        Assert.AreEqual(0f, Time.timeScale,
            "Closing a pause panel must not restart simulation after the match has ended.");
    }

    [Test]
    public void StartGame_ClearsPendingLoad()
    {
        SaveGameService.RequestLoad(new SaveGameData());
        var go = new GameObject("Main menu pending-load probe");
        var menu = go.AddComponent<MainMenu>();
        menu.fadeGroup = go.AddComponent<CanvasGroup>(); // stay in fade-out; do not load a scene
        menu.StartGame();
        Assert.IsFalse(SaveGameService.ConsumePendingLoad(out _, out _),
            "Starting a new match must discard any stale queued continuation.");
    }

    PauseMenu CreateMenu()
    {
        var panel = new GameObject("Test pause panel");
        var text = new GameObject("Test pause status").AddComponent<Text>();
        var go = new GameObject("Test pause menu");
        var menu = go.AddComponent<PauseMenu>();
        menu.panel = panel;
        menu.statusText = text;
        return menu;
    }
}
